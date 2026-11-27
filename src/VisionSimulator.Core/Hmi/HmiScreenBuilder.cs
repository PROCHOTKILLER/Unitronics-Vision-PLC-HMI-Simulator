using System.Globalization;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Core.Hmi;

/// <summary>Buduje migawkę ekranu HMI z definicji projektu i bieżącej pamięci sterownika.</summary>
/// <remarks>
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Wywołujący musi trzymać <c>lock (memory.SyncRoot)</c> na czas budowy; migawka jest niezmienna i może być
/// wyświetlana już poza blokadą.
/// </remarks>
public static class HmiScreenBuilder
{
    public const int ScreenWidth = 320;
    public const int ScreenHeight = 240;
    public const string UnknownValueText = "?";

    private const double BargraphFullScale = 100.0;
    private const uint ButtonFill = 0xFFDDE3EC;
    private const uint FrameFill = 0xFFDCE1E8;
    private const uint IndicatorFill = 0xFF3C4858;
    private const uint NumericFill = 0xFFFFFFFF;
    private const uint BargraphFill = 0xFFB8C2CE;

    public static HmiScreen Build(HmiDisplay display, PlcMemory memory)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(memory);

        var items = new List<HmiRenderItem>(display.Elements.Count);
        foreach (var element in display.Elements)
        {
            items.Add(BuildItem(element, memory));
        }

        return new HmiScreen(
            display.Id,
            display.Name ?? string.Empty,
            HmiColor.Decode(display.ColorBack, HmiColor.DefaultBackground),
            items);
    }

    private static HmiRenderItem BuildItem(HmiElement element, PlcMemory memory)
    {
        var kind = HmiElementKindResolver.Resolve(element);

        // [ZADANIA DO WYKONANIA (TODO)]
        // VisibleOperandAddress: semantyka (typ operandu, negacja) jest niepotwierdzona, więc element jest zawsze widoczny.
        var (x, y, width, height) = Clip(element);

        OperandAddress? operand = null;
        if (element.VarOperandAddress >= 0
            && VisiLogicOperandCodes.TryGetOperandType(element.VarOperandType, out var operandType))
        {
            operand = new OperandAddress(operandType, element.VarOperandAddress);
        }

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Dotyk ma każdy element z adresem dotyku (nie tylko przycisk): VisiLogic pozwala przypisać bit dotyku
        // także wskaźnikom i ramkom. Typ operandu pochodzi z HMI_Operands; dotyk obsługuje tylko operandy bitowe.
        OperandAddress? touch = null;
        if (element.TouchOperandAddress >= 0
            && VisiLogicOperandCodes.TryGetOperandType(element.TouchOperandType, out var touchType)
            && touchType.IsBit())
        {
            touch = new OperandAddress(touchType, element.TouchOperandAddress);
        }

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Wpis wartości tylko do zmiennej liczbowej z rejestrem w pamięci; liczniki i timery pomijamy,
        // bo ich wartość bieżącą nadpisuje skan.
        OperandAddress? edit = kind == HmiElementKind.NumericVariable
                               && operand is { } editable
                               && !editable.Type.IsBit()
                               && !editable.Type.IsTimerOrCounter()
                               && memory.Contains(editable)
            ? editable
            : null;

        var elementText = element.Text ?? string.Empty;
        var text = elementText;
        var isActive = false;
        double? ratio = null;

        switch (kind)
        {
            case HmiElementKind.TouchButton:
                isActive = touch is { } touchAddress && ReadBit(memory, touchAddress);
                break;
            case HmiElementKind.BitIndicator:
            case HmiElementKind.BinaryIndicator:
                isActive = operand is { } bitAddress && ReadBit(memory, bitAddress);
                if (kind == HmiElementKind.BinaryIndicator && elementText.Length == 0)
                {
                    text = isActive ? "ON" : "OFF";
                }

                break;
            case HmiElementKind.NumericVariable:
                text = operand is { } numberAddress ? FormatValue(memory, numberAddress) : UnknownValueText;
                break;
            case HmiElementKind.Bargraph:
                text = operand is { } barAddress ? FormatValue(memory, barAddress) : UnknownValueText;

                // [ZADANIA DO WYKONANIA (TODO)]
                // Zakres min/max bargrafu nie jest odczytywany z projektu; przyjęto skalę 0..100.
                ratio = operand is { } ratioAddress ? ComputeRatio(memory, ratioAddress) : 0.0;
                break;
        }

        return new HmiRenderItem(
            element.Id,
            kind,
            x,
            y,
            width,
            height,
            HmiColor.Decode(element.ColorBack, DefaultFill(kind)),
            HmiColor.Decode(element.ColorFore, HmiColor.DefaultForeground),
            text,
            operand?.ToString(),
            touch,
            isActive,
            ratio,
            edit,
            edit is not null && element.KeypadEntry);
    }

    private static (int X, int Y, int Width, int Height) Clip(HmiElement element)
    {
        var x = Math.Clamp(element.Xs, 0, ScreenWidth - 1);
        var y = Math.Clamp(element.Ys, 0, ScreenHeight - 1);
        var width = Math.Clamp(element.Dx, 1, ScreenWidth - x);
        var height = Math.Clamp(element.Dy, 1, ScreenHeight - y);
        return (x, y, width, height);
    }

    private static uint DefaultFill(HmiElementKind kind) => kind switch
    {
        HmiElementKind.TouchButton => ButtonFill,
        HmiElementKind.Frame => FrameFill,
        HmiElementKind.BitIndicator or HmiElementKind.BinaryIndicator => IndicatorFill,
        HmiElementKind.NumericVariable => NumericFill,
        HmiElementKind.Bargraph => BargraphFill,
        _ => HmiColor.Transparent,
    };

    private static bool ReadBit(PlcMemory memory, OperandAddress address) =>
        address.Type.IsBit() && memory.Contains(address) && memory.GetBit(address);

    /// <summary>
    /// Tekst wartości operandu (liczby w <c>InvariantCulture</c>, bity jako 0/1, adres spoza pamięci jako „?”).
    /// Wywołujący musi trzymać <c>lock (memory.SyncRoot)</c>.
    /// </summary>
    public static string FormatValue(PlcMemory memory, OperandAddress address)
    {
        ArgumentNullException.ThrowIfNull(memory);

        // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
        // Operand spoza układu pamięci pokazujemy jako „?”, bez wyjątku.
        if (!memory.Contains(address))
        {
            return UnknownValueText;
        }

        if (address.Type == OperandType.MF)
        {
            return memory.GetFloat(address.Index).ToString("G7", CultureInfo.InvariantCulture);
        }

        if (address.Type.IsBit())
        {
            return memory.GetBit(address) ? "1" : "0";
        }

        return memory.GetNumber(address).ToString(CultureInfo.InvariantCulture);
    }

    private static double ComputeRatio(PlcMemory memory, OperandAddress address)
    {
        if (!memory.Contains(address))
        {
            return 0.0;
        }

        double value;
        if (address.Type == OperandType.MF)
        {
            value = memory.GetFloat(address.Index);
        }
        else if (address.Type.IsBit())
        {
            value = memory.GetBit(address) ? BargraphFullScale : 0.0;
        }
        else
        {
            value = memory.GetNumber(address);
        }

        return double.IsFinite(value) ? Math.Clamp(value / BargraphFullScale, 0.0, 1.0) : 0.0;
    }
}
