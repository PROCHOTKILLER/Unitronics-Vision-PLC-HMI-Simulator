using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;
using static VisionSimulator.Tests.LadderTestKit;

namespace VisionSimulator.Tests;

public sealed class LadderInterpreterTests
{
    private static ElementOperand Mb(int index) => Mem(OperandType.MB, index);

    private static ElementOperand Mi(int index) => Mem(OperandType.MI, index);

    [Fact]
    public void NormallyOpenContact_DrivesCoil()
    {
        var h = new LadderHarness(MainProject(Rung(Linear(1, 200, Mb(0)), Linear(2, 204, Mb(1)))));

        h.Scan();
        Assert.False(h.Bit(OperandType.MB, 1));

        h.SetBit(OperandType.MB, 0, true);
        h.Scan();
        Assert.True(h.Bit(OperandType.MB, 1));
    }

    [Fact]
    public void NormallyClosedContact_ConductsWhenOperandIsOff()
    {
        var h = new LadderHarness(MainProject(Rung(Linear(1, 201, Mb(0)), Linear(2, 204, Mb(1)))));

        h.Scan();
        Assert.True(h.Bit(OperandType.MB, 1));

        h.SetBit(OperandType.MB, 0, true);
        h.Scan();
        Assert.False(h.Bit(OperandType.MB, 1));
    }

    [Fact]
    public void PositiveEdgeContact_PulsesOnlyOnRisingEdge()
    {
        var h = new LadderHarness(MainProject(Rung(Linear(1, 202, Mb(0)), Linear(2, 204, Mb(1)))));

        h.SetBit(OperandType.MB, 0, true);
        h.Scan();
        Assert.True(h.Bit(OperandType.MB, 1));

        h.Scan();
        Assert.False(h.Bit(OperandType.MB, 1));
    }

