using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;

namespace VisionSimulator.App.Services;

/// <summary>Dziennik zdarzeń dla interfejsu: ostatnie wpisy, najnowsze na górze.</summary>
/// <remarks>
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// <see cref="Add"/> można wołać z dowolnego wątku (PCOM, pętla skanu); kolekcja jest modyfikowana wyłącznie
/// na wątku UI przez Dispatcher, więc <see cref="Entries"/> wolno wiązać w XAML.
/// </remarks>
public sealed class SimulatorLog
{
    public const int Capacity = 200;

    public ObservableCollection<string> Entries { get; } = [];

    public void Add(string message)
    {
        ArgumentNullException.ThrowIfNull(message);

        var line = $"{DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}  {message}";
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            Insert(line);
            return;
        }

        // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
        // Brak oczekiwania na UI: wątek skanu i wątki PCOM nie mogą być blokowane przez zajęty interfejs.
        dispatcher.InvokeAsync(() => Insert(line));
    }

    private void Insert(string line)
    {
        Entries.Insert(0, line);
        while (Entries.Count > Capacity)
        {
            Entries.RemoveAt(Entries.Count - 1);
        }
    }
}
