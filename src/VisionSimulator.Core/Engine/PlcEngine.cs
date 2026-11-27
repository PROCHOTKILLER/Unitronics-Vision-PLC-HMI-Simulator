using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Engine;

public enum PlcRunState
{
    Stop,
    Run,
}

/// <summary>Cykl skanu sterownika: bity systemowe, a potem etapy w kolejności rejestracji.</summary>
public sealed class PlcEngine
{
    private static readonly OperandAddress AlwaysOff = new(OperandType.SB, 0);
    private static readonly OperandAddress AlwaysOn = new(OperandType.SB, 1);
    private static readonly OperandAddress PowerUpBit = new(OperandType.SB, 2);

    private readonly IReadOnlyList<IScanStep> _steps;
    private bool _firstScanPending = true;

    public PlcEngine(PlcMemory memory, IEnumerable<IScanStep> steps)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(steps);
        Memory = memory;
        _steps = steps.ToList();
    }

    public PlcMemory Memory { get; }

    public PlcRunState State { get; private set; } = PlcRunState.Stop;

    public long ScanCount { get; private set; }

    public event EventHandler? ScanCompleted;

    public void Run() => State = PlcRunState.Run;

    public void Stop() => State = PlcRunState.Stop;

    /// <summary>Zimny start: czyści pamięć i ponownie ustawia bit pierwszego skanu (SB 2).</summary>
    public void Reset()
    {
        Stop();
        Memory.Clear();
        ScanCount = 0;
        _firstScanPending = true;
    }

    public void Scan(TimeSpan elapsed)
    {
        if (State != PlcRunState.Run)
        {
            return;
        }

        // [INTENCJA I POWÓD DZIAŁANIA]
        // SB 0 = zawsze 0, SB 1 = zawsze 1, SB 2 = tylko pierwszy skan po starcie (inicjalizacja w LAD).
        Memory.SetBit(AlwaysOff, false);
        Memory.SetBit(AlwaysOn, true);
        Memory.SetBit(PowerUpBit, _firstScanPending);

        var context = new ScanContext(elapsed, ScanCount, _firstScanPending);
        foreach (var step in _steps)
        {
            step.Execute(Memory, context);
        }

        _firstScanPending = false;
        ScanCount++;
        ScanCompleted?.Invoke(this, EventArgs.Empty);
    }
}
