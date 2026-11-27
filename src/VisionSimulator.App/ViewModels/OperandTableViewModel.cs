using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Hmi;
using VisionSimulator.Core.Ladder;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.App.ViewModels;

/// <summary>Tabela operandów projektu: wszystkie operandy z opisem, użyciem w LAD/HMI i wartością na żywo.</summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Wiersze powstają przy wczytaniu projektu (<see cref="SetProject"/>), filtr podmienia całą listę <see cref="Rows"/>
/// naraz (jedno powiadomienie zamiast tysięcy zdarzeń kolekcji). Odczyt wartości dzieli się na
/// <see cref="CaptureValues"/> (pod blokadą pamięci) i <see cref="ApplyValues"/> (poza nią).
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Przełączenie bitu wymuszonego jest nadpisywane w następnym skanie — wtedy wiersz pokazuje komunikat.
/// </remarks>
public sealed partial class OperandTableViewModel : ObservableObject
{
    public const string AllTypes = "Wszystkie";

    private readonly PlcMemory _memory;
    private readonly ForceTable _force;
    private readonly Dispatcher _dispatcher;
    private OperandRow[] _all = [];

    [ObservableProperty]
    private IReadOnlyList<OperandRow> _rows = [];

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _typeFilter = AllTypes;

    [ObservableProperty]
    private bool _usedOnly;

    [ObservableProperty]
    private string _summary = "Wczytaj projekt, aby zobaczyć operandy.";

    [ObservableProperty]
    private string _message = string.Empty;

    /// <summary>Zakładka widoczna — tylko wtedy wartości są odczytywane co tick timera UI.</summary>
    [ObservableProperty]
    private bool _isActive;

    public OperandTableViewModel(PlcMemory memory, ForceTable force)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(force);
        _memory = memory;
        _force = force;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // [OSTRZEŻENIA I KONSEKWENCJE]
        // Changed może przyjść z dowolnego wątku; modyfikacja wierszy tylko na wątku UI.
        _force.Changed += (_, _) => _dispatcher.InvokeAsync(RefreshForced);
    }

    public ObservableCollection<string> Types { get; } = [AllTypes];

    /// <summary>Buduje wiersze z operandów projektu; <paramref name="layout"/> to układ wczytanej drabinki.</summary>
    public void SetProject(VisiLogicProject project, LadderLayout layout)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(layout);

        IReadOnlyList<ProjectOperand> operands;
        lock (_memory.SyncRoot)
        {
            operands = ProjectOperandCatalog.Build(project, layout, _memory);
        }

        _all = operands
            .Select(o => new OperandRow(o.Address, o.Description, ProjectOperandCatalog.FormatUsage(o), ProjectOperandCatalog.FormatUsageDetails(o)))
            .ToArray();

        Types.Clear();
        Types.Add(AllTypes);
        foreach (var type in _all.Select(r => r.Address.Type).Distinct().Order())
        {
            Types.Add(type.ToString());
        }

        if (!Types.Contains(TypeFilter))
        {
            TypeFilter = AllTypes;
        }

        Message = string.Empty;
        ApplyFilter();
        RefreshForced();
    }

    /// <summary>Odczytuje wartości widocznych wierszy; wywołujący trzyma <c>lock (memory.SyncRoot)</c>.</summary>
    public IReadOnlyList<KeyValuePair<OperandRow, string>>? CaptureValues()
    {
        if (!IsActive)
        {
            return null;
        }

        // [OPTYMALIZACJA WYDAJNOŚCI (PERFORMANCE)]
        // Odczyt tylko wierszy po filtrze; formatowanie kilku tysięcy wartości mieści się w ułamku milisekundy.
        var rows = Rows;
        var values = new List<KeyValuePair<OperandRow, string>>(rows.Count);
        foreach (var row in rows)
        {
            values.Add(new(row, HmiScreenBuilder.FormatValue(_memory, row.Address)));
        }

        return values;
    }

    /// <summary>Zapisuje odczytane wartości w wierszach; wołać poza blokadą pamięci.</summary>
    public static void ApplyValues(IReadOnlyList<KeyValuePair<OperandRow, string>>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var (row, value) in values)
        {
            row.Value = value;
        }
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    partial void OnTypeFilterChanged(string value) => ApplyFilter();

    partial void OnUsedOnlyChanged(bool value) => ApplyFilter();

    [RelayCommand]
    private void ClearFilter()
    {
        FilterText = string.Empty;
        TypeFilter = AllTypes;
        UsedOnly = false;
    }

    [RelayCommand]
    private void Toggle(OperandRow? row)
    {
        if (row is null || !row.IsBit)
        {
            return;
        }

        bool value;
        lock (_memory.SyncRoot)
        {
            value = !_memory.GetBit(row.Address);
            _memory.SetBit(row.Address, value);
        }

        row.Message = _force.Snapshot().ContainsKey(row.Address)
            ? "Bit jest wymuszony — skan przywróci wartość wymuszenia."
            : string.Empty;
        Message = $"{row.Address} = {(value ? 1 : 0)}";
    }

    [RelayCommand]
    private void Force(OperandRow? row)
    {
        if (row is null || !row.IsBit)
        {
            return;
        }

        bool value;
        lock (_memory.SyncRoot)
        {
            value = _memory.GetBit(row.Address);
        }

        _force.Force(row.Address, value);
        Message = $"Wymuszono {row.Address} = {(value ? 1 : 0)} — zmień lub zwolnij w zakładce „Tabela wymuszeń”.";
    }

    [RelayCommand]
    private void Write(OperandRow? row)
    {
        if (row is null || !row.IsWritable)
        {
            return;
        }

        bool written;
        string error;
        lock (_memory.SyncRoot)
        {
            written = HmiValueEntry.TryWrite(_memory, row.Address, row.Input, out error);
        }

        row.Message = written ? string.Empty : error;
        if (written)
        {
            Message = $"{row.Address} = {row.Input.Trim()}";
            row.Input = string.Empty;
        }
    }

    private void ApplyFilter()
    {
        var text = FilterText.Trim();
        var hasType = TypeFilter != AllTypes && Enum.TryParse<OperandType>(TypeFilter, out _);
        var type = hasType ? Enum.Parse<OperandType>(TypeFilter) : default;

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Wpis adresu („MB 10”, „mb10”) filtruje po dokładnym adresie; inny tekst — po fragmencie adresu lub opisu.
        OperandAddress? exact = OperandAddress.TryParse(text, out var parsed) ? parsed : null;

        var rows = _all.Where(r =>
                (!hasType || r.Address.Type == type)
                && (!UsedOnly || r.Usage != "—")
                && (text.Length == 0
                    || (exact is { } address
                        ? r.Address == address
                        : r.AddressText.Contains(text, StringComparison.OrdinalIgnoreCase)
                          || r.Description.Contains(text, StringComparison.OrdinalIgnoreCase))))
            .ToArray();

        Rows = rows;
        Summary = _all.Length == 0
            ? "Projekt nie ma operandów w pamięci sterownika."
            : string.Create(CultureInfo.CurrentCulture, $"Operandy projektu: {_all.Length:N0} | pokazane: {rows.Length:N0}");
    }

    private void RefreshForced()
    {
        var forced = _force.Snapshot();
        foreach (var row in _all)
        {
            row.ForceText = forced.TryGetValue(row.Address, out var value) ? (value ? "WYMUSZONY = 1" : "WYMUSZONY = 0") : string.Empty;
        }
    }
}
