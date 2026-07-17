using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FleetOrganizer.App.ViewModels;

namespace FleetOrganizer.App;

public partial class MainWindow : Window
{
    private const string AssignmentDragFormat = "FleetOrganizer.ProfileAssignment";
    private const string LiveMemberDragFormat = "FleetOrganizer.LiveFleetMember";
    private readonly MainWindowViewModel viewModel;
    private Point dragStartPoint;

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
        _ = sender;
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

    private static void OnLiveSquadDragOver(object sender, DragEventArgs e)
    {
        e.Effects = sender is FrameworkElement { DataContext: LiveFleetBoardSquadViewModel } &&
            e.Data.GetDataPresent(LiveMemberDragFormat)
                ? DragDropEffects.Move
                : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnLiveSquadDrop(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LiveFleetBoardSquadViewModel squad } &&
            e.Data.GetData(LiveMemberDragFormat) is long characterId)
        {
            viewModel.StageLiveMemberMove(characterId, squad.WingId, squad.SquadId);
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
