using System.Windows;
using FleetOrganizer.App.ViewModels;

namespace FleetOrganizer.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        viewModel.SetFleetPollingEnabled(WindowState != System.Windows.WindowState.Minimized);
    }
}
