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
        Assert.Contains("Profiles.RunPrimaryActionText", xaml, StringComparison.Ordinal);
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
    public void LiveFleetIsACompactCommandWorkspaceRatherThanAReadOnlyPage()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("Text=\"Fleet board\"", xaml, StringComparison.Ordinal);
        Assert.Contains("DragOver=\"OnLiveSquadDragOver\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Drop=\"OnLiveSquadDrop\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyPendingLiveChangesCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("InviteNowCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("PreviewSelectedTemplateCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Click • Ctrl-click • Shift-click range", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Move (staged)\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CancelStagedLiveMemberCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectionMode=\"Extended\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CleanRebuildFleetCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Invite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Saved setup\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Changes\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Header=\"Danger\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.CanStartReviewedOperation", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Stage invitations\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Review pending changes\"", xaml, StringComparison.Ordinal);
        Assert.Contains("UnlockHighImpactActions", xaml, StringComparison.Ordinal);
        Assert.Contains("KickSelectedLiveMembersCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("TransferFleetBossToSelectedCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ApplyFleetSettingsCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void FcScaleAndMaintenanceControlsAreVisible()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("LiveFleetSearchText", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectAllVisibleLiveMembersCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ClearLiveFleetFilterCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("LiveFleetSearchSummary", xaml, StringComparison.Ordinal);
        Assert.Contains("StageSelectedLiveMembersCommand", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"Home\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.OperationHistory", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.MoveShipRuleUpCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Profiles.InvitationTimeoutMinutes", xaml, StringComparison.Ordinal);
        Assert.Contains("ExportDiagnosticsCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("CheckForUpdatesCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("ResetLocalDataCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyFleetAndOptionalSetupEditorsStayCompact()
    {
        var xaml = ReadMainWindowXaml();

        Assert.Contains("Text=\"Waiting for a fleet\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding IsLiveFleetReady", xaml, StringComparison.Ordinal);
        Assert.Contains("Optional • expand to edit rules", xaml, StringComparison.Ordinal);
        Assert.Contains("Paste names only when changing the roster", xaml, StringComparison.Ordinal);
        Assert.Contains("Squad cards — expand for drag &amp; drop", xaml, StringComparison.Ordinal);
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
