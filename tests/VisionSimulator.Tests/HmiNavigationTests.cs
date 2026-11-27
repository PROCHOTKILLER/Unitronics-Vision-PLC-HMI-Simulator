using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Hmi;
using VisionSimulator.Core.Ladder;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;
using static VisionSimulator.Tests.LadderTestKit;

namespace VisionSimulator.Tests;

public sealed class HmiNavigationTests
{
    // Kody elementów LAD: 200 = styk NO, 204 = cewka, 800 = skok do ekranu, 900 = MOV.
    private const int ContactNo = 200;
    private const int Coil = 204;
    private const int JumpHmi = 800;
    private const int Move = 900;

    private static readonly int[] ExpectedMb6Rungs = [1, 2];

    private static ElementOperand Mb(int index) => Mem(OperandType.MB, index);

    [Fact]
    public void DisplayStatus_PublishesCurrentAndPreviousDisplay()
    {
        var memory = new PlcMemory(PlcMemoryLayout.V350);
        var hmi = new HmiState();
        var step = new HmiDisplayStatusStep(hmi);

        hmi.ShowDisplay(1);
        hmi.ShowDisplay(4);
        step.Execute(memory, new ScanContext(TimeSpan.FromMilliseconds(10), 1, true));

        Assert.Equal(4, memory.GetNumber(HmiDisplayStatusStep.CurrentDisplay));
        Assert.Equal(1, memory.GetNumber(HmiDisplayStatusStep.PreviousDisplay));
        Assert.Equal(1, hmi.PreviousDisplayId);
    }

    [Fact]
    public void DisplayStatus_IsVisibleToLadderInTheSameScan()
    {
        var memory = new PlcMemory(PlcMemoryLayout.V350);
        var hmi = new HmiState();
        var interpreter = new LadderInterpreter(hmi);
        interpreter.Load(MainProject(Rung(Linear(1, Move, Mem(OperandType.SI, 252), Mem(OperandType.MI, 0)))));
        var engine = new PlcEngine(memory, [new HmiDisplayStatusStep(hmi), interpreter]);
        engine.Run();

        hmi.ShowDisplay(7);
        engine.Scan(TimeSpan.FromMilliseconds(10));

        Assert.Equal(7, memory.GetNumber(new OperandAddress(OperandType.MI, 0)));
    }

    [Fact]
    public void JumpHmi_UsesMoreParamsWhenElementHasNoOperand()
    {
        var jump = new LadderElement(2, JumpHmi, 0, 0, 0, 0, string.Empty, 3, []);
        var h = new LadderHarness(MainProject(Rung(Linear(1, ContactNo, Mb(5)), jump)));
        h.Hmi.ShowDisplay(1);

        h.Scan();
        Assert.Equal(1, h.Hmi.CurrentDisplayId);

        h.SetBit(OperandType.MB, 5, true);
        h.Scan();
        Assert.Equal(3, h.Hmi.CurrentDisplayId);
        Assert.Equal(1, h.Hmi.PreviousDisplayId);
    }

    [Fact]
    public void JumpHmi_WithoutTargetReportsAnomaly()
    {
        var jump = new LadderElement(2, JumpHmi, 0, 0, 0, 0, string.Empty, 0, []);
        var h = new LadderHarness(MainProject(Rung(Linear(1, ContactNo, Mb(5)), jump)));
        h.Hmi.ShowDisplay(1);
        h.SetBit(OperandType.MB, 5, true);

        h.Scan();

        Assert.Equal(1, h.Hmi.CurrentDisplayId);
        Assert.True(h.Interpreter.AnomalyCount > 0);
    }

    [Fact]
    public void Layout_FindUsagesListsRungsOfOperand()
    {
        var h = new LadderHarness(MainProject(
            Rung(Linear(1, ContactNo, Mb(5)), Linear(2, Coil, Mb(6))),
            Rung(Linear(3, ContactNo, Mb(6)), Linear(4, Coil, Mb(7)))));
        var layout = h.Interpreter.GetLayout();

        var usages = layout.FindUsages(new OperandAddress(OperandType.MB, 6));

        Assert.Equal(ExpectedMb6Rungs, usages.Select(u => u.RungNumber).ToArray());
        Assert.Empty(layout.FindUsages(new OperandAddress(OperandType.MB, 99)));
        Assert.Contains(new OperandAddress(OperandType.MB, 7), layout.UsedAddresses);
    }
}
