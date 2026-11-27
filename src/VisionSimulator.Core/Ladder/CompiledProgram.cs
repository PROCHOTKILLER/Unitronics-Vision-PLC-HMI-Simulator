using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Core.Ladder;

/// <summary>Operand elementu LAD po kompilacji: rodzaj, adres pamięci albo stała.</summary>
internal sealed class CompiledOperand
{
    public required ElementOperandKind Kind { get; init; }

    /// <summary>Adres w pamięci; ważny tylko dla <see cref="ElementOperandKind.Memory"/>.</summary>
    public OperandAddress Address { get; init; }

    public long Constant { get; init; }

    /// <summary>Surowy adres z bazy (np. identyfikator podprogramu lub ekranu).</summary>
    public int RawAddress { get; init; }

    public bool IsMemory => Kind == ElementOperandKind.Memory;

    public bool IsFloat => IsMemory && Address.Type == OperandType.MF;

    public bool IsTimer => IsMemory && Address.Type == OperandType.T;

    public bool IsCounter => IsMemory && Address.Type == OperandType.C;
}

/// <summary>Element LAD po kompilacji wraz z wyznaczonymi węzłami siatki 2D.</summary>
internal sealed class CompiledElement
{
    public required int Id { get; init; }

    public required int Type { get; init; }

    public required int Xs { get; init; }

    public required int Ys { get; init; }

    public required int MoreParamsLng { get; init; }

    public required InstructionDefinition Definition { get; init; }

    public required CompiledOperand[] Operands { get; init; }

    /// <summary>Indeks pamięci zbocza (kontakty P/N); -1, gdy element nie ma stanu.</summary>
    public int EdgeSlot { get; init; } = -1;

    /// <summary>Węzeł wejściowy siatki 2D (Xs, Ys); -1 w szczeblu liniowym.</summary>
    public int InNode { get; init; } = -1;

    /// <summary>Węzeł wyjściowy dla styków, porównań i bloków; -1 dla przewodów i RET.</summary>
    public int OutNode { get; init; } = -1;

    /// <summary>Węzły obejmowane przez przewód poziomy (po prawej od wejścia) lub pionowy (łącznie z początkiem).</summary>
    public int[] SpanNodes { get; init; } = [];
}

/// <summary>Szczebel po kompilacji: tryb liniowy albo siatka 2D z gotową numeracją węzłów.</summary>
internal sealed class CompiledRung
{
    public required bool IsReturnStrip { get; init; }

    public required bool Is2D { get; init; }

    public required CompiledElement[] Elements { get; init; }

    public int NodeCount { get; init; }

    /// <summary>Węzły lewej szyny zasilającej (X = 0), zasilone zawsze.</summary>
    public int[] SeedNodes { get; init; } = [];

    /// <summary>Numer szczebla w całym programie (indeks śladu wykonania <see cref="RungTrace"/>).</summary>
    public int TraceIndex { get; init; }
}

/// <summary>Ślad ostatniego wykonania szczebla dla widoku drabinki: zasilenie wejścia i wyjścia oraz stan warunku każdego elementu.</summary>
/// <remarks>
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Zapisuje go wyłącznie wątek skanu; odczyt z innego wątku tylko pod <c>lock (memory.SyncRoot)</c>.
/// </remarks>
internal sealed class RungTrace(int elementCount)
{
    public bool[] Input { get; } = new bool[elementCount];

    public bool[] Output { get; } = new bool[elementCount];

    /// <summary>Wynik warunku niezależnie od zasilenia (styk zamknięty, porównanie spełnione); dla pozostałych ról true.</summary>
    public bool[] Conducts { get; } = new bool[elementCount];

    /// <summary>Numer wykonania programu, w którym szczebel był ostatnio rozwiązany; -1 = jeszcze nigdy.</summary>
    public long Stamp { get; set; } = -1;
}

internal sealed class CompiledSubroutine
{
    public required int Id { get; init; }

    public required string Name { get; init; }

    public required CompiledRung[] Rungs { get; init; }
}

/// <summary>Wartość początkowa operandu stosowana przy pierwszym skanie.</summary>
internal readonly record struct PowerUpValue(OperandAddress Address, long IntegerValue, double FloatValue);

/// <summary>Nastawa timera lub licznika (T w jednostkach 10 ms).</summary>
internal readonly record struct PresetValue(OperandAddress Address, long Value);

/// <summary>Niezmienny, skompilowany program LAD; podmieniany atomowo przy <c>Load</c>.</summary>
internal sealed class CompiledProgram
{
    public required IReadOnlyDictionary<int, CompiledSubroutine> Subroutines { get; init; }

    /// <summary>Wszystkie podprogramy w kolejności projektu (także zdublowane identyfikatory), 1:1 z <c>VisiLogicProject.Subroutines</c>.</summary>
    public required CompiledSubroutine[] Ordered { get; init; }

    /// <summary>Liczba szczebli całego programu (rozmiar tablicy śladów).</summary>
    public required int RungCount { get; init; }

    /// <summary>Podprogramy wykonywane w każdym skanie (główny albo wszystkie po kolei).</summary>
    public required CompiledSubroutine[] EntryPoints { get; init; }

    public required int EdgeSlotCount { get; init; }

    public required IReadOnlyList<PowerUpValue> PowerUpValues { get; init; }

    public required IReadOnlyList<PresetValue> Presets { get; init; }

    public required IReadOnlyList<int> UnsupportedElementTypes { get; init; }

    public required IReadOnlyList<string> Diagnostics { get; init; }
}
