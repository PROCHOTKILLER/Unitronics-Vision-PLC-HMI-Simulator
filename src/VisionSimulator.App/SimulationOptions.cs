namespace VisionSimulator.App;

/// <summary>Ustawienia pętli symulacji (sekcja <c>Simulation</c> w appsettings.json).</summary>
public sealed class SimulationOptions
{
    public const string SectionName = "Simulation";

    public int ScanIntervalMs { get; set; } = 25;

    /// <summary>
    /// Profil projektu JSON wczytywany przy starcie (ścieżka bezwzględna albo względna do katalogu programu).
    /// Pusty = model V350 bez symulacji obiektu. Układ pamięci z profilu działa tylko przy starcie.
    /// </summary>
    public string? ProfilePath { get; set; }
}
