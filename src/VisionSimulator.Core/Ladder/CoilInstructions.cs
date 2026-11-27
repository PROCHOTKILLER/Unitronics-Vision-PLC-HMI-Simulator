using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Ladder;

/// <summary>Cewki: bezpośrednia (204), zanegowana (205), SET (206), RESET (207) i przełączająca (212).</summary>
internal static class CoilInstructions
{
    private const int CoilWidth = 6;

    public static IEnumerable<InstructionDefinition> Definitions()
    {
        yield return Coil(204, "OUT", Direct);
        yield return Coil(205, "OUT-N", Inverted);
        yield return Coil(206, "SET", Set);
        yield return Coil(207, "RST", Reset);
        yield return Coil(212, "TOG", Toggle);
    }

    private static InstructionDefinition Coil(int opcode, string name, ActionHandler action) =>
        new(opcode, name, InstructionRole.Action, CoilWidth, Action: (element, execution, power) =>
        {
            if (element.Operands.Length > 0)
            {
                action(element, execution, power);
            }
        });

    // [ARCHITEKTURA I ADR]
    // Cewka bezpośrednia na operandzie T steruje timerem (zasilenie = odliczanie), a na C zlicza zbocza
    // narastające; na pozostałych operandach zapisuje stan zasilenia do bitu.
    private static void Direct(CompiledElement element, LadderExecution execution, bool power)
    {
        var operand = element.Operands[0];
        if (operand.IsTimer)
        {
            execution.Memory.GetTimer(operand.Address.Index).Update(power, execution.Elapsed);
        }
        else if (operand.IsCounter)
        {
            execution.Memory.GetCounter(operand.Address.Index).Count(power);
        }
        else
        {
            execution.WriteBit(element, operand, power);
        }
    }

    private static void Inverted(CompiledElement element, LadderExecution execution, bool power) =>
        execution.WriteBit(element, element.Operands[0], !power);

    private static void Set(CompiledElement element, LadderExecution execution, bool power)
    {
        if (power)
        {
            execution.WriteBit(element, element.Operands[0], true);
        }
    }

    private static void Reset(CompiledElement element, LadderExecution execution, bool power)
    {
        if (!power)
        {
            return;
        }

        var operand = element.Operands[0];
        if (operand.IsTimer)
        {
            execution.Memory.GetTimer(operand.Address.Index).Reset();
        }
        else if (operand.IsCounter)
        {
            execution.Memory.GetCounter(operand.Address.Index).Reset();
        }
        else
        {
            execution.WriteBit(element, operand, false);
        }
    }

    private static void Toggle(CompiledElement element, LadderExecution execution, bool power)
    {
        if (power)
        {
            var operand = element.Operands[0];
            execution.WriteBit(element, operand, !execution.ReadBit(operand));
        }
    }
}
