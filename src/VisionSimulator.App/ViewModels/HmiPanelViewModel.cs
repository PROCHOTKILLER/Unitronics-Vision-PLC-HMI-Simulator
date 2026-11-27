using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VisionSimulator.App.Services;
using VisionSimulator.Core.Hmi;
using VisionSimulator.Core.Ladder;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.App.ViewModels;

/// <summary>Pozycja listy ekranów HMI projektu.</summary>
public sealed record DisplayChoice(int Id, string Name)
{
    public string Label => Name.Length == 0 ? $"Ekran {Id}" : $"{Id}: {Name}";
}

/// <summary>Panel HMI: lista ekranów, bieżąca migawka ekranu, dotyk, wpis wartości i diagnostyka dotyku.</summary>
/// <remarks>
/// [ARCHITEKTURA I ADR]
/// Migawkę buduje <see cref="CaptureScreen"/> pod blokadą pamięci (wołaną przez <see cref="MainViewModel"/>),
/// a <see cref="ApplyScreen"/> publikuje ją do interfejsu już poza blokadą.
/// </remarks>
public sealed partial class HmiPanelViewModel : ObservableObject
{
    // [INTENCJA I POWÓD DZIAŁANIA]
    // Kliknięcie myszą trwa często krócej niż kilka cykli skanu; bit dotyku trzymamy co najmniej tyle,
    // żeby drabinka (styk P, SET, skok do ekranu) na pewno go zobaczyła.
    private static readonly TimeSpan MinimumTouchHold = TimeSpan.FromMilliseconds(150);

    private readonly PlcMemory _memory;
    private readonly HmiState _hmi;
    private readonly HmiJumpStep _jumps;
    private readonly LadderInterpreter _ladder;
    private readonly IValueEntryDialog _valueEntry;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<OperandAddress, (long Stamp, int Generation)> _touches = [];
    private IReadOnlyDictionary<int, HmiDisplay> _displaysById = new Dictionary<int, HmiDisplay>();
    private bool _isSyncingSelection;

    [ObservableProperty]
    private DisplayChoice? _selectedDisplay;

    [ObservableProperty]
    private HmiScreen? _screen;

    [ObservableProperty]
    private bool _hasDisplays;

    [ObservableProperty]
    private bool _isPlcStopped = true;

    [ObservableProperty]
    private string _touchStatus = "Dotyk: kliknij element ekranu.";

    [ObservableProperty]
    private string _jumpSummary = string.Empty;

