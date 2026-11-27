using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.Core.Hmi;

/// <summary>Krok skanu: warunki skoku ekranu z tabeli HMIJumps dla aktualnie wyświetlanego ekranu.</summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Krok jest rejestrowany na końcu skanu, więc widzi bity po drabince i wymuszeniach. Tabela skoków jest
/// niezmienna i podmieniana atomowo (<see cref="Load"/> z wątku UI); stan zboczy należy wyłącznie do wątku skanu.
/// [ZADANIA DO WYKONANIA (TODO)]
/// Semantyka warunku (zbocze narastające bitu) do potwierdzenia na sterowniku. Na V350 tabela jest zwykle pusta —
/// nawigację realizuje wtedy drabinka (instrukcja skoku do ekranu).
/// </remarks>
public sealed class HmiJumpStep : IScanStep
{
    private readonly HmiState _hmi;
    private JumpTable? _table;

    // [OSTRZEŻENIA I KONSEKWENCJE]
    // Pola poniżej zmienia tylko wątek skanu (Execute).
    private JumpTable? _armedTable;
    private int _armedDisplayId = int.MinValue;
    private bool[] _previous = [];

    public HmiJumpStep(HmiState hmi)
    {
        ArgumentNullException.ThrowIfNull(hmi);
        _hmi = hmi;
    }

    /// <summary>Liczba skoków ekranów we wczytanym projekcie.</summary>
    public int JumpCount => Volatile.Read(ref _table)?.Count ?? 0;

    /// <summary>Wczytuje warunki skoku wszystkich ekranów projektu.</summary>
    public void Load(IReadOnlyList<HmiDisplay> displays)
    {
        ArgumentNullException.ThrowIfNull(displays);

        var byDisplay = new Dictionary<int, HmiJump[]>();
        var count = 0;
        foreach (var display in displays)
        {
            var jumps = display.Jumps.Where(IsUsable).OrderBy(j => j.Order).ToArray();
            if (jumps.Length > 0 && byDisplay.TryAdd(display.Id, jumps))
            {
                count += jumps.Length;
            }
        }

        Volatile.Write(ref _table, new JumpTable(byDisplay, displays.Select(d => d.Id).ToHashSet(), count));
    }

    public void Unload() => Volatile.Write(ref _table, null);

    public void Execute(PlcMemory memory, ScanContext context)
    {
        ArgumentNullException.ThrowIfNull(memory);

        var table = Volatile.Read(ref _table);
        var displayId = _hmi.CurrentDisplayId;
        if (table is null || !table.ByDisplay.TryGetValue(displayId, out var jumps))
        {
            _armedTable = table;
            _armedDisplayId = displayId;
            _previous = [];
            return;
        }

        // [WYJAŚNIENIE ALGORYTMU I ZAAWANSOWANEJ MATEMATYKI]
        // Po wejściu na ekran (lub po wczytaniu projektu) zapamiętujemy bieżące stany bitów bez skoku. Skok wykonuje
        // dopiero zbocze 0 → 1, więc bit trzymany po poprzednim skoku nie przerzuca od razu na kolejny ekran.
        if (!ReferenceEquals(table, _armedTable) || displayId != _armedDisplayId || _previous.Length != jumps.Length)
        {
            _armedTable = table;
            _armedDisplayId = displayId;
            _previous = new bool[jumps.Length];
            for (var i = 0; i < jumps.Length; i++)
            {
                _previous[i] = ReadCondition(memory, jumps[i]);
            }

            return;
        }

        for (var i = 0; i < jumps.Length; i++)
        {
            var now = ReadCondition(memory, jumps[i]);
            var rising = now && !_previous[i];
            _previous[i] = now;
            if (rising && TryResolveTarget(memory, jumps[i], table, out var target) && target != displayId)
            {
                _hmi.ShowDisplay(target);
                return;
            }
        }
    }

    private static bool IsUsable(HmiJump jump) =>
        jump.OperandAddress >= 0
        && VisiLogicOperandCodes.TryGetOperandType(jump.OperandTypeCode, out var type)
        && type.IsBit();

    private static bool ReadCondition(PlcMemory memory, HmiJump jump) =>
        VisiLogicOperandCodes.TryGetOperandType(jump.OperandTypeCode, out var type)
        && new OperandAddress(type, jump.OperandAddress) is var address
        && memory.Contains(address)
        && memory.GetBit(address);

    private static bool TryResolveTarget(PlcMemory memory, HmiJump jump, JumpTable table, out int target)
    {
        target = jump.TargetAddress;
        if (jump.TargetTypeCode != HmiJump.DisplayTargetCode)
        {
            // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
            // Cel w rejestrze: numer ekranu czytany z pamięci; rejestr spoza pamięci lub typ bitowy = brak skoku.
            if (!VisiLogicOperandCodes.TryGetOperandType(jump.TargetTypeCode, out var type) || type.IsBit())
            {
                return false;
            }

            var register = new OperandAddress(type, jump.TargetAddress);
            if (!memory.Contains(register))
            {
                return false;
            }

            var value = type == OperandType.MF ? (long)memory.GetFloat(register.Index) : memory.GetNumber(register);
            if (value is < int.MinValue or > int.MaxValue)
            {
                return false;
            }

            target = (int)value;
        }

        // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
        // Skok do nieistniejącego ekranu jest pomijany, żeby panel nie został pusty.
        return table.DisplayIds.Contains(target);
    }

    private sealed record JumpTable(IReadOnlyDictionary<int, HmiJump[]> ByDisplay, IReadOnlySet<int> DisplayIds, int Count);
}
