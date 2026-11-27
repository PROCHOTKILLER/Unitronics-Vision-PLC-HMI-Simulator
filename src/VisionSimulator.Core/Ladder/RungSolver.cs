namespace VisionSimulator.Core.Ladder;

/// <summary>
/// Rozwiązuje jeden szczebel drabinki: wylicza stany warunków, propaguje moc przez siatkę 2D (albo szereg
/// w trybie liniowym) i wykonuje akcje w kolejności elementów.
/// </summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Topologia (numeracja węzłów, rozpiętość przewodów) jest wyznaczana raz, przy kompilacji programu.
/// W skanie solver operuje wyłącznie na tablicy flag zasilenia węzłów.
/// </remarks>
internal static class RungSolver
{
    private const int StackLimit = 128;
    private const int MinIterationBudget = 8;
    private const int IterationsPerElement = 4;

    /// <summary>Wykonuje szczebel; zwraca true, gdy zasilony element RET kończy bieżący podprogram.</summary>
    public static bool Execute(CompiledRung rung, LadderExecution execution)
    {
        var elements = rung.Elements;
        if (elements.Length == 0)
        {
            return false;
        }

        // [OPTYMALIZACJA WYDAJNOŚCI (PERFORMANCE)]
        // Wyniki warunków żyją na stosie (zwykły szczebel ma kilkanaście elementów) — zero alokacji na skan.
        Span<bool> conditions = elements.Length <= StackLimit
            ? stackalloc bool[StackLimit]
            : new bool[elements.Length];
        conditions = conditions[..elements.Length];

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Warunki liczone są raz na szczebel, przed propagacją mocy, aby zbocza P/N były deterministyczne.
        for (var i = 0; i < elements.Length; i++)
        {
            if (elements[i].Definition.Role == InstructionRole.Condition)
            {
                conditions[i] = execution.EvaluateCondition(elements[i]);
            }
        }

        var trace = execution.TraceFor(rung);
        return rung.Is2D
            ? ExecuteGrid(rung, conditions, execution, trace)
            : ExecuteSeries(rung, conditions, execution, trace);
    }

    // [ARCHITEKTURA I ADR]
    // Tryb liniowy (szczebel bez współrzędnych): wszystkie warunki szeregowo (AND), akcje otrzymują wynik.
    private static bool ExecuteSeries(CompiledRung rung, ReadOnlySpan<bool> conditions, LadderExecution execution, RungTrace? trace)
    {
        var elements = rung.Elements;
        var power = true;
        for (var i = 0; i < elements.Length; i++)
        {
            if (elements[i].Definition.Role == InstructionRole.Condition && !conditions[i])
            {
                power = false;
                break;
            }
        }

        if (trace is not null)
        {
            TraceSeries(rung, conditions, power, execution.Stamp, trace);
        }

        foreach (var element in elements)
        {
            switch (element.Definition.Role)
            {
                case InstructionRole.Return:
                    if (power)
                    {
                        return true;
                    }

                    break;
                case InstructionRole.Action:
                    execution.ExecuteAction(element, power);
                    break;
            }
        }

        return false;
    }

