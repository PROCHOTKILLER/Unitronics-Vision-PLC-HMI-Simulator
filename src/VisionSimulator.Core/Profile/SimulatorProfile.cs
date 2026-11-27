namespace VisionSimulator.Core.Profile;

/// <summary>
/// Profil projektu symulatora (plik JSON): model i układ pamięci sterownika, wartości początkowe oraz prosta
/// symulacja obiektu (sprzężenia zwrotne wyjść na wejścia i wielkości analogowe z inercją I rzędu).
/// </summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Zachowania specyficzne dla konkretnej maszyny opisuje wyłącznie profil — silnik i interpreter LAD nie zawierają
/// warunków pod żaden projekt. Adresy operandów zapisuje się tekstowo, np. <c>"MI 10"</c>, <c>"O 0"</c>.
/// </remarks>
public sealed record SimulatorProfile
{
    /// <summary>Nazwa profilu widoczna w dzienniku.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Model bazowy układu pamięci (obecnie <c>V350</c>).</summary>
    public string Model { get; init; } = "V350";

    /// <summary>Opcjonalne nadpisanie liczności operandów modelu bazowego.</summary>
    public MemoryLayoutOverrides? Memory { get; init; }

    /// <summary>Wartości zapisywane w pierwszym skanie po zimnym starcie (RESET), przed drabinką.</summary>
    public IReadOnlyList<InitialValueDefinition> InitialValues { get; init; } = [];

    /// <summary>Sprzężenia bitowe z opóźnieniem, np. wyjście stycznika → wejście potwierdzenia.</summary>
    public IReadOnlyList<DigitalFeedbackDefinition> DigitalFeedback { get; init; } = [];

    /// <summary>Wielkości analogowe dążące do wartości zadanej z inercją I rzędu (temperatura, ciśnienie, AO → AI).</summary>
    public IReadOnlyList<AnalogFollowerDefinition> AnalogFollowers { get; init; } = [];
}

/// <summary>Nadpisania liczności operandów; pole puste = wartość modelu bazowego.</summary>
public sealed record MemoryLayoutOverrides
{
    public int? Inputs { get; init; }

    public int? Outputs { get; init; }

    public int? MemoryBits { get; init; }

    public int? SystemBits { get; init; }

    public int? FastBits { get; init; }

    public int? MemoryIntegers { get; init; }

    public int? SystemIntegers { get; init; }

    public int? FastIntegers { get; init; }

    public int? MemoryLongs { get; init; }

    public int? SystemLongs { get; init; }

    public int? FastLongs { get; init; }

    public int? DoubleWords { get; init; }

    public int? SystemDoubleWords { get; init; }

    public int? FastDoubleWords { get; init; }

    public int? MemoryFloats { get; init; }

    public int? Timers { get; init; }

    public int? Counters { get; init; }
}

/// <summary>Wartość początkowa operandu (bit: 0/1, rejestr: liczba, MF: liczba zmiennoprzecinkowa).</summary>
public sealed record InitialValueDefinition
{
    public string Address { get; init; } = string.Empty;

    public double Value { get; init; }
}

/// <summary>Bit docelowy podąża za bitem źródłowym po upływie opóźnienia załączenia / wyłączenia.</summary>
public sealed record DigitalFeedbackDefinition
{
    public string Source { get; init; } = string.Empty;

    public string Target { get; init; } = string.Empty;

    public double OnDelayMs { get; init; }

    public double OffDelayMs { get; init; }

    /// <summary>Negacja: bit docelowy = NOT źródło (np. styk NC krańcówki).</summary>
    public bool Invert { get; init; }
}

/// <summary>
/// Rejestr docelowy dąży do wartości zadanej: z bitu (<see cref="SourceBit"/>: <see cref="OnValue"/> / <see cref="OffValue"/>)
/// albo z rejestru (<see cref="SourceRegister"/>: <c>Gain · źródło + Offset</c>). Dokładnie jedno źródło jest wymagane.
/// </summary>
public sealed record AnalogFollowerDefinition
{
    public string Target { get; init; } = string.Empty;

    public string? SourceBit { get; init; }

    public double OnValue { get; init; }

    public double OffValue { get; init; }

    public string? SourceRegister { get; init; }

    public double Gain { get; init; } = 1.0;

    public double Offset { get; init; }

    /// <summary>Stała czasowa inercji w ms; 0 = wartość zadana od razu.</summary>
    public double TimeConstantMs { get; init; }

    public double? Min { get; init; }

    public double? Max { get; init; }
}
