using VisionSimulator.Core.Memory;

namespace VisionSimulator.Tests;

public sealed class OperandAddressTests
{
    [Theory]
    [InlineData("MI 101", OperandType.MI, 101)]
    [InlineData("MI101", OperandType.MI, 101)]
    [InlineData("mb 22", OperandType.MB, 22)]
    [InlineData("SDW 3", OperandType.SDW, 3)]
    [InlineData("I 32", OperandType.I, 32)]
    [InlineData("mi5", OperandType.MI, 5)]
    [InlineData("ML 0", OperandType.ML, 0)]
    [InlineData("  MB   10 ", OperandType.MB, 10)]
    public void TryParse_AcceptsValidAddress(string text, OperandType type, int index)
    {
        Assert.True(OperandAddress.TryParse(text, out var address));
        Assert.Equal(new OperandAddress(type, index), address);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MI")]
    [InlineData("101")]
    [InlineData("QQ 1")]
    [InlineData("MI -1")]
    public void TryParse_RejectsInvalidAddress(string? text)
    {
        Assert.False(OperandAddress.TryParse(text, out _));
    }

    [Fact]
    public void ToString_UsesVisiLogicNotation()
    {
        Assert.Equal("ML 200", new OperandAddress(OperandType.ML, 200).ToString());
    }
}
