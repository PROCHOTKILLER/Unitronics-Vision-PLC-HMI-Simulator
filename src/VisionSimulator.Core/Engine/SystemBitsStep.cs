using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Engine;

/// <summary>
/// Impulsy zegarowe sterownika: SB 3 (okres 1 s) i SB 7 (okres 100 ms), wypełnienie 50%.
/// Etap musi być zarejestrowany przed interpreterem LAD, aby drabinka widziała bieżący stan bitów.
/// </summary>
/// <remarks>
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Stan akumulatorów czasu jest zerowany w pierwszym skanie (po zimnym starcie). Okresy SB 3 i SB 7 pochodzą
/// z zachowania dotychczasowego symulatora i nie są zweryfikowane ze specyfikacją V350.
/// </remarks>
public sealed class SystemBitsStep : IScanStep
{
    private static readonly OperandAddress Clock1Second = new(OperandType.SB, 3);
    private static readonly OperandAddress Clock100Milliseconds = new(OperandType.SB, 7);

    private readonly HalfPeriodClock _oneSecond = new(TimeSpan.FromMilliseconds(500));
    private readonly HalfPeriodClock _hundredMilliseconds = new(TimeSpan.FromMilliseconds(50));

    public void Execute(PlcMemory memory, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(context);

        if (context.IsFirstScan)
        {
            _oneSecond.Reset();
            _hundredMilliseconds.Reset();
        }

        if (_oneSecond.Advance(context.Elapsed))
        {
            memory.SetBit(Clock1Second, !memory.GetBit(Clock1Second));
        }

        if (_hundredMilliseconds.Advance(context.Elapsed))
        {
            memory.SetBit(Clock100Milliseconds, !memory.GetBit(Clock100Milliseconds));
        }
    }

    /// <summary>Akumulator czasu, który informuje, czy w danym kroku bit powinien zmienić stan.</summary>
    private sealed class HalfPeriodClock(TimeSpan halfPeriod)
    {
        private TimeSpan _accumulated;

        public void Reset() => _accumulated = TimeSpan.Zero;

        // [WYJAŚNIENIE ALGORYTMU I ZAAWANSOWANEJ MATEMATYKI]
        // Liczba pełnych półokresów w kroku decyduje o parzystości przełączeń: nieparzysta liczba = zmiana stanu bitu,
        // parzysta = bit wraca do tej samej wartości, więc wolny skan nie gubi fazy zegara.
        public bool Advance(TimeSpan elapsed)
        {
            _accumulated += elapsed;
            var halves = (long)(_accumulated / halfPeriod);
            _accumulated -= halfPeriod * halves;
            return (halves & 1) == 1;
        }
    }
}
