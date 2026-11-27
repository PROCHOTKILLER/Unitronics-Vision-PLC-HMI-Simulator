using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Hmi;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Tests;

public sealed class HmiInteractionTests
{
    // Kody typów operandów VisiLogic (VisiLogicOperandCodes): 1 = MB, 3 = MI, 30 = ekran HMI.
    private const int CodeMb = 1;
    private const int CodeMi = 3;

    private static PlcMemory NewMemory() => new(PlcMemoryLayout.V350);

    private static ScanContext Scan(long number) => new(TimeSpan.FromMilliseconds(25), number, number == 1);

    private static HmiDisplay Display(int id, params HmiJump[] jumps) => new(id, $"Ekran {id}", 0, [], jumps);

    private static HmiJump JumpToDisplay(int from, int order, int bit, int target) =>
        new(from, order, CodeMb, bit, HmiJump.DisplayTargetCode, target);

    [Fact]
    public void ValueEntry_WritesIntegerAndRejectsOutOfRange()
    {
        var memory = NewMemory();
        var address = new OperandAddress(OperandType.MI, 3);

        Assert.True(HmiValueEntry.TryWrite(memory, address, " -120 ", out _));
        Assert.Equal(-120, memory.GetNumber(address));

        Assert.False(HmiValueEntry.TryWrite(memory, address, "40000", out var error));
        Assert.Contains("Zakres", error);
        Assert.Equal(-120, memory.GetNumber(address));

        Assert.False(HmiValueEntry.TryWrite(memory, address, "12.5", out _));
    }

    [Fact]
    public void ValueEntry_AcceptsDecimalCommaForFloat()
    {
        var memory = NewMemory();

        Assert.True(HmiValueEntry.TryWrite(memory, new OperandAddress(OperandType.MF, 2), "12,5", out _));
        Assert.Equal(12.5f, memory.GetFloat(2));
    }

    [Fact]
    public void ValueEntry_RejectsBitTimerAndAddressOutsideMemory()
    {
        var memory = NewMemory();

        Assert.False(HmiValueEntry.TryWrite(memory, new OperandAddress(OperandType.MB, 1), "1", out _));
        Assert.False(HmiValueEntry.TryWrite(memory, new OperandAddress(OperandType.T, 1), "1", out _));
        Assert.False(HmiValueEntry.TryWrite(memory, new OperandAddress(OperandType.MI, 1_000_000), "1", out _));
    }

    [Fact]
    public void JumpStep_ArmsOnEntryAndJumpsOnRisingEdge()
    {
        var memory = NewMemory();
        var hmi = new HmiState();
        var step = new HmiJumpStep(hmi);
        var bit = new OperandAddress(OperandType.MB, 20);
        step.Load([Display(1, JumpToDisplay(1, 1, 20, 2)), Display(2)]);
        hmi.ShowDisplay(1);

        // Bit już ustawiony przy wejściu na ekran nie powoduje skoku.
        memory.SetBit(bit, true);
        step.Execute(memory, Scan(1));
        step.Execute(memory, Scan(2));
        Assert.Equal(1, hmi.CurrentDisplayId);

        memory.SetBit(bit, false);
        step.Execute(memory, Scan(3));
        memory.SetBit(bit, true);
        step.Execute(memory, Scan(4));
        Assert.Equal(2, hmi.CurrentDisplayId);
        Assert.Equal(1, step.JumpCount);
    }

    [Fact]
    public void JumpStep_ReadsTargetFromRegisterAndSkipsMissingDisplay()
    {
        var memory = NewMemory();
        var hmi = new HmiState();
        var step = new HmiJumpStep(hmi);
        var register = new OperandAddress(OperandType.MI, 7);
        var bit = new OperandAddress(OperandType.MB, 21);
        step.Load([Display(1, new HmiJump(1, 1, CodeMb, 21, CodeMi, 7)), Display(3)]);
        hmi.ShowDisplay(1);
        step.Execute(memory, Scan(1));

        memory.SetNumber(register, 99);
        memory.SetBit(bit, true);
        step.Execute(memory, Scan(2));
        Assert.Equal(1, hmi.CurrentDisplayId);

        memory.SetBit(bit, false);
        step.Execute(memory, Scan(3));
        memory.SetNumber(register, 3);
        memory.SetBit(bit, true);
        step.Execute(memory, Scan(4));
        Assert.Equal(3, hmi.CurrentDisplayId);
    }

    [Fact]
    public void JumpStep_IgnoresNonBitCondition()
    {
        var step = new HmiJumpStep(new HmiState());

        step.Load([Display(1, new HmiJump(1, 1, CodeMi, 0, HmiJump.DisplayTargetCode, 2)), Display(2)]);

        Assert.Equal(0, step.JumpCount);
    }
}
