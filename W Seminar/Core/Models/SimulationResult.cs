using System;

namespace PokerSimPlatform.Core.Models;

/// <summary>
/// Ein einzelnes Simulationsergebnis. Wird in JSON serialisiert.
/// </summary>
public record SimulationResult(
    string SessionId,
    DateTime Timestamp,
    TablePosition Position,
    string StartingHand,      // z. B. "AKs", "72o", "TT"
    PlayerDecision Decision,  // Fold, Call, Raise
    double Equity,            // 0..1
    int NumSimulations,       // Anzahl Monte-Carlo-Iterationen
    int TableSize,            // 6 oder 9
    int NumOpponents,         // Konkrete Anzahl Gegner in der Hand
    string Phase,             // "Preflop" oder "Flop"
    double ExpectedValue = 0, // Expected Value basierend auf Pot und CallCost
    double Pot = 0,           // Pot-Größe (für EW-Berechnung)
    double CallCost = 0,      // Call-Kosten (für EW-Berechnung)
    string? Board = null,     // bekanntes Board (z. B. "AsKhQd"), sonst null
    double EquityStdError = 0,        // Standardfehler der Equity
    double ExpectedValueStdError = 0, // Standardfehler des Call-EW
    double ExpectedValueRaise = 0,    // EW des Raise (mit Fold Equity)
    string? OpponentRange = null      // kanonische Gegnerrange (für Filter)
);