    [Fact]
    public void NegativeEdgeContact_PulsesOnlyOnFallingEdge()
    {
        var h = new LadderHarness(MainProject(Rung(Linear(1, 203, Mb(0)), Linear(2, 204, Mb(1)))));

        h.SetBit(OperandType.MB, 0, true);
        h.Scan();
        Assert.False(h.Bit(OperandType.MB, 1));

        h.SetBit(OperandType.MB, 0, false);
        h.Scan();
        Assert.True(h.Bit(OperandType.MB, 1));

        h.Scan();
        Assert.False(h.Bit(OperandType.MB, 1));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void ParallelBranch_ActsAsOr(bool a, bool b, bool expected)
    {
        // Styk A (wiersz 0) i styk B (wiersz 1) łączy pionowy przewód 101 w kolumnie 6, za nim cewka.
        var rung = Rung(
            At(1, 200, 0, 0, 6, 0, Mb(0)),
            At(2, 200, 0, 1, 6, 0, Mb(1)),
            At(3, 101, 6, 0, 0, 1),
            At(4, 204, 6, 0, 6, 0, Mb(2)));
        var h = new LadderHarness(MainProject(rung));
        h.SetBit(OperandType.MB, 0, a);
        h.SetBit(OperandType.MB, 1, b);

        h.Scan();

        Assert.Equal(expected, h.Bit(OperandType.MB, 2));
    }

    [Fact]
    public void SeriesContacts_InGridMode_ActAsAnd()
    {
        var rung = Rung(
            At(1, 200, 0, 0, 6, 0, Mb(0)),
            At(2, 200, 6, 0, 6, 0, Mb(1)),
            At(3, 204, 12, 0, 6, 0, Mb(2)));
        var h = new LadderHarness(MainProject(rung));

        h.SetBit(OperandType.MB, 0, true);
        h.Scan();
        Assert.False(h.Bit(OperandType.MB, 2));

        h.SetBit(OperandType.MB, 1, true);
        h.Scan();
        Assert.True(h.Bit(OperandType.MB, 2));
    }

    [Fact]
    public void CompareAndMove_CopiesConstantOnlyWhenConditionHolds()
    {
        var h = new LadderHarness(MainProject(Rung(Linear(1, 300, Mi(0), Const(5)), Linear(2, 900, Const(42), Mi(1)))));

        h.SetNumber(OperandType.MI, 0, 5);
        h.Scan();
        Assert.Equal(0, h.Number(OperandType.MI, 1));

        h.SetNumber(OperandType.MI, 0, 6);
        h.Scan();
        Assert.Equal(42, h.Number(OperandType.MI, 1));
    }

    [Fact]
    public void Arithmetic_ComputesAllBinaryOperations()
    {
        var h = new LadderHarness(MainProject(
            Rung(Linear(1, 405, Mi(0), Mi(1), Mi(10))),
            Rung(Linear(2, 400, Mi(0), Mi(1), Mi(11))),
            Rung(Linear(3, 406, Mi(0), Mi(1), Mi(12))),
            Rung(Linear(4, 401, Mi(0), Mi(1), Mi(13))),
            Rung(Linear(5, 407, Mi(0), Mi(1), Mi(14)))));
        h.SetNumber(OperandType.MI, 0, 7);
        h.SetNumber(OperandType.MI, 1, 2);

        h.Scan();

        Assert.Equal(9, h.Number(OperandType.MI, 10));
        Assert.Equal(5, h.Number(OperandType.MI, 11));
        Assert.Equal(14, h.Number(OperandType.MI, 12));
        Assert.Equal(3, h.Number(OperandType.MI, 13));
        Assert.Equal(1, h.Number(OperandType.MI, 14));
    }

    [Fact]
    public void Division_ByZero_KeepsDestinationAndCountsAnomaly()
    {
        var h = new LadderHarness(MainProject(Rung(Linear(1, 401, Mi(0), Mi(1), Mi(2)))));
        h.SetNumber(OperandType.MI, 0, 7);
        h.SetNumber(OperandType.MI, 2, 99);

        h.Scan();

        Assert.Equal(99, h.Number(OperandType.MI, 2));
        Assert.True(h.Interpreter.AnomalyCount > 0);
        Assert.NotNull(h.Interpreter.LastAnomaly);

        h.SetNumber(OperandType.MI, 1, 2);
        h.Scan();
        Assert.Equal(3, h.Number(OperandType.MI, 2));
    }

    [Fact]
    public void SetAndReset_LatchBit()
    {
        var h = new LadderHarness(MainProject(
            Rung(Linear(1, 200, Mb(0)), Linear(2, 206, Mb(5))),
            Rung(Linear(3, 200, Mb(1)), Linear(4, 207, Mb(5)))));

        h.SetBit(OperandType.MB, 0, true);
        h.Scan();
        h.SetBit(OperandType.MB, 0, false);
        h.Scan();
        Assert.True(h.Bit(OperandType.MB, 5));

        h.SetBit(OperandType.MB, 1, true);
        h.Scan();
        Assert.False(h.Bit(OperandType.MB, 5));
    }

    [Fact]
    public void Timer_CountsDownAndSetsBitAfterPreset()
    {
        var h = new LadderHarness(MainProject(Rung(Linear(1, 200, Mb(0)), Linear(2, 204, Mem(OperandType.T, 0)))));
        h.Memory.GetTimer(0).LoadPreset(100);
        h.SetBit(OperandType.MB, 0, true);

        for (var i = 0; i < 9; i++)
        {
            h.Scan(100);
        }

        Assert.False(h.Bit(OperandType.T, 0));
        Assert.Equal(10, h.Number(OperandType.T, 0));

        h.Scan(100);
        Assert.True(h.Bit(OperandType.T, 0));

        h.SetBit(OperandType.MB, 0, false);
        h.Scan(100);
        Assert.False(h.Bit(OperandType.T, 0));
        Assert.Equal(100, h.Number(OperandType.T, 0));
    }

    [Fact]
    public void Counter_CountsRisingEdgesAndResets()
    {
        var h = new LadderHarness(MainProject(
            Rung(Linear(1, 200, Mb(0)), Linear(2, 204, Mem(OperandType.C, 0))),
            Rung(Linear(3, 200, Mb(1)), Linear(4, 207, Mem(OperandType.C, 0)))));
        h.Memory.GetCounter(0).Preset = 2;

        foreach (var level in new[] { true, true, false, true })
        {
            h.SetBit(OperandType.MB, 0, level);
            h.Scan();
        }

        Assert.Equal(2, h.Number(OperandType.C, 0));
        Assert.True(h.Bit(OperandType.C, 0));

        h.SetBit(OperandType.MB, 1, true);
        h.Scan();
        Assert.Equal(0, h.Number(OperandType.C, 0));
        Assert.False(h.Bit(OperandType.C, 0));
    }

    [Fact]
    public void Call_RunsSubroutineOnlyWhenPowered_AndReturnStopsIt()
    {
        var main = MainRoutine(Rung(Linear(1, 200, Mb(0)), Linear(2, 601, Sub(5))));
        var sub = new LadderSubroutine(5, "Sub", new[]
        {
            Rung(Linear(3, 200, Mb(1)), Linear(4, 602)),
            Rung(Linear(5, 206, Mb(6))),
        });
        var h = new LadderHarness(Project([main, sub]));

        h.Scan();
        Assert.False(h.Bit(OperandType.MB, 6));

        h.SetBit(OperandType.MB, 0, true);
        h.SetBit(OperandType.MB, 1, true);
        h.Scan();
        Assert.False(h.Bit(OperandType.MB, 6));

        h.SetBit(OperandType.MB, 1, false);
        h.Scan();
        Assert.True(h.Bit(OperandType.MB, 6));
    }

    [Fact]
    public void DisplayJump_SwitchesHmiState()
    {
        var h = new LadderHarness(MainProject(Rung(Linear(1, 200, Mb(0)), Linear(2, 800, Page(3)))));

        h.Scan();
        Assert.Equal(0, h.Hmi.CurrentDisplayId);

        h.SetBit(OperandType.MB, 0, true);
        h.Scan();
        Assert.Equal(3, h.Hmi.CurrentDisplayId);
    }

    [Fact]
    public void PowerUp_AppliesValuesAndTimerPresetsOnFirstScan()
    {
        var operands = new[]
        {
            new OperandInfo(0, 10, new OperandAddress(OperandType.MI, 10), string.Empty, "123", true),
            new OperandInfo(0, 3, new OperandAddress(OperandType.T, 3), string.Empty, "50", false),
        };
        var h = new LadderHarness(Project([MainRoutine()], operands));

        h.Scan();

        Assert.Equal(123, h.Number(OperandType.MI, 10));
        Assert.Equal(50, h.Memory.GetTimer(3).Preset);
    }

    [Fact]
    public void UnsupportedElementType_IsReportedAndPassesPower()
    {
        var h = new LadderHarness(MainProject(Rung(Linear(1, 9999), Linear(2, 204, Mb(1)))));

        h.Scan();

        Assert.Contains(9999, h.Interpreter.UnsupportedElementTypes);
        Assert.True(h.Bit(OperandType.MB, 1));
        Assert.NotEmpty(h.Interpreter.LoadDiagnostics);
    }

    [Fact]
    public void Unload_StopsExecutingProgram()
    {
        var h = new LadderHarness(MainProject(Rung(Linear(1, 204, Mb(1)))));
        Assert.True(h.Interpreter.IsLoaded);

        h.Interpreter.Unload();
        h.Scan();

        Assert.False(h.Interpreter.IsLoaded);
        Assert.False(h.Bit(OperandType.MB, 1));
    }
}
