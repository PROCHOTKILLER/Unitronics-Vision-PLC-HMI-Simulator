using Microsoft.Win32;

namespace VisionSimulator.App.Services;

/// <summary>Wybór pliku projektu; interfejs pozwala testować ViewModel bez okien systemowych.</summary>
public interface IProjectFileDialog
{
    string? PickProjectFile();

    string? PickSchemaReportFile(string suggestedName);

    string? PickProfileFile();
}

public sealed class ProjectFileDialog : IProjectFileDialog
{
    public string? PickProfileFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Wczytaj profil symulacji",
            Filter = "Profil JSON (*.json)|*.json|Wszystkie pliki (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickSchemaReportFile(string suggestedName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Eksport schematu bazy projektu",
            Filter = "Markdown (*.md)|*.md|Wszystkie pliki (*.*)|*.*",
            FileName = suggestedName,
            AddExtension = true,
            OverwritePrompt = true,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickProjectFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Wczytaj projekt VisiLogic",
            Filter = "Projekty VisiLogic (*.vlp;*.vlx;*.udb)|*.vlp;*.vlx;*.udb|Wszystkie pliki (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
