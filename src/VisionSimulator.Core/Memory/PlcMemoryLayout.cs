namespace VisionSimulator.Core.Memory;

/// <summary>Liczba operandów każdego typu dla danego modelu sterownika.</summary>
public sealed record PlcMemoryLayout(
    string Model,
    int Inputs,
    int Outputs,
    int MemoryBits,
    int SystemBits,
    int FastBits,
    int MemoryIntegers,
    int SystemIntegers,
    int FastIntegers,
    int MemoryLongs,
    int SystemLongs,
    int FastLongs,
    int DoubleWords,
    int SystemDoubleWords,
    int FastDoubleWords,
    int MemoryFloats,
    int Timers,
    int Counters)
{
    // [ZADANIA DO WYKONANIA (TODO)]
    // Liczności zweryfikować z dokumentacją techniczną V350-35-RA22 (Desktop\Materiały informacyjne\Unitronics)
    // i z oknem Hardware Configuration w VisiLogic przed implementacją PCOM (zakresy odczytu/zapisu).
    public static PlcMemoryLayout V350 { get; } = new(
        Model: "V350",
        Inputs: 544,
        Outputs: 544,
        MemoryBits: 8192,
        SystemBits: 512,
        FastBits: 1024,
        MemoryIntegers: 4096,
        SystemIntegers: 512,
        FastIntegers: 512,
        MemoryLongs: 512,
        SystemLongs: 56,
        FastLongs: 256,
        DoubleWords: 256,
        SystemDoubleWords: 64,
        FastDoubleWords: 64,
        MemoryFloats: 24,
        Timers: 384,
        Counters: 32);
}
