namespace VisionSimulator.Core.Memory;

/// <summary>Typy operandów sterownika Unitronics Vision.</summary>
public enum OperandType
{
    I,
    O,
    MB,
    SB,
    XB,
    MI,
    SI,
    XI,
    ML,
    SL,
    XL,
    DW,
    SDW,
    XDW,
    MF,
    T,
    C,
}

public static class OperandTypeExtensions
{
    public static bool IsBit(this OperandType type) =>
        type is OperandType.I or OperandType.O or OperandType.MB or OperandType.SB or OperandType.XB;

    /// <summary>Timery i liczniki mają osobne obiekty stanu (<c>PlcTimer</c>, <c>PlcCounter</c>), a nie tablice bitów/liczb.</summary>
    public static bool IsTimerOrCounter(this OperandType type) => type is OperandType.T or OperandType.C;
}
