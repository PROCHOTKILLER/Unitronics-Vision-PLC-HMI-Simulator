using System.Globalization;

namespace VisionSimulator.Core.Pcom;

/// <summary>
/// Kodek ramek PCOM ASCII: <c>/UUCCdane KK\r</c>, gdzie UU to identyfikator jednostki (2 cyfry hex), CC to polecenie
/// (2 lub 3 litery), KK to suma kontrolna (2 cyfry hex). Odpowiedź ma przedrostek <c>/A</c>, a błąd <c>/E</c>.
/// </summary>
/// <remarks>
/// [INTENCJA I POWÓD DZIAŁANIA]
/// Suma kontrolna to suma kodów znaków między „/” a sumą (bez nich) modulo 256, zapisana wielkimi literami hex.
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Zgodność algorytmu sumy z dokumentacją Unitronics potwierdzono wyłącznie na przykładowej ramce z testów.
/// </remarks>
public static class PcomAsciiCodec
{
    public const char StartMarker = '/';
    public const char Terminator = '\r';

    private const int MinimumFrameLength = 7;

    /// <summary>Suma kontrolna (2 znaki hex, wielkie litery) dla treści między „/” a sumą.</summary>
    public static string Checksum(ReadOnlySpan<char> body)
    {
        var sum = 0;
        foreach (var character in body)
        {
            sum += character;
        }

        return (sum & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Polecenia trzyliterowe PCOM ASCII. Rozpoznawane przed dwuliterowymi, bo adres hex po poleceniu może zaczynać się
    /// literą A–F (np. <c>SNL</c> vs <c>SN</c> + adres), więc długości polecenia nie da się ustalić z samych liter.
    /// </summary>
    public static readonly IReadOnlySet<string> ThreeLetterCommands = new HashSet<string>(StringComparer.Ordinal)
    {
        "RNL", "RNH", "RND", "RNJ", "SNL", "SNH", "SND", "SNJ", "CCR", "CCS", "CCI",
    };

    /// <summary>
    /// Składa ramkę odpowiedzi <c>/A</c> z zakończeniem CR; suma kontrolna obejmuje identyfikator, polecenie i dane
    /// (bez znaku <c>A</c>).
    /// </summary>
    /// <remarks>
    /// [OSTRZEŻENIA I KONSEKWENCJE]
    /// Zakres sumy w odpowiedzi przyjęto wg opisu protokołu PCOM ASCII; do potwierdzenia klientem Unitronics
    /// (Remote Operator / UniDownloader) lub bibliotekę PCOM.
    /// </remarks>
    public static string BuildResponse(string unitId, string command, string data)
    {
        var body = $"{unitId}{command}{data}";
        return $"{StartMarker}A{body}{Checksum(body)}{Terminator}";
    }

    /// <summary>Składa ramkę błędu <c>/E</c> z kodem w dwóch cyfrach hex.</summary>
    public static string BuildError(string unitId, PcomErrorCode code) =>
        Seal($"E{unitId}{((byte)code).ToString("X2", CultureInfo.InvariantCulture)}");

    /// <summary>Składa ramkę żądania (używane przez klientów i testy).</summary>
    public static string BuildRequest(string unitId, string command, string data) =>
        Seal($"{unitId}{command}{data}");

    /// <summary>Zapis liczby jako hex o stałej liczbie cyfr (wielkie litery).</summary>
    public static string FormatHex(uint value, int digits) =>
        value.ToString("X" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

    /// <summary>Odczyt liczby hex; odrzuca znaki spoza cyfr szesnastkowych (spacje, znaki, prefiksy).</summary>
    public static bool TryParseHex(ReadOnlySpan<char> text, out uint value)
    {
        value = 0;
        if (text.IsEmpty || text.Length > 8)
        {
            return false;
        }

        foreach (var character in text)
        {
            if (!char.IsAsciiHexDigit(character))
            {
                return false;
            }
        }

        return uint.TryParse(text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// Parsuje ramkę żądania (z CR/LF na końcu lub bez). Przy błędzie sumy kontrolnej <paramref name="request"/>
    /// zawiera rozpoznane pola, aby serwer mógł odpowiedzieć na właściwy identyfikator jednostki.
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> frame, out PcomRequest request, out PcomErrorCode error)
    {
        request = new PcomRequest("00", string.Empty, string.Empty);
        error = PcomErrorCode.BadFrame;

        var text = frame.TrimEnd("\r\n");
        if (text.Length < MinimumFrameLength || text[0] != StartMarker)
        {
            return false;
        }

        var body = text[1..^2];
        foreach (var character in body)
        {
            if (character is < ' ' or > '~')
            {
                return false;
            }
        }

        var unit = body[..2];
        var commandLength = body.Length >= 5 && ThreeLetterCommands.Contains(body.Slice(2, 3).ToString()) ? 3 : 2;
        var command = body.Slice(2, commandLength);
        if (!TryParseHex(unit, out _) || !char.IsAsciiLetterUpper(command[0]) || !char.IsAsciiLetterUpper(command[1]))
        {
            return false;
        }

        request = new PcomRequest(unit.ToString(), command.ToString(), body[(2 + commandLength)..].ToString());
        if (!MemoryExtensions.Equals(text[^2..], Checksum(body), StringComparison.OrdinalIgnoreCase))
        {
            error = PcomErrorCode.BadChecksum;
            return false;
        }

        error = PcomErrorCode.None;
        return true;
    }

    private static string Seal(string body) => $"{StartMarker}{body}{Checksum(body)}{Terminator}";
}
