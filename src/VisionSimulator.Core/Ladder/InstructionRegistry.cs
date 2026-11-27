namespace VisionSimulator.Core.Ladder;

/// <summary>
/// Rejestr instrukcji LAD: opcode → definicja.
/// </summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Nową instrukcję dodaje się jednym wpisem w odpowiedniej grupie (<c>Definitions()</c>), bez zmian w solverze
/// ani interpreterze (zasada otwarte-zamknięte).
/// </remarks>
internal static class InstructionRegistry
{
    public const int HorizontalWireOpcode = 100;
    public const int VerticalWireOpcode = 101;
    public const int ReturnOpcode = 602;
    public const int DefaultBlockWidth = 6;

    private static readonly Dictionary<int, InstructionDefinition> Definitions = Build();

    /// <summary>Definicja dla typów nieobsługiwanych: element przepuszcza moc i nic nie robi.</summary>
    public static InstructionDefinition NoOp { get; } = new(
        -1,
        "NOP",
        InstructionRole.Action,
        DefaultBlockWidth,
        Action: static (_, _, _) => { });

    public static IReadOnlyCollection<int> Opcodes => Definitions.Keys;

    public static bool TryGet(int opcode, out InstructionDefinition definition) =>
        Definitions.TryGetValue(opcode, out definition!);

    private static Dictionary<int, InstructionDefinition> Build()
    {
        var map = new Dictionary<int, InstructionDefinition>
        {
            [HorizontalWireOpcode] = new(HorizontalWireOpcode, "WIRE-H", InstructionRole.HorizontalWire, 0),
            [VerticalWireOpcode] = new(VerticalWireOpcode, "WIRE-V", InstructionRole.VerticalWire, 0),
            [ReturnOpcode] = new(ReturnOpcode, "RET", InstructionRole.Return, DefaultBlockWidth),
        };

        foreach (var definition in ContactInstructions.Definitions()
                     .Concat(CoilInstructions.Definitions())
                     .Concat(ArithmeticInstructions.Definitions())
                     .Concat(FlowInstructions.Definitions()))
        {
            map.Add(definition.Opcode, definition);
        }

        return map;
    }
}
