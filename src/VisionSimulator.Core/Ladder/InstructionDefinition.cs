namespace VisionSimulator.Core.Ladder;

/// <summary>Rola elementu w rozwiązywaniu szczebla.</summary>
internal enum InstructionRole
{
    HorizontalWire,
    VerticalWire,
    Condition,
    Action,
    Return,
}

/// <summary>Warunek (styk, porównanie): zwraca, czy element przewodzi.</summary>
internal delegate bool ConditionHandler(CompiledElement element, LadderExecution execution);

/// <summary>Akcja (cewka, blok funkcyjny): <paramref name="power"/> to stan zasilenia wejścia elementu.</summary>
internal delegate void ActionHandler(CompiledElement element, LadderExecution execution, bool power);

/// <summary>Wpis rejestru instrukcji: opcode, rola, domyślna szerokość w siatce i procedura obsługi.</summary>
internal sealed record InstructionDefinition(
    int Opcode,
    string Name,
    InstructionRole Role,
    int DefaultWidth,
    ConditionHandler? Condition = null,
    ActionHandler? Action = null);
