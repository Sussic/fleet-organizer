using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;
using FleetOrganizer.Core.Planning;
using FleetOrganizer.Core.Profiles;
using Microsoft.Win32;

namespace FleetOrganizer.App.ViewModels;

public partial class ProfilesViewModel : ObservableObject
{
    private static readonly char[] TagSeparators = [',', ';'];

    private readonly IFleetProfileRepository repository;
    private readonly ICharacterNameResolver characterNameResolver;
    private readonly ILiveFleetService liveFleetService;
    private readonly IFleetOperationService operationService;
    private bool isLoadingEditor;
    private FleetDryRunPlan? lastDryRunPlan;
    private FleetOperation? currentOperation;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPrepareFleet))]
    [NotifyPropertyChangedFor(nameof(SelectedProfileQuickSummary))]
    public partial ProfileListItemViewModel? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial Guid EditingProfileId { get; set; }

    [ObservableProperty]
    public partial string ProfileName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanPrepareFleet))]
    public partial bool IsEditorActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    [NotifyPropertyChangedFor(nameof(CanPrepareFleet))]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string ProfileSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string AssignmentSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsAdvancedMode { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditorStateText))]
    [NotifyPropertyChangedFor(nameof(CanPrepareFleet))]
    public partial bool HasUnsavedChanges { get; set; }

    [ObservableProperty]
    public partial string StatusMessage { get; set; } =
        "Create a profile or capture the current live fleet.";

    [ObservableProperty]
    public partial string ValidationSummary { get; set; } = "No profile selected.";

    [ObservableProperty]
    public partial string RosterPasteText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial Guid? BulkTargetSquadId { get; set; }

    [ObservableProperty]
    public partial DesiredFleetRole BulkDesiredRole { get; set; } =
        DesiredFleetRole.SquadMember;

    [ObservableProperty]
    public partial string BulkTagsText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasDryRun { get; set; }

    [ObservableProperty]
    public partial string DryRunTitle { get; set; } = "No comparison generated.";

    [ObservableProperty]
    public partial string DryRunSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DryRunSafetyMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string DryRunPrimaryMessage { get; set; } =
        "Choose a profile and check the live fleet.";

    [ObservableProperty]
    public partial bool ShowAlreadyCorrect { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    public partial bool HasOperation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanPrepareFleet))]
    public partial bool HasActiveOperation { get; set; }

    [ObservableProperty]
    public partial string OperationTitle { get; set; } = "No saved operation.";

    [ObservableProperty]
    public partial string OperationSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OperationStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool OperationIsTerminal { get; set; }

    [ObservableProperty]
    public partial string OperationPhaseTitle { get; set; } = "No fleet run is active";

    [ObservableProperty]
    public partial string OperationNextAction { get; set; } =
        "Choose a profile on Home to prepare a run.";

    [ObservableProperty]
    public partial string OperationProgressText { get; set; } = "0 of 0 steps finished";

    [ObservableProperty]
    public partial double OperationProgressPercent { get; set; }

    public ProfilesViewModel(
        IFleetProfileRepository repository,
        ICharacterNameResolver characterNameResolver,
        ILiveFleetService liveFleetService,
        IFleetOperationService operationService)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(characterNameResolver);
        ArgumentNullException.ThrowIfNull(liveFleetService);
        ArgumentNullException.ThrowIfNull(operationService);

        this.repository = repository;
        this.characterNameResolver = characterNameResolver;
        this.liveFleetService = liveFleetService;
        this.operationService = operationService;

        FilteredProfileItems = CollectionViewSource.GetDefaultView(ProfileItems);
        FilteredProfileItems.Filter = FilterProfile;
        FilteredAssignments = CollectionViewSource.GetDefaultView(Assignments);
        FilteredAssignments.Filter = FilterAssignment;
    }

    public ObservableCollection<ProfileListItemViewModel> ProfileItems { get; } = [];

    public ObservableCollection<ProfileWingEditorViewModel> Wings { get; } = [];

    public ObservableCollection<ProfileAssignmentEditorViewModel> Assignments { get; } = [];

    public ObservableCollection<ProfileSquadOptionViewModel> SquadOptions { get; } = [];

    public ObservableCollection<string> UnresolvedRosterEntries { get; } = [];

    public ObservableCollection<FleetPlanItemViewModel> DryRunItems { get; } = [];

    public ObservableCollection<FleetOperationStepViewModel> OperationItems { get; } = [];

    public ICollectionView FilteredProfileItems { get; }

    public ICollectionView FilteredAssignments { get; }

    public DesiredRoleOptionViewModel[] RoleOptions { get; } =
    [
        new(DesiredFleetRole.SquadMember, "Squad member"),
        new(DesiredFleetRole.SquadCommander, "Squad commander"),
        new(DesiredFleetRole.WingCommander, "Wing commander"),
        new(DesiredFleetRole.FleetCommander, "Fleet commander"),
    ];

    public bool CanEdit => IsEditorActive && !IsBusy && !HasActiveOperation;

    public bool CanOperate => HasOperation && !IsBusy;

    public bool CanPrepareFleet =>
        IsEditorActive && !IsBusy && !HasActiveOperation && !HasUnsavedChanges;

    public string SelectedProfileQuickSummary => SelectedProfile is null
        ? "No profile selected"
        : $"{SelectedProfile.Summary} • saved on this PC";

    public string ProfileSearchSummary =>
        $"{FilteredProfileItems.Cast<object>().Count()} of {ProfileItems.Count} profiles";

    public string AssignmentSearchSummary
    {
        get
        {
            var selectedCount = Assignments.Count(assignment => assignment.IsSelected);
            var visibleCount = FilteredAssignments.Cast<object>().Count();
            return $"Showing {visibleCount} of {Assignments.Count} characters • {selectedCount} selected";
        }
    }

    public string EditorStateText => HasUnsavedChanges
        ? "Unsaved local changes"
        : "All changes saved locally";

    public async Task InitializeAsync()
    {
        try
        {
            await ReloadProfilesAsync(null);
            var resumableOperation = await operationService.LoadLatestResumableAsync();
            if (resumableOperation is not null)
            {
                ShowOperation(resumableOperation);
            }

            StatusMessage = ProfileItems.Count == 0
                ? "No saved profiles yet. Create one or capture the current live fleet."
                : $"Loaded {ProfileItems.Count} saved profile{(ProfileItems.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profiles could not be loaded: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task PrepareFleetAsync()
    {
        if (!CanPrepareFleet)
        {
            StatusMessage = SelectedProfile is null
                ? "Choose or create a saved profile first."
                : HasUnsavedChanges
                    ? "Save the local profile changes before checking the live fleet."
                    : "Finish or cancel the active fleet run before preparing another one.";
            return;
        }

        await CompareCurrentProfileAsync();
    }

    [RelayCommand]
    private void ClearProfileSearch() => ProfileSearchText = string.Empty;

    [RelayCommand]
    private void ClearAssignmentSearch() => AssignmentSearchText = string.Empty;

    [RelayCommand]
    private void ShowAdvancedEditor() => IsAdvancedMode = true;

    [RelayCommand]
    private async Task NewProfileAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var wingId = Guid.NewGuid();
            var squadId = Guid.NewGuid();
            var profile = new FleetProfile(
                Guid.NewGuid(),
                MakeUniqueProfileName("New Profile"),
                [new ProfileWing(wingId, "Wing 1", 0, [new ProfileSquad(squadId, "Squad 1", 0)])],
                []);
            await repository.SaveAsync(profile);
            await ReloadProfilesAsync(profile.Id);
            StatusMessage = "New profile created. Rename it and add your roster, then save.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profile could not be created: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CaptureCurrentFleetAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Reading and capturing the current fleet…";
        try
        {
            var liveResult = await liveFleetService.LoadCurrentAsync();
            if (liveResult.Status != LiveFleetLoadStatus.Ready ||
                liveResult.Snapshot is null)
            {
                StatusMessage = liveResult.UserMessage;
                return;
            }

            var profile = FleetProfileFactory.FromLiveFleet(
                liveResult.Snapshot,
                MakeUniqueProfileName("Current Fleet"));
            await repository.SaveAsync(profile);
            await ReloadProfilesAsync(profile.Id);
            StatusMessage =
                $"Captured {profile.Assignments.Count} characters and {profile.Wings.Count} wings from fleet {liveResult.Snapshot.FleetId}.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Current fleet could not be captured: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveProfileAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        var profile = BuildCurrentProfile();
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0)
        {
            ValidationSummary = FormatValidation(errors);
            StatusMessage = "Fix the validation errors before saving.";
            return;
        }

        if (ProfileItems.Any(item =>
            item.Id != profile.Id &&
            string.Equals(item.Name, profile.Name, StringComparison.OrdinalIgnoreCase)))
        {
            ValidationSummary = "Profile name is already in use.";
            StatusMessage = "Choose a unique profile name before saving.";
            return;
        }

        IsBusy = true;
        try
        {
            await repository.SaveAsync(profile);
            await ReloadProfilesAsync(profile.Id);
            StatusMessage = $"Saved '{profile.Name}'.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profile could not be saved: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DuplicateProfileAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        var source = BuildCurrentProfile();
        var duplicate = FleetProfileFactory.Duplicate(
            source,
            MakeUniqueProfileName($"{source.Name} Copy"));
        IsBusy = true;
        try
        {
            await repository.SaveAsync(duplicate);
            await ReloadProfilesAsync(duplicate.Id);
            StatusMessage = $"Created '{duplicate.Name}'.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profile could not be duplicated: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task DeleteProfileAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"Delete profile '{ProfileName}'?\n\nThis removes only the saved local profile. It does not change the EVE fleet.",
            "Delete profile",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await repository.DeleteAsync(EditingProfileId);
            await ReloadProfilesAsync(null);
            StatusMessage = "Profile deleted. No EVE fleet changes were made.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profile could not be deleted: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ExportProfileAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        var profile = BuildCurrentProfile();
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0)
        {
            ValidationSummary = FormatValidation(errors);
            StatusMessage = "Fix validation errors before exporting.";
            return;
        }

        var dialog = new SaveFileDialog
        {
            AddExtension = true,
            DefaultExt = ".json",
            FileName = $"{GetSafeFileName(profile.Name)}.fleet-profile.json",
            Filter = "Fleet Organizer profile (*.json)|*.json|All files (*.*)|*.*",
            Title = "Export Fleet Organizer profile",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            await File.WriteAllTextAsync(
                dialog.FileName,
                FleetProfileJsonSerializer.Serialize(profile),
                Encoding.UTF8);
            StatusMessage = $"Exported '{profile.Name}'.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profile could not be exported: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportProfileAsync()
    {
        if (IsBusy)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            CheckFileExists = true,
            Filter = "Fleet Organizer profile (*.json)|*.json|All files (*.*)|*.*",
            Multiselect = false,
            Title = "Import Fleet Organizer profile",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var json = await File.ReadAllTextAsync(dialog.FileName, Encoding.UTF8);
            var imported = FleetProfileJsonSerializer.Deserialize(json);
            var errors = ProfileValidator.Validate(imported);
            if (errors.Count > 0)
            {
                StatusMessage = $"Imported profile is invalid: {errors[0].Message}";
                return;
            }

            var copy = FleetProfileFactory.Duplicate(
                imported,
                MakeUniqueProfileName(imported.Name));
            await repository.SaveAsync(copy);
            await ReloadProfilesAsync(copy.Id);
            StatusMessage = $"Imported '{copy.Name}'.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Profile could not be imported: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ResolveRosterAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        var parsedEntries = RosterPasteParser.Parse(RosterPasteText);
        if (parsedEntries.Length == 0)
        {
            StatusMessage = "Paste at least one EVE character name first.";
            return;
        }

        if (SquadOptions.Count == 0)
        {
            StatusMessage = "Add at least one squad before adding characters.";
            return;
        }

        IsBusy = true;
        StatusMessage = $"Resolving {parsedEntries.Length} exact EVE character name{(parsedEntries.Length == 1 ? string.Empty : "s")}…";
        try
        {
            var result = await characterNameResolver.ResolveAsync(
                parsedEntries.Select(entry => entry.CharacterName).ToArray());
            var parsedByName = parsedEntries.ToDictionary(
                entry => entry.CharacterName,
                StringComparer.OrdinalIgnoreCase);
            var existingCharacterIds = Assignments
                .Select(assignment => assignment.CharacterId)
                .ToHashSet();
            var addedCount = 0;
            var duplicateCount = 0;

            foreach (var character in result.Resolved)
            {
                if (!existingCharacterIds.Add(character.CharacterId))
                {
                    duplicateCount++;
                    continue;
                }

                parsedByName.TryGetValue(character.CharacterName, out var parsedEntry);
                var targetSquad = FindSquadOption(parsedEntry?.SquadName) ?? SquadOptions[0];
                var assignment = new ProfileAssignmentEditorViewModel(
                    character.CharacterId,
                    character.CharacterName,
                    targetSquad.Id,
                    ParseRole(parsedEntry?.RoleText),
                    string.Empty);
                HookAssignment(assignment);
                Assignments.Add(assignment);
                addedCount++;
            }

            UnresolvedRosterEntries.Clear();
            foreach (var unresolvedName in result.UnresolvedNames)
            {
                UnresolvedRosterEntries.Add($"{unresolvedName} — no exact character match");
            }

            RosterPasteText = result.UnresolvedNames.Length == 0
                ? string.Empty
                : string.Join(Environment.NewLine, result.UnresolvedNames);
            StatusMessage = result.UserMessage ??
                $"Added {addedCount}; skipped {duplicateCount} already assigned; {result.UnresolvedNames.Length} unresolved.";
            if (addedCount > 0)
            {
                MarkEditorDirty();
                FilteredAssignments.Refresh();
                RefreshAssignmentSummary();
            }

            RefreshValidation();
            InvalidateDryRun();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Roster could not be resolved: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CompareCurrentProfileAsync()
    {
        if (!CanEdit)
        {
            return;
        }

        var profile = BuildCurrentProfile();
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0)
        {
            ValidationSummary = FormatValidation(errors);
            StatusMessage = "Fix the validation errors before comparing this profile.";
            return;
        }

        IsBusy = true;
        StatusMessage = "Reading the current fleet and building a dry run…";
        try
        {
            var liveResult = await liveFleetService.LoadCurrentAsync();
            if (liveResult.Status != LiveFleetLoadStatus.Ready || liveResult.Snapshot is null)
            {
                InvalidateDryRun();
                StatusMessage = liveResult.UserMessage;
                return;
            }

            var plan = FleetPlanner.Build(profile, liveResult.Snapshot);
            ApplyDryRun(plan, liveResult.Snapshot.ConfirmedAtUtc);
            StatusMessage = plan.BlockingIssues == 0
                ? $"Dry run ready: {plan.TotalChanges} proposed change{(plan.TotalChanges == 1 ? string.Empty : "s")}."
                : $"Dry run found {plan.BlockingIssues} blocking issue{(plan.BlockingIssues == 1 ? string.Empty : "s")}.";
        }
        catch (Exception exception)
        {
            InvalidateDryRun();
            StatusMessage = $"Dry run could not be built: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void HideDryRun() => InvalidateDryRun();

    [RelayCommand]
    private async Task StartOperationAsync()
    {
        var reviewedPlan = lastDryRunPlan;
        if (!CanEdit || reviewedPlan is null)
        {
            StatusMessage = "Generate and review a current dry run before starting.";
            return;
        }

        if (!reviewedPlan.CanExecute)
        {
            StatusMessage = "Resolve the dry-run safety blockers before starting.";
            return;
        }

        var answer = MessageBox.Show(
            $"Start a guarded fleet operation for '{reviewedPlan.ProfileName}'?\n\n" +
            $"Fleet: {reviewedPlan.FleetId}\n" +
            $"Structure creates: {reviewedPlan.StructureCreates}\n" +
            $"Structure renames: {reviewedPlan.StructureRenames}\n" +
            $"Invitations: {reviewedPlan.CharacterInvites}\n" +
            $"Planned moves/role differences: {reviewedPlan.CharacterMoves + reviewedPlan.RoleChanges}\n\n" +
            "The app will re-check the live fleet and fleet boss before writing. Structure and commander changes are serialized. It will not delete hierarchy, kick members, transfer fleet boss, or demote unmanaged commanders. Invitations must still be accepted in EVE.",
            "Start guarded operation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Re-checking the reviewed plan before the first write…";
        try
        {
            var result = await operationService.StartAsync(
                BuildCurrentProfile(),
                reviewedPlan);
            ApplyDryRun(result.CurrentPlan, DateTimeOffset.UtcNow);
            if (!result.Started || result.Operation is null)
            {
                StatusMessage = result.UserMessage;
                return;
            }

            ShowOperation(result.Operation);
            StatusMessage = result.UserMessage;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Operation could not start: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ContinueOperationAsync()
    {
        var operation = currentOperation;
        if (!CanOperate || operation is null || operation.IsTerminal)
        {
            return;
        }

        await RunOperationActionAsync(
            () => operationService.ContinueAsync(operation.Id),
            "Checking accepted invitations and current placements…");
    }

    [RelayCommand]
    private async Task RetryOperationStepAsync(FleetOperationStepViewModel? item)
    {
        var operation = currentOperation;
        if (!CanOperate || operation is null || item is null || !item.CanRetry)
        {
            return;
        }

        await RunOperationActionAsync(
            () => operationService.RetryStepAsync(operation.Id, item.StepKey),
            $"Re-checking {item.Title} before retry…");
    }

    [RelayCommand]
    private async Task SkipOperationStepAsync(FleetOperationStepViewModel? item)
    {
        var operation = currentOperation;
        if (!CanOperate || operation is null || item is null || !item.CanSkip)
        {
            return;
        }

        var answer = MessageBox.Show(
            $"Skip this operation step?\n\n{item.Title}\n\nNo further write will be sent for it during this run.",
            "Skip operation step",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunOperationActionAsync(
            () => operationService.SkipStepAsync(operation.Id, item.StepKey),
            $"Skipping {item.Title}…");
    }

    [RelayCommand]
    private async Task CancelOperationAsync()
    {
        var operation = currentOperation;
        if (!CanOperate || operation is null || operation.IsTerminal)
        {
            return;
        }

        var answer = MessageBox.Show(
            "Cancel this saved operation?\n\nAlready accepted ESI writes are not undone. Pending steps will stop and the run will not resume automatically.",
            "Cancel operation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunOperationActionAsync(
            () => operationService.CancelAsync(
                operation.Id,
                "Cancelled by the user. Already accepted writes were not undone."),
            "Cancelling the saved operation…");
    }

    [RelayCommand]
    private void HideOperation()
    {
        if (currentOperation is not null && !currentOperation.IsTerminal)
        {
            StatusMessage = "Complete or cancel the active operation before hiding it.";
            return;
        }

        currentOperation = null;
        OperationItems.Clear();
        HasOperation = false;
        HasActiveOperation = false;
        OperationIsTerminal = false;
        OperationTitle = "No saved operation.";
        OperationSummary = string.Empty;
        OperationStatusMessage = string.Empty;
        OperationPhaseTitle = "No fleet run is active";
        OperationNextAction = "Choose a profile on Home to prepare a run.";
        OperationProgressText = "0 of 0 steps finished";
        OperationProgressPercent = 0;
    }

    [RelayCommand]
    private void AddWing()
    {
        if (!CanEdit)
        {
            return;
        }

        var wing = new ProfileWingEditorViewModel(
            Guid.NewGuid(),
            MakeUniqueHierarchyName(
                $"Wing {Wings.Count + 1}",
                Wings.Select(existing => existing.Name).ToArray()));
        var squad = new ProfileSquadEditorViewModel(Guid.NewGuid(), "Squad 1");
        wing.Squads.Add(squad);
        HookWing(wing);
        Wings.Add(wing);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    [RelayCommand]
    private void AddSquad(ProfileWingEditorViewModel? wing)
    {
        if (!CanEdit || wing is null)
        {
            return;
        }

        var squad = new ProfileSquadEditorViewModel(
            Guid.NewGuid(),
            MakeUniqueHierarchyName(
                $"Squad {wing.Squads.Count + 1}",
                wing.Squads.Select(existing => existing.Name).ToArray()));
        HookSquad(squad);
        wing.Squads.Add(squad);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    [RelayCommand]
    private void DuplicateWing(ProfileWingEditorViewModel? wing)
    {
        if (!CanEdit || wing is null)
        {
            return;
        }

        var copy = new ProfileWingEditorViewModel(
            Guid.NewGuid(),
            MakeUniqueHierarchyName(
                $"{wing.Name} Copy",
                Wings.Select(existing => existing.Name).ToArray()));
        foreach (var squad in wing.Squads)
        {
            copy.Squads.Add(new ProfileSquadEditorViewModel(Guid.NewGuid(), squad.Name));
        }

        HookWing(copy);
        Wings.Insert(Wings.IndexOf(wing) + 1, copy);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    [RelayCommand]
    private void DuplicateSquad(ProfileSquadEditorViewModel? squad)
    {
        var wing = FindParentWing(squad);
        if (!CanEdit || squad is null || wing is null)
        {
            return;
        }

        var copy = new ProfileSquadEditorViewModel(
            Guid.NewGuid(),
            MakeUniqueHierarchyName(
                $"{squad.Name} Copy",
                wing.Squads.Select(existing => existing.Name).ToArray()));
        HookSquad(copy);
        wing.Squads.Insert(wing.Squads.IndexOf(squad) + 1, copy);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    [RelayCommand]
    private void MoveWingUp(ProfileWingEditorViewModel? wing) => MoveWing(wing, -1);

    [RelayCommand]
    private void MoveWingDown(ProfileWingEditorViewModel? wing) => MoveWing(wing, 1);

    [RelayCommand]
    private void MoveSquadUp(ProfileSquadEditorViewModel? squad) => MoveSquad(squad, -1);

    [RelayCommand]
    private void MoveSquadDown(ProfileSquadEditorViewModel? squad) => MoveSquad(squad, 1);

    [RelayCommand]
    private void DeleteWing(ProfileWingEditorViewModel? wing)
    {
        if (!CanEdit || wing is null)
        {
            return;
        }

        var squadIds = wing.Squads.Select(squad => squad.Id).ToHashSet();
        if (Assignments.Any(assignment => squadIds.Contains(assignment.TargetSquadId)))
        {
            StatusMessage = "Move or remove characters assigned to this wing before deleting it.";
            return;
        }

        UnhookWing(wing);
        Wings.Remove(wing);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    [RelayCommand]
    private void DeleteSquad(ProfileSquadEditorViewModel? squad)
    {
        var wing = FindParentWing(squad);
        if (!CanEdit || squad is null || wing is null)
        {
            return;
        }

        if (Assignments.Any(assignment => assignment.TargetSquadId == squad.Id))
        {
            StatusMessage = "Move or remove characters assigned to this squad before deleting it.";
            return;
        }

        UnhookSquad(squad);
        wing.Squads.Remove(squad);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    [RelayCommand]
    private void SelectAllAssignments()
    {
        foreach (var assignment in FilteredAssignments.Cast<ProfileAssignmentEditorViewModel>())
        {
            assignment.IsSelected = true;
        }

        RefreshAssignmentSummary();
    }

    [RelayCommand]
    private void ClearAssignmentSelection()
    {
        foreach (var assignment in Assignments)
        {
            assignment.IsSelected = false;
        }

        RefreshAssignmentSummary();
    }

    [RelayCommand]
    private void ApplyBulkSquad()
    {
        if (BulkTargetSquadId is not Guid squadId)
        {
            StatusMessage = "Choose a target squad first.";
            return;
        }

        ApplyToSelected(assignment => assignment.TargetSquadId = squadId, "squad");
    }

    [RelayCommand]
    private void ApplyBulkRole() =>
        ApplyToSelected(assignment => assignment.DesiredRole = BulkDesiredRole, "role");

    [RelayCommand]
    private void ApplyBulkTags()
    {
        var normalizedTags = string.Join(", ", ParseTags(BulkTagsText));
        ApplyToSelected(assignment => assignment.TagsText = normalizedTags, "tags");
    }

    [RelayCommand]
    private void RemoveSelectedAssignments()
    {
        var selected = Assignments.Where(assignment => assignment.IsSelected).ToArray();
        foreach (var assignment in selected)
        {
            UnhookAssignment(assignment);
            Assignments.Remove(assignment);
        }

        StatusMessage = selected.Length == 0
            ? "Select at least one character first."
            : $"Removed {selected.Length} character{(selected.Length == 1 ? string.Empty : "s")} from this local profile.";
        RefreshValidation();
        if (selected.Length > 0)
        {
            MarkEditorDirty();
            FilteredAssignments.Refresh();
            RefreshAssignmentSummary();
            InvalidateDryRun();
        }
    }

    partial void OnSelectedProfileChanged(ProfileListItemViewModel? value)
    {
        if (value is null)
        {
            ClearEditor();
            return;
        }

        LoadEditor(value.Profile);
    }

    partial void OnProfileSearchTextChanged(string value)
    {
        _ = value;
        FilteredProfileItems.Refresh();
        OnPropertyChanged(nameof(ProfileSearchSummary));
    }

    partial void OnAssignmentSearchTextChanged(string value)
    {
        _ = value;
        FilteredAssignments.Refresh();
        OnPropertyChanged(nameof(AssignmentSearchSummary));
    }

    partial void OnProfileNameChanged(string value)
    {
        _ = value;
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    partial void OnShowAlreadyCorrectChanged(bool value)
    {
        _ = value;
        RefreshVisibleDryRunItems();
    }

    private async Task ReloadProfilesAsync(Guid? selectedProfileId)
    {
        var profiles = await repository.LoadAllAsync();
        ProfileItems.Clear();
        foreach (var profile in profiles)
        {
            ProfileItems.Add(new ProfileListItemViewModel(profile));
        }

        FilteredProfileItems.Refresh();
        OnPropertyChanged(nameof(ProfileSearchSummary));

        SelectedProfile = selectedProfileId is Guid id
            ? ProfileItems.FirstOrDefault(item => item.Id == id)
            : ProfileItems.FirstOrDefault();
        if (SelectedProfile is null)
        {
            ClearEditor();
        }
    }

    private void LoadEditor(FleetProfile profile)
    {
        InvalidateDryRun();
        isLoadingEditor = true;
        ClearEditorCollections();
        EditingProfileId = profile.Id;
        ProfileName = profile.Name;

        foreach (var wing in profile.Wings.OrderBy(wing => wing.SortOrder))
        {
            var wingEditor = new ProfileWingEditorViewModel(wing.Id, wing.Name);
            foreach (var squad in wing.Squads.OrderBy(squad => squad.SortOrder))
            {
                wingEditor.Squads.Add(new ProfileSquadEditorViewModel(squad.Id, squad.Name));
            }

            HookWing(wingEditor);
            Wings.Add(wingEditor);
        }

        foreach (var assignment in profile.Assignments
            .OrderBy(assignment => assignment.CharacterName, StringComparer.OrdinalIgnoreCase))
        {
            var editor = new ProfileAssignmentEditorViewModel(
                assignment.CharacterId,
                assignment.CharacterName,
                assignment.TargetSquadId,
                assignment.DesiredRole,
                string.Join(", ", assignment.Tags));
            HookAssignment(editor);
            Assignments.Add(editor);
        }

        UnresolvedRosterEntries.Clear();
        RosterPasteText = string.Empty;
        IsEditorActive = true;
        UpdateSquadOptions();
        isLoadingEditor = false;
        HasUnsavedChanges = false;
        FilteredAssignments.Refresh();
        RefreshAssignmentSummary();
        RefreshValidation();
    }

    private void ClearEditor()
    {
        InvalidateDryRun();
        isLoadingEditor = true;
        ClearEditorCollections();
        EditingProfileId = Guid.Empty;
        ProfileName = string.Empty;
        IsEditorActive = false;
        SquadOptions.Clear();
        UnresolvedRosterEntries.Clear();
        ValidationSummary = "No profile selected.";
        isLoadingEditor = false;
        HasUnsavedChanges = false;
        FilteredAssignments.Refresh();
        RefreshAssignmentSummary();
    }

    private void ClearEditorCollections()
    {
        foreach (var wing in Wings)
        {
            UnhookWing(wing);
        }

        foreach (var assignment in Assignments)
        {
            UnhookAssignment(assignment);
        }

        Wings.Clear();
        Assignments.Clear();
    }

    private FleetProfile BuildCurrentProfile() =>
        new(
            EditingProfileId,
            ProfileName.Trim(),
            Wings.Select((wing, wingIndex) => new ProfileWing(
                wing.Id,
                wing.Name.Trim(),
                wingIndex,
                wing.Squads.Select((squad, squadIndex) => new ProfileSquad(
                    squad.Id,
                    squad.Name.Trim(),
                    squadIndex)).ToArray())).ToArray(),
            Assignments.Select(assignment => new ProfileAssignment(
                assignment.CharacterId,
                assignment.CharacterName,
                assignment.TargetSquadId,
                assignment.DesiredRole)
            {
                Tags = ParseTags(assignment.TagsText),
            }).ToArray());

    private void RefreshValidation()
    {
        if (isLoadingEditor || !IsEditorActive)
        {
            return;
        }

        var errors = ProfileValidator.Validate(BuildCurrentProfile());
        ValidationSummary = errors.Count == 0
            ? $"Valid profile • {Assignments.Count} characters • {Wings.Count} wings • {SquadOptions.Count} squads"
            : FormatValidation(errors);
    }

    private void UpdateSquadOptions()
    {
        var previousBulkTarget = BulkTargetSquadId;
        SquadOptions.Clear();
        foreach (var wing in Wings)
        {
            foreach (var squad in wing.Squads)
            {
                SquadOptions.Add(new ProfileSquadOptionViewModel(
                    squad.Id,
                    $"{wing.Name} / {squad.Name}"));
            }
        }

        BulkTargetSquadId = previousBulkTarget is Guid previousId &&
            SquadOptions.Any(option => option.Id == previousId)
                ? previousId
                : SquadOptions.FirstOrDefault()?.Id;
    }

    private void HookWing(ProfileWingEditorViewModel wing)
    {
        wing.PropertyChanged += OnHierarchyPropertyChanged;
        foreach (var squad in wing.Squads)
        {
            HookSquad(squad);
        }
    }

    private void UnhookWing(ProfileWingEditorViewModel wing)
    {
        wing.PropertyChanged -= OnHierarchyPropertyChanged;
        foreach (var squad in wing.Squads)
        {
            UnhookSquad(squad);
        }
    }

    private void HookSquad(ProfileSquadEditorViewModel squad)
    {
        squad.PropertyChanged += OnHierarchyPropertyChanged;
    }

    private void UnhookSquad(ProfileSquadEditorViewModel squad)
    {
        squad.PropertyChanged -= OnHierarchyPropertyChanged;
    }

    private void HookAssignment(ProfileAssignmentEditorViewModel assignment)
    {
        assignment.PropertyChanged += OnAssignmentPropertyChanged;
    }

    private void UnhookAssignment(ProfileAssignmentEditorViewModel assignment)
    {
        assignment.PropertyChanged -= OnAssignmentPropertyChanged;
    }

    private void OnHierarchyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (string.Equals(e.PropertyName, "Name", StringComparison.Ordinal))
        {
            UpdateSquadOptions();
        }

        MarkEditorDirty();
        FilteredAssignments.Refresh();
        RefreshValidation();
        InvalidateDryRun();
    }

    private void OnAssignmentPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        RefreshAssignmentSummary();
        RefreshValidation();
        if (!string.Equals(e.PropertyName, nameof(ProfileAssignmentEditorViewModel.IsSelected), StringComparison.Ordinal))
        {
            MarkEditorDirty();
            FilteredAssignments.Refresh();
            InvalidateDryRun();
        }
    }

    private bool FilterProfile(object item)
    {
        if (item is not ProfileListItemViewModel profile || string.IsNullOrWhiteSpace(ProfileSearchText))
        {
            return true;
        }

        return profile.Name.Contains(ProfileSearchText.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private bool FilterAssignment(object item)
    {
        if (item is not ProfileAssignmentEditorViewModel assignment ||
            string.IsNullOrWhiteSpace(AssignmentSearchText))
        {
            return true;
        }

        var search = AssignmentSearchText.Trim();
        var squadName = SquadOptions
            .FirstOrDefault(option => option.Id == assignment.TargetSquadId)
            ?.DisplayName;
        return assignment.CharacterName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            assignment.TagsText.Contains(search, StringComparison.OrdinalIgnoreCase) ||
            (squadName?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) ||
            assignment.DesiredRole.ToString().Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private void MarkEditorDirty()
    {
        if (!isLoadingEditor && IsEditorActive)
        {
            HasUnsavedChanges = true;
        }
    }

    private void RefreshAssignmentSummary() =>
        OnPropertyChanged(nameof(AssignmentSearchSummary));

    private void MoveWing(ProfileWingEditorViewModel? wing, int offset)
    {
        if (!CanEdit || wing is null)
        {
            return;
        }

        MoveItem(Wings, wing, offset);
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    private void MoveSquad(ProfileSquadEditorViewModel? squad, int offset)
    {
        var wing = FindParentWing(squad);
        if (!CanEdit || squad is null || wing is null)
        {
            return;
        }

        MoveItem(wing.Squads, squad, offset);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    private ProfileWingEditorViewModel? FindParentWing(ProfileSquadEditorViewModel? squad) =>
        squad is null
            ? null
            : Wings.FirstOrDefault(wing => wing.Squads.Contains(squad));

    private ProfileSquadOptionViewModel? FindSquadOption(string? requestedName)
    {
        if (string.IsNullOrWhiteSpace(requestedName))
        {
            return null;
        }

        var normalized = requestedName.Trim();
        var fullMatch = SquadOptions.FirstOrDefault(option =>
            string.Equals(option.DisplayName, normalized, StringComparison.OrdinalIgnoreCase));
        if (fullMatch is not null)
        {
            return fullMatch;
        }

        var leafMatches = Wings
            .SelectMany(wing => wing.Squads)
            .Where(squad => string.Equals(squad.Name, normalized, StringComparison.OrdinalIgnoreCase))
            .Select(squad => SquadOptions.First(option => option.Id == squad.Id))
            .ToArray();
        return leafMatches.Length == 1 ? leafMatches[0] : null;
    }

    private void ApplyToSelected(
        Action<ProfileAssignmentEditorViewModel> action,
        string fieldName)
    {
        var selected = Assignments.Where(assignment => assignment.IsSelected).ToArray();
        foreach (var assignment in selected)
        {
            action(assignment);
        }

        StatusMessage = selected.Length == 0
            ? "Select at least one character first."
            : $"Updated {fieldName} for {selected.Length} selected character{(selected.Length == 1 ? string.Empty : "s")}.";
        RefreshValidation();
    }

    private string MakeUniqueProfileName(string baseName)
    {
        var existingNames = ProfileItems
            .Select(item => item.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (existingNames.Add(baseName))
        {
            return baseName;
        }

        for (var suffix = 2; ; suffix++)
        {
            var candidate = $"{baseName} {suffix}";
            if (existingNames.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static string MakeUniqueHierarchyName(string baseName, string[] existingNames)
    {
        var names = existingNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var normalizedBase = baseName.Trim();
        var firstCandidate = normalizedBase[..Math.Min(
            normalizedBase.Length,
            ProfileValidator.MaximumHierarchyNameLength)];
        if (names.Add(firstCandidate))
        {
            return firstCandidate;
        }

        for (var suffix = 2; ; suffix++)
        {
            var suffixText = $" {suffix}";
            var prefixLength = Math.Max(
                1,
                ProfileValidator.MaximumHierarchyNameLength - suffixText.Length);
            var prefix = normalizedBase[..Math.Min(normalizedBase.Length, prefixLength)];
            var candidate = $"{prefix}{suffixText}";
            if (names.Add(candidate))
            {
                return candidate;
            }
        }
    }

    private static void MoveItem<T>(ObservableCollection<T> items, T item, int offset)
    {
        var currentIndex = items.IndexOf(item);
        var targetIndex = currentIndex + offset;
        if (currentIndex >= 0 && targetIndex >= 0 && targetIndex < items.Count)
        {
            items.Move(currentIndex, targetIndex);
        }
    }

    private static DesiredFleetRole ParseRole(string? value)
    {
        var normalized = value?
            .Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
        return normalized switch
        {
            "fleetcommander" or "fleetboss" or "fc" => DesiredFleetRole.FleetCommander,
            "wingcommander" or "wc" => DesiredFleetRole.WingCommander,
            "squadcommander" or "sc" => DesiredFleetRole.SquadCommander,
            _ => DesiredFleetRole.SquadMember,
        };
    }

    private static string[] ParseTags(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(
                    TagSeparators,
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static string FormatValidation(IReadOnlyList<ProfileValidationError> errors) =>
        string.Join(" • ", errors.Take(3).Select(error => error.Message));

    private void InvalidateDryRun()
    {
        lastDryRunPlan = null;
        DryRunItems.Clear();
        HasDryRun = false;
        DryRunTitle = "No comparison generated.";
        DryRunSummary = string.Empty;
        DryRunSafetyMessage = string.Empty;
        DryRunPrimaryMessage = "Choose a profile and check the live fleet.";
    }

    private void ApplyDryRun(FleetDryRunPlan plan, DateTimeOffset confirmedAtUtc)
    {
        lastDryRunPlan = plan;
        DryRunTitle =
            $"'{plan.ProfileName}' compared with fleet {plan.FleetId} • live state confirmed {confirmedAtUtc.ToLocalTime():t}";
        DryRunSummary = BuildDryRunSummary(plan);
        DryRunSafetyMessage = BuildDryRunSafetyMessage(plan);
        DryRunPrimaryMessage = plan.BlockingIssues > 0
            ? "This run needs attention before it can start"
            : plan.TotalChanges == 0
                ? "Fleet is already organised"
                : "Ready for your confirmation";
        HasDryRun = true;
        RefreshVisibleDryRunItems();
    }

    private async Task RunOperationActionAsync(
        Func<Task<FleetOperation>> action,
        string progressMessage)
    {
        IsBusy = true;
        StatusMessage = progressMessage;
        try
        {
            var operation = await action();
            ShowOperation(operation);
            StatusMessage = operation.Message ?? "Operation updated.";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Operation could not continue: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ShowOperation(FleetOperation operation)
    {
        currentOperation = operation;
        OperationItems.Clear();
        foreach (var step in operation.Steps.OrderBy(step => step.SortOrder))
        {
            OperationItems.Add(new FleetOperationStepViewModel(step));
        }

        HasOperation = true;
        HasActiveOperation = !operation.IsTerminal;
        OperationIsTerminal = operation.IsTerminal;
        OperationTitle =
            $"{operation.ProfileName} • fleet {operation.FleetId} • {HumanizeOperationState(operation.State)}";
        OperationSummary =
            $"{operation.SucceededSteps} confirmed • {operation.WaitingSteps} waiting • " +
            $"{operation.PendingSteps} pending • {operation.FailedSteps} failed • {operation.SkippedSteps} skipped";
        OperationStatusMessage = operation.Message ?? string.Empty;
        OperationPhaseTitle = GetOperationPhaseTitle(operation.State);
        OperationNextAction = GetOperationNextAction(operation.State);
        var finishedSteps = operation.SucceededSteps + operation.FailedSteps + operation.SkippedSteps;
        var totalSteps = operation.Steps.Length;
        OperationProgressText =
            $"{finishedSteps} of {totalSteps} steps finished";
        OperationProgressPercent = totalSteps == 0
            ? operation.IsTerminal ? 100 : 0
            : Math.Clamp((double)finishedSteps / totalSteps * 100, 0, 100);
    }

    private static string GetOperationPhaseTitle(OperationState state) => state switch
    {
        OperationState.EnsureStructure => "1. Preparing wings and squads",
        OperationState.InviteMissing => "2. Sending invitations",
        OperationState.AwaitAcceptance => "3. Waiting for characters to accept",
        OperationState.PlaceMembers => "4. Placing characters",
        OperationState.AssignCommanders => "5. Assigning commanders",
        OperationState.Verify => "6. Checking the finished fleet",
        OperationState.NeedsAttention => "Action needed",
        OperationState.Complete => "Fleet ready",
        OperationState.Cancelled => "Run cancelled safely",
        _ => "Preparing the fleet run",
    };

    private static string GetOperationNextAction(OperationState state) => state switch
    {
        OperationState.AwaitAcceptance =>
            "Accept the invitations in EVE, then choose Check accepted characters.",
        OperationState.NeedsAttention =>
            "Open the technical steps below, then retry or skip the failed item.",
        OperationState.Complete =>
            "The requested layout was confirmed against the live fleet.",
        OperationState.Cancelled =>
            "No more writes will be sent. Writes already accepted by EVE were left in place.",
        _ => "Fleet Organizer will perform one guarded step at a time.",
    };

    private static string HumanizeOperationState(OperationState state) => state switch
    {
        OperationState.InviteMissing => "inviting",
        OperationState.EnsureStructure => "repairing structure",
        OperationState.AwaitAcceptance => "awaiting acceptance",
        OperationState.PlaceMembers => "placing members",
        OperationState.AssignCommanders => "assigning commanders",
        OperationState.Verify => "verifying",
        OperationState.NeedsAttention => "needs attention",
        OperationState.Complete => "complete",
        OperationState.Cancelled => "cancelled",
        _ => state.ToString(),
    };

    private void RefreshVisibleDryRunItems()
    {
        DryRunItems.Clear();
        if (lastDryRunPlan is null)
        {
            return;
        }

        foreach (var item in lastDryRunPlan.Items)
        {
            var viewModel = new FleetPlanItemViewModel(item);
            if (ShowAlreadyCorrect || !viewModel.IsAlreadyCorrect)
            {
                DryRunItems.Add(viewModel);
            }
        }
    }

    private static string BuildDryRunSummary(FleetDryRunPlan plan) =>
        $"{plan.StructureChanges} structure • {plan.CharacterInvites} invites • " +
        $"{plan.CharacterMoves} moves • {plan.RoleChanges} role changes • " +
        $"{plan.AlreadyCorrect} already correct • {plan.IgnoredLiveMembers} live members left untouched";

    private static string BuildDryRunSafetyMessage(FleetDryRunPlan plan)
    {
        if (plan.BlockingIssues > 0)
        {
            return $"Blocked: resolve {plan.BlockingIssues} safety issue{(plan.BlockingIssues == 1 ? string.Empty : "s")} before an operation could start.";
        }

        return plan.TotalChanges == 0
            ? "The saved assignments already match the live fleet. No changes are needed."
            : "Milestone 5 can repair shown structure, invite/place members, and apply serialized squad/wing commander roles after one final confirmation. No hierarchy is deleted and unmanaged live members remain untouched.";
    }

    private static string GetSafeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var safeCharacters = value
            .Select(character => invalidCharacters.Contains(character) ? '-' : character)
            .ToArray();
        var safeName = new string(safeCharacters).Trim();
        return safeName.Length == 0 ? "fleet-profile" : safeName;
    }
}
