namespace VisionSimulator.Core.Hmi;

/// <summary>Stan panelu HMI współdzielony między interpreterem LAD (skok do ekranu) a interfejsem użytkownika.</summary>
public sealed class HmiState
{
    private int _currentDisplayId;
    private int _previousDisplayId;

    /// <summary>Identyfikator aktualnie wyświetlanego ekranu; 0 oznacza, że żaden ekran nie został jeszcze wybrany.</summary>
    public int CurrentDisplayId => Volatile.Read(ref _currentDisplayId);

    /// <summary>Identyfikator ekranu wyświetlanego przed ostatnią zmianą; 0 przed pierwszą zmianą.</summary>
    public int PreviousDisplayId => Volatile.Read(ref _previousDisplayId);

    /// <summary>
    /// Zdarzenie zmiany ekranu.
    /// [OSTRZEŻENIA I KONSEKWENCJE]
    /// Wywoływane w wątku, który zmienił ekran (zwykle wątek skanu) — obsługa w UI musi przejść na wątek interfejsu
    /// i nie może blokować.
    /// </summary>
    public event EventHandler? DisplayChanged;

    /// <summary>Przełącza ekran; zdarzenie jest zgłaszane tylko przy faktycznej zmianie identyfikatora.</summary>
    public void ShowDisplay(int id)
    {
        var previous = Interlocked.Exchange(ref _currentDisplayId, id);
        if (previous != id)
        {
            Volatile.Write(ref _previousDisplayId, previous);
            DisplayChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
