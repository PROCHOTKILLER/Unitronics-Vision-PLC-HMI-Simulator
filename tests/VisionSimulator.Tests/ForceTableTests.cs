using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Hmi;
using VisionSimulator.Core.Ladder;
using VisionSimulator.Core.Memory;
using static VisionSimulator.Tests.LadderTestKit;

namespace VisionSimulator.Tests;

public sealed class ForceTableTests
{
    private static readonly OperandAddress Output0 = new(OperandType.O, 0);
    private static readonly OperandAddress Input0 = new(OperandType.I, 0);

    [Fact]
    public void Force_RejectsNonBitOperand()
    {
        var table = new ForceTable();

        Assert.Throws<ArgumentException>(() => table.Force(new OperandAddress(OperandType.MI, 0), true));
    }

    [Fact]
    public void ForceReleaseAndSnapshot_TrackEntriesAndRaiseChanged()
    {
        var table = new ForceTable();
        var changes = 0;
        table.Changed += (_, _) => changes++;

        table.Force(Input0, true);
        var snapshot = table.Snapshot();
        table.Force(Input0, false);

        Assert.True(snapshot[Input0]);
        Assert.False(table.Snapshot()[Input0]);
        Assert.True(table.Release(Input0));
        Assert.False(table.Release(Input0));
        Assert.Empty(table.Snapshot());
        Assert.Equal(3, changes);
    }

    [Fact]
    public void ReleaseAll_ClearsEveryForce()
    {
        var table = new ForceTable();
        table.Force(Input0, true);
        table.Force(Output0, true);

        table.ReleaseAll();

        Assert.Empty(table.Snapshot());
    }

    [Fact]
    public void ForcedInput_IsVisibleToProgram()
    {
        var table = new ForceTable();
        var hmi = new HmiState();
        var interpreter = new LadderInterpreter(hmi);
        interpreter.Load(MainProject(Rung(Linear(1, 200, Mem(OperandType.I, 0)), Linear(2, 204, Mem(OperandType.MB, 1)))));
        var memory = new PlcMemory(PlcMemoryLayout.V350);
        var engine = new PlcEngine(memory, [table.BeforeLogic, interpreter, table.AfterLogic]);
        engine.Run();
        table.Force(Input0, true);

        engine.Scan(TimeSpan.FromMilliseconds(10));

        Assert.True(memory.GetBit(new OperandAddress(OperandType.MB, 1)));
    }

    [Fact]
    public void ForcedOutput_IsAppliedAfterLogicButNotBefore()
    {
        var table = new ForceTable();
        var memory = new PlcMemory(PlcMemoryLayout.V350);
        var seenByProgram = new ProbeStep(Output0);
        var engine = new PlcEngine(memory, [table.BeforeLogic, seenByProgram, table.AfterLogic]);
        engine.Run();
        table.Force(Output0, true);

        engine.Scan(TimeSpan.FromMilliseconds(10));

        Assert.False(seenByProgram.Observed);
        Assert.True(memory.GetBit(Output0));
    }

    [Fact]
    public void ForceOutsideLayout_IsIgnoredWithoutFailingScan()
    {
        var table = new ForceTable();
        var memory = new PlcMemory(PlcMemoryLayout.V350);
        var engine = new PlcEngine(memory, [table.BeforeLogic, table.AfterLogic]);
        engine.Run();
        table.Force(new OperandAddress(OperandType.MB, 100_000), true);

        var exception = Record.Exception(() => engine.Scan(TimeSpan.FromMilliseconds(10)));

        Assert.Null(exception);
    }

    private sealed class ProbeStep(OperandAddress address) : IScanStep
    {
        public bool Observed { get; private set; }

        public void Execute(PlcMemory memory, ScanContext context) => Observed = memory.GetBit(address);
    }
}
