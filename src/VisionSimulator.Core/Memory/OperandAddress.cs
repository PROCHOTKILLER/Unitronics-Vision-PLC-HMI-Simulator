using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace VisionSimulator.Core.Memory;

/// <summary>Adres operandu, np. <c>MI 101</c>, <c>MB22</c>, <c>I 32</c>.</summary>
public readonly record struct OperandAddress(OperandType Type, int Index)
{
    public override string ToString() => $"{Type} {Index.ToString(CultureInfo.InvariantCulture)}";

    public static OperandAddress Parse(string text) =>
        TryParse(text, out var address)
            ? address
            : throw new FormatException($"Niepoprawny adres operandu: '{text}'.");

    public static bool TryParse([NotNullWhen(true)] string? text, out OperandAddress address)
    {
        address = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var span = text.AsSpan().Trim();
        var split = 0;
        while (split < span.Length && char.IsLetter(span[split]))
        {
            split++;
        }

        // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
        // VisiLogic zapisuje adresy raz ze spacją ("MI 101"), raz bez ("MI101"); obie formy są poprawne.
        if (split == 0
            || !Enum.TryParse(span[..split], ignoreCase: true, out OperandType type)
            || !int.TryParse(span[split..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var index))
        {
            return false;
        }

        address = new OperandAddress(type, index);
        return true;
    }
}
