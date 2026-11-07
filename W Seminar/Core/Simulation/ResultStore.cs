using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PokerSimPlatform.Core.Models;

namespace PokerSimPlatform.Core.Simulation;

/// <summary>
/// Persistiert Simulationsergebnisse als NDJSON (eine JSON-Zeile pro Ergebnis).
///
/// - Append ist wirklich append-only (kein Neuschreiben der Datei).
/// - Beschädigte Zeilen/Dateien gehen nicht verloren: sie werden geloggt und in eine
///   ".corrupt"-Datei gesichert, gültige Zeilen bleiben lesbar.
/// - Schreiben ist prozessintern per Lock und prozessübergreifend per exklusivem
///   Schreibzugriff (FileShare.Read) mit Retry geschützt.
/// - Ist als Pfad eine ".json"-Datei konfiguriert, wird daneben eine ".ndjson"-Datei verwendet und
///   der alte JSON-Array-Inhalt einmalig migriert (Original bleibt unverändert).
/// </summary>
public sealed class ResultStore
{
    private readonly object _gate = new();
    private readonly HashSet<string> _reportedBadLines = new();
    private readonly ILogger? _logger;
    private readonly string _filePath;
    private readonly string? _legacyPath;

    private static readonly JsonSerializerOptions LineOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static readonly JsonSerializerOptions ArrayOptions = new(LineOptions) { WriteIndented = true };

    public ResultStore(string filePath, ILogger? logger = null)
    {
        _logger = logger;
        if (string.Equals(Path.GetExtension(filePath), ".json", StringComparison.OrdinalIgnoreCase))
        {
            _legacyPath = filePath;
            _filePath = Path.ChangeExtension(filePath, ".ndjson");
        }
        else
        {
            _filePath = filePath;
        }

        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        MigrateLegacyIfNeeded();
    }

    public string FilePath => _filePath;

    private void MigrateLegacyIfNeeded()
    {
        if (_legacyPath == null || File.Exists(_filePath) || !File.Exists(_legacyPath)) return;

        lock (_gate)
        {
            if (File.Exists(_filePath)) return;
            try
            {
                using var stream = File.OpenRead(_legacyPath);
                var data = JsonSerializer.Deserialize<List<SimulationResult>>(stream, LineOptions) ?? new();
                WriteLines(data, FileMode.CreateNew);
                _logger?.LogInformation("Legacy-JSON migriert: {Count} Ergebnisse → {Path}", data.Count, _filePath);
            }
            catch (JsonException ex)
            {
                var backup = $"{_legacyPath}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                File.Copy(_legacyPath, backup, overwrite: false);
                _logger?.LogError(ex, "Legacy-JSON beschädigt, Sicherung unter {Backup}", backup);
            }
        }
    }

    public IReadOnlyList<SimulationResult> LoadAll()
    {
        lock (_gate)
        {
            if (!File.Exists(_filePath)) return Array.Empty<SimulationResult>();

            var results = new List<SimulationResult>();
            var bad = new List<string>();
            using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var reader = new StreamReader(stream, Encoding.UTF8))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    try
                    {
                        var item = JsonSerializer.Deserialize<SimulationResult>(line, LineOptions);
                        if (item != null) results.Add(item);
                    }
                    catch (JsonException)
                    {
                        bad.Add(line);
                    }
                }
            }

            if (bad.Count > 0) BackupBadLines(bad);
            return results;
        }
    }

    private void BackupBadLines(List<string> bad)
    {
        var fresh = bad.Where(_reportedBadLines.Add).ToList();
        if (fresh.Count == 0) return;
        _logger?.LogWarning("{Count} beschädigte Zeilen in {Path} übersprungen und nach .corrupt gesichert.", fresh.Count, _filePath);
        File.AppendAllLines(_filePath + ".corrupt", fresh);
    }

    public void Append(IEnumerable<SimulationResult> newResults)
    {
        lock (_gate) WriteLines(newResults, FileMode.Append);
    }

    public Task AppendAsync(IEnumerable<SimulationResult> newResults, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.Run(() => Append(newResults), ct);
    }

    /// <summary>Ersetzt den Inhalt atomar (temporäre Datei + Move).</summary>
    public void Save(IEnumerable<SimulationResult> results)
    {
        lock (_gate)
        {
            var tmp = _filePath + ".tmp";
            File.WriteAllLines(tmp, results.Select(r => JsonSerializer.Serialize(r, LineOptions)), new UTF8Encoding(false));
            File.Move(tmp, _filePath, overwrite: true);
        }
    }

    private void WriteLines(IEnumerable<SimulationResult> items, FileMode mode)
    {
        var sb = new StringBuilder();
        foreach (var item in items)
            sb.Append(JsonSerializer.Serialize(item, LineOptions)).Append('\n');
        if (sb.Length == 0) return;
        var bytes = new UTF8Encoding(false).GetBytes(sb.ToString());

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using var fs = new FileStream(_filePath, mode, FileAccess.Write, FileShare.Read);
                // Falls ein früherer Schreibvorgang abgebrochen wurde: Zeilenende sicherstellen.
                if (mode == FileMode.Append && fs.Length > 0 && !EndsWithNewline())
                    fs.WriteByte((byte)'\n');
                fs.Write(bytes);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(50 * (attempt + 1));
            }
        }
    }

    private bool EndsWithNewline()
    {
        using var fs = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length == 0) return true;
        fs.Seek(-1, SeekOrigin.End);
        return fs.ReadByte() == '\n';
    }

    /// <summary>Alle Ergebnisse als JSON-Array (für Download).</summary>
    public string ToJsonArray(IEnumerable<SimulationResult> results)
        => JsonSerializer.Serialize(results.ToList(), ArrayOptions);

    /// <summary>Schreibt die Ergebnisse als CSV (Invariant Culture, Semikolon-frei, Felder in Anführungszeichen bei Bedarf).</summary>
    public static void WriteCsv(TextWriter writer, IEnumerable<SimulationResult> results)
    {
        var ci = CultureInfo.InvariantCulture;
        writer.WriteLine("SessionId,Timestamp,Position,StartingHand,Decision,Equity,EquityStdError,NumSimulations," +
                         "TableSize,NumOpponents,Phase,Board,Pot,CallCost,ExpectedValue,ExpectedValueStdError,ExpectedValueRaise,OpponentRange");
        foreach (var r in results)
        {
            writer.WriteLine(string.Join(",",
                r.SessionId,
                r.Timestamp.ToString("o", ci),
                r.Position,
                r.StartingHand,
                r.Decision,
                r.Equity.ToString("R", ci),
                r.EquityStdError.ToString("R", ci),
                r.NumSimulations.ToString(ci),
                r.TableSize.ToString(ci),
                r.NumOpponents.ToString(ci),
                r.Phase,
                r.Board ?? "",
                r.Pot.ToString("R", ci),
                r.CallCost.ToString("R", ci),
                r.ExpectedValue.ToString("R", ci),
                r.ExpectedValueStdError.ToString("R", ci),
                r.ExpectedValueRaise.ToString("R", ci),
                Csv(r.OpponentRange)));
        }
    }

    private static string Csv(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Contains(',') || value.Contains('"') ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
