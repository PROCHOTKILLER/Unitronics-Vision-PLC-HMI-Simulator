using VisionSimulator.Core.Project;

namespace VisionSimulator.Core.Ladder;

/// <summary>Sterowanie przebiegiem i transfer danych: CALL (601), skok do ekranu (800), MOV (900), LIN/timer (1300).</summary>
internal static class FlowInstructions
{
    private const int BlockWidth = 6;

    public const int CallOpcode = 601;
    public const int StoreOpcode = 900;

    public static IEnumerable<InstructionDefinition> Definitions()
    {
        yield return new(CallOpcode, "CALL", InstructionRole.Action, BlockWidth, Action: CallOrStore);
        yield return new(800, "JUMP-HMI", InstructionRole.Action, BlockWidth, Action: ShowDisplay);
        yield return new(StoreOpcode, "MOV", InstructionRole.Action, BlockWidth, Action: StoreOrCall);
        yield return new(1300, "LIN/TMR", InstructionRole.Action, BlockWidth, Action: LinearizeOrTimer);
    }

    /// <summary>Czy element wywołuje podprogram (z operandu albo z parametru MoreParamsLng); używane do wyboru programu głównego.</summary>
    public static bool IsSubroutineCall(int opcode, int operandCount, int moreParamsLng) =>
        (opcode == CallOpcode && (operandCount == 1 || moreParamsLng > 0))
        || (opcode == StoreOpcode && operandCount < 2 && moreParamsLng > 0);

    // [ARCHITEKTURA I ADR]
    // VisiLogic zapisuje wywołanie podprogramu raz z operandem typu „podprogram” (jeden operand), raz z numerem
    // w parametrze elementu (MoreParamsLng, zero operandów). Dwa operandy to przypisanie wartości.
    private static void CallOrStore(CompiledElement element, LadderExecution execution, bool power)
    {
        if (!power)
        {
            return;
        }

        if (element.Operands.Length >= 2)
        {
            Move(element, execution);
        }
        else
        {
            CallFromElement(element, execution);
        }
    }

    private static void StoreOrCall(CompiledElement element, LadderExecution execution, bool power)
    {
        if (!power)
        {
            return;
        }

        if (element.Operands.Length >= 2)
        {
            Move(element, execution);
        }
        else if (element.MoreParamsLng > 0)
        {
            execution.Call(element, element.MoreParamsLng);
        }
    }

    private static void CallFromElement(CompiledElement element, LadderExecution execution)
    {
        if (element.Operands.Length == 1)
        {
            var operand = element.Operands[0];
            var id = operand.Kind == ElementOperandKind.Constant ? (int)operand.Constant : operand.RawAddress;
            execution.Call(element, id);
        }
        else if (element.MoreParamsLng > 0)
        {
            execution.Call(element, element.MoreParamsLng);
        }
    }

    private static void Move(CompiledElement element, LadderExecution execution)
    {
        var source = element.Operands[0];
        var destination = element.Operands[1];
        if (source.IsFloat || destination.IsFloat)
        {
            execution.WriteDouble(element, destination, execution.ReadDouble(source));
        }
        else
        {
            execution.WriteLong(element, destination, execution.ReadLong(source));
        }
    }

    private static void ShowDisplay(CompiledElement element, LadderExecution execution, bool power)
    {
        if (!power)
        {
            return;
        }

        // [ARCHITEKTURA I ADR]
        // Operand typu „ekran” niesie identyfikator w adresie, inny operand — w wartości. Element bez operandu
        // ma numer ekranu w parametrze MoreParamsLng (tak jak wywołanie podprogramu).
        int id;
        if (element.Operands.Length >= 1)
        {
            var operand = element.Operands[0];
            id = operand.Kind == ElementOperandKind.HmiPage ? operand.RawAddress : (int)execution.ReadLong(operand);
        }
        else
        {
            id = element.MoreParamsLng;
        }

        if (id > 0)
        {
            execution.Hmi.ShowDisplay(id);
        }
        else
        {
            execution.ReportAnomaly(element, "skok do ekranu bez numeru ekranu");
        }
    }

    // [WYJAŚNIENIE ALGORYTMU I ZAAWANSOWANEJ MATEMATYKI]
    // Sześć operandów: linearyzacja Y = Y1 + (X - X1) * (Y2 - Y1) / (X2 - X1) z dzieleniem całkowitym;
    // X2 = X1 oznacza dzielenie przez zero (brak zapisu, anomalia). Mniej operandów: blok timera,
    // gdzie operand 0 to T, a opcjonalny operand 1 to nastawa w jednostkach 10 ms.
    private static void LinearizeOrTimer(CompiledElement element, LadderExecution execution, bool power)
    {
        var operands = element.Operands;
        if (operands.Length >= 6)
        {
            if (!power)
            {
                return;
            }

            var x1 = execution.ReadLong(operands[0]);
            var y1 = execution.ReadLong(operands[1]);
            var x2 = execution.ReadLong(operands[2]);
            var y2 = execution.ReadLong(operands[3]);
            var x = execution.ReadLong(operands[4]);
            if (x2 == x1)
            {
                execution.ReportAnomaly(element, "linearyzacja z X1 równym X2");
                return;
            }

            execution.WriteLong(element, operands[5], y1 + ((x - x1) * (y2 - y1) / (x2 - x1)));
        }
        else if (operands.Length >= 1 && operands[0].IsTimer)
        {
            var timer = execution.Memory.GetTimer(operands[0].Address.Index);
            if (operands.Length >= 2)
            {
                timer.Preset = Math.Max(0, execution.ReadLong(operands[1]));
            }

            timer.Update(power, execution.Elapsed);
        }
    }
}
