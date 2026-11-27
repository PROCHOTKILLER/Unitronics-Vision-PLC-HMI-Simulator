using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.Tests;

public sealed class PlcEngineTests
{
    private static readonly OperandAddress Sb1 = new(OperandType.SB, 1);
    private static readonly OperandAddress Sb2 = new(OperandType.SB, 2);

    [Fact]
    public void Scan_WhenStopped_DoesNothing()
    {
        var engine = new PlcEngine(new PlcMemory(PlcMemoryLayout.V350), []);

        engine.Scan(TimeSpan.FromMilliseconds(10));

        Assert.Equal(0, engine.ScanCount);
        Assert.False(engine.Memory.GetBit(Sb1));
    }

    [Fact]
    public void Scan_SetsSystemBits_AndFirstScanOnlyOnce()
    {
        var step = new RecordingStep();
        var engine = new PlcEngine(new PlcMemory(PlcMemoryLayout.V350), [step]);
        engine.Run();

        engine.Scan(TimeSpan.FromMilliseconds(10));
        Assert.True(engine.Memory.GetBit(Sb1));
        Assert.True(engine.Memory.GetBit(Sb2));

        engine.Scan(TimeSpan.FromMilliseconds(10));
        Assert.False(engine.Memory.GetBit(Sb2));

        Assert.Equal([true, false], step.FirstScanFlags);
        Assert.Equal(2, engine.ScanCount);
    }

    [Fact]
    public void Reset_RestoresFirstScan()
    {
        var engine = new PlcEngine(new PlcMemory(PlcMemoryLayout.V350), []);
        engine.Run();
        engine.Scan(TimeSpan.Zero);

        engine.Reset();
        engine.Run();
        engine.Scan(TimeSpan.Zero);

        Assert.True(engine.Memory.GetBit(Sb2));
        Assert.Equal(1, engine.ScanCount);
    }

    private sealed class RecordingStep : IScanStep
    {
        public List<bool> FirstScanFlags { get; } = [];

        public void Execute(PlcMemory memory, ScanContext context) => FirstScanFlags.Add(context.IsFirstScan);
    }
}
