using System.Globalization;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Core.Ladder;

/// <summary>Kształt elementu drabinki w widoku.</summary>
public enum LadderShape
{
    HorizontalWire,
    VerticalWire,
    Contact,
    Compare,
    Coil,
    Block,
    Return,
}

/// <summary>Operand elementu w widoku: tekst (adres, stała, ekran, podprogram), adres pamięci i opis z projektu.</summary>
public sealed record LadderOperandLayout(string Text, OperandAddress? Address, string Description);

/// <summary>
/// Element drabinki w siatce VisiLogic: pozycja (X, Y), szerokość w polach siatki, wysokość przewodu pionowego,
/// mnemonik i operandy.
/// </summary>
public sealed record LadderElementLayout(
    int Index,
    int Id,
    int Opcode,
    string Mnemonic,
    LadderShape Shape,
    int X,
    int Y,
    int Width,
    int Height,
    IReadOnlyList<LadderOperandLayout> Operands);

/// <summary>Szczebel w widoku; <see cref="Number"/> liczony od 1 w obrębie podprogramu.</summary>
public sealed record LadderRungLayout(
    int Number,
    string Comment,
    bool IsReturnStrip,
    bool Is2D,
    IReadOnlyList<LadderElementLayout> Elements)
{
    /// <summary>Szerokość szczebla w polach siatki (prawa krawędź najdalszego elementu).</summary>
    public int Width { get; } = Elements.Count == 0 ? 0 : Elements.Max(e => e.X + e.Width);
}

/// <summary>Podprogram w widoku; <see cref="Index"/> to pozycja w projekcie (identyfikator może być zdublowany).</summary>
public sealed record LadderSubroutineLayout(
    int Index,
    int Id,
    string Name,
    bool IsEntryPoint,
    IReadOnlyList<LadderRungLayout> Rungs)
{
    public string DisplayName => IsEntryPoint ? $"{Name} (FuncID {Id}, wykonywany w skanie)" : $"{Name} (FuncID {Id})";
}

/// <summary>Niezmienny układ całego programu LAD dla widoku drabinki.</summary>
public sealed record LadderLayout(IReadOnlyList<LadderSubroutineLayout> Subroutines)
{
    private Lazy<Dictionary<OperandAddress, LadderOperandUsage[]>>? _usages;

    public static LadderLayout Empty { get; } = new([]);

    /// <summary>Wszystkie operandy pamięci użyte w drabince (bez powtórzeń).</summary>
    public IEnumerable<OperandAddress> UsedAddresses => Usages.Keys;

    /// <summary>Miejsca użycia adresu w drabince: podprogram, numer szczebla i mnemonik elementu.</summary>
    public IReadOnlyList<LadderOperandUsage> FindUsages(OperandAddress address) =>
        Usages.TryGetValue(address, out var usages) ? usages : [];

    // [OPTYMALIZACJA WYDAJNOŚCI (PERFORMANCE)]
    // Indeks budowany leniwie przy pierwszym zapytaniu (Lazy jest bezpieczny wątkowo); układ jest niezmienny.
    private Dictionary<OperandAddress, LadderOperandUsage[]> Usages =>
        (_usages ??= new Lazy<Dictionary<OperandAddress, LadderOperandUsage[]>>(BuildUsages)).Value;

    private Dictionary<OperandAddress, LadderOperandUsage[]> BuildUsages()
    {
        var index = new Dictionary<OperandAddress, List<LadderOperandUsage>>();
        foreach (var subroutine in Subroutines)
        {
            foreach (var rung in subroutine.Rungs)
            {
                foreach (var element in rung.Elements)
                {
                    foreach (var operand in element.Operands)
                    {
                        if (operand.Address is not { } address)
                        {
                            continue;
                        }

                        if (!index.TryGetValue(address, out var list))
                        {
                            list = [];
                            index[address] = list;
                        }

                        var usage = new LadderOperandUsage(subroutine.Name, rung.Number, element.Mnemonic);
                        if (!list.Contains(usage))
                        {
                            list.Add(usage);
                        }
                    }
                }
            }
        }

        return index.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
    }
}

/// <summary>Użycie operandu w drabince.</summary>
public sealed record LadderOperandUsage(string SubroutineName, int RungNumber, string Mnemonic)
{
    public override string ToString() => $"{SubroutineName}, szczebel {RungNumber} ({Mnemonic})";
}

/// <summary>Stan elementu po ostatnim wykonaniu: zasilenie wejścia i wyjścia, wynik warunku i wartości operandów.</summary>
public sealed record LadderElementState(bool Input, bool Output, bool Conducts, IReadOnlyList<string> Values);

/// <summary>Stan szczebla; <see cref="Executed"/> = rozwiązany w ostatnim skanie.</summary>
public sealed record LadderRungState(bool Executed, IReadOnlyList<LadderElementState> Elements)
{
    // [INTENCJA I POWÓD DZIAŁANIA]
    // Porównanie treści (rekord porównuje listy przez referencję): widok przerysowuje szczebel tylko przy zmianie.
    public bool ContentEquals(LadderRungState? other)
    {
        if (other is null || other.Executed != Executed || other.Elements.Count != Elements.Count)
        {
            return false;
        }

        for (var i = 0; i < Elements.Count; i++)
        {
            var a = Elements[i];
            var b = other.Elements[i];
            if (a.Input != b.Input || a.Output != b.Output || a.Conducts != b.Conducts || !a.Values.SequenceEqual(b.Values))
            {
                return false;
            }
        }

        return true;
    }
}

/// <summary>Migawka stanu jednego podprogramu.</summary>
public sealed record LadderSubroutineState(LadderSubroutineLayout Subroutine, IReadOnlyList<LadderRungState> Rungs);

