namespace VisionSimulator.Core.Ladder;

/// <summary>Instrukcje warunkowe: styki NO/NC/P/N (200–203) i porównania (300–305).</summary>
internal static class ContactInstructions
{
    private const int ContactWidth = 6;
    private const int CompareWidth = 13;

    public static IEnumerable<InstructionDefinition> Definitions()
    {
        yield return Contact(200, "NO", static (_, _, current) => current);
        yield return Contact(201, "NC", static (_, _, current) => !current);
        yield return Contact(202, "P", static (execution, element, current) => current && !execution.EdgeState[element.EdgeSlot]);
        yield return Contact(203, "N", static (execution, element, current) => !current && execution.EdgeState[element.EdgeSlot]);

        yield return Compare(300, "GT", static c => c > 0);
        yield return Compare(301, "GE", static c => c >= 0);
        yield return Compare(302, "EQ", static c => c == 0);
        yield return Compare(303, "NE", static c => c != 0);
        yield return Compare(304, "LE", static c => c <= 0);
        yield return Compare(305, "LT", static c => c < 0);
    }

    // [ARCHITEKTURA I ADR]
    // Zbocze jest pamiętane per element (EdgeSlot), a nie per operand: dwa kontakty P na tym samym bicie
    // wykryją zbocze niezależnie od siebie, a wielokrotne wywołanie podprogramu nie „zjada” zbocza.
    private static InstructionDefinition Contact(int opcode, string name, Func<LadderExecution, CompiledElement, bool, bool> evaluate) =>
        new(opcode, name, InstructionRole.Condition, ContactWidth, Condition: (element, execution) =>
        {
            if (element.Operands.Length == 0)
            {
                return true;
            }

            var current = execution.ReadBit(element.Operands[0]);
            var result = evaluate(execution, element, current);
            if (element.EdgeSlot >= 0)
            {
                execution.EdgeState[element.EdgeSlot] = current;
            }

            return result;
        });

    // [WYJAŚNIENIE ALGORYTMU I ZAAWANSOWANEJ MATEMATYKI]
    // Porównanie wykonywane jest w double, gdy którykolwiek operand jest typu MF, w przeciwnym razie w long
    // (bez utraty precyzji dla rejestrów 32-bitowych).
    private static InstructionDefinition Compare(int opcode, string name, Func<int, bool> accept) =>
        new(opcode, name, InstructionRole.Condition, CompareWidth, Condition: (element, execution) =>
        {
            if (element.Operands.Length < 2)
            {
                return false;
            }

            var left = element.Operands[0];
            var right = element.Operands[1];
            var comparison = left.IsFloat || right.IsFloat
                ? execution.ReadDouble(left).CompareTo(execution.ReadDouble(right))
                : execution.ReadLong(left).CompareTo(execution.ReadLong(right));
            return accept(comparison);
        });
}
