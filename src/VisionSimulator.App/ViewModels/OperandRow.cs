using CommunityToolkit.Mvvm.ComponentModel;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.App.ViewModels;

/// <summary>Wiersz tabeli operandów projektu: adres, opis, miejsca użycia i bieżąca wartość.</summary>
public sealed partial class OperandRow : ObservableObject
{
    public OperandRow(OperandAddress address, string description, string usage, string usageDetails)
    {
        Address = address;
        Description = description;
        Usage = usage;
        UsageDetails = usageDetails;
    }

    public OperandAddress Address { get; }

    public string AddressText => Address.ToString();

    public string TypeText => Address.Type.ToString();

    public string Description { get; }

    /// <summary>Skrót użyć, np. „LAD 3 | HMI 2”.</summary>
    public string Usage { get; }

    /// <summary>Lista miejsc użycia w podpowiedzi.</summary>
    public string UsageDetails { get; }

    /// <summary>Bit można przełączyć i wymusić.</summary>
    public bool IsBit => Address.Type.IsBit();

    /// <summary>Rejestr można zapisać wartością (timery i liczniki aktualizuje skan, więc są tylko do odczytu).</summary>
    public bool IsWritable => !IsBit && !Address.Type.IsTimerOrCounter();

    [ObservableProperty]
    private string _value = "…";

    [ObservableProperty]
    private string _forceText = string.Empty;

    /// <summary>Tekst wpisywany przez użytkownika do zapisu wartości liczbowej.</summary>
    [ObservableProperty]
    private string _input = string.Empty;

    /// <summary>Komunikat zapisu w tym wierszu.</summary>
    [ObservableProperty]
    private string _message = string.Empty;
}
