using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Hmi;

/// <summary>Krok skanu: publikuje numer bieżącego (SI 252) i poprzedniego (SI 251) ekranu HMI w operandach systemowych.</summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Sterownik Vision udostępnia numer wyświetlanego ekranu w SI 252, a poprzedniego w SI 251; drabinka często
/// warunkuje nimi logikę przycisków. Krok jest rejestrowany przed interpreterem LAD, więc drabinka widzi ekran
/// ustawiony w poprzednim skanie (przez skok, listę ekranów w UI albo tablicę skoków).
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Zapis do SI 252 z drabinki nie przełącza ekranu — dokumentacja Unitronics opisuje ten operand jako odczyt.
/// </remarks>
public sealed class HmiDisplayStatusStep : IScanStep
{
    /// <summary>SI 251 — numer poprzedniego ekranu HMI.</summary>
    public static readonly OperandAddress PreviousDisplay = new(OperandType.SI, 251);

    /// <summary>SI 252 — numer bieżącego ekranu HMI.</summary>
    public static readonly OperandAddress CurrentDisplay = new(OperandType.SI, 252);

    private readonly HmiState _hmi;

    public HmiDisplayStatusStep(HmiState hmi)
    {
        ArgumentNullException.ThrowIfNull(hmi);
        _hmi = hmi;
    }

    public void Execute(PlcMemory memory, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(memory);

        // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
        // Układ pamięci z profilu może mieć mniej SI — wtedy operand po prostu nie jest publikowany.
        if (memory.Contains(CurrentDisplay))
        {
            memory.SetNumber(CurrentDisplay, _hmi.CurrentDisplayId);
        }

        if (memory.Contains(PreviousDisplay))
        {
            memory.SetNumber(PreviousDisplay, _hmi.PreviousDisplayId);
        }
    }
}
