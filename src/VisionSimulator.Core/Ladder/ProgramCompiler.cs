using System.Globalization;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Core.Ladder;

/// <summary>
/// Kompiluje projekt VisiLogic do niezmiennej struktury wykonawczej: rozwiązuje opcody, operandy i numerację
/// węzłów siatki 2D oraz wybiera program główny.
/// </summary>
internal static class ProgramCompiler
{
    public const string MainRoutineName = "Main Routine";
    private const int MaxSpan = 512;
    private const int LegacyMainRoutineId = 2;

    public static CompiledProgram Compile(VisiLogicProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var state = new CompilationState();
        var ordered = new List<CompiledSubroutine>();
        var byId = new Dictionary<int, CompiledSubroutine>();

        foreach (var source in project.Subroutines)
        {
            var subroutine = new CompiledSubroutine
            {
                Id = source.Id,
                Name = source.Name ?? string.Empty,
                Rungs = [.. source.Rungs.Select(rung => CompileRung(rung, state))],
            };

            ordered.Add(subroutine);
            if (!byId.TryAdd(subroutine.Id, subroutine))
            {
                state.Diagnostics.Add($"Zdublowany identyfikator podprogramu {subroutine.Id} ('{subroutine.Name}') — wywołania wskażą pierwszy.");
            }
        }

        var entryPoints = SelectEntryPoints(ordered, byId, state.Diagnostics);
        var (values, presets) = PowerUpInitializer.Compile(project.Operands);

        if (state.UnknownOperandCodes.Count > 0)
        {
            state.Diagnostics.Add($"Operandy o nierozpoznanych kodach typu ({string.Join(", ", state.UnknownOperandCodes)}) są odczytywane jako 0, a zapis do nich jest pomijany.");
        }

        if (state.UnsupportedTypes.Count > 0)
        {
            state.Diagnostics.Add($"Nieobsługiwane typy elementów ({string.Join(", ", state.UnsupportedTypes)}): wykonywane jako no-op, przepuszczają moc w szeregu.");
        }

        if (state.ClampedSpans > 0)
        {
            state.Diagnostics.Add($"Rozpiętość {state.ClampedSpans} elementów przekraczała {MaxSpan} pól siatki i została ograniczona.");
        }

        return new CompiledProgram
        {
            Subroutines = byId,
            Ordered = [.. ordered],
            RungCount = state.Rungs,
            EntryPoints = entryPoints,
            EdgeSlotCount = state.EdgeSlots,
            PowerUpValues = values,
            Presets = presets,
            UnsupportedElementTypes = [.. state.UnsupportedTypes],
            Diagnostics = state.Diagnostics,
        };
    }

    // [ARCHITEKTURA I ADR]
    // Program główny: podprogram „Main Routine”; w razie braku FuncID 2, a potem pierwszy z listy. Gdy główny
    // wywołuje podprogramy, wykonywany jest tylko on (reszta przez CALL); w przeciwnym razie — wszystkie po kolei.
    private static CompiledSubroutine[] SelectEntryPoints(
        List<CompiledSubroutine> ordered,
        Dictionary<int, CompiledSubroutine> byId,
        List<string> diagnostics)
    {
        if (ordered.Count == 0)
        {
            diagnostics.Add("Projekt nie zawiera podprogramów LAD.");
            return [];
        }

        var main = ordered.FirstOrDefault(s => string.Equals(s.Name.Trim(), MainRoutineName, StringComparison.OrdinalIgnoreCase));
        if (main is not null)
        {
            diagnostics.Add($"Program główny: podprogram '{main.Name}' (FuncID {main.Id.ToString(CultureInfo.InvariantCulture)}) wskazany nazwą.");
        }
        else if (byId.TryGetValue(LegacyMainRoutineId, out main))
        {
            diagnostics.Add($"Program główny: FuncID {LegacyMainRoutineId} ('{main.Name}') — brak podprogramu o nazwie '{MainRoutineName}'.");
        }
        else
        {
            main = ordered[0];
            diagnostics.Add($"Program główny: pierwszy podprogram '{main.Name}' (FuncID {main.Id.ToString(CultureInfo.InvariantCulture)}) — brak '{MainRoutineName}' i FuncID {LegacyMainRoutineId}.");
        }

        var callsSubroutines = main.Rungs.Any(rung => rung.Elements.Any(
            e => FlowInstructions.IsSubroutineCall(e.Type, e.Operands.Length, e.MoreParamsLng)));
        if (callsSubroutines)
        {
            diagnostics.Add("Program główny wywołuje podprogramy: w skanie wykonywany jest tylko on.");
            return [main];
        }

        diagnostics.Add($"Program główny nie wywołuje podprogramów: w skanie wykonywane są po kolei wszystkie podprogramy ({ordered.Count}).");
        return [.. ordered];
    }

