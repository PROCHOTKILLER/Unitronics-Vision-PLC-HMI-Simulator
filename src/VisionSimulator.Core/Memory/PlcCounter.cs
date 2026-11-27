namespace VisionSimulator.Core.Memory;

/// <summary>
/// Licznik sterownika (operand C): zlicza zbocza narastające sygnału wejściowego;
/// bit wyjściowy jest ustawiony, gdy wartość bieżąca osiągnie nastawę (Preset &gt; 0).
/// </summary>
/// <remarks>
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Klasa nie jest bezpieczna wątkowo; dostęp wyłącznie pod <see cref="PlcMemory.SyncRoot"/>.
/// </remarks>
public sealed class PlcCounter
{
    private bool _previousInput;

    /// <summary>Nastawa licznika.</summary>
    public long Preset { get; set; }

    /// <summary>Wartość bieżąca.</summary>
    public long Current { get; set; }

    /// <summary>Bit licznika: 1, gdy Current &gt;= Preset (i Preset &gt; 0).</summary>
    public bool Output { get; set; }

    /// <summary>Jeden krok licznika: zlicza tylko przejście 0 → 1 sygnału <paramref name="input"/>.</summary>
    public void Count(bool input)
    {
        if (input && !_previousInput && Current < int.MaxValue)
        {
            Current++;
        }

        _previousInput = input;
        Output = Preset > 0 && Current >= Preset;
    }

    /// <summary>Zerowanie licznika (cewka R): Current = 0, bit = 0.</summary>
    public void Reset()
    {
        Current = 0;
        Output = false;
    }

    /// <summary>Zimny start: zeruje także nastawę i pamięć zbocza.</summary>
    public void Clear()
    {
        Preset = 0;
        _previousInput = false;
        Reset();
    }
}
