using System.Windows;
using VisionSimulator.App.ViewModels;

namespace VisionSimulator.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
