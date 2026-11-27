using System.Globalization;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Hmi;

/// <summary>Wpis wartości do zmiennej liczbowej ekranu HMI (odpowiednik klawiatury ekranowej sterownika).</summary>
public static class HmiValueEntry
{
    /// <summary>
    /// Parsuje tekst (kropka lub przecinek dziesiętny) i zapisuje wartość do rejestru.
    /// Wywołujący musi trzymać <c>lock (memory.SyncRoot)</c>.
    /// </summary>
    /// <returns>True po zapisie; false z opisem błędu w <paramref name="error"/>.</returns>
    public static bool TryWrite(PlcMemory memory, OperandAddress address, string? text, out string error)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var normalized = (text ?? string.Empty).Trim().Replace(',', '.');
        if (address.Type.IsBit() || address.Type.IsTimerOrCounter())
        {
            error = $"{address} nie jest rejestrem liczbowym.";
            return false;
        }

        if (!memory.Contains(address))
        {
            error = $"{address} jest poza układem pamięci.";
            return false;
        }

        if (address.Type == OperandType.MF)
        {
            if (!float.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var real) || !float.IsFinite(real))
            {
                error = "Niepoprawna liczba (np. 12.5).";
                return false;
            }

            memory.SetFloat(address.Index, real);
            error = string.Empty;
            return true;
        }

        if (!long.TryParse(normalized, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
        {
            error = "Niepoprawna liczba całkowita.";
            return false;
        }

        var (min, max) = Range(address.Type);
        if (number < min || number > max)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Wartość spoza zakresu typu odrzucamy zamiast obcinać bitowo — operator HMI dostaje czytelny komunikat.
            error = string.Create(CultureInfo.InvariantCulture, $"Zakres {address.Type}: {min}..{max}.");
            return false;
        }

        memory.SetNumber(address, number);
        error = string.Empty;
        return true;
    }

    private static (long Min, long Max) Range(OperandType type) => type switch
    {
        OperandType.MI or OperandType.SI or OperandType.XI => (short.MinValue, short.MaxValue),
        OperandType.ML or OperandType.SL or OperandType.XL => (int.MinValue, int.MaxValue),
        OperandType.DW or OperandType.SDW or OperandType.XDW => (uint.MinValue, uint.MaxValue),
        _ => (long.MinValue, long.MaxValue),
    };
}
