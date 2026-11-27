namespace VisionSimulator.Core.Pcom;

/// <summary>Żądanie sterowania pracą symulowanego sterownika, zgłaszane przez polecenie ST.</summary>
public enum PcomControlCommand
{
    Stop = 0,
    Run = 1,
    Reset = 2,
}
