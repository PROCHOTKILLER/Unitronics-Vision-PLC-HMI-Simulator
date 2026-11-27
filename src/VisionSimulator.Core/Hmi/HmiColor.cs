namespace VisionSimulator.Core.Hmi;

/// <summary>Dekodowanie kolorów zapisanych w projekcie VisiLogic na ARGB (<see cref="uint"/>).</summary>
public static class HmiColor
{
    /// <summary>Domyślne tło ekranu, gdy kolor z projektu jest pusty lub niepoprawny.</summary>
    public const uint DefaultBackground = 0xFFE6EBF0;

    /// <summary>Domyślny kolor tekstu i obrysu.</summary>
    public const uint DefaultForeground = 0xFF1E232D;

    /// <summary>Brak wypełnienia.</summary>
    public const uint Transparent = 0x00000000;

    /// <summary>
    /// Dekoduje kod koloru: bajty 0xRRGGBB w niskich 24 bitach, kanał alfa ustawiany na 0xFF.
    /// Zero (czarny, w projekcie także „kolor nieustawiony”) oraz wartość ujemna zwracają <paramref name="fallbackArgb"/>.
    /// </summary>
    /// <remarks>
    /// [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
    /// Kolejność kanałów (RGB a BGR) nie jest potwierdzona specyfikacją; przyjęto RGB. Czysty czarny nie jest
    /// rozróżniany od braku koloru, więc zawsze zastępuje go kolor domyślny.
    /// </remarks>
    public static uint Decode(int code, uint fallbackArgb)
    {
        if (code <= 0)
        {
            return fallbackArgb;
        }

        return 0xFF000000u | ((uint)code & 0x00FFFFFFu);
    }
}
