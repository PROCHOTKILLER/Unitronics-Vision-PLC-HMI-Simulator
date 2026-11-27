using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Hmi;
using VisionSimulator.Core.Ladder;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Tests;

/// <summary>Budowniczowie syntetycznych projektów LAD oraz uprząż z silnikiem i interpreterem na potrzeby testów.</summary>
internal static class LadderTestKit
{
    public static ElementOperand Mem(OperandType type, int index) =>
        new(0, index, 0, 0, ElementOperandKind.Memory, new OperandAddress(type, index), null);

    public static ElementOperand Const(long value) =>
        new(0, (int)value, 0, 0, ElementOperandKind.Constant, null, value);

    public static ElementOperand Page(int id) =>
        new(0, id, 0, 0, ElementOperandKind.HmiPage, null, null);

    public static ElementOperand Sub(int id) =>
        new(0, id, 0, 0, ElementOperandKind.Subroutine, null, null);

    /// <summary>Element w układzie liniowym (wszystkie współrzędne zerowe).</summary>
    public static LadderElement Linear(int id, int type, params ElementOperand[] operands) =>
        new(id, type, 0, 0, 0, 0, string.Empty, 0, operands);

    public static LadderElement At(int id, int type, int xs, int ys, int dx, int dy, params ElementOperand[] operands) =>
        new(id, type, xs, ys, dx, dy, string.Empty, 0, operands);

    public static LadderRung Rung(params LadderElement[] elements) =>
        new(0, 0, string.Empty, false, elements);

    public static LadderSubroutine MainRoutine(params LadderRung[] rungs) => new(2, "Main Routine", rungs);

    public static VisiLogicProject Project(IReadOnlyList<LadderSubroutine> subroutines, IReadOnlyList<OperandInfo>? operands = null) =>
        new(string.Empty, "test", "V350", [], subroutines, operands ?? [], [], [], []);

    public static VisiLogicProject MainProject(params LadderRung[] rungs) => Project([MainRoutine(rungs)]);
}

/// <summary>Silnik, pamięć V350 i interpreter LAD gotowe do skanowania krok po kroku.</summary>
internal sealed class LadderHarness
{
    public LadderHarness(VisiLogicProject project)
    {
        Memory = new PlcMemory(PlcMemoryLayout.V350);
        Hmi = new HmiState();
        Interpreter = new LadderInterpreter(Hmi);
        Interpreter.Load(project);
        Engine = new PlcEngine(Memory, [Interpreter]);
        Engine.Run();
    }

    public PlcMemory Memory { get; }

    public HmiState Hmi { get; }

    public LadderInterpreter Interpreter { get; }

    public PlcEngine Engine { get; }

    public void Scan(int milliseconds = 10) => Engine.Scan(TimeSpan.FromMilliseconds(milliseconds));

    public bool Bit(OperandType type, int index) => Memory.GetBit(new OperandAddress(type, index));

    public void SetBit(OperandType type, int index, bool value) => Memory.SetBit(new OperandAddress(type, index), value);

    public long Number(OperandType type, int index) => Memory.GetNumber(new OperandAddress(type, index));

    public void SetNumber(OperandType type, int index, long value) => Memory.SetNumber(new OperandAddress(type, index), value);
}
