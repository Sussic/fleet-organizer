using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetOrganizer.App.Services;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;
using FleetOrganizer.Core.Planning;
using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.App.ViewModels;

public partial class ProfilesViewModel : ObservableObject, IDisposable
{
    private static readonly char[] TagSeparators = [',', ';'];
    private readonly IFleetProfileRepository repository;
    private readonly ICharacterNameResolver characterNameResolver;
    private readonly ILiveFleetService liveFleetService;
    private readonly IFleetOperationService operationService;
    private readonly IFleetDeskPreferencesRepository preferencesRepository;
    private readonly ILiveFleetRunCoordinator liveFleetRunCoordinator;
    private readonly IUserInteractionService userInteraction;
    private readonly IFileDialogService fileDialogs;
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
        IFleetDeskPreferencesRepository preferencesRepository,
        ILiveFleetRunCoordinator liveFleetRunCoordinator,
        IUserInteractionService userInteraction,
        IFileDialogService fileDialogs)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(characterNameResolver);
        ArgumentNullException.ThrowIfNull(liveFleetService);
        ArgumentNullException.ThrowIfNull(operationService);
        ArgumentNullException.ThrowIfNull(preferencesRepository);
        ArgumentNullException.ThrowIfNull(liveFleetRunCoordinator);
        ArgumentNullException.ThrowIfNull(userInteraction);
        ArgumentNullException.ThrowIfNull(fileDialogs);

        this.repository = repository;
        this.characterNameResolver = characterNameResolver;
        this.liveFleetService = liveFleetService;
        this.operationService = operationService;
        this.preferencesRepository = preferencesRepository;
        this.liveFleetRunCoordinator = liveFleetRunCoordinator;
        this.userInteraction = userInteraction;
        this.fileDialogs = fileDialogs;

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
        !IsBusy && !HasActiveOperation &&
        lastDryRunPlan is { CanExecute: true, TotalChanges: > 0 };

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

}
