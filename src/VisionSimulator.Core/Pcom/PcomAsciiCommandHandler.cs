using System.Text;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Pcom;

/// <summary>
/// Wykonuje polecenia PCOM ASCII na pamięci symulatora i składa ramkę odpowiedzi.
/// Obsługiwane: <c>ID</c> (nazwa), standardowe odczyty <c>RE RA RB GS RW GF RNL RNH RND RNJ</c>, zapisy
/// <c>SA SB SS SW SF SNL SNH SND SNJ</c>, sterowanie <c>CCR</c>/<c>CCS</c>/<c>CCI</c> oraz rozszerzenia symulatora
/// <c>RO</c>/<c>WO</c> (dowolny typ operandu) i <c>ST</c>.
/// Pobieranie i wysyłanie programu (<c>DP</c>, <c>UP</c>) kończy się błędem <see cref="PcomErrorCode.Unsupported"/>.
/// </summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Polecenia standardowe: <c>AAAA NN [wartości]</c> (adres 4 hex, liczba 1..255 w 2 hex). Rozszerzenia RO/WO:
/// <c>TT AAAA NN [wartości]</c>, gdzie TT to numer <see cref="OperandType"/> (2 cyfry hex). Bity jako znaki 0/1;
/// MI/SI/XI jako 4 cyfry hex (16 bit), pozostałe liczby (w tym MF jako bity IEEE 754 oraz Current dla T/C) jako 8 cyfr hex.
/// Odpowiedź na zapis standardowy nie zawiera danych.
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Zgodność poleceń standardowych z firmware Vision trzeba potwierdzić prawdziwym klientem PCOM (SCADA/OPC, Remote
/// Operator). Dostęp do pamięci odbywa się pod <see cref="PlcMemory.SyncRoot"/>.
/// </remarks>
public sealed class PcomAsciiCommandHandler(PlcMemory memory, string plcName)
{
    private const int TargetLength = 8;
    private const int RangeLength = 6;
    private const int MaxCount = 255;

    // [ARCHITEKTURA I ADR]
    // Standardowe polecenia PCOM ASCII sterowników Vision: odczyt i zapis zakresu operandów (AAAA = adres 4 hex,
    // NN = liczba 2 hex; bity jako 0/1, MI/SI jako 4 hex, ML/SL/DW/SDW jako 8 hex). To z nich korzystają klienci
    // SCADA/OPC i Remote Operator; RO/WO/ST to rozszerzenia symulatora (pozostałe typy, MF, T/C).
    private static readonly Dictionary<string, OperandType> StandardReads = new(StringComparer.Ordinal)
    {
        ["RE"] = OperandType.I,
        ["RA"] = OperandType.O,
        ["RB"] = OperandType.MB,
        ["GS"] = OperandType.SB,
        ["RW"] = OperandType.MI,
        ["GF"] = OperandType.SI,
        ["RNL"] = OperandType.ML,
        ["RNH"] = OperandType.SL,
        ["RND"] = OperandType.DW,
        ["RNJ"] = OperandType.SDW,
    };

    private static readonly Dictionary<string, OperandType> StandardWrites = new(StringComparer.Ordinal)
    {
        ["SA"] = OperandType.O,
        ["SB"] = OperandType.MB,
        ["SS"] = OperandType.SB,
        ["SW"] = OperandType.MI,
        ["SF"] = OperandType.SI,
        ["SNL"] = OperandType.ML,
        ["SNH"] = OperandType.SL,
        ["SND"] = OperandType.DW,
        ["SNJ"] = OperandType.SDW,
    };

    private readonly PlcMemory _memory = memory ?? throw new ArgumentNullException(nameof(memory));
    private readonly string _plcName = plcName ?? string.Empty;

    /// <summary>Zgłaszane po poprawnym poleceniu ST, CCR, CCS lub CCI, poza blokadą pamięci.</summary>
    public event EventHandler<PcomControlCommand>? ControlRequested;

    /// <summary>Przetwarza jedną ramkę żądania i zwraca ramkę odpowiedzi (<c>/A</c> albo <c>/E</c>) z CR.</summary>
    public string Handle(string frame)
    {
        ArgumentNullException.ThrowIfNull(frame);

        if (!PcomAsciiCodec.TryParse(frame, out var request, out var parseError))
        {
            return PcomAsciiCodec.BuildError(request.UnitId, parseError);
        }

        string data;
        PcomErrorCode error;
        try
        {
            error = request.Command switch
            {
                "ID" => Identify(out data),
                "RO" => Read(request.Data, out data),
                "WO" => Write(request.Data, out data),
                "ST" => Control(request.Data, out data),
                "DP" or "UP" => Unsupported(out data),
                "CCR" => StandardControl(PcomControlCommand.Run, request.Data, out data),
                "CCS" => StandardControl(PcomControlCommand.Stop, request.Data, out data),
                "CCI" => StandardControl(PcomControlCommand.Reset, request.Data, out data),
                var read when StandardReads.TryGetValue(read, out var readType) => ReadStandard(readType, request.Data, out data),
                var write when StandardWrites.TryGetValue(write, out var writeType) => WriteStandard(writeType, request.Data, out data),
                _ => Unknown(out data),
            };
        }
        catch (ArgumentException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Typ operandu bez magazynu w danym układzie pamięci zgłasza ArgumentException — to adres niedostępny.
            error = PcomErrorCode.AddressOutOfRange;
            data = string.Empty;
        }

        return error == PcomErrorCode.None
            ? PcomAsciiCodec.BuildResponse(request.UnitId, request.Command, data)
            : PcomAsciiCodec.BuildError(request.UnitId, error);
    }

    private PcomErrorCode Identify(out string data)
    {
        data = _plcName;
        return PcomErrorCode.None;
    }

    private static PcomErrorCode Unsupported(out string data)
    {
        data = string.Empty;
        return PcomErrorCode.Unsupported;
    }

    private static PcomErrorCode Unknown(out string data)
    {
        data = string.Empty;
        return PcomErrorCode.UnknownCommand;
    }

    private PcomErrorCode Control(string payload, out string data)
    {
        data = string.Empty;
        if (payload.Length != 2 || !PcomAsciiCodec.TryParseHex(payload, out var code) || code > (uint)PcomControlCommand.Reset)
        {
            return PcomErrorCode.BadParameters;
        }

        data = payload.ToUpperInvariant();
        ControlRequested?.Invoke(this, (PcomControlCommand)code);
        return PcomErrorCode.None;
    }

    private PcomErrorCode Read(string payload, out string data)
    {
        data = string.Empty;
        if (payload.Length != TargetLength)
        {
            return PcomErrorCode.BadParameters;
        }

        var error = ParseTarget(payload, out var type, out var start, out var count);
        return error == PcomErrorCode.None ? ReadValues(type, start, count, out data) : error;
    }

    private PcomErrorCode ReadStandard(OperandType type, string payload, out string data)
    {
        data = string.Empty;
        if (payload.Length != RangeLength)
        {
            return PcomErrorCode.BadParameters;
        }

        var error = ParseRange(type, payload, out var start, out var count);
        return error == PcomErrorCode.None ? ReadValues(type, start, count, out data) : error;
    }

    private PcomErrorCode WriteStandard(OperandType type, string payload, out string data)
    {
        data = string.Empty;
        if (payload.Length < RangeLength)
        {
            return PcomErrorCode.BadParameters;
        }

        var error = ParseRange(type, payload.AsSpan(0, RangeLength), out var start, out var count);
        return error == PcomErrorCode.None ? WriteValues(type, start, count, payload.AsSpan(RangeLength)) : error;
    }

    private PcomErrorCode StandardControl(PcomControlCommand command, string payload, out string data)
    {
        data = string.Empty;
        if (payload.Length != 0)
        {
            return PcomErrorCode.BadParameters;
        }

        ControlRequested?.Invoke(this, command);
        return PcomErrorCode.None;
    }

    private PcomErrorCode ReadValues(OperandType type, int start, int count, out string data)
    {
        var builder = new StringBuilder(count * DigitsOf(type));
        lock (_memory.SyncRoot)
        {
            for (var i = 0; i < count; i++)
            {
                var address = new OperandAddress(type, start + i);
                if (type.IsBit())
                {
                    builder.Append(_memory.GetBit(address) ? '1' : '0');
                }
                else
                {
                    builder.Append(PcomAsciiCodec.FormatHex(ReadRaw(address), DigitsOf(type)));
                }
            }
        }

        data = builder.ToString();
        return PcomErrorCode.None;
    }

    private PcomErrorCode Write(string payload, out string data)
    {
        data = string.Empty;
        if (payload.Length < TargetLength)
        {
            return PcomErrorCode.BadParameters;
        }

        var error = ParseTarget(payload[..TargetLength], out var type, out var start, out var count);
        if (error == PcomErrorCode.None)
        {
            error = WriteValues(type, start, count, payload.AsSpan(TargetLength));
        }

        if (error == PcomErrorCode.None)
        {
            data = payload[..TargetLength];
        }

        return error;
    }

    private PcomErrorCode WriteValues(OperandType type, int start, int count, ReadOnlySpan<char> values)
    {
        // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
        // Wszystkie wartości są walidowane przed pierwszym zapisem, aby błąd nie zostawił pamięci częściowo zmienionej.
        var digits = DigitsOf(type);
        if (values.Length != count * digits)
        {
            return PcomErrorCode.BadParameters;
        }

        var raw = new uint[count];
        for (var i = 0; i < count; i++)
        {
            var chunk = values.Slice(i * digits, digits);
            if (type.IsBit())
            {
                if (chunk[0] is not ('0' or '1'))
                {
                    return PcomErrorCode.BadParameters;
                }

                raw[i] = (uint)(chunk[0] - '0');
            }
            else if (!PcomAsciiCodec.TryParseHex(chunk, out raw[i]))
            {
                return PcomErrorCode.BadParameters;
            }
        }

        lock (_memory.SyncRoot)
        {
            for (var i = 0; i < count; i++)
            {
                WriteRaw(new OperandAddress(type, start + i), raw[i]);
            }
        }

        return PcomErrorCode.None;
    }

    private PcomErrorCode ParseTarget(string target, out OperandType type, out int start, out int count)
    {
        type = default;
        start = 0;
        count = 0;

        if (!PcomAsciiCodec.TryParseHex(target.AsSpan(0, 2), out var typeCode)
            || !Enum.IsDefined((OperandType)typeCode))
        {
            return PcomErrorCode.BadParameters;
        }

        type = (OperandType)typeCode;
        return ParseRange(type, target.AsSpan(2, RangeLength), out start, out count);
    }

    /// <summary>Zakres <c>AAAA NN</c>: adres początkowy (4 hex) i liczba operandów 1..255 (2 hex).</summary>
    private PcomErrorCode ParseRange(OperandType type, ReadOnlySpan<char> range, out int start, out int count)
    {
        start = 0;
        count = 0;
        if (range.Length != RangeLength
            || !PcomAsciiCodec.TryParseHex(range[..4], out var address)
            || !PcomAsciiCodec.TryParseHex(range[4..], out var length)
            || length is < 1 or > MaxCount)
        {
            return PcomErrorCode.BadParameters;
        }

        start = (int)address;
        count = (int)length;
        return start + count <= _memory.GetCapacity(type) ? PcomErrorCode.None : PcomErrorCode.AddressOutOfRange;
    }

    private static int DigitsOf(OperandType type) => type switch
    {
        _ when type.IsBit() => 1,
        OperandType.MI or OperandType.SI or OperandType.XI => 4,
        _ => 8,
    };

    private uint ReadRaw(OperandAddress address) => address.Type switch
    {
        OperandType.MF => BitConverter.SingleToUInt32Bits(_memory.GetFloat(address.Index)),
        OperandType.MI or OperandType.SI or OperandType.XI => (ushort)_memory.GetNumber(address),
        _ => unchecked((uint)_memory.GetNumber(address)),
    };

    private void WriteRaw(OperandAddress address, uint raw)
    {
        switch (address.Type)
        {
            case var bit when bit.IsBit():
                _memory.SetBit(address, raw != 0);
                break;
            case OperandType.MF:
                _memory.SetFloat(address.Index, BitConverter.UInt32BitsToSingle(raw));
                break;
            case OperandType.MI or OperandType.SI or OperandType.XI:
                _memory.SetNumber(address, unchecked((short)(ushort)raw));
                break;
            case OperandType.ML or OperandType.SL or OperandType.XL or OperandType.T or OperandType.C:
                _memory.SetNumber(address, unchecked((int)raw));
                break;
            default:
                _memory.SetNumber(address, raw);
                break;
        }
    }
}
