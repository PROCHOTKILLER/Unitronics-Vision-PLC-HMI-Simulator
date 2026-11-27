using VisionSimulator.Core.Hmi;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Tests;

public sealed class HmiScreenBuilderTests
{
    // Kody typów operandów VisiLogic (VisiLogicOperandCodes): 1 = MB, 3 = MI, 7 = MF, 10 = O.
    private const int CodeMb = 1;
    private const int CodeMi = 3;
    private const int CodeMf = 7;
    private const int CodeO = 10;

    private static PlcMemory NewMemory() => new(PlcMemoryLayout.V350);

    private static HmiElement Element(
        int type,
        int varType = 0,
        string text = "",
        int xs = 0,
        int ys = 0,
        int dx = 40,
        int dy = 20,
        int colorBack = 0,
        int colorFore = 0,
        int varOperandType = 0,
        int varOperandAddress = -1,
        int touch = -1,
        int touchType = HmiElement.DefaultTouchOperandType,
        bool keypad = false) =>
        new(1, type, varType, text, xs, ys, dx, dy, colorBack, colorFore, varOperandType, varOperandAddress, touchType, touch, -1, keypad);

    private static HmiScreen Build(PlcMemory memory, params HmiElement[] elements) =>
        HmiScreenBuilder.Build(new HmiDisplay(7, "Ekran", 0, elements, []), memory);

    [Theory]
    [InlineData(11, 0, 0, HmiElementKind.TouchButton)]
    [InlineData(6, 0, 0, HmiElementKind.BitIndicator)]
    [InlineData(5, 24, CodeMi, HmiElementKind.Bargraph)]
    [InlineData(5, 2, CodeMi, HmiElementKind.BinaryIndicator)]
    [InlineData(5, 0, CodeO, HmiElementKind.BinaryIndicator)]
    [InlineData(5, 0, CodeMb, HmiElementKind.BinaryIndicator)]
    [InlineData(5, 0, CodeMi, HmiElementKind.NumericVariable)]
    [InlineData(5, 0, 0, HmiElementKind.NumericVariable)]
    [InlineData(1, 0, 0, HmiElementKind.Frame)]
    [InlineData(4, 0, 0, HmiElementKind.StaticText)]
    [InlineData(99, 0, 0, HmiElementKind.Unknown)]
    public void Resolve_DispatchesByTypeVarTypeAndOperand(int type, int varType, int operandType, HmiElementKind expected)
    {
        var kind = HmiElementKindResolver.Resolve(Element(type, varType, varOperandType: operandType, varOperandAddress: 0));

        Assert.Equal(expected, kind);
    }

    [Fact]
    public void Build_ClipsRectangleToScreen()
    {
        var screen = Build(
            NewMemory(),
            Element(1, xs: 300, ys: 230, dx: 100, dy: 100),
            Element(1, xs: -5, ys: -5, dx: 0, dy: -3),
            Element(1, xs: 1000, ys: 1000, dx: 10, dy: 10));

        var clipped = screen.Items[0];
        Assert.Equal((300, 230, 20, 10), (clipped.X, clipped.Y, clipped.Width, clipped.Height));

        var negative = screen.Items[1];
        Assert.Equal((0, 0, 1, 1), (negative.X, negative.Y, negative.Width, negative.Height));

        var outside = screen.Items[2];
        Assert.InRange(outside.X + outside.Width, 1, HmiScreenBuilder.ScreenWidth);
        Assert.InRange(outside.Y + outside.Height, 1, HmiScreenBuilder.ScreenHeight);
    }

    [Fact]
    public void Build_NumericVariableShowsMemoryValue()
    {
        var memory = NewMemory();
        memory.SetNumber(new OperandAddress(OperandType.MI, 5), 123);
        memory.SetFloat(2, 1.5f);

        var screen = Build(
            memory,
            Element(5, varOperandType: CodeMi, varOperandAddress: 5),
            Element(5, varOperandType: CodeMf, varOperandAddress: 2));

        Assert.Equal("123", screen.Items[0].Text);
        Assert.Equal("MI 5", screen.Items[0].OperandText);
        Assert.Equal("1.5", screen.Items[1].Text);
    }

    [Fact]
    public void Build_OperandOutsideMemoryShowsQuestionMark()
    {
        var screen = Build(
            NewMemory(),
            Element(5, varOperandType: CodeMi, varOperandAddress: 1_000_000),
            Element(5));

        Assert.Equal("?", screen.Items[0].Text);
        Assert.Equal("?", screen.Items[1].Text);
        Assert.Null(screen.Items[1].OperandText);
    }

