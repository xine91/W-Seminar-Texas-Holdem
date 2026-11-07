using System.ComponentModel.DataAnnotations;

namespace W_Seminar.Pages;

public class SimulationInput
{
    public int TableSize { get; set; } = 6;
    public string Phase { get; set; } = "Preflop";
    public string? OpponentRange { get; set; }

    [Range(50, 200000, ErrorMessage = "Iterationen: 50 bis 200000.")]
    public int MonteCarloIterations { get; set; } = 2000;

    [Range(1, 8, ErrorMessage = "Gegner: 1 bis 8.")]
    public int NumOpponents { get; set; } = 1;

    public string? HandFilter { get; set; }
    public string? Board { get; set; }

    /// <summary>Fester Seed für reproduzierbare Läufe; leer = zufällig (wird im Ergebnis angezeigt).</summary>
    public int? Seed { get; set; }

    /// <summary>Dynamische Iterationen: Abbruch, sobald die halbe 95%-KI-Breite darunter liegt (0 = aus).</summary>
    [Range(0, 0.5)]
    public double TargetCiHalfWidth { get; set; }

    // Expected Value Felder
    [Range(0, double.MaxValue)] public double SmallBlind { get; set; } = 0.5;
    [Range(0, double.MaxValue)] public double BigBlind { get; set; } = 1.0;
    [Range(0, double.MaxValue)] public double Pot { get; set; } = 1.5;
    [Range(0, double.MaxValue)] public double CallCost { get; set; } = 1.0;

    // Erweiterte EV-Parameter
    [Range(0, double.MaxValue)] public double EffectiveStack { get; set; } = 100;
    [Range(0, 100)] public double RakePercent { get; set; }
    [Range(0, double.MaxValue)] public double RakeCap { get; set; }
    [Range(0, 1)] public double ImpliedOddsFactor { get; set; }
    [Range(0, double.MaxValue)] public double RaiseSize { get; set; }

    /// <summary>Fold Equity pro Gegner in % (leer = Schätzung aus der Rangebreite).</summary>
    [Range(0, 100)] public double? FoldEquityPercent { get; set; }
}
