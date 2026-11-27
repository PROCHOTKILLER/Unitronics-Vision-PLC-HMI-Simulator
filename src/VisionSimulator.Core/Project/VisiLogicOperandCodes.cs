using System.Globalization;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Project;

/// <summary>Kody typów operandów zapisywane przez VisiLogic w kolumnach OprndType / Elmnt_Oprnd_Type.</summary>
public static class VisiLogicOperandCodes
{
    public const int HmiPage = 30;
    public const int Subroutine = 31;

    // [ZADANIA DO WYKONANIA (TODO)]
    // Kody XB/XI/XL/XDW nie są jeszcze potwierdzone na projekcie; operand z nieznanym kodem trafia do
    // ElementOperandKind.Unknown i do ostrzeżeń, zamiast być zgadywany.
    private static readonly Dictionary<int, OperandType> MemoryCodes = new()
    {
        [1] = OperandType.MB,
        [2] = OperandType.SB,
        [3] = OperandType.MI,
        [4] = OperandType.SI,
        [5] = OperandType.ML,
        [6] = OperandType.SL,
        [7] = OperandType.MF,
        [9] = OperandType.I,
        [10] = OperandType.O,
        [11] = OperandType.T,
        [12] = OperandType.C,
        [16] = OperandType.DW,
        [36] = OperandType.SDW,
    };

    // Stałe bezpośrednie: wartość leży w LdrOperands.OprndPowerUp pod kluczem (kod, adres).
    private static readonly HashSet<int> ConstantCodes = [15, 34, 35, 37, 38];

    public static bool TryGetOperandType(int code, out OperandType type) => MemoryCodes.TryGetValue(code, out type);

    public static bool IsConstant(int code) => ConstantCodes.Contains(code);

    public static ElementOperandKind Classify(int code) => code switch
    {
        HmiPage => ElementOperandKind.HmiPage,
        Subroutine => ElementOperandKind.Subroutine,
        _ when IsConstant(code) => ElementOperandKind.Constant,
        _ when MemoryCodes.ContainsKey(code) => ElementOperandKind.Memory,
        _ => ElementOperandKind.Unknown,
    };

    /// <summary>Parsuje tekst OprndPowerUp (liczba całkowita lub zmiennoprzecinkowa, ze znakami \0).</summary>
    public static bool TryParsePowerUp(string? raw, out long value)
    {
        value = 0;
        var text = raw?.Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
        // VisiLogic zapisuje część wartości jako "5.0"; ułamek obcinamy jak przy przypisaniu do rejestru całkowitego.
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            && number is >= long.MinValue and <= long.MaxValue)
        {
            value = (long)number;
            return true;
        }

        return false;
    }
}
