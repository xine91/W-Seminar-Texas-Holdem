using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokerSimPlatform.Core.Simulation;

public sealed record ScenarioRunParameters(
    string OpponentRange,
    int Iterations,
    int? Seed,
    double? Stack = null,
    double? FoldProbability = null,
    double? OpenSize = null,
    double? DeadMoney = null,
    double? Realization = null,
    string? SbPushRange = null,
    string? InitialCallRange = null,
    string? ResultCallRange = null);

public sealed record ScenarioRunRecord(
    string RunId,
    DateTime TimestampUtc,
    string Scenario,
    ScenarioRunParameters Parameters,
    IReadOnlyList<ScenarioMatrixCell> Cells,
    IReadOnlyList<ScenarioMatrixCell>? BbCells = null,
    int? BestResponseSteps = null,
    bool? Converged = null,
    string? StopReason = null,
    IReadOnlyList<StrategyHistoryEntry>? History = null);

public sealed class ScenarioResultStore
{
    private readonly object _gate = new();
    private readonly string _filePath;
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ScenarioResultStore(string filePath)
    {
        _filePath = filePath;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);
    }

    public string FilePath => _filePath;

    public IReadOnlyList<ScenarioRunRecord> LoadAll()
    {
        lock (_gate)
        {
            return ReadRecords();
        }
    }

    public string Append(ScenarioRunRecord record)
    {
        lock (_gate)
        {
            var records = ReadRecords();

            records.Add(record);
            var temporaryPath = _filePath + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(records, Options));
            File.Move(temporaryPath, _filePath, overwrite: true);
        }

        return record.RunId;
    }

    private List<ScenarioRunRecord> ReadRecords()
    {
        if (!File.Exists(_filePath))
            return new List<ScenarioRunRecord>();

        var json = File.ReadAllText(_filePath);
        if (string.IsNullOrWhiteSpace(json))
            return new List<ScenarioRunRecord>();

        return JsonSerializer.Deserialize<List<ScenarioRunRecord>>(json, Options)
               ?? new List<ScenarioRunRecord>();
    }
}