/// <summary>Buduje <see cref="LadderLayout"/> z projektu i jego skompilowanej postaci (elementy 1:1).</summary>
internal static class LadderLayoutBuilder
{
    private const int MaxSpan = 512;

    public static LadderLayout Build(VisiLogicProject project, CompiledProgram program)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(program);

        var descriptions = new Dictionary<OperandAddress, string>();
        foreach (var operand in project.Operands)
        {
            if (operand.Memory is { } address && operand.Description.Length > 0)
            {
                descriptions.TryAdd(address, operand.Description);
            }
        }

        var displays = new Dictionary<int, string>();
        foreach (var display in project.Displays)
        {
            displays.TryAdd(display.Id, display.Name);
        }

        var subroutineNames = new Dictionary<int, string>();
        foreach (var subroutine in project.Subroutines)
        {
            subroutineNames.TryAdd(subroutine.Id, subroutine.Name);
        }

        var context = new NameContext(descriptions, displays, subroutineNames);
        var count = Math.Min(project.Subroutines.Count, program.Ordered.Length);
        var result = new LadderSubroutineLayout[count];
        for (var s = 0; s < count; s++)
        {
            var source = project.Subroutines[s];
            var compiled = program.Ordered[s];
            var rungs = new LadderRungLayout[Math.Min(source.Rungs.Count, compiled.Rungs.Length)];
            for (var r = 0; r < rungs.Length; r++)
            {
                rungs[r] = BuildRung(r + 1, source.Rungs[r], compiled.Rungs[r], context);
            }

            result[s] = new LadderSubroutineLayout(
                s,
                source.Id,
                source.Name ?? string.Empty,
                program.EntryPoints.Any(e => ReferenceEquals(e, compiled)),
                rungs);
        }

        return new LadderLayout(result);
    }

    private static LadderRungLayout BuildRung(int number, LadderRung source, CompiledRung compiled, NameContext context)
    {
        var count = Math.Min(source.Elements.Count, compiled.Elements.Length);
        var elements = new LadderElementLayout[count];
        var cursor = 0;
        for (var i = 0; i < count; i++)
        {
            var element = source.Elements[i];
            var definition = compiled.Elements[i].Definition;
            var shape = ShapeOf(element.Type, definition.Role);
            var defaultWidth = definition.DefaultWidth > 0 ? definition.DefaultWidth : InstructionRegistry.DefaultBlockWidth;
            var dx = Math.Clamp(element.Dx, 0, MaxSpan);
            var dy = Math.Clamp(element.Dy, 0, MaxSpan);

            int x, y, width;
            if (compiled.Is2D)
            {
                x = Math.Max(0, element.Xs);
                y = Math.Max(0, element.Ys);
                width = shape switch
                {
                    LadderShape.HorizontalWire => Math.Max(1, dx),
                    LadderShape.VerticalWire => 0,
                    _ => dx > 0 ? dx : defaultWidth,
                };
            }
            else
            {
                // [ARCHITEKTURA I ADR]
                // Szczebel bez współrzędnych rysujemy jako szereg w jednym wierszu, w kolejności elementów.
                x = cursor;
                y = 0;
                width = defaultWidth;
                cursor += width;
            }

            var mnemonic = ReferenceEquals(definition, InstructionRegistry.NoOp)
                ? "?" + element.Type.ToString(CultureInfo.InvariantCulture)
                : definition.Name;

            elements[i] = new LadderElementLayout(
                i,
                element.Id,
                element.Type,
                mnemonic,
                shape,
                x,
                y,
                width,
                shape == LadderShape.VerticalWire ? Math.Max(1, dy) : 0,
                [.. element.Operands.Select(context.Describe)]);
        }

        return new LadderRungLayout(number, source.Comment ?? string.Empty, source.IsReturnStrip, compiled.Is2D, elements);
    }

    private static LadderShape ShapeOf(int opcode, InstructionRole role) => role switch
    {
        InstructionRole.HorizontalWire => LadderShape.HorizontalWire,
        InstructionRole.VerticalWire => LadderShape.VerticalWire,
        InstructionRole.Return => LadderShape.Return,
        InstructionRole.Condition => opcode is >= 200 and <= 203 ? LadderShape.Contact : LadderShape.Compare,
        _ => opcode is 204 or 205 or 206 or 207 or 212 ? LadderShape.Coil : LadderShape.Block,
    };

    private sealed class NameContext(
        Dictionary<OperandAddress, string> descriptions,
        Dictionary<int, string> displays,
        Dictionary<int, string> subroutines)
    {
        public LadderOperandLayout Describe(ElementOperand operand)
        {
            var address = operand.Address.ToString(CultureInfo.InvariantCulture);
            switch (operand.Kind)
            {
                case ElementOperandKind.Memory when operand.Memory is { } memory:
                    return new LadderOperandLayout(memory.ToString(), memory, descriptions.GetValueOrDefault(memory, string.Empty));
                case ElementOperandKind.Constant:
                    return new LadderOperandLayout(
                        (operand.ConstantValue ?? 0).ToString(CultureInfo.InvariantCulture),
                        null,
                        "stała");
                case ElementOperandKind.HmiPage:
                    return new LadderOperandLayout("Ekran " + address, null, displays.GetValueOrDefault(operand.Address, string.Empty));
                case ElementOperandKind.Subroutine:
                    return new LadderOperandLayout(
                        subroutines.GetValueOrDefault(operand.Address, "FuncID " + address),
                        null,
                        "podprogram");
                default:
                    return new LadderOperandLayout(
                        "?" + operand.TypeCode.ToString(CultureInfo.InvariantCulture) + ":" + address,
                        null,
                        "nierozpoznany kod typu operandu");
            }
        }
    }
}
