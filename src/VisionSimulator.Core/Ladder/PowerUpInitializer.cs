using System.Globalization;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Core.Ladder;

/// <summary>
/// Wartości początkowe operandów z definicji projektu (LdrOperands): wartości power-up oraz nastawy timerów i liczników.
/// </summary>
/// <remarks>
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Nastawa timera T jest czytana z pola power-up niezależnie od flagi „włączone”, a jednostką jest 10 ms.
/// Operandy wejściowe (I) oraz systemowe (SB, SI, SL, SDW) są pomijane — należą do sterownika, nie do programu.
/// </remarks>
internal static class PowerUpInitializer
{
    public static (IReadOnlyList<PowerUpValue> Values, IReadOnlyList<PresetValue> Presets) Compile(IEnumerable<OperandInfo> operands)
    {
        var values = new List<PowerUpValue>();
        var presets = new List<PresetValue>();

        foreach (var info in operands)
        {
            if (info.Memory is not { } address)
            {
                continue;
            }

            if (address.Type.IsTimerOrCounter())
            {
                if (VisiLogicOperandCodes.TryParsePowerUp(info.PowerUpRaw, out var preset))
                {
                    presets.Add(new PresetValue(address, preset));
                }

                continue;
            }

            if (!info.PowerUpEnabled || IsOwnedByController(address.Type) || !TryParseValue(info.PowerUpRaw, address.Type, out var integer, out var real))
            {
                continue;
            }

            values.Add(new PowerUpValue(address, integer, real));
        }

        return (values, presets);
    }

    /// <summary>Zapisuje wartości power-up do pamięci (ignoruje adresy poza zakresem układu pamięci).</summary>
    public static void ApplyValues(IReadOnlyList<PowerUpValue> values, PlcMemory memory)
    {
        foreach (var value in values)
        {
            var address = value.Address;
            if (!memory.Contains(address))
            {
                continue;
            }

            if (address.Type == OperandType.MF)
            {
                memory.SetFloat(address.Index, (float)value.FloatValue);
            }
            else if (address.Type.IsBit())
            {
                memory.SetBit(address, value.IntegerValue != 0);
            }
            else
            {
                memory.SetNumber(address, value.IntegerValue);
            }
        }
    }

    /// <summary>Ustawia nastawy timerów (jednostka 10 ms) i liczników.</summary>
    public static void ApplyPresets(IReadOnlyList<PresetValue> presets, PlcMemory memory)
    {
        foreach (var preset in presets)
        {
            if (!memory.Contains(preset.Address))
            {
                continue;
            }

            if (preset.Address.Type == OperandType.T)
            {
                memory.GetTimer(preset.Address.Index).LoadPreset(preset.Value);
            }
            else
            {
                memory.GetCounter(preset.Address.Index).Preset = Math.Max(0, preset.Value);
            }
        }
    }

    private static bool IsOwnedByController(OperandType type) =>
        type is OperandType.I or OperandType.SB or OperandType.SI or OperandType.SL or OperandType.SDW;

    // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
    // Tekst power-up bywa liczbą całkowitą, ułamkiem ("5.0") albo słowem True/False; kultura niezmienna.
    private static bool TryParseValue(string? raw, OperandType type, out long integer, out double real)
    {
        integer = 0;
        real = 0;
        var text = raw?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (bool.TryParse(text, out var flag))
        {
            integer = flag ? 1 : 0;
            real = integer;
            return true;
        }

        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out real) || !double.IsFinite(real))
        {
            return false;
        }

        if (type == OperandType.MF)
        {
            return true;
        }

        return VisiLogicOperandCodes.TryParsePowerUp(text, out integer);
    }
}
