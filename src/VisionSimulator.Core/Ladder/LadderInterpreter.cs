using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Hmi;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Core.Ladder;

/// <summary>
/// Interpreter drabinki (LAD) jako etap cyklu skanu. Projekt kompilowany jest przy <see cref="Load"/> do niezmiennej
/// struktury i podmieniany atomowo, więc <see cref="Load"/> i <see cref="Unload"/> można wołać z wątku UI w trakcie pracy.
/// </summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Interpreter nie zawiera logiki specyficznej dla projektu — wykonuje wyłącznie semantykę instrukcji z
/// <see cref="InstructionRegistry"/>. Stan zbocza kontaktów P/N jest per element i resetowany przy każdym Load.
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Typy elementów spoza rejestru są wykonywane jako no-op i przepuszczają moc (zob. <see cref="UnsupportedElementTypes"/>).
/// Wywołanie <see cref="Execute"/> wymaga blokady <see cref="PlcMemory.SyncRoot"/> (zapewnia ją <see cref="ScanLoop"/>).
/// </remarks>
public sealed class LadderInterpreter : IScanStep
{
    private readonly HmiState _hmi;
    private readonly AnomalyCounter _anomalies = new();

    private LoadedProgram? _loaded;
    private IReadOnlyCollection<int> _unsupportedElementTypes = [];
    private IReadOnlyList<string> _loadDiagnostics = [];

    public LadderInterpreter(HmiState hmi)
    {
        ArgumentNullException.ThrowIfNull(hmi);
        _hmi = hmi;
    }

    /// <summary>Czy wczytano program.</summary>
    public bool IsLoaded => Volatile.Read(ref _loaded) is not null;

    /// <summary>Typy elementów z projektu, które interpreter traktuje jako no-op.</summary>
    public IReadOnlyCollection<int> UnsupportedElementTypes => Volatile.Read(ref _unsupportedElementTypes);

    /// <summary>Opis wyboru programu głównego oraz ostrzeżenia z kompilacji projektu.</summary>
    public IReadOnlyList<string> LoadDiagnostics => Volatile.Read(ref _loadDiagnostics);

    /// <summary>Liczba anomalii wykonania od ostatniego <see cref="Load"/> (dzielenie przez zero, operand poza zakresem itp.).</summary>
    public long AnomalyCount => _anomalies.Count;

    /// <summary>Opis ostatniej anomalii wykonania albo null.</summary>
    public string? LastAnomaly => _anomalies.LastMessage;

    /// <summary>Kompiluje projekt i atomowo podmienia program; presety T/C trafią do pamięci przy najbliższym skanie.</summary>
    public void Load(VisiLogicProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var program = ProgramCompiler.Compile(project);
        var layout = LadderLayoutBuilder.Build(project, program);
        _anomalies.Clear();
        Volatile.Write(ref _unsupportedElementTypes, program.UnsupportedElementTypes);
        Volatile.Write(ref _loadDiagnostics, program.Diagnostics);
        Volatile.Write(ref _loaded, new LoadedProgram(program, layout));
    }

    /// <summary>Układ drabinki wczytanego programu (pusty bez programu); niezmienny, bezpieczny z dowolnego wątku.</summary>
    public LadderLayout GetLayout() => Volatile.Read(ref _loaded)?.Layout ?? LadderLayout.Empty;

    /// <summary>
    /// Stan szczebli podprogramu po ostatnim skanie: zasilenie elementów i bieżące wartości operandów.
    /// Zwraca null, gdy <paramref name="subroutine"/> nie należy do wczytanego programu (np. po ponownym Load).
    /// </summary>
    /// <remarks>
    /// [OSTRZEŻENIA I KONSEKWENCJE]
    /// Wywołujący musi trzymać <c>lock (memory.SyncRoot)</c> — ślad i pamięć zapisuje wątek skanu pod tą blokadą.
    /// Podprogram wywołany w skanie kilka razy pokazuje stan ostatniego wywołania.
    /// </remarks>
    public LadderSubroutineState? CaptureState(PlcMemory memory, LadderSubroutineLayout subroutine)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(subroutine);

        var loaded = Volatile.Read(ref _loaded);
        if (loaded is null
            || (uint)subroutine.Index >= (uint)loaded.Layout.Subroutines.Count
            || !ReferenceEquals(loaded.Layout.Subroutines[subroutine.Index], subroutine))
        {
            return null;
        }

