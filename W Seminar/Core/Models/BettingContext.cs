namespace PokerSimPlatform.Core.Models;

/// <summary>
/// Parameter für die EV-basierte Entscheidung (Pot Odds, Rake, Stack Depth, Implied Odds, Fold Equity).
/// Alle Beträge in derselben Einheit (z. B. Big Blinds). Pot = Pot vor der eigenen Aktion
/// (inkl. Wette des Gegners), CallCost = Betrag, den der Held zum Callen zahlen muss.
/// </summary>
public sealed record BettingContext(
    double Pot = 0,
    double CallCost = 0,
    double EffectiveStack = 100,       // Effektiver Stack (Stack Depth)
    double RakePercent = 0,            // z. B. 0.05 = 5 % des gewonnenen Pots
    double RakeCap = 0,                // 0 = kein Cap
    double ImpliedOddsFactor = 0,      // Anteil des Reststacks, der bei getroffener Hand zusätzlich gewonnen wird (0..1)
    double RaiseSize = 0,              // zusätzlicher Einsatz beim Raise; 0 = Pot-Size-Raise (Pot + CallCost)
    double? FoldEquityOverride = null  // fester Wert für Fold Equity pro Gegner; null = Schätzung aus Rangebreite
)
{
    /// <summary>EV-Modus ist aktiv, wenn Pot und Call-Kosten gesetzt sind.</summary>
    public bool IsEvMode => Pot > 0 && CallCost > 0;
}
