using CommunityToolkit.Mvvm.ComponentModel;
using VisionSimulator.Core.Memory;

namespace VisionSimulator.App.ViewModels;

/// <summary>Wiersz tabeli wymuszeń: bit, opis, wartość wymuszona i stan w pamięci.</summary>
public sealed partial class ForceRow : ObservableObject
{
    public ForceRow(OperandAddress address, string description, bool forcedValue)
    {
        Address = address;
        Description = description;
        _forcedValue = forcedValue;
    }

    public OperandAddress Address { get; }

    public string AddressText => Address.ToString();

    public string Description { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ForcedText))]
    private bool _forcedValue;

    /// <summary>Stan bitu w pamięci po ostatnim skanie (w STOP może się różnić od wymuszenia).</summary>
    [ObservableProperty]
    private string _memoryValue = "…";

    public string ForcedText => ForcedValue ? "1" : "0";
}
