using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Tests;

public sealed class VisiLogicOperandCodesTests
{
    [Theory]
    [InlineData(1, OperandType.MB)]
    [InlineData(2, OperandType.SB)]
    [InlineData(3, OperandType.MI)]
    [InlineData(5, OperandType.ML)]
    [InlineData(7, OperandType.MF)]
    [InlineData(9, OperandType.I)]
    [InlineData(10, OperandType.O)]
    [InlineData(11, OperandType.T)]
    [InlineData(16, OperandType.DW)]
    [InlineData(36, OperandType.SDW)]
    public void MemoryCodes_MapToOperandTypes(int code, OperandType expected)
    {
        Assert.True(VisiLogicOperandCodes.TryGetOperandType(code, out var type));
        Assert.Equal(expected, type);
        Assert.Equal(ElementOperandKind.Memory, VisiLogicOperandCodes.Classify(code));
    }

    [Theory]
    [InlineData(15, ElementOperandKind.Constant)]
    [InlineData(34, ElementOperandKind.Constant)]
    [InlineData(38, ElementOperandKind.Constant)]
    [InlineData(30, ElementOperandKind.HmiPage)]
    [InlineData(31, ElementOperandKind.Subroutine)]
    [InlineData(8, ElementOperandKind.Unknown)]
    [InlineData(999, ElementOperandKind.Unknown)]
    public void Classify_SpecialCodes(int code, ElementOperandKind expected)
    {
        Assert.Equal(expected, VisiLogicOperandCodes.Classify(code));
        Assert.False(VisiLogicOperandCodes.TryGetOperandType(code, out _));
    }

    [Theory]
    [InlineData("150", 150)]
    [InlineData("-32768", -32768)]
    [InlineData(" 42\0\0", 42)]
    [InlineData("5.0", 5)]
    [InlineData("12.9", 12)]
    [InlineData("1E3", 1000)]
    public void TryParsePowerUp_ParsesIntegerAndFloatText(string raw, long expected)
    {
        Assert.True(VisiLogicOperandCodes.TryParsePowerUp(raw, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("\0")]
    [InlineData("abc")]
    [InlineData("5,0")]
    public void TryParsePowerUp_RejectsEmptyAndNonNumeric(string? raw)
    {
        Assert.False(VisiLogicOperandCodes.TryParsePowerUp(raw, out _));
    }
}
