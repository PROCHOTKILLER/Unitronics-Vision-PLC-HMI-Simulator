namespace VisionSimulator.Core.Memory;

/// <summary>Obraz pamięci sterownika: bity, rejestry całkowite i zmiennoprzecinkowe.</summary>
public sealed class PlcMemory
{
    private readonly Dictionary<OperandType, bool[]> _bits;
    private readonly Dictionary<OperandType, long[]> _numbers;
    private readonly float[] _floats;
    private readonly PlcTimer[] _timers;
    private readonly PlcCounter[] _counters;

    public PlcMemory(PlcMemoryLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Layout = layout;

        _bits = new()
        {
            [OperandType.I] = new bool[layout.Inputs],
            [OperandType.O] = new bool[layout.Outputs],
            [OperandType.MB] = new bool[layout.MemoryBits],
            [OperandType.SB] = new bool[layout.SystemBits],
            [OperandType.XB] = new bool[layout.FastBits],
        };

        // [ARCHITEKTURA I ADR]
        // Wszystkie rejestry całkowite trzymamy jako long, a zakres typu (16/32 bit, ze znakiem lub bez)
        // egzekwuje Normalize(). Jedna ścieżka odczytu/zapisu upraszcza interpreter LAD i PCOM.
        _numbers = new()
        {
            [OperandType.MI] = new long[layout.MemoryIntegers],
            [OperandType.SI] = new long[layout.SystemIntegers],
            [OperandType.XI] = new long[layout.FastIntegers],
            [OperandType.ML] = new long[layout.MemoryLongs],
            [OperandType.SL] = new long[layout.SystemLongs],
            [OperandType.XL] = new long[layout.FastLongs],
            [OperandType.DW] = new long[layout.DoubleWords],
            [OperandType.SDW] = new long[layout.SystemDoubleWords],
            [OperandType.XDW] = new long[layout.FastDoubleWords],
        };

        _floats = new float[layout.MemoryFloats];

        _timers = new PlcTimer[layout.Timers];
        for (var i = 0; i < _timers.Length; i++)
        {
            _timers[i] = new PlcTimer();
        }

        _counters = new PlcCounter[layout.Counters];
        for (var i = 0; i < _counters.Length; i++)
        {
            _counters[i] = new PlcCounter();
        }
    }

    public PlcMemoryLayout Layout { get; }

    // [ARCHITEKTURA I ADR]
    // Jedna blokada dla wątku skanu, UI i serwera PCOM. PlcMemory sama jej nie zakłada przy każdym odczycie
    // (koszt i ryzyko zakleszczeń) — odpowiada za nią wywołujący, który zmienia lub odczytuje spójny fragment pamięci.
    /// <summary>Wspólna blokada pamięci: każdy dostęp spoza wątku skanu wymaga <c>lock (memory.SyncRoot)</c>.</summary>
    public Lock SyncRoot { get; } = new();

    public bool GetBit(OperandAddress address) => address.Type switch
    {
        OperandType.T => GetTimer(address.Index).Output,
        OperandType.C => GetCounter(address.Index).Output,
        _ => BitArray(address)[address.Index],
    };

    public void SetBit(OperandAddress address, bool value)
    {
        switch (address.Type)
        {
            case OperandType.T:
                GetTimer(address.Index).Output = value;
                break;
            case OperandType.C:
                GetCounter(address.Index).Output = value;
                break;
            default:
                BitArray(address)[address.Index] = value;
                break;
        }
    }

    public long GetNumber(OperandAddress address) => address.Type switch
    {
        OperandType.T => GetTimer(address.Index).Current,
        OperandType.C => GetCounter(address.Index).Current,
        _ => NumberArray(address)[address.Index],
    };

    public void SetNumber(OperandAddress address, long value)
    {
        switch (address.Type)
        {
            case OperandType.T:
                GetTimer(address.Index).Current = value;
                break;
            case OperandType.C:
                GetCounter(address.Index).Current = value;
                break;
            default:
                NumberArray(address)[address.Index] = Normalize(address.Type, value);
                break;
        }
    }

    public PlcTimer GetTimer(int index) => _timers[CheckIndex(index, _timers.Length, OperandType.T)];

    public PlcCounter GetCounter(int index) => _counters[CheckIndex(index, _counters.Length, OperandType.C)];

    /// <summary>Liczba operandów danego typu dostępnych w tym układzie pamięci (0, gdy typ nieobsługiwany).</summary>
    public int GetCapacity(OperandType type)
    {
        switch (type)
        {
            case OperandType.MF:
                return _floats.Length;
            case OperandType.T:
                return _timers.Length;
            case OperandType.C:
                return _counters.Length;
        }

        if (_bits.TryGetValue(type, out var bits))
        {
            return bits.Length;
        }

        return _numbers.TryGetValue(type, out var numbers) ? numbers.Length : 0;
    }

    /// <summary>Czy adres mieści się w zakresie pamięci (bez rzucania wyjątku).</summary>
    public bool Contains(OperandAddress address) => (uint)address.Index < (uint)GetCapacity(address.Type);

    public float GetFloat(int index) => _floats[CheckIndex(index, _floats.Length, OperandType.MF)];

    public void SetFloat(int index, float value) => _floats[CheckIndex(index, _floats.Length, OperandType.MF)] = value;

    public void Clear()
    {
        foreach (var array in _bits.Values)
        {
            Array.Clear(array);
        }

        foreach (var array in _numbers.Values)
        {
            Array.Clear(array);
        }

        Array.Clear(_floats);

        foreach (var timer in _timers)
        {
            timer.Clear();
        }

        foreach (var counter in _counters)
        {
            counter.Clear();
        }
    }

    private bool[] BitArray(OperandAddress address)
    {
        if (!_bits.TryGetValue(address.Type, out var array))
        {
            throw new ArgumentException($"{address.Type} nie jest operandem bitowym.", nameof(address));
        }

        CheckIndex(address.Index, array.Length, address.Type);
        return array;
    }

    private long[] NumberArray(OperandAddress address)
    {
        if (!_numbers.TryGetValue(address.Type, out var array))
        {
            throw new ArgumentException($"{address.Type} nie jest rejestrem całkowitym.", nameof(address));
        }

        CheckIndex(address.Index, array.Length, address.Type);
        return array;
    }

    private static int CheckIndex(int index, int length, OperandType type) =>
        (uint)index < (uint)length
            ? index
            : throw new ArgumentOutOfRangeException(nameof(index), index, $"{type}: dozwolony zakres 0..{length - 1}.");

    private static long Normalize(OperandType type, long value) => type switch
    {
        OperandType.MI or OperandType.SI or OperandType.XI => (short)value,
        OperandType.ML or OperandType.SL or OperandType.XL => (int)value,
        OperandType.DW or OperandType.SDW or OperandType.XDW => (uint)value,
        _ => value,
    };
}
