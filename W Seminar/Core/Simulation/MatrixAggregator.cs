using System;
using System.Collections.Generic;
using System.Linq;
using PokerSimPlatform.Core.Models;

namespace PokerSimPlatform.Core.Simulation;

/// <summary>
/// Aggregationsergebnis für eine einzelne Zelle der 13×13-Matrix.
/// Ci95-Werte sind halbe Breiten des 95%-Konfidenzintervalls des Mittelwerts
/// (Quadratsumme der Standardfehler der Einzelwerte, unabhängig angenommen).
/// </summary>
public record MatrixCell(
    string Hand,
    int TotalSamples,
    double AvgEquity,
    double RaiseRate,
    double CallRate,
    double FoldRate,
    double AvgExpectedValue = 0,
    double EquityCi95 = 0,
    double ExpectedValueCi95 = 0,
    long TotalSimulations = 0,
    bool LowSample = false
);

/// <summary>Filter für Matrix/Rohdaten – Session und Gegnerrange verhindern das Vermischen von Läufen.</summary>
public sealed record ResultFilter(
    TablePosition? Position = null,
    string? Phase = null,
    string? SessionId = null,
    string? OpponentRange = null)
{
    public IReadOnlyList<SimulationResult> Apply(IReadOnlyList<SimulationResult> source)
    {
        IEnumerable<SimulationResult> q = source;
        if (Position.HasValue) q = q.Where(r => r.Position == Position.Value);
        if (!string.IsNullOrEmpty(Phase) && Phase != "Alle") q = q.Where(r => r.Phase == Phase);
        if (!string.IsNullOrEmpty(SessionId)) q = q.Where(r => r.SessionId == SessionId);
        if (!string.IsNullOrEmpty(OpponentRange)) q = q.Where(r => r.OpponentRange == OpponentRange);
        return q.ToList();
    }
}

/// <summary>
/// Erzeugt eine 13×13-Matrix aggregiert nach Starthand.
/// </summary>
public static class MatrixAggregator
{
    /// <summary>Simulationen pro Zelle, unter denen die Zelle als unsicher markiert wird.</summary>
    public const int MinReliableSimulations = 1000;

    private static readonly char[] Ranks = { 'A', 'K', 'Q', 'J', 'T', '9', '8', '7', '6', '5', '4', '3', '2' };

    public static IReadOnlyList<char> RanksOrder() => Ranks;

    /// <summary>
    /// Matrix-Notation für eine Zelle. Ränge sind absteigend (A..2) angeordnet:
    /// - Diagonale → Paar ("AA")
    /// - Zeile vor Spalte (oberhalb der Diagonale) → suited ("AKs")
    /// - Zeile nach Spalte (unterhalb der Diagonale) → offsuit ("AKo")
    /// Die Notation nennt immer den höheren Rang zuerst.
    /// </summary>
    public static string CellNotation(char rowRank, char colRank)
    {
        int rowIdx = Array.IndexOf(Ranks, rowRank);
        int colIdx = Array.IndexOf(Ranks, colRank);
        if (rowIdx < 0 || colIdx < 0)
            throw new ArgumentException($"Ungültiger Rang: '{rowRank}'/'{colRank}'.");

        if (rowIdx == colIdx) return $"{rowRank}{colRank}";

        bool suited = rowIdx < colIdx;
        char high = suited ? rowRank : colRank;
        char low = suited ? colRank : rowRank;
        return $"{high}{low}{(suited ? 's' : 'o')}";
    }

    public static MatrixCell[,] BuildMatrix(IReadOnlyList<SimulationResult> results, TablePosition? positionFilter)
        => BuildMatrix(new ResultFilter(Position: positionFilter).Apply(results));

    /// <summary>Erzeugt die Matrix aus bereits gefilterten Ergebnissen.</summary>
    public static MatrixCell[,] BuildMatrix(IReadOnlyList<SimulationResult> filtered)
    {
        var byHand = filtered.GroupBy(r => r.StartingHand).ToDictionary(g => g.Key, g => g.ToList());

        var cells = new MatrixCell[13, 13];
        for (int r = 0; r < 13; r++)
        {
            for (int c = 0; c < 13; c++)
            {
                var notation = CellNotation(Ranks[r], Ranks[c]);
                if (!byHand.TryGetValue(notation, out var matching) || matching.Count == 0)
                {
                    cells[r, c] = new MatrixCell(notation, 0, 0, 0, 0, 0);
                    continue;
                }

                int total = matching.Count;
                long sims = matching.Sum(x => (long)x.NumSimulations);
                double eqCi = 1.96 * Math.Sqrt(matching.Sum(x => x.EquityStdError * x.EquityStdError)) / total;
                double evCi = 1.96 * Math.Sqrt(matching.Sum(x => x.ExpectedValueStdError * x.ExpectedValueStdError)) / total;

                cells[r, c] = new MatrixCell(
                    notation,
                    total,
                    matching.Average(x => x.Equity),
                    (double)matching.Count(x => x.Decision == PlayerDecision.Raise) / total,
                    (double)matching.Count(x => x.Decision == PlayerDecision.Call) / total,
                    (double)matching.Count(x => x.Decision == PlayerDecision.Fold) / total,
                    matching.Average(x => x.ExpectedValue),
                    eqCi,
                    evCi,
                    sims,
                    sims < MinReliableSimulations);
            }
        }
        return cells;
    }
}
