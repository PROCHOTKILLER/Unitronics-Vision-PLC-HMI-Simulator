using VisionSimulator.Core.Hmi;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Core.Ladder;

/// <summary>
/// Kontekst jednego skanu drabinki: dostęp do pamięci i HMI, stos wywołań podprogramów oraz odczyt i zapis operandów.
/// </summary>
/// <remarks>
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Obiekt jest tworzony na każdy skan i używany wyłącznie w wątku skanu (pod <see cref="PlcMemory.SyncRoot"/>).
/// </remarks>
internal sealed class LadderExecution(
    PlcMemory memory,
    HmiState hmi,
    CompiledProgram program,
    bool[] edgeState,
    TimeSpan elapsed,
    AnomalyCounter anomalies,
    RungTrace[]? traces = null,
    long stamp = 0)
{
    /// <summary>Maksymalne zagnieżdżenie wywołań podprogramów (ochrona przed rekurencją).</summary>
    public const int MaxCallDepth = 16;

    private int _callDepth;

    public PlcMemory Memory { get; } = memory;

    public HmiState Hmi { get; } = hmi;

    public TimeSpan Elapsed { get; } = elapsed;

    /// <summary>Poprzednie wartości sygnałów kontaktów P/N, indeksowane <see cref="CompiledElement.EdgeSlot"/>.</summary>
    public bool[] EdgeState { get; } = edgeState;

    /// <summary>Numer bieżącego wykonania programu, zapisywany w śladzie szczebla.</summary>
    public long Stamp { get; } = stamp;

    /// <summary>Ślad wykonania szczebla albo null, gdy ślad nie jest zbierany.</summary>
    public RungTrace? TraceFor(CompiledRung rung) =>
        traces is not null && (uint)rung.TraceIndex < (uint)traces.Length ? traces[rung.TraceIndex] : null;

    public void RunProgram()
    {
        foreach (var subroutine in program.EntryPoints)
        {
            RunSubroutine(subroutine);
        }
    }

    public void RunSubroutine(CompiledSubroutine subroutine)
    {
        foreach (var rung in subroutine.Rungs)
        {
            if (RungSolver.Execute(rung, this))
            {
                return;
            }
        }
    }

    /// <summary>Wywołuje podprogram o danym identyfikatorze; brak podprogramu i zbyt głębokie zagnieżdżenie to anomalie.</summary>
    public void Call(CompiledElement caller, int subroutineId)
    {
        if (_callDepth >= MaxCallDepth)
        {
            ReportAnomaly(caller, "przekroczono maksymalne zagnieżdżenie wywołań podprogramów");
            return;
        }

        if (!program.Subroutines.TryGetValue(subroutineId, out var target))
        {
            ReportAnomaly(caller, "wywołanie nieistniejącego podprogramu");
            return;
        }

        _callDepth++;
        try
        {
            RunSubroutine(target);
        }
        finally
        {
            _callDepth--;
        }
    }

    public void ReportAnomaly(CompiledElement element, string reason) => anomalies.Report(element.Id, reason);

    public bool EvaluateCondition(CompiledElement element)
    {
        try
        {
            return element.Definition.Condition?.Invoke(element, this) ?? true;
        }
        catch (ArgumentException)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Operand poza zakresem układu pamięci lub niewłaściwego typu: warunek nie przewodzi, skan trwa dalej.
            ReportAnomaly(element, "operand poza zakresem pamięci lub niewłaściwego typu");
            return false;
        }
    }

    public void ExecuteAction(CompiledElement element, bool power)
    {
        try
        {
            element.Definition.Action?.Invoke(element, this, power);
        }
        catch (ArgumentException)
        {
            ReportAnomaly(element, "operand poza zakresem pamięci lub niewłaściwego typu");
        }
    }

    /// <summary>Wartość logiczna operandu: bity i bity T/C wprost, pozostałe typy jako „różne od zera”.</summary>
    public bool ReadBit(CompiledOperand operand)
    {
        if (operand.IsMemory && (operand.Address.Type.IsBit() || operand.Address.Type.IsTimerOrCounter()))
        {
            return Memory.GetBit(operand.Address);
        }

        return ReadLong(operand) != 0;
    }

    public long ReadLong(CompiledOperand operand)
    {
        switch (operand.Kind)
        {
            case ElementOperandKind.Memory:
                if (operand.IsFloat)
                {
                    return ToLong(Memory.GetFloat(operand.Address.Index));
                }

                return operand.Address.Type.IsBit()
                    ? (Memory.GetBit(operand.Address) ? 1 : 0)
                    : Memory.GetNumber(operand.Address);
            case ElementOperandKind.Constant:
                return operand.Constant;
            case ElementOperandKind.HmiPage:
            case ElementOperandKind.Subroutine:
                return operand.RawAddress;
            default:
                return 0;
        }
    }

    public double ReadDouble(CompiledOperand operand) =>
        operand.IsFloat ? Memory.GetFloat(operand.Address.Index) : ReadLong(operand);

    public void WriteBit(CompiledElement element, CompiledOperand operand, bool value)
    {
        if (!operand.IsMemory)
        {
            ReportAnomaly(element, "zapis do operandu, który nie jest pamięcią");
            return;
        }

        if (operand.IsFloat)
        {
            Memory.SetFloat(operand.Address.Index, value ? 1f : 0f);
        }
        else if (operand.Address.Type.IsBit() || operand.Address.Type.IsTimerOrCounter())
        {
            Memory.SetBit(operand.Address, value);
        }
        else
        {
            Memory.SetNumber(operand.Address, value ? 1 : 0);
        }
    }

    public void WriteLong(CompiledElement element, CompiledOperand operand, long value)
    {
        if (!operand.IsMemory)
        {
            ReportAnomaly(element, "zapis do operandu, który nie jest pamięcią");
        }
        else if (operand.IsFloat)
        {
            Memory.SetFloat(operand.Address.Index, value);
        }
        else if (operand.Address.Type.IsBit())
        {
            Memory.SetBit(operand.Address, value != 0);
        }
        else
        {
            Memory.SetNumber(operand.Address, value);
        }
    }

    public void WriteDouble(CompiledElement element, CompiledOperand operand, double value)
    {
        if (operand.IsFloat)
        {
            Memory.SetFloat(operand.Address.Index, (float)value);
        }
        else
        {
            WriteLong(element, operand, ToLong(value));
        }
    }

    // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
    // Rzutowanie double → long poza zakresem lub dla NaN daje wynik nieokreślony; rejestry mają najwyżej 32 bity,
    // więc obcinamy do zakresu mieszczącego wszystkie typy, a NaN i nieskończoność zamieniamy na 0.
    private static long ToLong(double value) =>
        double.IsFinite(value) ? (long)Math.Clamp(value, -4_294_967_296.0, 4_294_967_295.0) : 0;
}