        var compiled = loaded.Program.Ordered[subroutine.Index];
        var rungs = new LadderRungState[subroutine.Rungs.Count];
        for (var r = 0; r < rungs.Length; r++)
        {
            var layout = subroutine.Rungs[r];
            var trace = loaded.Traces[compiled.Rungs[r].TraceIndex];
            var executed = loaded.Stamp > 0 && trace.Stamp == loaded.Stamp;
            var elements = new LadderElementState[layout.Elements.Count];
            for (var i = 0; i < elements.Length; i++)
            {
                var operands = layout.Elements[i].Operands;
                var values = new string[operands.Count];
                for (var o = 0; o < values.Length; o++)
                {
                    values[o] = operands[o].Address is { } address ? FormatValue(memory, address) : string.Empty;
                }

                elements[i] = new LadderElementState(trace.Input[i], trace.Output[i], trace.Conducts[i], values);
            }

            rungs[r] = new LadderRungState(executed, elements);
        }

        return new LadderSubroutineState(subroutine, rungs);
    }

    // [INTENCJA I POWÓD DZIAŁANIA]
    // Timer i licznik: wartość bieżąca / nastawa oraz bit wyjścia; pozostałe typy jak na ekranie HMI.
    private static string FormatValue(PlcMemory memory, OperandAddress address)
    {
        if (!memory.Contains(address))
        {
            return HmiScreenBuilder.FormatValue(memory, address);
        }

        switch (address.Type)
        {
            case OperandType.T:
                var timer = memory.GetTimer(address.Index);
                return FormattableString.Invariant($"{timer.Current}/{timer.Preset} {(timer.Output ? "ON" : "OFF")}");
            case OperandType.C:
                var counter = memory.GetCounter(address.Index);
                return FormattableString.Invariant($"{counter.Current}/{counter.Preset} {(counter.Output ? "ON" : "OFF")}");
            default:
                return HmiScreenBuilder.FormatValue(memory, address);
        }
    }

    /// <summary>Usuwa program; kolejne skany nie wykonują drabinki.</summary>
    public void Unload()
    {
        Volatile.Write(ref _loaded, null);
        Volatile.Write(ref _unsupportedElementTypes, []);
        Volatile.Write(ref _loadDiagnostics, []);
    }

    public void Execute(PlcMemory memory, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(context);

        var loaded = Volatile.Read(ref _loaded);
        if (loaded is null)
        {
            return;
        }

        var program = loaded.Program;

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Nastawy timerów/liczników i wartości power-up stosujemy w pierwszym skanie sterownika oraz w pierwszym
        // skanie po Load (projekt wczytany, gdy sterownik już pracuje).
        if (!loaded.Initialized || context.IsFirstScan)
        {
            PowerUpInitializer.ApplyPresets(program.Presets, memory);
            PowerUpInitializer.ApplyValues(program.PowerUpValues, memory);
            loaded.Initialized = true;
        }

        loaded.Stamp++;
        new LadderExecution(memory, _hmi, program, loaded.EdgeState, context.Elapsed, _anomalies, loaded.Traces, loaded.Stamp)
            .RunProgram();
    }

    private sealed class LoadedProgram(CompiledProgram program, LadderLayout layout)
    {
        public CompiledProgram Program { get; } = program;

        public LadderLayout Layout { get; } = layout;

        public bool[] EdgeState { get; } = new bool[program.EdgeSlotCount];

        /// <summary>Ślady szczebli indeksowane <see cref="CompiledRung.TraceIndex"/>; zapis tylko w wątku skanu.</summary>
        public RungTrace[] Traces { get; } = CreateTraces(program);

        /// <summary>Numer wykonania programu; zapis w wątku skanu, odczyt pod <c>lock (memory.SyncRoot)</c>.</summary>
        public long Stamp { get; set; }

        /// <summary>Dostępne wyłącznie z wątku skanu.</summary>
        public bool Initialized { get; set; }

        private static RungTrace[] CreateTraces(CompiledProgram program)
        {
            var traces = new RungTrace[program.RungCount];
            foreach (var rung in program.Ordered.SelectMany(s => s.Rungs))
            {
                traces[rung.TraceIndex] = new RungTrace(rung.Elements.Length);
            }

            return traces;
        }
    }
}
