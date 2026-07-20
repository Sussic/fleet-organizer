using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FleetOrganizer.App.ViewModels;
using FleetOrganizer.Core.Profiles;
using Application = System.Windows.Application;
using DragEventArgs = System.Windows.DragEventArgs;
using Forms = System.Windows.Forms;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;

namespace FleetOrganizer.App;

public sealed partial class MainWindow : Window, IDisposable
{
    private const string AssignmentDragFormat = "FleetOrganizer.ProfileAssignment";
    private const string LiveMemberDragFormat = "FleetOrganizer.LiveFleetMember";
    private readonly MainWindowViewModel viewModel;
    private readonly Forms.NotifyIcon trayIcon;
    private readonly Forms.ContextMenuStrip trayMenu;
    private Point dragStartPoint;
    private bool isExplicitExit;
    private bool isDisposed;

    public MainWindow(MainWindowViewModel viewModel)
    {
        this.viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "Fleet Desk",
        };
        trayIcon.DoubleClick += OnTrayIconDoubleClick;
        trayMenu = new Forms.ContextMenuStrip();
        trayMenu.Items.Add("Open Fleet Desk", null, (_, _) => RestoreFromTray());
        trayMenu.Items.Add("Exit", null, (_, _) => ExitFromTray());
        trayIcon.ContextMenuStrip = trayMenu;
        viewModel.Profiles.PropertyChanged += OnProfilePreferencesChanged;
        ApplyTheme(viewModel.Profiles.SelectedTheme);
    }

    public void ApplyStartupPreferences()
    {
        ApplyTheme(viewModel.Profiles.SelectedTheme);
        if (viewModel.StartMinimized)
        {
            WindowState = System.Windows.WindowState.Minimized;
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == System.Windows.WindowState.Minimized && viewModel.MinimizeToTray)
        {
            trayIcon.Visible = true;
            Hide();
            viewModel.SetFleetPollingEnabled(enabled: true);
            return;
        }

        viewModel.SetFleetPollingEnabled(WindowState != System.Windows.WindowState.Minimized);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!isExplicitExit &&
            !Application.Current.Dispatcher.HasShutdownStarted &&
            viewModel.MinimizeToTray)
        {
            e.Cancel = true;
            trayIcon.Visible = true;
            Hide();
            viewModel.SetFleetPollingEnabled(enabled: true);
            return;
        }

        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }

    public void Dispose()
    {
        if (isDisposed)
        {
            return;
        }

        isDisposed = true;
        viewModel.Profiles.PropertyChanged -= OnProfilePreferencesChanged;
        trayIcon.Visible = false;
        trayIcon.Dispose();
        trayMenu.Dispose();
        GC.SuppressFinalize(this);
    }

    private void OnTrayIconDoubleClick(object? sender, EventArgs e)
    {
        _ = sender;
        _ = e;
        RestoreFromTray();
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = System.Windows.WindowState.Normal;
        Activate();
        trayIcon.Visible = false;
        viewModel.SetFleetPollingEnabled(enabled: true);
    }

    private void ExitFromTray()
    {
        isExplicitExit = true;
        trayIcon.Visible = false;
        Application.Current.Shutdown();
    }

    private void OnProfilePreferencesChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.PropertyName == nameof(ProfilesViewModel.SelectedTheme))
        {
            ApplyTheme(viewModel.Profiles.SelectedTheme);
        }
    }

    private static void ApplyTheme(FleetDeskTheme theme)
    {
        var useDark = theme == FleetDeskTheme.Dark ||
            (theme == FleetDeskTheme.System && WindowsUsesDarkTheme());
        SetBrush("AppBackgroundBrush", useDark ? "#0B1220" : "#F4F7FB");
        SetBrush("SurfaceBrush", useDark ? "#151E2E" : "#FFFFFF");
        SetBrush("SurfaceAltBrush", useDark ? "#1B2638" : "#F7F9FC");
        SetBrush("BorderBrush", useDark ? "#334155" : "#DCE3EC");
        SetBrush("TextPrimaryBrush", useDark ? "#F1F5F9" : "#172033");
        SetBrush("TextMutedBrush", useDark ? "#A9B5C7" : "#667085");
        SetBrush("AccentSoftBrush", useDark ? "#172D50" : "#EAF2FF");
    }

    private static bool WindowsUsesDarkTheme()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private static void SetBrush(string key, string color)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush)
        {
            if (brush.IsFrozen)
            {
                Application.Current.Resources[key] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            }
            else
            {
                brush.Color = (Color)ColorConverter.ConvertFromString(color);
            }
        }
    }

    private void OnRosterPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _ = sender;
        dragStartPoint = e.GetPosition(null);
    }

    private void OnRosterMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not DataGrid grid)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row?.Item is not ProfileAssignmentEditorViewModel assignment)
        {
            return;
        }

        var data = new DataObject(AssignmentDragFormat, assignment.CharacterId);
        DragDrop.DoDragDrop(grid, data, DragDropEffects.Move);
    }

    private void OnSquadDragOver(object sender, DragEventArgs e)
    {
        e.Effects = viewModel.Profiles.CanEdit &&
            sender is FrameworkElement { DataContext: ProfileSquadEditorViewModel } &&
            e.Data.GetDataPresent(AssignmentDragFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnSquadDrop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ProfileSquadEditorViewModel squad } &&
            e.Data.GetData(AssignmentDragFormat) is long characterId)
        {
            viewModel.Profiles.MoveAssignmentsToSquad(squad.Id, characterId);
        }

        e.Handled = true;
    }

    private void OnLiveMemberPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement
            {
                DataContext: LiveFleetBoardMemberViewModel { CanStage: true } member,
            })
        {
            var modifiers = Keyboard.Modifiers;
            viewModel.SelectLiveMember(
                member.CharacterId,
                extendRange: modifiers.HasFlag(ModifierKeys.Shift),
                toggle: modifiers.HasFlag(ModifierKeys.Control));
        }

        dragStartPoint = e.GetPosition(null);
    }

    private void OnLiveMemberMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed ||
            sender is not FrameworkElement
            {
                DataContext: LiveFleetBoardMemberViewModel { CanStage: true } member,
            } element)
        {
            return;
        }

        var position = e.GetPosition(null);
        if (Math.Abs(position.X - dragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new DataObject(LiveMemberDragFormat, member.CharacterId);
        DragDrop.DoDragDrop(element, data, DragDropEffects.Move);
    }

    private void OnLiveSquadDragOver(object sender, DragEventArgs e)
    {
        e.Effects = sender is FrameworkElement
            {
                DataContext: LiveFleetBoardSquadViewModel { CanAcceptDrop: true },
            } &&
            e.Data.GetDataPresent(LiveMemberDragFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnLiveSquadDrop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement
            {
                DataContext: LiveFleetBoardSquadViewModel { CanAcceptDrop: true } squad,
            } &&
            e.Data.GetData(LiveMemberDragFormat) is long characterId)
        {
            viewModel.StageDraggedLiveMembers(
                characterId,
                squad.WingId,
                squad.SquadId,
                squad.DropRole);
        }

        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? source)
        where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match)
            {
                return match;
            }

            source = VisualTreeHelper.GetParent(source);
        }

        return null;
    }
}
