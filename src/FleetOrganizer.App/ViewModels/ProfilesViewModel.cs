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

public partial class ProfilesViewModel : ObservableObject, IDisposable
{
    private static readonly char[] TagSeparators = [',', ';'];

    private readonly IFleetProfileRepository repository;
    private readonly ICharacterNameResolver characterNameResolver;
    private readonly ILiveFleetService liveFleetService;
    private readonly IFleetOperationService operationService;
    private readonly IFleetDeskPreferencesRepository preferencesRepository;
    private readonly SemaphoreSlim preferencesGate = new(1, 1);
    private bool isLoadingEditor;
    private bool isLoadingPreferences;
    private FleetDryRunPlan? lastDryRunPlan;
    private FleetProfile? lastPreparedProfile;
    private int lastShipRuleMatchCount;
    private int lastShipRuleCapacitySkipCount;
    private FleetOperation? currentOperation;
    private Guid? inviteTimeoutRaisedForOperation;
    private FleetDeskPreferences preferences = new();

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
    [NotifyPropertyChangedFor(nameof(CanStartReviewedOperation))]
    public partial bool IsEditorActive { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    [NotifyPropertyChangedFor(nameof(CanPrepareFleet))]
    [NotifyPropertyChangedFor(nameof(CanPreviewRestore))]
    [NotifyPropertyChangedFor(nameof(CanStartReviewedOperation))]
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
    public partial string DryRunBlockingDetails { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string ShipRuleMatchSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool ShowAlreadyCorrect { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanOperate))]
    [NotifyPropertyChangedFor(nameof(CanPreviewRestore))]
    public partial bool HasOperation { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyPropertyChangedFor(nameof(CanPrepareFleet))]
    [NotifyPropertyChangedFor(nameof(CanStartReviewedOperation))]
    public partial bool HasActiveOperation { get; set; }

    [ObservableProperty]
    public partial string OperationTitle { get; set; } = "No saved operation.";

