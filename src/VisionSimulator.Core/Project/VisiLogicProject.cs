using VisionSimulator.Core.Memory;

namespace VisionSimulator.Core.Project;

/// <summary>Niezmienny obraz projektu VisiLogic wczytany z bazy Jet.</summary>
public sealed record VisiLogicProject(
    string SourcePath,
    string ProjectName,
    string ControllerModel,
    IReadOnlyList<HardwareModule> Modules,
    IReadOnlyList<LadderSubroutine> Subroutines,
    IReadOnlyList<OperandInfo> Operands,
    IReadOnlyList<HmiDisplay> Displays,
    IReadOnlyList<DatabaseTable> Schema,
    IReadOnlyList<string> Warnings)
{
    public int RungCount => Subroutines.Sum(s => s.Rungs.Count);

    public int ElementCount => Subroutines.Sum(s => s.Rungs.Sum(r => r.Elements.Count));

    public int DescribedOperandCount => Operands.Count(o => o.Description.Length > 0);
}

/// <summary>Tabela bazy projektu: nazwa, liczba wierszy (-1 = nie udało się policzyć) i kolumny; bez wartości danych.</summary>
public sealed record DatabaseTable(string Name, int RowCount, bool IsReadBySimulator, IReadOnlyList<DatabaseColumn> Columns);

/// <summary>Kolumna tabeli bazy projektu z typem OLE DB.</summary>
public sealed record DatabaseColumn(string Name, string DataType);

/// <summary>Moduł sprzętowy (tabela HW_Elements); pozycja -1 oznacza jednostkę centralną.</summary>
public sealed record HardwareModule(int Position, int RailIndex, string Model);

/// <summary>Podprogram LAD (tabela LdrFunctions).</summary>
public sealed record LadderSubroutine(int Id, string Name, IReadOnlyList<LadderRung> Rungs);

/// <summary>Szczebel drabinki (tabela LdrStrips).</summary>
public sealed record LadderRung(int StripId, int Sequence, string Comment, bool IsReturnStrip, IReadOnlyList<LadderElement> Elements);

/// <summary>Element LAD (tabela LdrElements) z operandami (LdrElmsOprs) w kolejności: wejścia, potem wyjścia.</summary>
public sealed record LadderElement(
    int Id,
    int Type,
    int Xs,
    int Ys,
    int Dx,
    int Dy,
    string MoreParamsStr,
    int MoreParamsLng,
    IReadOnlyList<ElementOperand> Operands);

public enum ElementOperandKind
{
    Memory,
    Constant,
    HmiPage,
    Subroutine,
    Unknown,
}

/// <summary>Operand elementu LAD w postaci surowej (kod typu VisiLogic) i rozpoznanej.</summary>
public sealed record ElementOperand(
    int TypeCode,
    int Address,
    int OutIndex,
    int InIndex,
    ElementOperandKind Kind,
    OperandAddress? Memory,
    long? ConstantValue);

/// <summary>Definicja operandu (tabela LdrOperands): opis i wartość po włączeniu zasilania.</summary>
public sealed record OperandInfo(
    int TypeCode,
    int Address,
    OperandAddress? Memory,
    string Description,
    string PowerUpRaw,
    bool PowerUpEnabled);

/// <summary>Ekran HMI (tabela Pages) z elementami (HMI_Elements) i warunkami skoku (HMIJumps).</summary>
public sealed record HmiDisplay(int Id, string Name, int ColorBack, IReadOnlyList<HmiElement> Elements, IReadOnlyList<HmiJump> Jumps);

/// <summary>Warunek skoku ekranu (tabela HMIJumps): bit warunku i ekran docelowy.</summary>
/// <param name="DisplayId">Ekran, na którym warunek obowiązuje (PageNum).</param>
/// <param name="Order">Numer porządkowy skoku na ekranie (JumpType).</param>
/// <param name="OperandTypeCode">Kod typu operandu warunku VisiLogic (JumpOpType, 1 = MB).</param>
/// <param name="OperandAddress">Adres operandu warunku (JumpOpAddress).</param>
/// <param name="TargetTypeCode">Rodzaj celu (DispOpType): <see cref="DisplayTargetCode"/> = stały ekran, inny kod = rejestr z numerem ekranu.</param>
/// <param name="TargetAddress">Identyfikator ekranu docelowego albo adres rejestru (DispOpAddress).</param>
public sealed record HmiJump(int DisplayId, int Order, int OperandTypeCode, int OperandAddress, int TargetTypeCode, int TargetAddress)
{
    /// <summary>Kod DispOpType oznaczający stały ekran docelowy.</summary>
    public const int DisplayTargetCode = VisiLogicOperandCodes.HmiPage;
}

/// <summary>Element ekranu HMI (tabela HMI_Elements) w postaci surowej; typ operandu dotyku z HMI_Operands (rola 2).</summary>
public sealed record HmiElement(
    int Id,
    int Type,
    int VarType,
    string Text,
    int Xs,
    int Ys,
    int Dx,
    int Dy,
    int ColorBack,
    int ColorFore,
    int VarOperandType,
    int VarOperandAddress,
    int TouchOperandType,
    int TouchOperandAddress,
    int VisibleOperandAddress,
    bool KeypadEntry)
{
    /// <summary>Kod typu operandu MB w VisiLogic — domyślny typ dotyku, gdy HMI_Operands go nie podaje.</summary>
    public const int DefaultTouchOperandType = 1;
}
