using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace VisionSimulator.App.Services;

/// <summary>Okno wpisu wartości zmiennej HMI.</summary>
public interface IValueEntryDialog
{
    /// <summary>Pokazuje okno modalne; zwraca wpisany tekst albo null po anulowaniu.</summary>
    string? Ask(string title, string currentValue, string? error = null);
}

/// <summary>Proste okno modalne z polem tekstowym i klawiaturą numeryczną (jak klawiatura ekranowa sterownika).</summary>
public sealed class ValueEntryDialog : IValueEntryDialog
{
    public string? Ask(string title, string currentValue, string? error = null)
    {
        var input = new TextBox
        {
            Text = currentValue,
            FontSize = 18,
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalContentAlignment = HorizontalAlignment.Right,
        };

        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        if (!string.IsNullOrEmpty(error))
        {
            panel.Children.Add(new TextBlock { Text = error, Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new Thickness(0, 0, 0, 6) });
        }

        panel.Children.Add(input);

        var window = new Window
        {
            Title = "Wpis wartości",
            SizeToContent = SizeToContent.WidthAndHeight,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Owner = Application.Current?.MainWindow,
            Content = panel,
        };

        // [INTENCJA I POWÓD DZIAŁANIA]
        // Układ klawiatury jak na panelu sterownika; klawiatura fizyczna działa równolegle (Enter = OK, Esc = anuluj).
        string[] keys = ["7", "8", "9", "CLR", "4", "5", "6", "←", "1", "2", "3", "ESC", "+/-", "0", ".", "OK"];
        var grid = new UniformGrid { Columns = 4 };
        foreach (var key in keys)
        {
            var button = new Button { Content = key, Width = 56, Height = 40, Margin = new Thickness(2), FontSize = 14 };
            button.Click += (_, _) => OnKey(window, input, key);
            grid.Children.Add(button);
        }

        panel.Children.Add(grid);
        window.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                window.DialogResult = true;
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                window.DialogResult = false;
                e.Handled = true;
            }
        };
        window.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };

        return window.ShowDialog() == true ? input.Text : null;
    }

    private static void OnKey(Window window, TextBox input, string key)
    {
        switch (key)
        {
            case "OK":
                window.DialogResult = true;
                return;
            case "ESC":
                window.DialogResult = false;
                return;
            case "CLR":
                input.Text = string.Empty;
                break;
            case "←":
                if (input.Text.Length > 0)
                {
                    input.Text = input.Text[..^1];
                }

                break;
            case "+/-":
                input.Text = input.Text.StartsWith('-') ? input.Text[1..] : "-" + input.Text;
                break;
            default:
                input.Text += key;
                break;
        }

        input.Focus();
        input.CaretIndex = input.Text.Length;
    }
}