    [ObservableProperty]
    public partial string OperationSummary { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string OperationStatusMessage { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviewRestore))]
    public partial bool OperationIsTerminal { get; set; }

    [ObservableProperty]
    public partial string OperationPhaseTitle { get; set; } = "No fleet run is active";

    [ObservableProperty]
    public partial string OperationNextAction { get; set; } =
        "Choose a saved setup on Live Fleet to prepare a run.";

    [ObservableProperty]
    public partial string OperationProgressText { get; set; } = "0 of 0 steps finished";

    [ObservableProperty]
    public partial double OperationProgressPercent { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedRunModeDescription))]
    [NotifyPropertyChangedFor(nameof(RunPrimaryActionText))]
    public partial FleetRunMode SelectedRunMode { get; set; } = FleetRunMode.FullOrganise;

    [ObservableProperty]
    public partial bool AttentionSoundsEnabled { get; set; } = true;

    [ObservableProperty]
    public partial int FleetPollingSeconds { get; set; } = 30;

    [ObservableProperty]
    public partial int InvitationCheckSeconds { get; set; } = 30;

    [ObservableProperty]
    public partial int InvitationTimeoutMinutes { get; set; } = 10;

    [ObservableProperty]
    public partial bool StartMinimized { get; set; }

    [ObservableProperty]
    public partial bool MinimizeToTray { get; set; }

    [ObservableProperty]
    public partial FleetDeskTheme SelectedTheme { get; set; } = FleetDeskTheme.System;

    [ObservableProperty]
    public partial bool IsWaitingForInvites { get; set; }

    [ObservableProperty]
    public partial string WaitingRoomSummary { get; set; } = "No invitations are waiting.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreviewRestore))]
    public partial bool HasRestoreSnapshot { get; set; }

    public ProfilesViewModel(
        IFleetProfileRepository repository,
        ICharacterNameResolver characterNameResolver,
        ILiveFleetService liveFleetService,
        IFleetOperationService operationService,
        IFleetDeskPreferencesRepository preferencesRepository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(characterNameResolver);
        ArgumentNullException.ThrowIfNull(liveFleetService);
        ArgumentNullException.ThrowIfNull(operationService);
        ArgumentNullException.ThrowIfNull(preferencesRepository);

        this.repository = repository;
        this.characterNameResolver = characterNameResolver;
        this.liveFleetService = liveFleetService;
        this.operationService = operationService;
        this.preferencesRepository = preferencesRepository;

        FilteredProfileItems = CollectionViewSource.GetDefaultView(ProfileItems);
        FilteredProfileItems.Filter = FilterProfile;
        FilteredAssignments = CollectionViewSource.GetDefaultView(Assignments);
        FilteredAssignments.Filter = FilterAssignment;
    }

    public ObservableCollection<ProfileListItemViewModel> ProfileItems { get; } = [];

    public ObservableCollection<ProfileWingEditorViewModel> Wings { get; } = [];

    public ObservableCollection<ProfileAssignmentEditorViewModel> Assignments { get; } = [];

    public ObservableCollection<ProfileShipRuleEditorViewModel> ShipRules { get; } = [];

    public ObservableCollection<ProfileSquadOptionViewModel> SquadOptions { get; } = [];

    public ObservableCollection<string> ObservedShipTypes { get; } = [];

    public ObservableCollection<string> UnresolvedRosterEntries { get; } = [];

    public ObservableCollection<FleetPlanItemViewModel> DryRunItems { get; } = [];

    public ObservableCollection<FleetOperationStepViewModel> OperationItems { get; } = [];

    public ObservableCollection<FleetOperationHistoryItemViewModel> OperationHistory { get; } = [];

    public ObservableCollection<WaitingCharacterViewModel> WaitingCharacters { get; } = [];

    public ICollectionView FilteredProfileItems { get; }

    public ICollectionView FilteredAssignments { get; }

    public DesiredRoleOptionViewModel[] RoleOptions { get; } =
    [
        new(DesiredFleetRole.SquadMember, "Squad member"),
        new(DesiredFleetRole.SquadCommander, "Squad commander"),
        new(DesiredFleetRole.WingCommander, "Wing commander"),
        new(DesiredFleetRole.FleetCommander, "Fleet commander"),
    ];

    public FleetRunModeOptionViewModel[] RunModeOptions { get; } =
    [
        new(FleetRunMode.FullOrganise, "Full organise", "Build structure, invite, place, and assign commanders."),
        new(FleetRunMode.InviteMissing, "Invite missing", "Send only missing-character invitations."),
        new(FleetRunMode.PlacePresent, "Place joined", "Move characters already in fleet; never invite."),
        new(FleetRunMode.FixStructure, "Fix structure", "Create or rename wings and squads only."),
        new(FleetRunMode.AssignCommanders, "Assign commanders", "Promote configured commanders after placement is ready."),
    ];

    public ThemeOptionViewModel[] ThemeOptions { get; } =
    [
        new(FleetDeskTheme.System, "Use Windows setting"),
        new(FleetDeskTheme.Light, "Light"),
        new(FleetDeskTheme.Dark, "Dark"),
    ];

    public event EventHandler<FleetAttentionEventArgs>? AttentionRequested;

    public bool CanEdit => IsEditorActive && !IsBusy && !HasActiveOperation;

    public bool CanOperate => HasOperation && !IsBusy;

    public bool CanPrepareFleet =>
        IsEditorActive && !IsBusy && !HasActiveOperation && !HasUnsavedChanges;

    public bool CanStartReviewedOperation =>
        CanEdit && lastDryRunPlan is { CanExecute: true, TotalChanges: > 0 };

    public bool CanPreviewRestore => HasOperation && OperationIsTerminal && HasRestoreSnapshot && !IsBusy;

    public string SelectedRunModeDescription => RunModeOptions
        .Single(option => option.Value == SelectedRunMode)
        .Description;

    public string RunPrimaryActionText => lastDryRunPlan switch
    {
        { BlockingIssues: > 0 } => "Resolve blockers first",
        { TotalChanges: 0 } => "No changes needed",
        { CharacterInvites: > 0, StructureChanges: 0, CharacterMoves: 0, RoleChanges: 0 } plan =>
            $"Send {plan.CharacterInvites} invite{(plan.CharacterInvites == 1 ? string.Empty : "s")}",
        { Mode: FleetRunMode.ApplyLiveChanges } plan =>
            $"Apply {plan.CharacterMoves + plan.RoleChanges} fleet change{(plan.CharacterMoves + plan.RoleChanges == 1 ? string.Empty : "s")}",
        _ => SelectedRunMode switch
        {
            FleetRunMode.InviteMissing => "Send missing invites",
            FleetRunMode.PlacePresent => "Place joined characters",
            FleetRunMode.FixStructure => "Fix fleet structure",
            FleetRunMode.AssignCommanders => "Assign commanders",
            _ => "Organise fleet now",
        },
    };

    public IEnumerable<ProfileListItemViewModel> PinnedProfiles => ProfileItems
        .Where(item => item.IsPinned || item.IsDefault)
        .OrderByDescending(item => item.IsDefault)
        .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase);

    public string DefaultProfileName => ProfileItems
        .FirstOrDefault(item => item.IsDefault)?.Name ?? "No default template";

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

    public FleetProfile? GetSelectedProfileSnapshot() =>
        IsEditorActive && EditingProfileId != Guid.Empty
            ? BuildCurrentProfile()
            : null;

    public void Dispose()
    {
        preferencesGate.Dispose();
        GC.SuppressFinalize(this);
    }

    public async Task InitializeAsync()
    {
        try
        {
            isLoadingPreferences = true;
            preferences = await preferencesRepository.LoadAsync();
            SelectedRunMode = Enum.IsDefined(preferences.RunMode)
                ? preferences.RunMode
                : FleetRunMode.FullOrganise;
            AttentionSoundsEnabled = preferences.AttentionSoundsEnabled;
            FleetPollingSeconds = Math.Clamp(preferences.FleetPollingSeconds, 15, 300);
            InvitationCheckSeconds = Math.Clamp(preferences.InvitationCheckSeconds, 15, 300);
            InvitationTimeoutMinutes = Math.Clamp(preferences.InvitationTimeoutMinutes, 1, 120);
            StartMinimized = preferences.StartMinimized;
            MinimizeToTray = preferences.MinimizeToTray;
            SelectedTheme = Enum.IsDefined(preferences.Theme)
                ? preferences.Theme
                : FleetDeskTheme.System;
            await ReloadProfilesAsync(preferences.DefaultProfileId ?? preferences.LastUsedProfileId);
            isLoadingPreferences = false;
            var resumableOperation = await operationService.LoadLatestResumableAsync();
            if (resumableOperation is not null)
            {
                ShowOperation(resumableOperation);
            }

            await ReloadOperationHistoryAsync();

            StatusMessage = ProfileItems.Count == 0
                ? "No saved profiles yet. Create one or capture the current live fleet."
                : $"Loaded {ProfileItems.Count} saved profile{(ProfileItems.Count == 1 ? string.Empty : "s")}.";
        }
        catch (Exception exception)
        {
            isLoadingPreferences = false;
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
    private void SelectProfile(ProfileListItemViewModel? item)
    {
        if (item is not null)
        {
            SelectedProfile = item;
        }
    }

    [RelayCommand]
    private async Task TogglePinAsync(ProfileListItemViewModel? item)
    {
        item ??= SelectedProfile;
        if (item is null)
        {
            return;
        }

        item.IsPinned = !item.IsPinned;
        await SavePreferencesAsync();
        OnPropertyChanged(nameof(PinnedProfiles));
        StatusMessage = item.IsPinned
            ? $"Pinned '{item.Name}' to the FC console."
            : $"Unpinned '{item.Name}'.";
    }

    [RelayCommand]
    private async Task SetDefaultProfileAsync(ProfileListItemViewModel? item)
    {
        item ??= SelectedProfile;
        if (item is null)
        {
            return;
        }

        foreach (var profile in ProfileItems)
        {
            profile.IsDefault = profile.Id == item.Id;
        }

        item.IsPinned = true;
        SelectedProfile = item;
        await SavePreferencesAsync();
        OnPropertyChanged(nameof(PinnedProfiles));
        OnPropertyChanged(nameof(DefaultProfileName));
        StatusMessage = $"'{item.Name}' is now the default FC template.";
    }

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
            await SavePreferencesAsync();
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

            UpdateObservedShipTypes(liveResult.Snapshot);
            var resolution = ShipRuleResolver.Resolve(profile, liveResult.Snapshot);
            lastPreparedProfile = resolution.EffectiveProfile;
            lastShipRuleMatchCount = resolution.Matches.Count;
            lastShipRuleCapacitySkipCount = resolution.CapacitySkipped.Count;
            var plan = FleetPlanModeFilter.Apply(
                FleetPlanner.Build(resolution.EffectiveProfile, liveResult.Snapshot),
                SelectedRunMode);
            ApplyDryRun(plan, liveResult.Snapshot.ConfirmedAtUtc);
            await SavePreferencesAsync();
            StatusMessage = plan.BlockingIssues == 0
                ? $"Preview ready: {plan.TotalChanges} proposed change{(plan.TotalChanges == 1 ? string.Empty : "s")}" +
                    (lastShipRuleMatchCount == 0
                        ? "."
                        : $"; {lastShipRuleMatchCount} live character{(lastShipRuleMatchCount == 1 ? string.Empty : "s")} matched by ship type.")
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
    private Task<bool> StartOperationAsync() => StartPreparedOperationAsync();

    public async Task<bool> StartPreparedOperationAsync()
    {
        var reviewedPlan = lastDryRunPlan;
        var preparedProfile = lastPreparedProfile;
        if (reviewedPlan is null || preparedProfile is null)
        {
            StatusMessage = "Generate and review a current dry run before starting.";
            return false;
        }

        if (!reviewedPlan.CanExecute)
        {
            StatusMessage = string.IsNullOrWhiteSpace(DryRunBlockingDetails)
                ? "Resolve the dry-run safety blockers before starting."
                : DryRunBlockingDetails;
            return false;
        }

        if (!CanStartReviewedOperation)
        {
            StatusMessage = reviewedPlan.TotalChanges == 0
                ? "The live fleet already matches. There is nothing to send."
                : "Finish the current fleet operation before starting another one.";
            return false;
        }

        var actionCount = reviewedPlan.TotalChanges;
        var isRoutineLiveChange = reviewedPlan.Mode == FleetRunMode.ApplyLiveChanges;
        var answer = MessageBox.Show(
            $"{(isRoutineLiveChange ? "Apply" : "Start")} {actionCount} reviewed fleet change{(actionCount == 1 ? string.Empty : "s")}?\n\n" +
            $"Setup: {reviewedPlan.ProfileName}\n" +
            $"Mode: {HumanizeRunMode(reviewedPlan.Mode)}\n" +
            $"Fleet: {reviewedPlan.FleetId}\n" +
            $"Structure creates: {reviewedPlan.StructureCreates}\n" +
            $"Structure renames: {reviewedPlan.StructureRenames}\n" +
            $"Invitations: {reviewedPlan.CharacterInvites}\n" +
            $"Moves and role changes: {reviewedPlan.CharacterMoves + reviewedPlan.RoleChanges}\n\n" +
            "Fleet Desk re-checks the fleet and fleet-boss authority before writing. Nothing outside this reviewed list is changed.",
            isRoutineLiveChange ? "Apply fleet changes" : "Start fleet setup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            StatusMessage = "No fleet changes were sent.";
            return false;
        }

        IsBusy = true;
        StatusMessage = "Re-checking the reviewed plan before the first write…";
        try
        {
            var result = await operationService.StartAsync(
                preparedProfile,
                reviewedPlan);
            ApplyDryRun(result.CurrentPlan, DateTimeOffset.UtcNow);
            if (!result.Started || result.Operation is null)
            {
                StatusMessage = result.UserMessage;
                return false;
            }

            ShowOperation(result.Operation);
            await ReloadOperationHistoryAsync();
            StatusMessage = result.UserMessage;
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Operation could not start: {exception.Message}";
            return false;
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

        RaiseInvitationTimeoutAttention(operation);

        await RunOperationActionAsync(
            () => operationService.ContinueAsync(operation.Id),
            "Checking accepted invitations and current placements…");
    }

    public async Task AutoContinueOperationAsync()
    {
        var operation = currentOperation;
        if (IsBusy ||
            operation is null ||
            operation.IsTerminal ||
            operation.State != OperationState.AwaitAcceptance)
        {
            return;
        }

        RaiseInvitationTimeoutAttention(operation);
        await RunOperationActionAsync(
            () => operationService.ContinueAsync(operation.Id),
            "Automatically checking for accepted invitations…");
    }

    private void RaiseInvitationTimeoutAttention(FleetOperation operation)
    {
        if (inviteTimeoutRaisedForOperation == operation.Id)
        {
            return;
        }

        var waitingSince = operation.Steps
            .Where(step => step.Type == FleetOperationStepType.Invite &&
                step.State == FleetOperationStepState.Waiting)
            .Select(step => step.UpdatedAtUtc)
            .DefaultIfEmpty(operation.CreatedAtUtc)
            .Min();
        if (DateTimeOffset.UtcNow - waitingSince < TimeSpan.FromMinutes(InvitationTimeoutMinutes))
        {
            return;
        }

        inviteTimeoutRaisedForOperation = operation.Id;
        AttentionRequested?.Invoke(
            this,
            new FleetAttentionEventArgs(
                $"Invitations have been waiting for {InvitationTimeoutMinutes} minutes. Automatic safe checks will continue; inspect the listed clients when convenient.",
                isUrgent: true));
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
        OperationNextAction = "Choose a saved setup on Live Fleet to prepare a run.";
        OperationProgressText = "0 of 0 steps finished";
        OperationProgressPercent = 0;
        WaitingCharacters.Clear();
        IsWaitingForInvites = false;
        WaitingRoomSummary = "No invitations are waiting.";
        HasRestoreSnapshot = false;
    }

    [RelayCommand]
    private async Task OpenHistoryOperationAsync(FleetOperationHistoryItemViewModel? item)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var operation = await operationService.LoadAsync(item.Id);
            if (operation is null)
            {
                StatusMessage = "That saved run is no longer available.";
                await ReloadOperationHistoryAsync();
                return;
            }

            ShowOperation(operation);
            StatusMessage = $"Opened saved run from {operation.UpdatedAtUtc.ToLocalTime():g}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<bool> PrepareRestorePreviewAsync()
    {
        var operation = currentOperation;
        if (operation is null || !operation.IsTerminal || IsBusy)
        {
            StatusMessage = "Finish the current run before preparing a restore preview.";
            return false;
        }

        IsBusy = true;
        StatusMessage = "Loading the pre-run snapshot and comparing it with the live fleet…";
        try
        {
            var initialSnapshot = await operationService.LoadInitialSnapshotAsync(operation.Id);
            if (initialSnapshot is null)
            {
                HasRestoreSnapshot = false;
                StatusMessage = "This run does not have a pre-run snapshot to preview.";
                return false;
            }

            var liveResult = await liveFleetService.LoadCurrentAsync();
            if (liveResult.Status != LiveFleetLoadStatus.Ready || liveResult.Snapshot is null)
            {
                StatusMessage = liveResult.UserMessage;
                return false;
            }

            var restoreProfile = FleetProfileFactory.FromLiveFleet(
                initialSnapshot,
                $"Restore before {operation.ProfileName}") with
            {
                Id = operation.ProfileId,
            };
            lastPreparedProfile = restoreProfile;
            lastShipRuleMatchCount = 0;
            lastShipRuleCapacitySkipCount = 0;
            var plan = FleetPlanModeFilter.Apply(
                FleetPlanner.Build(restoreProfile, liveResult.Snapshot),
                FleetRunMode.FullOrganise);
            ApplyDryRun(plan, liveResult.Snapshot.ConfirmedAtUtc);
            DryRunSafetyMessage +=
                " Restore is best-effort: the preview never kicks members or deletes hierarchy, and characters who left may appear as invitations.";
            StatusMessage = "Restore preview ready. Review every proposed change before starting it.";
            return true;
        }
        catch (Exception exception)
        {
            StatusMessage = $"Restore preview could not be prepared: {exception.Message}";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<bool> PrepareStagedMovesAsync(
        LiveFleetSnapshot snapshot,
        StagedLiveMoveViewModel[] stagedMoves) =>
        PrepareLiveDeskChangesAsync(snapshot, stagedMoves, []);

    public async Task<bool> PrepareLiveDeskChangesAsync(
        LiveFleetSnapshot snapshot,
        StagedLiveMoveViewModel[] stagedMoves,
        StagedLiveInviteViewModel[] stagedInvites)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(stagedMoves);
        ArgumentNullException.ThrowIfNull(stagedInvites);

        if (SelectedProfile is null)
        {
            var auditProfile = FleetProfileFactory.FromLiveFleet(
                snapshot,
                MakeUniqueProfileName("Live Desk"));
            await repository.SaveAsync(auditProfile);
            await ReloadProfilesAsync(auditProfile.Id);
        }

        if (SelectedProfile is null)
        {
            StatusMessage = "Fleet Desk could not create the local audit template required for a durable run.";
            return false;
        }

        if (stagedMoves.Length == 0 && stagedInvites.Length == 0)
        {
            StatusMessage = "Stage at least one live-fleet change first.";
            return false;
        }

        var profile = FleetProfileFactory.FromLiveFleet(snapshot, "Live Desk changes") with
        {
            Id = SelectedProfile.Id,
        };
        var targetSquadIds = new Dictionary<(long WingId, long SquadId), Guid>();
        foreach (var (liveWing, profileWing) in snapshot.Wings
            .Zip(profile.Wings.OrderBy(wing => wing.SortOrder)))
        {
            foreach (var (liveSquad, profileSquad) in liveWing.Squads
                .Zip(profileWing.Squads.OrderBy(squad => squad.SortOrder)))
            {
                targetSquadIds[(liveWing.WingId, liveSquad.SquadId)] = profileSquad.Id;
            }
        }

        var movesByCharacterId = stagedMoves.ToDictionary(move => move.CharacterId);
        var invitationAssignments = stagedInvites.Select(invite => new ProfileAssignment(
            invite.CharacterId,
            invite.CharacterName,
            targetSquadIds[(invite.TargetWingId, invite.TargetSquadId)],
            invite.DesiredRole));
        profile = profile with
        {
            Assignments = profile.Assignments
                .Select(assignment => movesByCharacterId.TryGetValue(
                    assignment.CharacterId,
                    out var move)
                        ? assignment with
                        {
                            TargetSquadId = ResolveLiveDeskTargetSquadId(
                                snapshot,
                                targetSquadIds,
                                move),
                            DesiredRole = move.DesiredRole,
                        }
                        : assignment)
                .Concat(invitationAssignments)
                .ToArray(),
        };
        lastPreparedProfile = profile;
        lastShipRuleMatchCount = 0;
        lastShipRuleCapacitySkipCount = 0;
        var plan = FleetPlanModeFilter.Apply(
            FleetPlanner.Build(profile, snapshot),
            stagedInvites.Length == 0
                ? FleetRunMode.ApplyLiveChanges
                : FleetRunMode.FullOrganise);
        ApplyDryRun(plan, snapshot.ConfirmedAtUtc);
        DryRunTitle = $"{stagedMoves.Length + stagedInvites.Length} staged Live Desk change{(stagedMoves.Length + stagedInvites.Length == 1 ? string.Empty : "s")} • fleet {snapshot.FleetId}";
        DryRunSafetyMessage +=
            " This run contains only the moves, roles, and invitations staged on Live Fleet. Fleet-boss transfer, kicks, and deletion use their separate high-impact unlock.";
        StatusMessage = "Live Desk preview ready. One final confirmation remains before any ESI write.";
        return true;
    }

    private static Guid ResolveLiveDeskTargetSquadId(
        LiveFleetSnapshot snapshot,
        Dictionary<(long WingId, long SquadId), Guid> targetSquadIds,
        StagedLiveMoveViewModel move)
    {
        if (move.TargetSquadId > 0)
        {
            return targetSquadIds[(move.TargetWingId, move.TargetSquadId)];
        }

        var firstSquad = snapshot.Wings
            .Single(wing => wing.WingId == move.TargetWingId)
            .Squads
            .OrderBy(squad => squad.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? throw new InvalidOperationException(
                $"{move.TargetName} has no squad that can anchor a saved commander placement.");
        return targetSquadIds[(move.TargetWingId, firstSquad.SquadId)];
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
        if (Assignments.Any(assignment => squadIds.Contains(assignment.TargetSquadId)) ||
            ShipRules.Any(rule => squadIds.Contains(rule.TargetSquadId) ||
                (rule.OverflowSquadId is Guid overflowId && squadIds.Contains(overflowId))))
        {
            StatusMessage = "Move or remove characters and ship rules assigned to this wing before deleting it.";
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

        if (Assignments.Any(assignment => assignment.TargetSquadId == squad.Id) ||
            ShipRules.Any(rule => rule.TargetSquadId == squad.Id || rule.OverflowSquadId == squad.Id))
        {
            StatusMessage = "Move or remove characters and ship rules assigned to this squad before deleting it.";
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
    private void AddShipRule()
    {
        if (!CanEdit || SquadOptions.Count == 0)
        {
            StatusMessage = "Add a squad before creating a ship placement rule.";
            return;
        }

        var rule = new ProfileShipRuleEditorViewModel(
            Guid.NewGuid(),
            string.Empty,
            SquadOptions[0].Id,
            label: $"Ship group {ShipRules.Count + 1}");
        HookShipRule(rule);
        ShipRules.Add(rule);
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
        StatusMessage = "Add one or more exact ship types, then choose primary and optional overflow squads.";
    }

    [RelayCommand]
    private void MoveShipRuleUp(ProfileShipRuleEditorViewModel? rule) => MoveShipRule(rule, -1);

    [RelayCommand]
    private void MoveShipRuleDown(ProfileShipRuleEditorViewModel? rule) => MoveShipRule(rule, 1);

    [RelayCommand]
    private void ClearShipRuleOverflow(ProfileShipRuleEditorViewModel? rule)
    {
        if (CanEdit && rule is not null)
        {
            rule.OverflowSquadId = null;
        }
    }

    [RelayCommand]
    private void DeleteShipRule(ProfileShipRuleEditorViewModel? rule)
    {
        if (!CanEdit || rule is null)
        {
            return;
        }

        UnhookShipRule(rule);
        ShipRules.Remove(rule);
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
        StatusMessage = "Ship placement rule removed from this local profile.";
    }

    public void MoveAssignmentsToSquad(Guid squadId, long draggedCharacterId)
    {
        if (!CanEdit || !SquadOptions.Any(option => option.Id == squadId))
        {
            return;
        }

        var dragged = Assignments.FirstOrDefault(
            assignment => assignment.CharacterId == draggedCharacterId);
        if (dragged is null)
        {
            return;
        }

        if (!dragged.IsSelected)
        {
            foreach (var assignment in Assignments)
            {
                assignment.IsSelected = false;
            }

            dragged.IsSelected = true;
        }

        var selected = Assignments
            .Where(assignment => assignment.IsSelected)
            .ToArray();
        foreach (var assignment in selected)
        {
            assignment.TargetSquadId = squadId;
        }

        var squadName = SquadOptions.First(option => option.Id == squadId).DisplayName;
        StatusMessage = $"Moved {selected.Length} character{(selected.Length == 1 ? string.Empty : "s")} to {squadName}. Save when the layout looks right.";
        RefreshSquadCards();
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
        if (!isLoadingPreferences)
        {
            _ = SavePreferencesSafelyAsync();
        }
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

    partial void OnSelectedRunModeChanged(FleetRunMode value)
    {
        _ = value;
        InvalidateDryRun();
        if (!isLoadingPreferences)
        {
            _ = SavePreferencesSafelyAsync();
        }
    }

    partial void OnAttentionSoundsEnabledChanged(bool value)
    {
        _ = value;
        if (!isLoadingPreferences)
        {
            _ = SavePreferencesSafelyAsync();
        }
    }

    partial void OnFleetPollingSecondsChanged(int value) => SaveOperationalPreference(value is >= 15 and <= 300);

    partial void OnInvitationCheckSecondsChanged(int value) => SaveOperationalPreference(value is >= 15 and <= 300);

    partial void OnInvitationTimeoutMinutesChanged(int value) => SaveOperationalPreference(value is >= 1 and <= 120);

    partial void OnStartMinimizedChanged(bool value)
    {
        _ = value;
        SaveOperationalPreference(isValid: true);
    }

    partial void OnMinimizeToTrayChanged(bool value)
    {
        _ = value;
        SaveOperationalPreference(isValid: true);
    }

    partial void OnSelectedThemeChanged(FleetDeskTheme value) => SaveOperationalPreference(Enum.IsDefined(value));

    private void SaveOperationalPreference(bool isValid)
    {
        if (!isLoadingPreferences && isValid)
        {
            _ = SavePreferencesSafelyAsync();
        }
    }

    private async Task ReloadProfilesAsync(Guid? selectedProfileId)
    {
        var profiles = await repository.LoadAllAsync();
        ProfileItems.Clear();
        foreach (var profile in profiles
            .OrderByDescending(profile => profile.Id == preferences.DefaultProfileId)
            .ThenByDescending(profile => preferences.PinnedProfileIds.Contains(profile.Id))
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase))
        {
            ProfileItems.Add(new ProfileListItemViewModel(profile)
            {
                IsDefault = profile.Id == preferences.DefaultProfileId,
                IsPinned = preferences.PinnedProfileIds.Contains(profile.Id) ||
                    profile.Id == preferences.DefaultProfileId,
            });
        }

        FilteredProfileItems.Refresh();
        OnPropertyChanged(nameof(ProfileSearchSummary));
        OnPropertyChanged(nameof(PinnedProfiles));
        OnPropertyChanged(nameof(DefaultProfileName));

        SelectedProfile = selectedProfileId is Guid id
            ? ProfileItems.FirstOrDefault(item => item.Id == id) ?? ProfileItems.FirstOrDefault()
            : ProfileItems.FirstOrDefault();
        if (SelectedProfile is null)
        {
            ClearEditor();
        }
    }

    private async Task SavePreferencesAsync()
    {
        await preferencesGate.WaitAsync();
        try
        {
            preferences = new FleetDeskPreferences
            {
                LastUsedProfileId = SelectedProfile?.Id ?? preferences.LastUsedProfileId,
                DefaultProfileId = ProfileItems.FirstOrDefault(item => item.IsDefault)?.Id,
                PinnedProfileIds = ProfileItems
                    .Where(item => item.IsPinned)
                    .Select(item => item.Id)
                    .Distinct()
                    .ToArray(),
                RunMode = SelectedRunMode,
                AttentionSoundsEnabled = AttentionSoundsEnabled,
                FleetPollingSeconds = Math.Clamp(FleetPollingSeconds, 15, 300),
                InvitationCheckSeconds = Math.Clamp(InvitationCheckSeconds, 15, 300),
                InvitationTimeoutMinutes = Math.Clamp(InvitationTimeoutMinutes, 1, 120),
                StartMinimized = StartMinimized,
                MinimizeToTray = MinimizeToTray,
                Theme = SelectedTheme,
            };
            await preferencesRepository.SaveAsync(preferences);
        }
        finally
        {
            preferencesGate.Release();
        }
    }

    private async Task SavePreferencesSafelyAsync()
    {
        try
        {
            await SavePreferencesAsync();
        }
        catch (Exception exception)
        {
            StatusMessage = $"Local Fleet Desk preferences could not be saved: {exception.Message}";
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

        foreach (var rule in profile.ShipRules.OrderBy(rule => rule.SortOrder))
        {
            var editor = new ProfileShipRuleEditorViewModel(
                rule.Id,
                rule.ShipTypeName,
                rule.TargetSquadId,
                rule.Label,
                rule.OverflowSquadId,
                rule.MaximumPerSquad,
                rule.BalanceAcrossTargets,
                rule.IsFallback);
            HookShipRule(editor);
            ShipRules.Add(editor);
        }

        UnresolvedRosterEntries.Clear();
        RosterPasteText = string.Empty;
        IsEditorActive = true;
        UpdateSquadOptions();
        isLoadingEditor = false;
        HasUnsavedChanges = false;
        FilteredAssignments.Refresh();
        RefreshAssignmentSummary();
        RefreshSquadCards();
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
        ObservedShipTypes.Clear();
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

        foreach (var rule in ShipRules)
        {
            UnhookShipRule(rule);
        }

        Wings.Clear();
        Assignments.Clear();
        ShipRules.Clear();
    }

    private FleetProfile BuildCurrentProfile() =>
        new FleetProfile(
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
            }).ToArray())
        {
            ShipRules = ShipRules.Select((rule, ruleIndex) => new ProfileShipRule(
                rule.Id,
                rule.ShipTypeName.Trim(),
                rule.TargetSquadId,
                ruleIndex)
            {
                Label = rule.Label.Trim(),
                OverflowSquadId = rule.OverflowSquadId,
                MaximumPerSquad = rule.MaximumPerSquad,
                BalanceAcrossTargets = rule.BalanceAcrossTargets,
                IsFallback = rule.IsFallback,
            }).ToArray(),
        };

    private void RefreshValidation()
    {
        if (isLoadingEditor || !IsEditorActive)
        {
            return;
        }

        var errors = ProfileValidator.Validate(BuildCurrentProfile());
        ValidationSummary = errors.Count == 0
            ? $"Ready • {Assignments.Count} exact characters • {ShipRules.Count} ship rules • {SquadOptions.Count} squads"
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
        RefreshSquadCards();
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

    private void HookShipRule(ProfileShipRuleEditorViewModel rule)
    {
        rule.PropertyChanged += OnShipRulePropertyChanged;
    }

    private void UnhookShipRule(ProfileShipRuleEditorViewModel rule)
    {
        rule.PropertyChanged -= OnShipRulePropertyChanged;
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
            if (string.Equals(e.PropertyName, nameof(ProfileAssignmentEditorViewModel.TargetSquadId), StringComparison.Ordinal))
            {
                RefreshSquadCards();
            }

            InvalidateDryRun();
        }
    }

    private void OnShipRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        _ = e;
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
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

    private void RefreshAssignmentSummary()
    {
        OnPropertyChanged(nameof(AssignmentSearchSummary));
        RefreshSquadCards();
    }

    private void RefreshSquadCards()
    {
        foreach (var squad in Wings.SelectMany(wing => wing.Squads))
        {
            var assigned = Assignments
                .Where(assignment => assignment.TargetSquadId == squad.Id)
                .OrderBy(assignment => assignment.CharacterName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            squad.AssignmentCount = assigned.Length;
            squad.CharacterPreview = assigned.Length == 0
                ? "Drop characters here"
                : string.Join(", ", assigned.Take(4).Select(assignment => assignment.CharacterName)) +
                    (assigned.Length > 4 ? $" +{assigned.Length - 4}" : string.Empty);
        }
    }

    private void UpdateObservedShipTypes(LiveFleetSnapshot snapshot)
    {
        var selectedShipNames = ShipRules
            .SelectMany(rule => ShipRuleResolver.ParseShipTypes(rule.ShipTypeName))
            .Where(name => name.Length > 0);
        var names = snapshot.Members
            .Select(member => member.ShipTypeName.Trim())
            .Concat(selectedShipNames)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ObservedShipTypes.Clear();
        foreach (var name in names)
        {
            ObservedShipTypes.Add(name);
        }
    }

    private void MoveWing(ProfileWingEditorViewModel? wing, int offset)
    {
        if (!CanEdit || wing is null)
        {
            return;
        }

        MoveItem(Wings, wing, offset);
        UpdateSquadOptions();
        MarkEditorDirty();
        RefreshValidation();
        InvalidateDryRun();
    }

    private void MoveShipRule(ProfileShipRuleEditorViewModel? rule, int offset)
    {
        if (!CanEdit || rule is null)
        {
            return;
        }

        MoveItem(ShipRules, rule, offset);
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
        lastPreparedProfile = null;
        lastShipRuleMatchCount = 0;
        lastShipRuleCapacitySkipCount = 0;
        DryRunItems.Clear();
        HasDryRun = false;
        DryRunTitle = "No comparison generated.";
        DryRunSummary = string.Empty;
        DryRunSafetyMessage = string.Empty;
        DryRunPrimaryMessage = "Choose a profile and check the live fleet.";
        DryRunBlockingDetails = string.Empty;
        ShipRuleMatchSummary = string.Empty;
        OnPropertyChanged(nameof(CanStartReviewedOperation));
        OnPropertyChanged(nameof(RunPrimaryActionText));
    }

    private void ApplyDryRun(FleetDryRunPlan plan, DateTimeOffset confirmedAtUtc)
    {
        lastDryRunPlan = plan;
        DryRunTitle =
            $"'{plan.ProfileName}' compared with fleet {plan.FleetId} • live state confirmed {confirmedAtUtc.ToLocalTime():t}";
        DryRunSummary = BuildDryRunSummary(plan);
        DryRunSafetyMessage = BuildDryRunSafetyMessage(plan);
        ShipRuleMatchSummary = ShipRules.Count == 0
            ? "No automatic ship placement rules are enabled."
            : lastShipRuleMatchCount == 0 && lastShipRuleCapacitySkipCount == 0
                ? "No current live characters matched the saved ship rules."
                : $"{lastShipRuleMatchCount} live character{(lastShipRuleMatchCount == 1 ? string.Empty : "s")} matched by ship policy" +
                    (lastShipRuleCapacitySkipCount == 0
                        ? "."
                        : $" • {lastShipRuleCapacitySkipCount} left untouched because every configured target was full.");
        DryRunPrimaryMessage = plan.BlockingIssues > 0
            ? "This run needs attention before it can start"
            : plan.TotalChanges == 0
                ? "Fleet is already organised"
                : "Ready for your confirmation";
        DryRunBlockingDetails = string.Join(
            Environment.NewLine,
            plan.Items
                .Where(item => item.Kind == FleetPlanItemKind.Blocked)
                .Take(3)
                .Select(item => $"{item.Title}: {item.Detail}"));
        HasDryRun = true;
        RefreshVisibleDryRunItems();
        OnPropertyChanged(nameof(CanStartReviewedOperation));
        OnPropertyChanged(nameof(RunPrimaryActionText));
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
            await ReloadOperationHistoryAsync();
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

    private async Task ReloadOperationHistoryAsync()
    {
        var operations = await operationService.LoadRecentAsync();
        OperationHistory.Clear();
        foreach (var operation in operations)
        {
            OperationHistory.Add(new FleetOperationHistoryItemViewModel(operation));
        }
    }

    private void ShowOperation(FleetOperation operation)
    {
        var previousState = currentOperation?.State;
        if (currentOperation?.Id != operation.Id)
        {
            inviteTimeoutRaisedForOperation = null;
        }

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

        WaitingCharacters.Clear();
        if (operation.State == OperationState.AwaitAcceptance)
        {
            var waitingIds = operation.Steps
                .Where(step => step.Type == FleetOperationStepType.Place &&
                    step.State == FleetOperationStepState.Waiting)
                .Select(step => step.Target.CharacterId)
                .ToHashSet();
            foreach (var invite in operation.Steps
                .Where(step => step.Type == FleetOperationStepType.Invite &&
                    waitingIds.Contains(step.Target.CharacterId))
                .OrderBy(step => step.Target.CharacterName, StringComparer.OrdinalIgnoreCase))
            {
                WaitingCharacters.Add(new WaitingCharacterViewModel(
                    invite.Target.CharacterId,
                    invite.Target.CharacterName,
                    $"{invite.Target.WingName} / {invite.Target.SquadName}",
                    "Invite sent — waiting in EVE"));
            }
        }

        IsWaitingForInvites = WaitingCharacters.Count > 0;
        WaitingRoomSummary = IsWaitingForInvites
            ? $"Waiting for {WaitingCharacters.Count} character{(WaitingCharacters.Count == 1 ? string.Empty : "s")} to accept in EVE."
            : "No invitations are waiting.";
        HasRestoreSnapshot = operation.IsTerminal;

        if (previousState == OperationState.AwaitAcceptance &&
            operation.State != OperationState.AwaitAcceptance)
        {
            AttentionRequested?.Invoke(
                this,
                new FleetAttentionEventArgs("All accepted characters are ready for the next fleet step.", false));
        }

        if (operation.State == OperationState.NeedsAttention &&
            previousState != OperationState.NeedsAttention)
        {
            AttentionRequested?.Invoke(
                this,
                new FleetAttentionEventArgs("Fleet run needs attention before it can continue.", true));
        }
        else if (operation.State == OperationState.Complete &&
            previousState != OperationState.Complete)
        {
            AttentionRequested?.Invoke(
                this,
                new FleetAttentionEventArgs("Fleet organisation is complete and verified.", false));
        }
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
            "Accept the invitations in EVE. Fleet Desk checks every 30 seconds while open; use Check now if you do not want to wait.",
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
        $"{HumanizeRunMode(plan.Mode)} • {plan.StructureChanges} structure • {plan.CharacterInvites} invites • " +
        $"{plan.CharacterMoves} moves • {plan.RoleChanges} role changes • " +
        $"{plan.AlreadyCorrect} already correct • {plan.IgnoredLiveMembers} live members left untouched";

    private static string BuildDryRunSafetyMessage(FleetDryRunPlan plan)
    {
        if (plan.BlockingIssues > 0)
        {
            return $"Blocked: resolve {plan.BlockingIssues} safety issue{(plan.BlockingIssues == 1 ? string.Empty : "s")} before an operation could start.";
        }

        var baseMessage = plan.TotalChanges == 0
            ? "The saved assignments already match the live fleet. No changes are needed."
            : "Only the actions shown for this run mode are eligible after one final confirmation. No hierarchy is deleted, nobody is kicked, and fleet boss is never transferred.";
        return plan.IgnoredLiveMembers == 0
            ? baseMessage
            : $"{baseMessage} {plan.IgnoredLiveMembers} unmanaged live member{(plan.IgnoredLiveMembers == 1 ? " is" : "s are")} intentionally left untouched.";
    }

    private static string HumanizeRunMode(FleetRunMode mode) => mode switch
    {
        FleetRunMode.FullOrganise => "Full organise",
        FleetRunMode.InviteMissing => "Invite missing",
        FleetRunMode.PlacePresent => "Place joined",
        FleetRunMode.FixStructure => "Fix structure",
        FleetRunMode.AssignCommanders => "Assign commanders",
        FleetRunMode.ApplyLiveChanges => "Live changes",
        _ => mode.ToString(),
    };

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
