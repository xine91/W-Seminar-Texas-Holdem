namespace PokerSimPlatform.Web.Services;

/// <summary>Konfiguration (Abschnitt "PokerSim" in appsettings.json).</summary>
public sealed class PokerSimOptions
{
    public const string SectionName = "PokerSim";

    public string DataDirectory { get; set; } = "Data";
    public string ResultFileName { get; set; } = "simulation_results.ndjson";

    /// <summary>Seed-Daten im Hintergrund erzeugen, falls noch keine Ergebnisse existieren.</summary>
    public bool SeedOnStartup { get; set; } = true;

    /// <summary>Fester Seed für reproduzierbare Seed-Daten.</summary>
    public int RandomSeed { get; set; } = 20240101;

    public int SeedIterations { get; set; } = 2000;
}
