namespace VisionSimulator.Core.Memory;

/// <summary>
/// Timer sterownika (operand T). Wartości Preset i Current są w jednostkach 10 ms;
/// bit wyjściowy (Output) jest ustawiony, gdy odliczanie dojdzie do zera.
/// </summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Semantyka Unitronics (TD): przy zasilonej cewce wartość bieżąca maleje od Preset do 0, a bit = 1 przy 0.
/// Brak zasilania cewki natychmiast przywraca Current = Preset i bit = 0.
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Klasa nie jest bezpieczna wątkowo; dostęp wyłącznie pod <see cref="PlcMemory.SyncRoot"/>.
/// </remarks>
public sealed class PlcTimer
{
    private const double UnitMilliseconds = 10.0;

    // [WYJAŚNIENIE ALGORYTMU I ZAAWANSOWANEJ MATEMATYKI]
    // Reszta poniżej 10 ms jest akumulowana między skanami, więc skan 25 ms daje po 4 skanach dokładnie 10 jednostek.
    private double _remainderMs;

    /// <summary>Nastawa w jednostkach 10 ms.</summary>
    public long Preset { get; set; }

    /// <summary>Wartość bieżąca w jednostkach 10 ms (odliczanie w dół).</summary>
    public long Current { get; set; }

    /// <summary>Bit timera: 1, gdy odliczanie zakończone.</summary>
    public bool Output { get; set; }

    /// <summary>Czy cewka timera była zasilona w ostatnim wywołaniu <see cref="Update"/>.</summary>
    public bool Enabled { get; private set; }

    /// <summary>Ustawia nastawę; dla niezasilonego timera przywraca też wartość bieżącą.</summary>
    public void LoadPreset(long preset)
    {
        Preset = Math.Max(0, preset);
        if (!Enabled)
        {
            Current = Preset;
            Output = false;
        }
    }

    /// <summary>Jeden krok timera: <paramref name="powered"/> to stan zasilenia cewki w bieżącym skanie.</summary>
    public void Update(bool powered, TimeSpan elapsed)
    {
        if (!powered)
        {
            Reset();
            return;
        }

        if (!Enabled)
        {
            Enabled = true;
            _remainderMs = 0;
            Current = Preset;
        }

        _remainderMs += Math.Max(0, elapsed.TotalMilliseconds);
        var units = (long)(_remainderMs / UnitMilliseconds);
        _remainderMs -= units * UnitMilliseconds;
        Current = Math.Max(0, Current - units);
        Output = Current == 0;
    }

    /// <summary>Zerowanie timera: Current = Preset, bit = 0, odliczanie wstrzymane.</summary>
    public void Reset()
    {
        Enabled = false;
        _remainderMs = 0;
        Current = Preset;
        Output = false;
    }

    /// <summary>Zimny start: zeruje także nastawę.</summary>
    public void Clear()
    {
        Preset = 0;
        Reset();
        Current = 0;
    }
}
