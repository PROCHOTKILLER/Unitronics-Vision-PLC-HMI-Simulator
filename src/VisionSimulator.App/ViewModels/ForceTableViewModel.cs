using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSimulator.Core.Engine;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.App.ViewModels;

/// <summary>Tabela wymuszeń: lista wymuszonych bitów z dodawaniem po adresie, zmianą wartości i zwalnianiem.</summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Źródłem prawdy jest <see cref="ForceTable"/>; wiersze są odtwarzane z <see cref="ForceTable.Snapshot"/> po każdym
/// zdarzeniu <see cref="ForceTable.Changed"/>, więc wymuszenie z tabeli operandów pojawia się tu automatycznie.
/// [OSTRZEŻENIA I KONSEKWENCJE]
/// Wymuszenie nakładają etapy skanu (przed i po drabince) — w STOP stan w pamięci zmieni się dopiero po RUN.
/// </remarks>
public sealed partial class ForceTableViewModel : ObservableObject
{
    private readonly PlcMemory _memory;
    private readonly ForceTable _force;
    private readonly Dispatcher _dispatcher;
    private IReadOnlyDictionary<OperandAddress, string> _descriptions = new Dictionary<OperandAddress, string>();

    [ObservableProperty]
    private string _addressInput = string.Empty;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private string _summary = "Brak wymuszeń.";

    /// <summary>Zakładka widoczna — tylko wtedy stan bitów jest odczytywany co tick timera UI.</summary>
    [ObservableProperty]
    private bool _isActive;

    public ForceTableViewModel(PlcMemory memory, ForceTable force)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(force);
        _memory = memory;
        _force = force;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // [OSTRZEŻENIA I KONSEKWENCJE]
        // Changed może przyjść z dowolnego wątku (np. polecenie PCOM); przebudowa wierszy tylko na wątku UI.
        _force.Changed += (_, _) => _dispatcher.InvokeAsync(Rebuild);
    }

    public ObservableCollection<ForceRow> Rows { get; } = [];

    /// <summary>Zapamiętuje opisy operandów projektu do kolumny „Opis”.</summary>
    public void SetProject(VisiLogicProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        _descriptions = ProjectOperandCatalog.Descriptions(project);
        Rebuild();
    }

    /// <summary>Odczytuje stan wymuszonych bitów w pamięci; wywołujący trzyma <c>lock (memory.SyncRoot)</c>.</summary>
    public IReadOnlyList<KeyValuePair<ForceRow, string>>? CaptureValues()
    {
        if (!IsActive || Rows.Count == 0)
        {
            return null;
        }

        var values = new List<KeyValuePair<ForceRow, string>>(Rows.Count);
        foreach (var row in Rows)
        {
            var value = _memory.Contains(row.Address) ? (_memory.GetBit(row.Address) ? "1" : "0") : "—";
            values.Add(new(row, value));
        }

        return values;
    }

    /// <summary>Zapisuje odczytane stany w wierszach; wołać poza blokadą pamięci.</summary>
    public static void ApplyValues(IReadOnlyList<KeyValuePair<ForceRow, string>>? values)
    {
        if (values is null)
        {
            return;
        }

        foreach (var (row, value) in values)
        {
            row.MemoryValue = value;
        }
    }

    [RelayCommand]
    private void AddOn() => AddFromInput(true);

    [RelayCommand]
    private void AddOff() => AddFromInput(false);

    [RelayCommand]
    private void SetOn(ForceRow? row) => SetValue(row, true);

    [RelayCommand]
    private void SetOff(ForceRow? row) => SetValue(row, false);

    [RelayCommand]
    private void Release(ForceRow? row)
    {
        if (row is not null && _force.Release(row.Address))
        {
            Message = $"Zwolniono {row.Address}.";
        }
    }

    [RelayCommand]
    private void ReleaseAll()
    {
        if (Rows.Count > 0)
        {
            _force.ReleaseAll();
            Message = "Zwolniono wszystkie wymuszenia.";
        }
    }

    private void AddFromInput(bool value)
    {
        if (!OperandAddress.TryParse(AddressInput, out var address))
        {
            Message = "Niepoprawny adres. Przykłady: I 3, O 12, MB 10, SB 2.";
            return;
        }

        if (!address.Type.IsBit())
        {
            Message = $"{address} nie jest bitem — wymuszać można I, O, MB, SB i XB. Rejestr zapisz w zakładce „Tabela operandów”.";
            return;
        }

        bool exists;
        lock (_memory.SyncRoot)
        {
            exists = _memory.Contains(address);
        }

        if (!exists)
        {
            Message = $"Adres {address} jest poza układem pamięci sterownika.";
            return;
        }

        _force.Force(address, value);
        AddressInput = string.Empty;
        Message = $"Wymuszono {address} = {(value ? 1 : 0)}.";
    }

    private void SetValue(ForceRow? row, bool value)
    {
        if (row is not null)
        {
            _force.Force(row.Address, value);
        }
    }

    private void Rebuild()
    {
        var forced = _force.Snapshot();
        var existing = Rows.ToDictionary(r => r.Address);

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Istniejące wiersze są aktualizowane na miejscu, żeby zmiana 1/0 nie gubiła zaznaczenia w tabeli.
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!forced.ContainsKey(Rows[i].Address))
            {
                Rows.RemoveAt(i);
            }
        }

        foreach (var (address, value) in forced.OrderBy(p => p.Key.Type).ThenBy(p => p.Key.Index))
        {
            if (existing.TryGetValue(address, out var row))
            {
                row.ForcedValue = value;
                continue;
            }

            var description = _descriptions.TryGetValue(address, out var text) ? text : string.Empty;
            var insertAt = 0;
            while (insertAt < Rows.Count && Compare(Rows[insertAt].Address, address) < 0)
            {
                insertAt++;
            }

            Rows.Insert(insertAt, new ForceRow(address, description, value));
        }

        Summary = Rows.Count == 0
            ? "Brak wymuszeń."
            : string.Create(CultureInfo.CurrentCulture, $"Wymuszone bity: {Rows.Count:N0}");
    }

    private static int Compare(OperandAddress left, OperandAddress right)
    {
        var byType = left.Type.CompareTo(right.Type);
        return byType != 0 ? byType : left.Index.CompareTo(right.Index);
    }
}
