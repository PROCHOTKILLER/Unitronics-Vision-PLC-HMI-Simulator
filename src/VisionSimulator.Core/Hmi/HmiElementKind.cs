using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Core.Hmi;

/// <summary>Rodzaj elementu ekranu HMI rozpoznany po surowych polach projektu.</summary>
public enum HmiElementKind
{
    /// <summary>Typ nierozpoznany; rysowany jako zwykły tekst.</summary>
    Unknown,
    TouchButton,
    BitIndicator,
    Bargraph,
    BinaryIndicator,
    NumericVariable,
    Frame,
    StaticText,
}

public static class HmiElementKindResolver
{
    private const int TypeFrame = 1;
    private const int TypeStaticText = 4;
    private const int TypeVariable = 5;
    private const int TypeBitIndicator = 6;
    private const int TypeTouchButton = 11;
    private const int VarTypeBargraph = 24;
    private const int VarTypeBit = 2;

    /// <summary>Klasyfikuje element wyłącznie wg pól <c>Type</c>, <c>VarType</c> i typu operandu zmiennej.</summary>
    /// <remarks>
    /// [ARCHITEKTURA I ADR]
    /// Kolejność: przycisk, wskaźnik bitu, zmienna (bargraf, wskaźnik binarny, liczba), ramka, tekst. Brak reguł
    /// zależnych od tekstu elementu lub numeru adresu — zachowania specyficzne dla projektu należą do profilu (etap 5).
    /// </remarks>
    public static HmiElementKind Resolve(HmiElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        switch (element.Type)
        {
            case TypeTouchButton:
                return HmiElementKind.TouchButton;
            case TypeBitIndicator:
                return HmiElementKind.BitIndicator;
            case TypeVariable:
                if (element.VarType == VarTypeBargraph)
                {
                    return HmiElementKind.Bargraph;
                }

                var isBitOperand = VisiLogicOperandCodes.TryGetOperandType(element.VarOperandType, out var type) && type.IsBit();
                return element.VarType == VarTypeBit || isBitOperand
                    ? HmiElementKind.BinaryIndicator
                    : HmiElementKind.NumericVariable;
            case TypeFrame:
                return HmiElementKind.Frame;
            case TypeStaticText:
                return HmiElementKind.StaticText;
            default:
                return HmiElementKind.Unknown;
        }
    }
}