    [Fact]
    public void Build_BargraphRatioIsClampedToUnitRange()
    {
        var memory = NewMemory();
        memory.SetNumber(new OperandAddress(OperandType.MI, 0), 40);
        memory.SetNumber(new OperandAddress(OperandType.MI, 1), 500);

        var screen = Build(
            memory,
            Element(5, varType: 24, varOperandType: CodeMi, varOperandAddress: 0),
            Element(5, varType: 24, varOperandType: CodeMi, varOperandAddress: 1));

        Assert.Equal(0.4, screen.Items[0].Ratio!.Value, 6);
        Assert.Equal(1.0, screen.Items[1].Ratio!.Value, 6);
    }

    [Fact]
    public void Build_IndicatorAndButtonReflectBitState()
    {
        var memory = NewMemory();
        memory.SetBit(new OperandAddress(OperandType.O, 3), true);

        var screen = Build(
            memory,
            Element(6, varOperandType: CodeO, varOperandAddress: 3),
            Element(11, text: "OK", touch: 9));

        Assert.True(screen.Items[0].IsActive);
        Assert.False(screen.Items[1].IsActive);
        Assert.Equal(new OperandAddress(OperandType.MB, 9), screen.Items[1].TouchAddress);

        memory.SetBit(new OperandAddress(OperandType.MB, 9), true);
        Assert.True(Build(memory, Element(11, touch: 9)).Items[0].IsActive);
    }

    [Fact]
    public void Build_TouchOnNonButtonElementUsesOperandTypeFromProject()
    {
        var screen = Build(
            NewMemory(),
            Element(6, varOperandType: CodeO, varOperandAddress: 1, touch: 4, touchType: CodeO),
            Element(1, touch: 2),
            Element(11, touch: 5, touchType: CodeMi));

        Assert.Equal(new OperandAddress(OperandType.O, 4), screen.Items[0].TouchAddress);
        Assert.Equal(new OperandAddress(OperandType.MB, 2), screen.Items[1].TouchAddress);
        Assert.Null(screen.Items[2].TouchAddress);
    }

    [Fact]
    public void Build_NumericVariableIsEditableOnlyForRegisterInMemory()
    {
        var screen = Build(
            NewMemory(),
            Element(5, varOperandType: CodeMi, varOperandAddress: 5, keypad: true),
            Element(5, varOperandType: CodeMf, varOperandAddress: 1),
            Element(5, varOperandType: CodeMi, varOperandAddress: 1_000_000, keypad: true),
            Element(5, varType: 24, varOperandType: CodeMi, varOperandAddress: 0, keypad: true));

        Assert.Equal(new OperandAddress(OperandType.MI, 5), screen.Items[0].EditAddress);
        Assert.True(screen.Items[0].IsKeypadEntry);
        Assert.Equal(new OperandAddress(OperandType.MF, 1), screen.Items[1].EditAddress);
        Assert.False(screen.Items[1].IsKeypadEntry);
        Assert.Null(screen.Items[2].EditAddress);
        Assert.False(screen.Items[2].IsKeypadEntry);
        Assert.Null(screen.Items[3].EditAddress);
    }

    [Fact]
    public void Press_SetsAndClearsMemoryBit()
    {
        var memory = NewMemory();
        var address = new OperandAddress(OperandType.MB, 10);

        Assert.True(HmiTouch.Press(memory, address, pressed: true));
        Assert.True(memory.GetBit(address));

        Assert.True(HmiTouch.Press(memory, address, pressed: false));
        Assert.False(memory.GetBit(address));
    }

    [Fact]
    public void Press_IgnoresAddressOutsideMemoryAndNonBitOperand()
    {
        var memory = NewMemory();

        Assert.False(HmiTouch.Press(memory, new OperandAddress(OperandType.MB, 1_000_000), pressed: true));
        Assert.False(HmiTouch.Press(memory, new OperandAddress(OperandType.MI, 0), pressed: true));
    }

    [Fact]
    public void Decode_UsesFallbackForEmptyColor()
    {
        Assert.Equal(HmiColor.DefaultBackground, HmiColor.Decode(0, HmiColor.DefaultBackground));
        Assert.Equal(HmiColor.DefaultBackground, HmiColor.Decode(-1, HmiColor.DefaultBackground));
    }

    [Fact]
    public void Decode_ProducesOpaqueRgb()
    {
        Assert.Equal(0xFF336699u, HmiColor.Decode(0x336699, HmiColor.DefaultBackground));
    }

    [Fact]
    public void Build_UsesDefaultBackgroundForEmptyDisplayColor()
    {
        var screen = Build(NewMemory());

        Assert.Equal(7, screen.DisplayId);
        Assert.Equal(HmiColor.DefaultBackground, screen.BackgroundArgb);
    }
}
