using System.Globalization;
using System.Text;

namespace VisionSimulator.Core.Project;

/// <summary>Raport Markdown ze schematu bazy projektu: tabele, kolumny, typy, liczby wierszy i ostrzeżenia odczytu.</summary>
public static class VisiLogicSchemaReport
{
    // [OSTRZEŻENIA I KONSEKWENCJE]
    // Raport zawiera wyłącznie strukturę bazy (bez wartości, opisów operandów i ścieżki pliku),
    // aby można go było przekazać do analizy bez ujawniania treści projektu.
    public static string Format(VisiLogicProject project, string runtimeInfo, DateTimeOffset createdAt)
    {
        ArgumentNullException.ThrowIfNull(project);

        var text = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;
        text.AppendLine(culture, $"# Schemat bazy VisiLogic: {Path.GetFileName(project.SourcePath)}");
        text.AppendLine();
        text.AppendLine(culture, $"- Utworzono: {createdAt.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC");
        text.AppendLine(culture, $"- Środowisko: {runtimeInfo}");
        text.AppendLine(culture, $"- Sterownik: {(project.ControllerModel.Length == 0 ? "?" : project.ControllerModel)}");
        text.AppendLine(culture, $"- Tabele: {project.Schema.Count} (czytane przez symulator: {project.Schema.Count(t => t.IsReadBySimulator)})");
        text.AppendLine();

        text.AppendLine("## Tabele");
        text.AppendLine();
        text.AppendLine("| Tabela | Wiersze | Kolumny | Czytana |");
        text.AppendLine("| :--- | ---: | ---: | :---: |");
        foreach (var table in project.Schema)
        {
            var rows = table.RowCount < 0 ? "?" : table.RowCount.ToString(culture);
            text.AppendLine(culture, $"| {table.Name} | {rows} | {table.Columns.Count} | {(table.IsReadBySimulator ? "tak" : "nie")} |");
        }

        text.AppendLine();
        text.AppendLine("## Kolumny tabel nieobsługiwanych");
        foreach (var table in project.Schema.Where(t => !t.IsReadBySimulator))
        {
            text.AppendLine();
            text.AppendLine(culture, $"### {table.Name}");
            text.AppendLine();
            text.AppendLine(string.Join(", ", table.Columns.Select(c => $"`{c.Name}` {c.DataType}")));
        }

        text.AppendLine();
        text.AppendLine("## Ostrzeżenia odczytu");
        text.AppendLine();
        if (project.Warnings.Count == 0)
        {
            text.AppendLine("Brak.");
        }
        else
        {
            foreach (var warning in project.Warnings)
            {
                text.AppendLine(culture, $"- {warning}");
            }
        }

        return text.ToString();
    }
}
