using System.Collections.Generic;
using PokerSimPlatform.Core.Engine;

namespace PokerSimPlatform.Core.Models;

/// <summary>
/// Konfiguration für einen Simulations-Lauf.
/// </summary>
public record TableConfig(
    int TableSize,                          // 6 oder 9
    IReadOnlyList<TablePosition> Positions, // aktive Positionen je Tischgröße
    IReadOnlyList<string> OpponentRange,    // Starthand-Range der Gegner
    int MonteCarloIterations = 2000,        // (Max-)Iterationen pro Equity-Berechnung
    bool IncludeFlop = false                // Flop-Phase: 3 feste Board-Karten, Turn+River simuliert
)
{
    public static TableConfig SixMax(IReadOnlyList<string> opponentRange, int iterations = 2000, bool flop = false) =>
        new(6,
            new[] { TablePosition.UTG, TablePosition.MP, TablePosition.CO, TablePosition.BTN, TablePosition.SB, TablePosition.BB },
            opponentRange, iterations, flop);

    public static TableConfig NineMax(IReadOnlyList<string> opponentRange, int iterations = 2000, bool flop = false) =>
        new(9,
            new[] { TablePosition.UTG, TablePosition.UTG1, TablePosition.MP, TablePosition.MP1,
                    TablePosition.HJ, TablePosition.CO, TablePosition.BTN, TablePosition.SB, TablePosition.BB },
            opponentRange, iterations, flop);
}

/// <summary>
/// Erweiterte Konfiguration mit Phaseninformation für den Runner.
/// </summary>
public record SimulationConfig(
    TableConfig Table,
    int TableSize,
    int NumOpponents,
    string Phase = "Preflop"
);
