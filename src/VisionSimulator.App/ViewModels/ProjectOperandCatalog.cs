using System.Globalization;
using VisionSimulator.Core.Ladder;
using VisionSimulator.Core.Memory;
using VisionSimulator.Core.Project;

namespace VisionSimulator.App.ViewModels;

/// <summary>Operand projektu z opisem i miejscami użycia w drabince oraz na ekranach HMI.</summary>
public sealed record ProjectOperand(
    OperandAddress Address,
    string Description,
    IReadOnlyList<LadderOperandUsage> LadderUsages,
    IReadOnlyList<int> HmiDisplays);

/// <summary>Katalog operandów wczytanego projektu: suma operandów z tabeli operandów, drabinki i ekranów HMI.</summary>
/// <remarks>
/// [INTENCJA I POWÓD DZIAŁANIA]
/// Tabela operandów VisiLogic (LdrOperands) zawiera tylko operandy z opisem lub wartością początkową, więc
/// dokładamy adresy użyte w drabince i na ekranach — inaczej tabela pomijałaby bity przycisków bez opisu.
/// </remarks>
public static class ProjectOperandCatalog
{
    public static IReadOnlyList<ProjectOperand> Build(VisiLogicProject project, LadderLayout layout, PlcMemory memory)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(memory);

        var descriptions = Descriptions(project);
        var displays = new Dictionary<OperandAddress, SortedSet<int>>();
        foreach (var display in project.Displays)
        {
            foreach (var element in display.Elements)
            {
                AddDisplay(displays, element.VarOperandType, element.VarOperandAddress, display.Id);
                AddDisplay(displays, element.TouchOperandType, element.TouchOperandAddress, display.Id);
            }
        }

        var addresses = new HashSet<OperandAddress>(descriptions.Keys);
        addresses.UnionWith(layout.UsedAddresses);
        addresses.UnionWith(displays.Keys);

        // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
        // Adresy spoza układu pamięci (np. profil z mniejszą liczbą rejestrów) nie mają wartości — pomijamy je.
        return addresses
            .Where(memory.Contains)
            .OrderBy(a => a.Type)
            .ThenBy(a => a.Index)
            .Select(a => new ProjectOperand(
                a,
                descriptions.TryGetValue(a, out var description) ? description : string.Empty,
                layout.FindUsages(a),
                displays.TryGetValue(a, out var ids) ? ids.ToArray() : []))
            .ToArray();
    }

    /// <summary>Opisy operandów z tabeli operandów projektu.</summary>
    public static IReadOnlyDictionary<OperandAddress, string> Descriptions(VisiLogicProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var descriptions = new Dictionary<OperandAddress, string>();
        foreach (var info in project.Operands)
        {
            if (info.Memory is { } address)
            {
                // [OBSŁUGA BŁĘDÓW, ANOMALII I PRZYPADKÓW BRZEGOWYCH (EDGE CASES)]
                // Pierwszy niepusty opis wygrywa; operand bez opisu też trafia do katalogu.
                if (!descriptions.TryGetValue(address, out var existing) || existing.Length == 0)
                {
                    descriptions[address] = info.Description;
                }
            }
        }

        return descriptions;
    }

    /// <summary>Skrót użyć do kolumny tabeli.</summary>
    public static string FormatUsage(ProjectOperand operand)
    {
        ArgumentNullException.ThrowIfNull(operand);

        var parts = new List<string>(2);
        if (operand.LadderUsages.Count > 0)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"LAD {operand.LadderUsages.Count}"));
        }

        if (operand.HmiDisplays.Count > 0)
        {
            parts.Add(string.Create(CultureInfo.CurrentCulture, $"HMI {operand.HmiDisplays.Count}"));
        }

        return parts.Count == 0 ? "—" : string.Join(" | ", parts);
    }

    /// <summary>Pełna lista użyć do podpowiedzi.</summary>
    public static string FormatUsageDetails(ProjectOperand operand)
    {
        ArgumentNullException.ThrowIfNull(operand);

        var lines = operand.LadderUsages.Select(u => $"LAD: {u}").ToList();
        if (operand.HmiDisplays.Count > 0)
        {
            lines.Add($"HMI: ekrany {string.Join(", ", operand.HmiDisplays)}");
        }

        return lines.Count == 0 ? "Operand tylko w tabeli operandów projektu (nieużywany w drabince i na ekranach)." : string.Join(Environment.NewLine, lines);
    }

    private static void AddDisplay(Dictionary<OperandAddress, SortedSet<int>> displays, int typeCode, int index, int displayId)
    {
        if (index < 0 || !VisiLogicOperandCodes.TryGetOperandType(typeCode, out var type))
        {
            return;
        }

        var address = new OperandAddress(type, index);
        if (!displays.TryGetValue(address, out var ids))
        {
            ids = [];
            displays[address] = ids;
        }

        ids.Add(displayId);
    }
}
