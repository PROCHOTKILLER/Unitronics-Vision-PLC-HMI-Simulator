using System.Text;

namespace VisionSimulator.Core.Pcom;

/// <summary>
/// Ramka odebrana z TCP: tekst ASCII oraz opcjonalny 6-bajtowy nagłówek PCOM/TCP, który odpowiedź musi powtórzyć.
/// </summary>
internal sealed record PcomFrame(byte[]? Header, string Text)
{
    /// <summary>Bajty odpowiedzi; przy nagłówku TCP kopiuje 4 pierwsze bajty i ustawia nową długość (little-endian).</summary>
    public byte[] BuildResponse(string response)
    {
        var payload = Encoding.ASCII.GetBytes(response);
        if (Header is null)
        {
            return payload;
        }

        var result = new byte[PcomFrameReader.HeaderLength + payload.Length];
        Array.Copy(Header, result, 4);
        result[4] = (byte)(payload.Length & 0xFF);
        result[5] = (byte)((payload.Length >> 8) & 0xFF);
        payload.CopyTo(result, PcomFrameReader.HeaderLength);
        return result;
    }
}

/// <summary>Odczyt ramek PCOM ze strumienia TCP: surowy ASCII zakończony CR albo ASCII poprzedzony nagłówkiem TCP.</summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Nagłówek rozpoznawany jest po bajtach 2 i 3 równych 0x65 0x00 (identyfikator protokołu); surowa ramka zaczyna się
/// od „/” i ma na tych pozycjach litery polecenia lub cyfry, więc oba tryby się nie mieszają.
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Układ nagłówka (id transakcji[2], protokół 0x65 = ASCII, 0x00, długość LE[2]) do potwierdzenia prawdziwym klientem PCOM.
/// Protokół binarny PCOM (nagłówek 0x66) nie jest obsługiwany — taka ramka zamyka połączenie z wpisem w logu.
/// </remarks>
internal static class PcomFrameReader
{
    public const int HeaderLength = 6;

    /// <summary>Czyta jedną ramkę; zwraca null przy czystym zamknięciu połączenia między ramkami.</summary>
    /// <exception cref="InvalidDataException">Ramka przekracza limit albo ma niepoprawną długość.</exception>
    /// <exception cref="EndOfStreamException">Połączenie zamknięto w trakcie ramki.</exception>
    public static async Task<PcomFrame?> ReadAsync(Stream stream, int maxLength, CancellationToken cancellationToken)
    {
        var one = new byte[1];
        var bytes = new List<byte>(64);

        while (true)
        {
            var read = await stream.ReadAsync(one.AsMemory(0, 1), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return bytes.Count == 0 ? null : throw new EndOfStreamException("Połączenie zamknięte w trakcie ramki PCOM.");
            }

            var value = one[0];
            if (bytes.Count == 0 && value is (byte)'\n' or (byte)' ')
            {
                continue;
            }

            bytes.Add(value);

            if (bytes.Count == 4 && bytes[2] == 0x65 && bytes[3] == 0x00)
            {
                return await ReadTcpFrameAsync(stream, bytes, maxLength, cancellationToken).ConfigureAwait(false);
            }

            if (bytes.Count == 4 && bytes[2] == 0x66 && bytes[3] == 0x00)
            {
                throw new InvalidDataException("Protokół binarny PCOM (0x66) nie jest obsługiwany przez symulator.");
            }

            if (value == (byte)PcomAsciiCodec.Terminator)
            {
                return new PcomFrame(null, Encoding.ASCII.GetString([.. bytes]));
            }

            if (bytes.Count > maxLength)
            {
                throw new InvalidDataException($"Ramka PCOM przekracza limit {maxLength} bajtów.");
            }
        }
    }

    private static async Task<PcomFrame> ReadTcpFrameAsync(
        Stream stream, List<byte> firstBytes, int maxLength, CancellationToken cancellationToken)
    {
        var header = new byte[HeaderLength];
        firstBytes.CopyTo(header);
        await stream.ReadExactlyAsync(header.AsMemory(4, 2), cancellationToken).ConfigureAwait(false);

        var length = header[4] | (header[5] << 8);
        if (length < 1 || length > maxLength)
        {
            throw new InvalidDataException($"Długość ramki PCOM/TCP {length} poza zakresem 1..{maxLength}.");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
        return new PcomFrame(header, Encoding.ASCII.GetString(payload));
    }
}
