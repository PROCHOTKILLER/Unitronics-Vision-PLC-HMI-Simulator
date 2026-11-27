using VisionSimulator.Core.Memory;

namespace VisionSimulator.Tests;

public sealed class PlcMemoryTests
{
    private readonly PlcMemory _memory = new(PlcMemoryLayout.V350);

    [Theory]
    [InlineData(OperandType.MI, 40000, -25536)]
    [InlineData(OperandType.MI, -1, -1)]
    [InlineData(OperandType.ML, 3_000_000_000L, -1_294_967_296L)]
    [InlineData(OperandType.DW, -1, 4_294_967_295L)]
    public void SetNumber_WrapsToRegisterWidth(OperandType type, long written, long expected)
    {
        var address = new OperandAddress(type, 0);
        _memory.SetNumber(address, written);
        Assert.Equal(expected, _memory.GetNumber(address));
    }

    [Fact]
    public void SetBit_OutOfRange_Throws()
    {
        var address = new OperandAddress(OperandType.MB, PlcMemoryLayout.V350.MemoryBits);
        Assert.Throws<ArgumentOutOfRangeException>(() => _memory.SetBit(address, true));
    }

    [Fact]
    public void GetNumber_OnBitOperand_Throws()
    {
        Assert.Throws<ArgumentException>(() => _memory.GetNumber(new OperandAddress(OperandType.MB, 0)));
    }

    [Fact]
    public void Clear_ResetsAllAreas()
    {
        _memory.SetBit(new OperandAddress(OperandType.O, 5), true);
        _memory.SetNumber(new OperandAddress(OperandType.MI, 7), 123);
        _memory.SetFloat(1, 1.5f);

        _memory.Clear();

        Assert.False(_memory.GetBit(new OperandAddress(OperandType.O, 5)));
        Assert.Equal(0, _memory.GetNumber(new OperandAddress(OperandType.MI, 7)));
        Assert.Equal(0f, _memory.GetFloat(1));
    }
}
