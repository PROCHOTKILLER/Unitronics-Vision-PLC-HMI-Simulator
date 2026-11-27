using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Hmi;

/// <summary>Gotowy do narysowania element ekranu HMI (współrzędne w logicznym ekranie 320x240).</summary>
/// <param name="ElementId">Identyfikator elementu w projekcie.</param>
/// <param name="Kind">Rodzaj elementu.</param>
/// <param name="X">Lewa krawędź po przycięciu do ekranu.</param>
/// <param name="Y">Górna krawędź po przycięciu do ekranu.</param>
/// <param name="Width">Szerokość po przycięciu (min. 1).</param>
/// <param name="Height">Wysokość po przycięciu (min. 1).</param>
/// <param name="FillArgb">Kolor wypełnienia ARGB (alfa 0 = brak).</param>
/// <param name="ForegroundArgb">Kolor tekstu i obrysu ARGB.</param>
/// <param name="Text">Tekst do wyświetlenia.</param>
/// <param name="OperandText">Adres operandu zmiennej, np. „MI 5”, albo null.</param>
/// <param name="TouchAddress">Bit ustawiany dotykiem (typ z HMI_Operands, domyślnie MB) albo null.</param>
/// <param name="IsActive">Stan bitu dla wskaźników i przycisków.</param>
/// <param name="Ratio">Wypełnienie bargrafu 0..1 albo null, gdy nie dotyczy.</param>
/// <param name="EditAddress">Rejestr zmiennej liczbowej, do którego można wpisać wartość, albo null.</param>
/// <param name="IsKeypadEntry">Zmienna z włączonym wprowadzaniem z klawiatury w projekcie (pojedyncze kliknięcie).</param>
public sealed record HmiRenderItem(
    int ElementId,
    HmiElementKind Kind,
    int X,
    int Y,
    int Width,
    int Height,
    uint FillArgb,
    uint ForegroundArgb,
    string Text,
    string? OperandText,
    OperandAddress? TouchAddress,
    bool IsActive,
    double? Ratio,
    OperandAddress? EditAddress = null,
    bool IsKeypadEntry = false);

/// <summary>Migawka ekranu HMI do wyświetlenia.</summary>
public sealed record HmiScreen(int DisplayId, string Name, uint BackgroundArgb, IReadOnlyList<HmiRenderItem> Items);
