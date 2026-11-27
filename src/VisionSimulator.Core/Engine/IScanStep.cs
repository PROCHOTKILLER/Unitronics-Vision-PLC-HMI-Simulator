using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Engine;

/// <summary>Jeden etap cyklu skanu (timery, drabinka, symulacja I/O).</summary>
public interface IScanStep
{
    void Execute(PlcMemory memory, ScanContext context);
}

/// <summary>Dane bieżącego cyklu skanu przekazywane do każdego etapu.</summary>
public sealed record ScanContext(TimeSpan Elapsed, long ScanNumber, bool IsFirstScan);
