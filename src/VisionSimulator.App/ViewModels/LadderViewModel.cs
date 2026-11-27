using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using VisionSimulator.Core.Ladder;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.App.ViewModels;

/// <summary>Szczebel w widoku drabinki: niezmienny układ i ostatni stan zasilenia.</summary>
public sealed partial class LadderRungViewModel : ObservableObject
{
    public LadderRungViewModel(LadderRungLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Layout = layout;
        Header = string.Format(
            CultureInfo.CurrentCulture,
            "Szczebel {0}{1}{2}",
            layout.Number,
            layout.IsReturnStrip ? " [RET]" : string.Empty,
            layout.Comment.Length > 0 ? " — " + layout.Comment : string.Empty);
    }

    public LadderRungLayout Layout { get; }

    public string Header { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private LadderRungState? _state;

    public string StatusText => State switch
    {
        null => string.Empty,
        { Executed: false } => "nie wykonany w ostatnim skanie",
        _ => string.Empty,
    };
}

/// <summary>
/// Zakładka „Drabinka”: wybór podprogramu i stan zasilenia jego szczebli odświeżany z cyklem UI.
/// </summary>
/// <remarks>
/// [OPTYMALIZACJA WYDAJNOŚCI (PERFORMANCE)]
/// Stan zbierany jest tylko dla wybranego podprogramu i tylko przy aktywnej zakładce; szczebel dostaje nowy stan
/// (i jest przerysowany) wyłącznie, gdy treść się zmieniła.
/// </remarks>
public sealed partial class LadderViewModel : ObservableObject
{
    private readonly LadderInterpreter _ladder;
    private readonly PlcMemory _memory;

    public LadderViewModel(LadderInterpreter ladder, PlcMemory memory)
    {
        ArgumentNullException.ThrowIfNull(ladder);
        ArgumentNullException.ThrowIfNull(memory);
        _ladder = ladder;
        _memory = memory;
    }

    public ObservableCollection<LadderSubroutineLayout> Subroutines { get; } = [];

    public ObservableCollection<LadderRungViewModel> Rungs { get; } = [];

    [ObservableProperty]
    private LadderSubroutineLayout? _selectedSubroutine;

    /// <summary>Czy zakładka jest widoczna (wiązanie z <c>TabItem.IsSelected</c>).</summary>
    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private string _summary = "Brak wczytanego programu LAD.";

    /// <summary>Przeładowuje listę podprogramów po wczytaniu (lub wyładowaniu) programu LAD.</summary>
    public void Reload()
    {
        var layout = _ladder.GetLayout();
        Subroutines.Clear();
        foreach (var subroutine in layout.Subroutines)
        {
            Subroutines.Add(subroutine);
        }

        SelectedSubroutine = layout.Subroutines.FirstOrDefault(s => s.IsEntryPoint)
            ?? (layout.Subroutines.Count > 0 ? layout.Subroutines[0] : null);
        if (SelectedSubroutine is null)
        {
            Rungs.Clear();
            Summary = "Brak wczytanego programu LAD.";
        }
    }

    /// <summary>Migawka stanu wybranego podprogramu; wywołujący trzyma <c>lock (memory.SyncRoot)</c>.</summary>
    public LadderSubroutineState? CaptureState() =>
        IsActive && SelectedSubroutine is { } subroutine ? _ladder.CaptureState(_memory, subroutine) : null;

    /// <summary>Nanosi migawkę na szczeble (poza blokadą pamięci).</summary>
    public void ApplyState(LadderSubroutineState? state)
    {
        // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
        // Migawka z poprzednio wybranego podprogramu (zmiana wyboru między odczytem a zastosowaniem) jest pomijana.
        if (state is null || !ReferenceEquals(state.Subroutine, SelectedSubroutine) || state.Rungs.Count != Rungs.Count)
        {
            return;
        }

        for (var i = 0; i < Rungs.Count; i++)
        {
            var rung = Rungs[i];
            if (!state.Rungs[i].ContentEquals(rung.State))
            {
                rung.State = state.Rungs[i];
            }
        }
    }

    partial void OnSelectedSubroutineChanged(LadderSubroutineLayout? value)
    {
        Rungs.Clear();
        if (value is null)
        {
            return;
        }

        foreach (var rung in value.Rungs)
        {
            Rungs.Add(new LadderRungViewModel(rung));
        }

        Summary = string.Format(
            CultureInfo.CurrentCulture,
            "{0}: szczeble {1:N0}, elementy {2:N0}. Zielony = zasilony, szary = bez zasilenia; wartości operandów na niebiesko.",
            value.Name,
            value.Rungs.Count,
            value.Rungs.Sum(r => r.Elements.Count));
    }

    partial void OnIsActiveChanged(bool value)
    {
        if (!value)
        {
            return;
        }

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Po powrocie na zakładkę od razu pokazujemy bieżący stan zamiast czekać na kolejny takt UI.
        LadderSubroutineState? state;
        lock (_memory.SyncRoot)
        {
            state = CaptureState();
        }

        ApplyState(state);
    }
}
