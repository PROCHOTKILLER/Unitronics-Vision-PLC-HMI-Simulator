namespace VisionSimulator.Core.Ladder;

/// <summary>Arytmetyka: SUB (400), DIV (401), ADD (405), MUL (406), MOD (407) oraz INC (1500) i DEC (1501).</summary>
internal static class ArithmeticInstructions
{
    private const int BlockWidth = 6;

    public static IEnumerable<InstructionDefinition> Definitions()
    {
        yield return Binary(405, "ADD", static (a, b) => a + b, static (a, b) => a + b, allowZeroDivisor: true);
        yield return Binary(400, "SUB", static (a, b) => a - b, static (a, b) => a - b, allowZeroDivisor: true);
        yield return Binary(406, "MUL", static (a, b) => a * b, static (a, b) => a * b, allowZeroDivisor: true);
        yield return Binary(401, "DIV", Divide, Divide, allowZeroDivisor: false);
        yield return Binary(407, "MOD", Remainder, Remainder, allowZeroDivisor: false);
        yield return Step(1500, "INC", +1);
        yield return Step(1501, "DEC", -1);
    }

    // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
    // long.MinValue / -1 rzuca OverflowException nawet w kontekście unchecked, więc dzielnik -1 obsługujemy osobno.
    private static long Divide(long a, long b) => b == -1 ? unchecked(-a) : a / b;

    private static double Divide(double a, double b) => a / b;

    private static long Remainder(long a, long b) => b == -1 ? 0 : a % b;

    private static double Remainder(double a, double b) => a % b;

    // [WYJAŚNIENIE ALGORYTMU I ZAAWANSOWANEJ MATEMATYKI]
    // Operandy: A, B → wynik. Gdy którykolwiek z trzech jest typu MF, liczymy w double (wynik do MF zapisany jako
    // float, do rejestru całkowitego obcięty); w przeciwnym razie w long, a szerokość rejestru docelowego
    // egzekwuje PlcMemory.SetNumber. Dzielnik zero: brak zapisu wyniku i wpis do licznika anomalii.
    private static InstructionDefinition Binary(
        int opcode,
        string name,
        Func<long, long, long> integerOperation,
        Func<double, double, double> floatOperation,
        bool allowZeroDivisor) =>
        new(opcode, name, InstructionRole.Action, BlockWidth, Action: (element, execution, power) =>
        {
            if (!power || element.Operands.Length < 3)
            {
                return;
            }

            var a = element.Operands[0];
            var b = element.Operands[1];
            var destination = element.Operands[2];

            if (a.IsFloat || b.IsFloat || destination.IsFloat)
            {
                var right = execution.ReadDouble(b);
                if (!allowZeroDivisor && right == 0)
                {
                    execution.ReportAnomaly(element, "dzielenie przez zero");
                    return;
                }

                execution.WriteDouble(element, destination, floatOperation(execution.ReadDouble(a), right));
                return;
            }

            var divisor = execution.ReadLong(b);
            if (!allowZeroDivisor && divisor == 0)
            {
                execution.ReportAnomaly(element, "dzielenie przez zero");
                return;
            }

            execution.WriteLong(element, destination, integerOperation(execution.ReadLong(a), divisor));
        });

    private static InstructionDefinition Step(int opcode, string name, int delta) =>
        new(opcode, name, InstructionRole.Action, BlockWidth, Action: (element, execution, power) =>
        {
            if (!power || element.Operands.Length < 1)
            {
                return;
            }

            var operand = element.Operands[0];
            if (operand.IsFloat)
            {
                execution.WriteDouble(element, operand, execution.ReadDouble(operand) + delta);
            }
            else
            {
                execution.WriteLong(element, operand, execution.ReadLong(operand) + delta);
            }
        });
}
