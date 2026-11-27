using VisionSimulator.Core.Ladder;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;
using static VisionSimulator.Tests.LadderTestKit;

namespace VisionSimulator.Tests;

public sealed class LadderTraceTests
{
    private static ElementOperand Mb(int index) => Mem(OperandType.MB, index);

    private static LadderSubroutineState Capture(LadderHarness h, int subroutine = 0) =>
        h.Interpreter.CaptureState(h.Memory, h.Interpreter.GetLayout().Subroutines[subroutine])!;

    [Fact]
    public void Grid_TracesPowerThroughParallelBranch()
    {
        var rung = Rung(
            At(1, 200, 0, 0, 6, 0, Mb(0)),
            At(2, 200, 0, 1, 6, 0, Mb(1)),
            At(3, 101, 6, 0, 0, 1),
            At(4, 204, 6, 0, 6, 0, Mb(2)));
        var h = new LadderHarness(MainProject(rung));
        h.SetBit(OperandType.MB, 1, true);

        h.Scan();
        var state = Capture(h).Rungs[0];

        Assert.True(state.Executed);
        var (upper, lower, wire, coil) = (state.Elements[0], state.Elements[1], state.Elements[2], state.Elements[3]);
        Assert.Equal((true, false, false), (upper.Input, upper.Conducts, upper.Output));
        Assert.Equal((true, true, true), (lower.Input, lower.Conducts, lower.Output));
        Assert.True(wire.Input);
        Assert.True(coil.Input);
        Assert.Equal("0", upper.Values[0]);
        Assert.Equal("1", lower.Values[0]);
        Assert.Equal("1", coil.Values[0]);
    }

    [Fact]
    public void Series_TracesPowerUntilOpenContact()
    {
        var rung = Rung(
            Linear(1, 200, Mb(0)),
            Linear(2, 200, Mb(1)),
            Linear(3, 204, Mb(2)));
        var h = new LadderHarness(MainProject(rung));
        h.SetBit(OperandType.MB, 0, true);

        h.Scan();
        var state = Capture(h).Rungs[0];

        Assert.Equal((true, true, true), (state.Elements[0].Input, state.Elements[0].Conducts, state.Elements[0].Output));
        Assert.Equal((true, false, false), (state.Elements[1].Input, state.Elements[1].Conducts, state.Elements[1].Output));
        Assert.False(state.Elements[2].Input);
        Assert.False(h.Bit(OperandType.MB, 2));
    }

    [Fact]
    public void Capture_MarksRungsNotExecutedBeforeScanAndAfterReturn()
    {
        var h = new LadderHarness(MainProject(
            Rung(Linear(1, InstructionRegistry.ReturnOpcode)),
            Rung(Linear(2, 200, Mb(0)), Linear(3, 204, Mb(1)))));

        Assert.All(Capture(h).Rungs, r => Assert.False(r.Executed));

        h.Scan();
        var rungs = Capture(h).Rungs;
        Assert.True(rungs[0].Executed);
        Assert.False(rungs[1].Executed);
    }

    [Fact]
    public void Layout_DescribesShapesPositionsAndOperands()
    {
        var project = Project(
            [MainRoutine(Rung(
                Linear(1, 200, Mb(0)),
                Linear(2, 302, Mem(OperandType.MI, 4), Const(25)),
                Linear(3, 204, Mb(2)),
                Linear(4, 9999)))],
            [new OperandInfo(1, 0, new OperandAddress(OperandType.MB, 0), "Start cyklu", string.Empty, false)]);
        var h = new LadderHarness(project);

        var subroutine = Assert.Single(h.Interpreter.GetLayout().Subroutines);
        Assert.True(subroutine.IsEntryPoint);
        var elements = subroutine.Rungs[0].Elements;

        Assert.Equal(
            new[] { LadderShape.Contact, LadderShape.Compare, LadderShape.Coil, LadderShape.Block },
            elements.Select(e => e.Shape).ToArray());
        Assert.Equal(0, elements[0].X);
        Assert.Equal(elements[0].Width, elements[1].X);
        Assert.Equal("Start cyklu", elements[0].Operands[0].Description);
        Assert.Equal("MI 4", elements[1].Operands[0].Text);
        Assert.Equal("25", elements[1].Operands[1].Text);
        Assert.Null(elements[1].Operands[1].Address);
        Assert.Equal("?9999", elements[3].Mnemonic);
    }

    [Fact]
    public void Capture_RejectsLayoutFromPreviousLoad()
    {
        var project = MainProject(Rung(Linear(1, 200, Mb(0)), Linear(2, 204, Mb(1))));
        var h = new LadderHarness(project);
        var stale = h.Interpreter.GetLayout().Subroutines[0];

        h.Interpreter.Load(project);

        Assert.Null(h.Interpreter.CaptureState(h.Memory, stale));
        Assert.NotNull(Capture(h));
    }

    [Fact]
    public void RungState_ContentEqualsComparesValues()
    {
        var a = new LadderRungState(true, [new LadderElementState(true, true, true, ["1"])]);
        var b = new LadderRungState(true, [new LadderElementState(true, true, true, ["1"])]);
        var c = new LadderRungState(true, [new LadderElementState(true, true, true, ["2"])]);

        Assert.True(a.ContentEquals(b));
        Assert.False(a.ContentEquals(c));
        Assert.False(a.ContentEquals(null));
    }
}