    public HmiPanelViewModel(PlcMemory memory, HmiState hmi, HmiJumpStep jumps, LadderInterpreter ladder, IValueEntryDialog valueEntry)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(hmi);
        ArgumentNullException.ThrowIfNull(jumps);
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(valueEntry);
        _memory = memory;
        _hmi = hmi;
        _jumps = jumps;
        _ladder = ladder;
        _valueEntry = valueEntry;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // [OSTRZEŻENIA I KONSEKWENCJE]
        // DisplayChanged przychodzi z wątku skanu: obsługa tylko przekazuje pracę na wątek UI i nie blokuje.
        _hmi.DisplayChanged += OnDisplayChanged;
    }

    public ObservableCollection<DisplayChoice> Displays { get; } = [];

    /// <summary>Podmienia listę ekranów i tablicę skoków po wczytaniu projektu i wybiera istniejący ekran.</summary>
    public void SetProject(VisiLogicProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var byId = new Dictionary<int, HmiDisplay>();
        foreach (var display in project.Displays)
        {
            byId.TryAdd(display.Id, display);
        }

        _displaysById = byId;
        _jumps.Load(project.Displays);
        JumpSummary = _jumps.JumpCount == 0
            ? "Skoki ekranów (HMIJumps): brak — nawigacja przez drabinkę (bit MB z przycisku → skok do ekranu, tylko w RUN)."
            : string.Create(CultureInfo.CurrentCulture, $"Skoki ekranów (HMIJumps): {_jumps.JumpCount} — działają w RUN, na zbocze bitu.");

        _isSyncingSelection = true;
        try
        {
            Displays.Clear();
            foreach (var display in byId.Values.OrderBy(d => d.Id))
            {
                Displays.Add(new DisplayChoice(display.Id, display.Name ?? string.Empty));
            }

            HasDisplays = Displays.Count > 0;
        }
        finally
        {
            _isSyncingSelection = false;
        }

        if (Displays.Count > 0 && !byId.ContainsKey(_hmi.CurrentDisplayId))
        {
            _hmi.ShowDisplay(Displays[0].Id);
        }

        SyncSelection();
    }

    /// <summary>Buduje migawkę bieżącego ekranu; wywołujący trzyma <c>lock (memory.SyncRoot)</c>.</summary>
    public HmiScreen? CaptureScreen() =>
        _displaysById.TryGetValue(_hmi.CurrentDisplayId, out var display)
            ? HmiScreenBuilder.Build(display, _memory)
            : null;

    /// <summary>Publikuje migawkę do interfejsu; pomija zapis, gdy zawartość się nie zmieniła.</summary>
    public void ApplyScreen(HmiScreen? screen)
    {
        if (Screen is { } current && screen is not null
            && current.DisplayId == screen.DisplayId
            && current.Name == screen.Name
            && current.BackgroundArgb == screen.BackgroundArgb
            && current.Items.SequenceEqual(screen.Items))
        {
            return;
        }

        Screen = screen;
    }

    partial void OnSelectedDisplayChanged(DisplayChoice? value)
    {
        if (!_isSyncingSelection && value is not null)
        {
            _hmi.ShowDisplay(value.Id);
        }
    }

    [RelayCommand]
    private void Press(HmiRenderItem? item)
    {
        if (item?.TouchAddress is not { } address)
        {
            return;
        }

        var generation = _touches.TryGetValue(address, out var previous) ? previous.Generation + 1 : 1;
        _touches[address] = (Stopwatch.GetTimestamp(), generation);

        bool written;
        lock (_memory.SyncRoot)
        {
            written = HmiTouch.Press(_memory, address, pressed: true);
        }

        TouchStatus = written
            ? $"Dotyk: element {item.ElementId} → {address} = 1 | {DescribeLadderUsage(address)}{StopHint()}"
            : $"Dotyk: element {item.ElementId} → {address} poza układem pamięci (zignorowano).";
    }

    // [INTENCJA I POWÓD DZIAŁANIA]
    // Diagnostyka nawigacji: przycisk zmienia ekran tylko wtedy, gdy drabinka (lub tablica skoków) czyta jego bit.
    // Brak użycia w LAD wskazuje, że akcja przycisku jest zapisana w danych projektu, których symulator nie czyta.
    private string DescribeLadderUsage(OperandAddress address)
    {
        var layout = _ladder.GetLayout();
        if (layout.Subroutines.Count == 0)
        {
            return "drabinka niewczytana";
        }

        var usages = layout.FindUsages(address);
        if (usages.Count == 0)
        {
            return "bit nie występuje w drabince";
        }

        const int Shown = 3;
        var text = string.Join("; ", usages.Take(Shown));
        return usages.Count > Shown
            ? string.Create(CultureInfo.CurrentCulture, $"w drabince: {text} i {usages.Count - Shown} więcej")
            : $"w drabince: {text}";
    }

    [RelayCommand]
    private void Release(HmiRenderItem? item)
    {
        if (item?.TouchAddress is not { } address || !_touches.TryGetValue(address, out var touch))
        {
            return;
        }

        var remaining = MinimumTouchHold - Stopwatch.GetElapsedTime(touch.Stamp);
        if (remaining <= TimeSpan.Zero)
        {
            ReleaseBit(address, touch.Generation);
        }
        else
        {
            _ = ReleaseLaterAsync(address, touch.Generation, remaining);
        }
    }

    [RelayCommand]
    private void Probe(HmiRenderItem? item)
    {
        if (item is null)
        {
            return;
        }

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Diagnostyka: kliknięty element bez operandu dotyku — projekt nie przypisał mu bitu (HMI_Elements / HMI_Operands),
        // więc symulator nie ma czego ustawić. Zwykle akcja przycisku jest wtedy zapisana w danych, których symulator nie czyta.
        TouchStatus = item.Kind == HmiElementKind.TouchButton
            ? $"Dotyk: przycisk {item.ElementId} „{item.Text}” nie ma operandu dotyku w projekcie."
            : $"Dotyk: element {item.ElementId} ({item.Kind}) nie jest dotykowy.";
    }

    [RelayCommand]
    private void Edit(HmiRenderItem? item)
    {
        if (item?.EditAddress is not { } address)
        {
            return;
        }

        string? error = null;
        while (true)
        {
            string current;
            lock (_memory.SyncRoot)
            {
                current = HmiScreenBuilder.FormatValue(_memory, address);
            }

            var text = _valueEntry.Ask($"{address} (element {item.ElementId})", current, error);
            if (text is null)
            {
                return;
            }

            bool written;
            lock (_memory.SyncRoot)
            {
                written = HmiValueEntry.TryWrite(_memory, address, text, out error);
            }

            if (written)
            {
                TouchStatus = $"Wpis: {address} = {text.Trim()}{StopHint()}";
                return;
            }
        }
    }

    /// <summary>Aktualizuje stan sterownika widoczny w panelu (wołane z wątku UI po odczycie stanu silnika).</summary>
    public void ApplyRunState(bool isRunning) => IsPlcStopped = !isRunning;

    private string StopHint() => IsPlcStopped ? " | sterownik w STOP — drabinka i skoki ekranów nie reagują" : string.Empty;

    private async Task ReleaseLaterAsync(OperandAddress address, int generation, TimeSpan delay)
    {
        await Task.Delay(delay);
        ReleaseBit(address, generation);
    }

    private void ReleaseBit(OperandAddress address, int generation)
    {
        // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
        // Ponowne wciśnięcie w czasie opóźnionego zwolnienia zwiększa generację — stare zwolnienie jest wtedy pomijane.
        if (!_touches.TryGetValue(address, out var touch) || touch.Generation != generation)
        {
            return;
        }

        _touches.Remove(address);
        lock (_memory.SyncRoot)
        {
            HmiTouch.Press(_memory, address, pressed: false);
        }
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => _dispatcher.InvokeAsync(SyncSelection);

    private void SyncSelection()
    {
        var id = _hmi.CurrentDisplayId;
        var match = Displays.FirstOrDefault(d => d.Id == id);

        _isSyncingSelection = true;
        try
        {
            SelectedDisplay = match;
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }
}
