using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Profile;

/// <summary>Zwalidowany profil: adresy sparsowane i sprawdzone względem układu pamięci.</summary>
public sealed record PlantModel(
    string Name,
    PlcMemoryLayout Layout,
    IReadOnlyList<InitialValue> InitialValues,
    IReadOnlyList<DigitalFeedback> DigitalFeedback,
    IReadOnlyList<AnalogFollower> AnalogFollowers)
{
    /// <summary>Czy profil zawiera jakąkolwiek symulację obiektu lub wartości początkowe.</summary>
    public bool HasBehaviour => InitialValues.Count > 0 || DigitalFeedback.Count > 0 || AnalogFollowers.Count > 0;
}

public sealed record InitialValue(OperandAddress Address, double Value);

public sealed record DigitalFeedback(OperandAddress Source, OperandAddress Target, TimeSpan OnDelay, TimeSpan OffDelay, bool Invert);

/// <summary>Skompilowany element analogowy; dokładnie jedno z <see cref="SourceBit"/> / <see cref="SourceRegister"/> jest ustawione.</summary>
public sealed record AnalogFollower(
    OperandAddress Target,
    OperandAddress? SourceBit,
    double OnValue,
    double OffValue,
    OperandAddress? SourceRegister,
    double Gain,
    double Offset,
    TimeSpan TimeConstant,
    double Min,
    double Max);