    // [WYJAŚNIENIE ALGORYTMU I ZAAWANSOWANEJ MATEMATYKI]
    // Siatka 2D: węzły (x, y) są numerowane przy kompilacji. Lewa szyna (x = 0) jest zasilona. Moc przechodzi:
    // przez przewód poziomy na kolejne węzły w prawo, przez przewód pionowy na wszystkie jego węzły (gdy którykolwiek
    // jest zasilony), przez przewodzący styk/porównanie oraz przez blok akcji na jego węzeł wyjściowy.
    // Relaksacja powtarza przebieg do braku zmian; limit iteracji (4 na element + 8) chroni przed pętlą
    // przy uszkodzonej geometrii.
    private static bool ExecuteGrid(CompiledRung rung, ReadOnlySpan<bool> conditions, LadderExecution execution, RungTrace? trace)
    {
        var elements = rung.Elements;

        Span<bool> powered = rung.NodeCount <= StackLimit * 2
            ? stackalloc bool[StackLimit * 2]
            : new bool[rung.NodeCount];
        powered = powered[..rung.NodeCount];

        foreach (var seed in rung.SeedNodes)
        {
            powered[seed] = true;
        }

        var budget = (elements.Length * IterationsPerElement) + MinIterationBudget;
        var changed = true;
        for (var iteration = 0; changed && iteration < budget; iteration++)
        {
            changed = false;
            for (var i = 0; i < elements.Length; i++)
            {
                changed |= Propagate(elements[i], conditions[i], powered);
            }
        }

        if (trace is not null)
        {
            TraceGrid(rung, conditions, powered, execution.Stamp, trace);
        }

        foreach (var element in elements)
        {
            var nodePowered = powered[element.InNode];
            switch (element.Definition.Role)
            {
                case InstructionRole.Return:
                    // RET na początku szczebla (X = 0) wykonuje się tylko w szczeblu zamykającym podprogram.
                    if (nodePowered && (element.Xs > 0 || rung.IsReturnStrip))
                    {
                        return true;
                    }

                    break;
                case InstructionRole.Action:
                    execution.ExecuteAction(element, nodePowered);
                    break;
            }
        }

        return false;
    }

    // [INTENCJA I POWÓD DZIAŁANIA]
    // Ślad dla widoku drabinki zapisywany jest przed akcjami: RET może zakończyć szczebel wcześniej, a stan
    // zasilenia jest już ustalony. Akcje i RET dostają zasilenie wejścia (to samo, co otrzymują w wykonaniu).
    private static void TraceSeries(CompiledRung rung, ReadOnlySpan<bool> conditions, bool power, long stamp, RungTrace trace)
    {
        var elements = rung.Elements;
        var running = true;
        for (var i = 0; i < elements.Length; i++)
        {
            if (elements[i].Definition.Role == InstructionRole.Condition)
            {
                trace.Input[i] = running;
                trace.Conducts[i] = conditions[i];
                running = running && conditions[i];
                trace.Output[i] = running;
            }
            else
            {
                trace.Input[i] = power;
                trace.Output[i] = power;
                trace.Conducts[i] = true;
            }
        }

        trace.Stamp = stamp;
    }

    private static void TraceGrid(CompiledRung rung, ReadOnlySpan<bool> conditions, ReadOnlySpan<bool> powered, long stamp, RungTrace trace)
    {
        var elements = rung.Elements;
        for (var i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            var input = powered[element.InNode];
            switch (element.Definition.Role)
            {
                case InstructionRole.VerticalWire:
                    foreach (var node in element.SpanNodes)
                    {
                        input |= powered[node];
                    }

                    trace.Input[i] = input;
                    trace.Output[i] = input;
                    trace.Conducts[i] = true;
                    break;
                case InstructionRole.Condition:
                    trace.Input[i] = input;
                    trace.Conducts[i] = conditions[i];
                    trace.Output[i] = input && conditions[i];
                    break;
                default:
                    trace.Input[i] = input;
                    trace.Output[i] = input;
                    trace.Conducts[i] = true;
                    break;
            }
        }

        trace.Stamp = stamp;
    }

    private static bool Propagate(CompiledElement element, bool conducts, Span<bool> powered)
    {
        switch (element.Definition.Role)
        {
            case InstructionRole.HorizontalWire:
                return powered[element.InNode] && EnergizeAll(element.SpanNodes, powered);
            case InstructionRole.VerticalWire:
                foreach (var node in element.SpanNodes)
                {
                    if (powered[node])
                    {
                        return EnergizeAll(element.SpanNodes, powered);
                    }
                }

                return false;
            case InstructionRole.Condition:
                return conducts && powered[element.InNode] && Energize(element.OutNode, powered);
            case InstructionRole.Action:
                return powered[element.InNode] && Energize(element.OutNode, powered);
            default:
                return false;
        }
    }

    private static bool Energize(int node, Span<bool> powered)
    {
        if (powered[node])
        {
            return false;
        }

        powered[node] = true;
        return true;
    }

    private static bool EnergizeAll(int[] nodes, Span<bool> powered)
    {
        var changed = false;
        foreach (var node in nodes)
        {
            changed |= Energize(node, powered);
        }

        return changed;
    }
}
