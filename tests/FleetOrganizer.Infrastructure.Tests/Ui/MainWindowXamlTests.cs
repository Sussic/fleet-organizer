namespace FleetOrganizer.Infrastructure.Tests.Ui;

public sealed class MainWindowXamlTests
{
    [Fact]
    public void ReadOnlyOperationRunBindingsAreExplicitlyOneWay()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains(
            "<Run Text=\"{Binding Detail, Mode=OneWay}\" />",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "<Run Text=\"{Binding AttemptsText, Mode=OneWay}\" />",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<Run Text=\"{Binding Detail}\" />",
            xaml,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RoutineWorkflowIsPresentAndOldPlaceholderIsRemoved()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("Preview fleet changes", xaml, StringComparison.Ordinal);
        Assert.Contains("Organise fleet now", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Check now\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"Activity\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Run details\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("This page is the next product milestone", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void GrowingProfileListsUseSearchAndRecyclingVirtualization()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("Profiles.FilteredProfileItems", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.FilteredAssignments", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.ProfileSearchText", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.AssignmentSearchText", xaml, StringComparison.Ordinal);
        Assert.True(
            CountOccurrences(xaml, "VirtualizingPanel.VirtualizationMode=\"Recycling\"") >= 3,
            "Profiles, roster, and activity should all opt in to recycling virtualization.");
        Assert.True(
            CountOccurrences(xaml, "EnableRowVirtualization=\"True\"") >= 2,
            "Roster and activity tables should both virtualize rows.");
    }

    [Fact]
    public void KeyboardShortcutsAndAdvancedEditorRemainAvailable()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("Modifiers=\"Control\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"F5\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Key=\"Escape\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.IsAdvancedMode", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"Fleet hierarchy\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FleetDeskSupportsDragPlacementAndShipRules()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("Text=\"Automatic ship placement\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.ObservedShipTypes", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.AddShipRuleCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("DragOver=\"OnSquadDragOver\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Drop=\"OnSquadDrop\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MouseMove=\"OnRosterMouseMove\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CommanderRunModeWaitingRoomAndRestoreAreVisibleWorkflows()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("Profiles.RunModeOptions", xaml, StringComparison.Ordinal);
        Assert.Contains("Invitation waiting room", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomaticCheckCountdownText", xaml, StringComparison.Ordinal);
        Assert.Contains("Preview pre-run restore", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.AttentionSoundsEnabled", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void LiveBoardStagesDragMovesBeforeReview()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("Text=\"Live Fleet Board\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DragOver=\"OnLiveSquadDragOver\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Drop=\"OnLiveSquadDrop\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ReviewStagedLiveMovesCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ClearStagedLiveMovesCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FcScaleAndMaintenanceControlsAreVisible()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("LiveFleetSearchText", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectAllVisibleLiveMembersCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("StageSelectedLiveMembersCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.OperationHistory", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.MoveShipRuleUpCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.InvitationTimeoutMinutes", xaml, StringComparison.Ordinal);
        Assert.Contains("ExportDiagnosticsCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("CheckForUpdatesCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ResetLocalDataCommand", xaml, StringComparison.Ordinal);
    }

    private static string ReadMainWindowXaml()
    {
        var xamlPath = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "MainWindow.xaml");
        return File.ReadAllText(xamlPath);
    }

    private static int CountOccurrences(string source, string value) =>
        source.Split(value, StringSplitOptions.None).Length - 1;
}