    private static CompiledRung CompileRung(LadderRung rung, CompilationState state)
    {
        var source = rung.Elements;
        var is2D = source.Any(e => e.Xs != 0 || e.Dx != 0 || e.Dy != 0
            || e.Type is InstructionRegistry.HorizontalWireOpcode or InstructionRegistry.VerticalWireOpcode);

        var nodes = is2D ? new NodeMap() : null;
        var seeds = new HashSet<int>();
        var elements = new CompiledElement[source.Count];

        for (var i = 0; i < source.Count; i++)
        {
            var element = source[i];
            var definition = Resolve(element.Type, state);
            var operands = CompileOperands(element, state);
            var edgeSlot = element.Type is 202 or 203 ? state.EdgeSlots++ : -1;

            if (nodes is null)
            {
                elements[i] = Build(element, definition, operands, edgeSlot, -1, -1, []);
                continue;
            }

            var grid = nodes;
            var dx = Clamp(element.Dx, state);
            var dy = Clamp(element.Dy, state);

            seeds.Add(grid.Get(0, element.Ys));
            for (var y = element.Ys + 1; y <= element.Ys + dy; y++)
            {
                seeds.Add(grid.Get(0, y));
            }

            var inNode = grid.Get(element.Xs, element.Ys);
            var outNode = -1;
            var span = Array.Empty<int>();

            switch (definition.Role)
            {
                case InstructionRole.HorizontalWire:
                    span = Enumerable.Range(1, Math.Max(1, dx))
                        .Select(offset => grid.Get(element.Xs + offset, element.Ys)).ToArray();
                    break;
                case InstructionRole.VerticalWire:
                    span = Enumerable.Range(0, Math.Max(1, dy) + 1)
                        .Select(offset => grid.Get(element.Xs, element.Ys + offset)).ToArray();
                    break;
                case InstructionRole.Condition:
                case InstructionRole.Action:
                    outNode = grid.Get(element.Xs + (dx > 0 ? dx : definition.DefaultWidth), element.Ys);
                    break;
            }

            elements[i] = Build(element, definition, operands, edgeSlot, inNode, outNode, span);
        }

        return new CompiledRung
        {
            IsReturnStrip = rung.IsReturnStrip,
            Is2D = is2D,
            Elements = elements,
            NodeCount = nodes?.Count ?? 0,
            SeedNodes = [.. seeds],
            TraceIndex = state.Rungs++,
        };
    }

    private static CompiledElement Build(
        LadderElement source,
        InstructionDefinition definition,
        CompiledOperand[] operands,
        int edgeSlot,
        int inNode,
        int outNode,
        int[] span) => new()
    {
        Id = source.Id,
        Type = source.Type,
        Xs = source.Xs,
        Ys = source.Ys,
        MoreParamsLng = source.MoreParamsLng,
        Definition = definition,
        Operands = operands,
        EdgeSlot = edgeSlot,
        InNode = inNode,
        OutNode = outNode,
        SpanNodes = span,
    };

    private static InstructionDefinition Resolve(int opcode, CompilationState state)
    {
        if (InstructionRegistry.TryGet(opcode, out var definition))
        {
            return definition;
        }

        state.UnsupportedTypes.Add(opcode);
        return InstructionRegistry.NoOp;
    }

    private static CompiledOperand[] CompileOperands(LadderElement element, CompilationState state)
    {
        var operands = new CompiledOperand[element.Operands.Count];
        for (var i = 0; i < operands.Length; i++)
        {
            var source = element.Operands[i];
            var kind = source.Kind;
            if (kind == ElementOperandKind.Memory && source.Memory is null)
            {
                kind = ElementOperandKind.Unknown;
            }

            if (kind == ElementOperandKind.Unknown)
            {
                state.UnknownOperandCodes.Add(source.TypeCode);
            }

            operands[i] = new CompiledOperand
            {
                Kind = kind,
                Address = source.Memory ?? default,
                Constant = source.ConstantValue ?? 0,
                RawAddress = source.Address,
            };
        }

        return operands;
    }

    private static int Clamp(int span, CompilationState state)
    {
        if (span > MaxSpan)
        {
            state.ClampedSpans++;
            return MaxSpan;
        }

        return Math.Max(0, span);
    }

    private sealed class CompilationState
    {
        public int EdgeSlots { get; set; }

        public int Rungs { get; set; }

        public int ClampedSpans { get; set; }

        public SortedSet<int> UnsupportedTypes { get; } = [];

        public SortedSet<int> UnknownOperandCodes { get; } = [];

        public List<string> Diagnostics { get; } = [];
    }

    /// <summary>Numeracja punktów siatki (x, y) w obrębie jednego szczebla.</summary>
    private sealed class NodeMap
    {
        private readonly Dictionary<(int X, int Y), int> _nodes = new();

        public int Count => _nodes.Count;

        public int Get(int x, int y)
        {
            if (!_nodes.TryGetValue((x, y), out var index))
            {
                index = _nodes.Count;
                _nodes[(x, y)] = index;
            }

            return index;
        }
    }
}
