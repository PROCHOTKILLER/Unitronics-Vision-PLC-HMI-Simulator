using System.IO;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Profile;

namespace VisionSimulator.App.Services;

/// <summary>Profil projektu ustalony przy starcie: układ pamięci, model obiektu i komunikaty dla dziennika.</summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Układ pamięci musi być znany przed zbudowaniem kontenera DI (singleton <see cref="PlcMemory"/>), dlatego profil
/// z <c>Simulation:ProfilePath</c> jest wczytywany przed <c>Build()</c>. Profil wczytany później z UI może zmienić
/// tylko zachowanie obiektu, nie układ pamięci.
/// [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
/// Błędny lub brakujący profil nie blokuje startu: program działa na układzie V350 bez symulacji obiektu,
/// a przyczyna trafia do dziennika.
/// </remarks>
public sealed record StartupProfile(PlcMemoryLayout Layout, PlantModel? Plant, string? Path, IReadOnlyList<string> Messages)
{
    public static StartupProfile Resolve(string? configuredPath, string baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return new StartupProfile(PlcMemoryLayout.V350, null, null, ["Profil: brak (Simulation:ProfilePath) — układ V350 bez symulacji obiektu."]);
        }

        var path = System.IO.Path.GetFullPath(configuredPath.Trim(), baseDirectory);
        try
        {
            var profile = SimulatorProfileLoader.Load(path);
            var plant = SimulatorProfileLoader.Compile(profile);
            return new StartupProfile(plant.Layout, plant, path, [$"Profil: wczytano '{System.IO.Path.GetFileName(path)}'. {Describe(plant)}"]);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new StartupProfile(
                PlcMemoryLayout.V350,
                null,
                path,
                [$"Profil: nie wczytano '{System.IO.Path.GetFileName(path)}' — układ V350 bez symulacji obiektu. {ex.Message}"]);
        }
    }

    /// <summary>Jednolinijkowy opis modelu do paska stanu i dziennika.</summary>
    public static string Describe(PlantModel plant)
    {
        ArgumentNullException.ThrowIfNull(plant);
        var name = plant.Name.Length == 0 ? "bez nazwy" : plant.Name;
        return $"{name} | {plant.Layout.Model} | wartości początkowe: {plant.InitialValues.Count}, " +
               $"sprzężenia bitowe: {plant.DigitalFeedback.Count}, analogi: {plant.AnalogFollowers.Count}";
    }
}
