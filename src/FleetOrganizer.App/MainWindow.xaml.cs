using System.Windows;
using FleetOrganizer.App.ViewModels;

namespace FleetOrganizer.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
