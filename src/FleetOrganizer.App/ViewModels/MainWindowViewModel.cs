using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetOrganizer.App.Services;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Authentication;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Profiles;
using FleetOrganizer.Infrastructure.Authentication;
using FleetOrganizer.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FleetOrganizer.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject, IDisposable
{
    private readonly IAppDataPaths paths;
    private readonly EveDeveloperOptions developerOptions;
    private readonly IEveAuthenticationService authenticationService;
    private readonly ILiveFleetService liveFleetService;
    private readonly ICharacterNameResolver characterNameResolver;
    private readonly IFleetInvitationService fleetInvitationService;
    private readonly IFleetAdministrationService fleetAdministrationService;
    private readonly IFleetRebuildService fleetRebuildService;
    private readonly IDiagnosticExportService diagnosticExportService;
    private readonly IWorkflowDiagnosticLog workflowDiagnosticLog;
    private readonly ILocalDataService localDataService;
    private readonly IUpdateCheckService updateCheckService;
    private readonly IUserInteractionService userInteraction;
    private readonly IFileDialogService fileDialogs;
    private readonly string applicationVersion;
    private readonly string requiredScopes;
    private readonly DispatcherTimer fleetRefreshTimer;

    private bool isFleetPollingEnabled = true;
    private int secondsUntilAutomaticCheck = 30;
    private int secondsUntilFleetRefresh = 30;
    private LiveFleetSnapshot? currentSnapshot;
    private bool isApplyingFleetSettings;
    private readonly LiveFleetPendingChanges pendingLiveChanges = new();
    private readonly LiveFleetSelectionModel liveSelection = new();

    [ObservableProperty]
    public partial string LiveFleetSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial LiveFleetSquadTargetViewModel? SelectedBulkLiveTarget { get; set; }

    [ObservableProperty]
    public partial string LiveInviteText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial LiveFleetSquadTargetViewModel? SelectedInviteTarget { get; set; }

    [ObservableProperty]
    public partial string NewLiveWingName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewLiveSquadName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial LiveFleetWingTargetViewModel? SelectedStructureWing { get; set; }

    [ObservableProperty]
    public partial LiveFleetStructureTargetViewModel? SelectedStructureRenameTarget { get; set; }

    [ObservableProperty]
    public partial string LiveStructureRenameText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int SelectedLiveActionTabIndex { get; set; }

    [ObservableProperty]
    public partial bool UnlockHighImpactActions { get; set; }

    [ObservableProperty]
    public partial string LiveCommandStatus { get; set; } =
        "Paste names and Invite now, or drag pilots to queue fleet changes.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLiveApplyFeedback))]
    public partial string LiveApplyFeedback { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool FleetSettingsFreeMove { get; set; }

    [ObservableProperty]
    public partial string FleetSettingsMotd { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasFleetSettingsChanges { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignedInCharacter))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshFleetCommand))]
    public partial bool IsAuthenticated { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshFleetCommand))]
    public partial bool IsAuthenticationBusy { get; set; }

    [ObservableProperty]
    public partial string AuthenticationMessage { get; set; } =
        "No EVE character is signed in.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SignedInCharacter))]
    public partial string? AuthenticatedCharacterName { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageSubtitle))]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    public partial string SelectedPage { get; set; } = "Live Fleet";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RefreshFleetCommand))]
    [NotifyPropertyChangedFor(nameof(CanApplyPendingLiveChanges))]
    [NotifyPropertyChangedFor(nameof(ApplyPendingLiveChangesText))]
    public partial bool IsFleetBusy { get; set; }

    [ObservableProperty]
    public partial string LiveFleetStatusTitle { get; set; } =
        "Open this page to detect your current fleet";

    [ObservableProperty]
    public partial string LiveFleetStatusDetail { get; set; } =
        "Fleet reads start only when you open Live Fleet or press Refresh.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetectedFleet))]
    public partial long? DetectedFleetId { get; set; }

    [ObservableProperty]
    public partial string LiveFleetBoss { get; set; } = "Not detected";

    [ObservableProperty]
    public partial string LiveFleetSummary { get; set; } = "No fleet data loaded";

    [ObservableProperty]
    public partial string LiveFleetFreshness { get; set; } = "Not refreshed yet";

    [ObservableProperty]
    public partial bool IsLiveFleetReady { get; set; }

    [ObservableProperty]
    public partial string AutomaticCheckCountdownText { get; set; } =
        "Automatic acceptance check in 30 seconds";

    [ObservableProperty]
    public partial string LiveFleetRefreshCountdownText { get; set; } =
        "Automatic fleet check starts when this page is open";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ResetLocalDataCommand))]
    public partial string ResetConfirmationText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string MaintenanceStatus { get; set; } =
        "Support exports are redacted and never include the local database or EVE tokens.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CheckForUpdatesCommand))]
    public partial bool IsUpdateCheckBusy { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenAvailableUpdateCommand))]
    public partial string? AvailableUpdateUrl { get; set; }

    [ObservableProperty]
    public partial bool HasAvailableUpdate { get; set; }

    [ObservableProperty]
    public partial string UpdateStatus { get; set; } =
        "Updates are checked only when you ask; Fleet Desk never installs one automatically.";

    [ObservableProperty]
    public partial bool HasAttentionBanner { get; set; }

    [ObservableProperty]
    public partial string AttentionBannerText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool AttentionIsUrgent { get; set; }

    public ObservableCollection<LiveFleetTreeNodeViewModel> FleetHierarchy { get; } = [];

    public ObservableCollection<LiveFleetBoardWingViewModel> FleetBoardWings { get; } = [];

    public ObservableCollection<LiveFleetBoardRowViewModel> FleetBoardRows { get; } = [];

    public ObservableCollection<StagedLiveMoveViewModel> StagedLiveMoves => pendingLiveChanges.Moves;

    public ObservableCollection<StagedLiveInviteViewModel> StagedLiveInvites => pendingLiveChanges.Invites;

    public ObservableCollection<StagedLiveStructureChangeViewModel> StagedLiveStructureChanges =>
        pendingLiveChanges.StructureChanges;

    public ObservableCollection<LiveFleetWingTargetViewModel> LiveStructureWings { get; } = [];

    public ObservableCollection<LiveFleetStructureTargetViewModel> LiveStructureTargets { get; } = [];

    public ObservableCollection<LiveFleetSquadTargetViewModel> LiveFleetSquadTargets { get; } = [];

    public ObservableCollection<LiveFleetSquadTargetViewModel> LiveBulkMoveTargets { get; } = [];

    public ObservableCollection<LiveFleetSquadTargetViewModel> LiveInviteTargets { get; } = [];

    public bool HasStagedLiveMoves => StagedLiveMoves.Count > 0;

    public bool HasStagedLiveInvites => StagedLiveInvites.Count > 0;

    public bool HasStagedLiveStructureChanges => StagedLiveStructureChanges.Count > 0;

    public bool HasPendingLiveChanges => pendingLiveChanges.HasQueuedChanges;

    public int PendingLiveChangeCount => pendingLiveChanges.QueuedCount;

    public string StagedLiveMovesSummary => HasStagedLiveMoves
        ? $"{StagedLiveMoves.Count} pending move{(StagedLiveMoves.Count == 1 ? string.Empty : "s")} — no ESI write yet"
        : "Drag a member to another squad to stage a move.";

    public string PendingLiveChangesSummary => HasPendingLiveChanges
        ? $"{PendingLiveChangeCount} queued fleet change{(PendingLiveChangeCount == 1 ? string.Empty : "s")} • nothing sent yet"
        : "No queued moves, role changes, or structure edits.";

    public string ApplyPendingLiveChangesText => IsFleetBusy
        ? "Fleet check in progress…"
        : PendingLiveChangeCount == 1
            ? "Apply 1 fleet change"
            : $"Apply {PendingLiveChangeCount} fleet changes";

    public bool CanApplyPendingLiveChanges => HasPendingLiveChanges && !IsFleetBusy;

    public bool HasLiveApplyFeedback => !string.IsNullOrWhiteSpace(LiveApplyFeedback);

    public string SentLiveInvitesSummary => HasStagedLiveInvites
        ? $"{StagedLiveInvites.Count} invitation{(StagedLiveInvites.Count == 1 ? string.Empty : "s")} sent • waiting for EVE acceptance"
        : "No invitations are currently being tracked.";

    public string LiveInviteTargetHint
    {
        get
        {
            var nameCount = RosterPasteParser.Parse(LiveInviteText).Length;
            return nameCount switch
            {
                0 => "Paste one name for an empty command seat, or several names for a squad-member invite.",
                1 when LiveInviteTargets.Any(target => target.IsCommandSeat) =>
                    "One pilot: empty wing- and squad-command seats are available.",
                1 => "One pilot: no command seat is currently empty; choose a squad-member destination.",
                _ => $"{nameCount} pilots: commander seats are hidden because each seat accepts exactly one pilot.",
            };
        }
    }

    public int LiveFleetSelectedCount => FleetBoardWings
        .SelectMany(wing => wing.Squads)
        .SelectMany(squad => squad.Members)
        .Count(member => member.IsSelected);

    public int LiveFleetVisibleCount => GetBoardMembers().Count(member => member.IsVisible);

    public string LiveFleetSelectionSummary => $"{LiveFleetSelectedCount} selected";

    public string LiveFleetSearchSummary
    {
        get
        {
            var total = GetBoardMembers().Count();
            return string.IsNullOrWhiteSpace(LiveFleetSearchText)
                ? $"Showing all {total} pilot{(total == 1 ? string.Empty : "s")}."
                : $"Showing {LiveFleetVisibleCount} of {total} pilots.";
        }
    }

    public MainWindowViewModel(
        IAppDataPaths paths,
        IOptions<EveDeveloperOptions> developerOptions,
        IEveAuthenticationService authenticationService,
        ILiveFleetService liveFleetService,
        ICharacterNameResolver characterNameResolver,
        IFleetInvitationService fleetInvitationService,
        IFleetAdministrationService fleetAdministrationService,
        IFleetRebuildService fleetRebuildService,
        IDiagnosticExportService diagnosticExportService,
        IWorkflowDiagnosticLog workflowDiagnosticLog,
        ILocalDataService localDataService,
        IUpdateCheckService updateCheckService,
        IUserInteractionService userInteraction,
        IFileDialogService fileDialogs,
        ProfilesViewModel profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        this.paths = paths;
        this.developerOptions = developerOptions.Value;
        this.authenticationService = authenticationService;
        this.liveFleetService = liveFleetService;
        this.characterNameResolver = characterNameResolver;
        this.fleetInvitationService = fleetInvitationService;
        this.fleetAdministrationService = fleetAdministrationService;
        this.fleetRebuildService = fleetRebuildService;
        this.diagnosticExportService = diagnosticExportService;
        this.workflowDiagnosticLog = workflowDiagnosticLog;
        this.localDataService = localDataService;
        this.updateCheckService = updateCheckService;
        this.userInteraction = userInteraction;
        this.fileDialogs = fileDialogs;
        applicationVersion =
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "development";
        Profiles = profiles;
        requiredScopes = string.Join(Environment.NewLine, EveDeveloperOptions.RequiredScopes);
        fleetRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        fleetRefreshTimer.Tick += OnFleetRefreshTimerTick;
        Profiles.PropertyChanged += OnProfilesPropertyChanged;
        Profiles.AttentionRequested += OnAttentionRequested;
    }

    public string PageSubtitle => SelectedPage switch
    {
        "Profiles" => "Create reusable layouts for the Run setup tab in Live Fleet.",
        "Live Fleet" => "Run the fleet from one place: invite, drag, apply a saved setup, confirm.",
        "Activity" => "Review the current run, recover individual steps, or reopen a previous fleet run.",
        "Settings" => "Configure EVE SSO, storage, polling, and appearance.",
        _ => string.Empty,
    };

    public string PageTitle => SelectedPage switch
    {
        "Profiles" => "Saved setups",
        "Activity" => "Fleet activity",
        _ => SelectedPage,
    };

    public ProfilesViewModel Profiles { get; }

    public string ConfigurationStatus => developerOptions.IsClientIdConfigured
        ? "EVE client ID configured"
        : "EVE client ID needs configuration";

    public string SignedInCharacter => AuthenticatedCharacterName ?? "Not signed in";

    public bool HasDetectedFleet => DetectedFleetId.HasValue;

    public string RedirectUri => developerOptions.RedirectUri;

    public string RequiredScopes => requiredScopes;

    public string DatabasePath => paths.DatabasePath;

    public string ApplicationVersion => applicationVersion;

    public bool StartMinimized => Profiles.StartMinimized;

    public bool MinimizeToTray => Profiles.MinimizeToTray;

    private bool CanSignIn() =>
        developerOptions.IsClientIdConfigured &&
        !IsAuthenticated &&
        !IsAuthenticationBusy;

    private bool CanSignOut() => IsAuthenticated && !IsAuthenticationBusy;

    private bool CanRefreshFleet() =>
        IsAuthenticated &&
        !IsAuthenticationBusy &&
        !IsFleetBusy;

    public async Task InitializeAsync()
    {
        await Profiles.InitializeAsync();
        secondsUntilAutomaticCheck = Profiles.InvitationCheckSeconds;
        secondsUntilFleetRefresh = Profiles.FleetPollingSeconds;
        AutomaticCheckCountdownText = Profiles.IsWaitingForInvites
            ? $"Automatic acceptance check in {secondsUntilAutomaticCheck} seconds"
            : "Automatic acceptance check starts when invitations are sent";

        if (!developerOptions.IsClientIdConfigured)
        {
            return;
        }

        IsAuthenticationBusy = true;
        AuthenticationMessage = "Checking for a saved EVE authorization…";

        try
        {
            var character = await authenticationService.RestoreSessionAsync();
            ApplyAuthenticationState(character);
            AuthenticationMessage = character is null
                ? "No saved EVE authorization was found."
                : $"Secure authorization restored for {character.CharacterName}.";
        }
        catch (Exception exception)
        {
            ApplyAuthenticationState(null);
            AuthenticationMessage = exception.Message;
        }
        finally
        {
            IsAuthenticationBusy = false;
            fleetRefreshTimer.Start();
        }

        if (IsAuthenticated)
        {
            await RefreshFleetAsync();
        }
    }

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignInAsync()
    {
        IsAuthenticationBusy = true;
        AuthenticationMessage = "Waiting for EVE authorization in your browser…";

        try
        {
            var character = await authenticationService.SignInAsync();
            ApplyAuthenticationState(character);
            AuthenticationMessage =
                $"Signed in securely as {character.CharacterName}. The developer secret was not used.";
        }
        catch (Exception exception)
        {
            ApplyAuthenticationState(null);
            AuthenticationMessage = exception.Message;
        }
        finally
        {
            IsAuthenticationBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSignOut))]
    private async Task SignOutAsync()
    {
        IsAuthenticationBusy = true;

        try
        {
            await authenticationService.SignOutAsync();
            ApplyAuthenticationState(null);
            ClearLiveFleet();
            AuthenticationMessage = "Signed out. The saved refresh token was removed from this PC.";
        }
        catch (Exception exception)
        {
            AuthenticationMessage = exception.Message;
        }
        finally
        {
            IsAuthenticationBusy = false;
        }
    }

    [RelayCommand]
    private async Task NavigateAsync(string? page)
    {
        if (!string.IsNullOrWhiteSpace(page))
        {
            SelectedPage = page;
            if (string.Equals(page, "Live Fleet", StringComparison.Ordinal))
            {
                await RefreshFleetAsync();
            }
        }
    }

    [RelayCommand]
    private async Task RunPagePrimaryActionAsync()
    {
        if (string.Equals(SelectedPage, "Live Fleet", StringComparison.Ordinal))
        {
            if (HasPendingLiveChanges)
            {
                await ApplyPendingLiveChangesAsync();
            }
            else if (!string.IsNullOrWhiteSpace(LiveInviteText))
            {
                await InviteNowAsync();
            }
            else
            {
                LiveCommandStatus = "Select pilots to move, paste invitation names, or queue a hierarchy edit first.";
            }

            return;
        }

        if (string.Equals(SelectedPage, "Profiles", StringComparison.Ordinal) &&
            Profiles.CanPrepareFleet)
        {
            await Profiles.PrepareFleetCommand.ExecuteAsync(null);
            return;
        }

        if (string.Equals(SelectedPage, "Activity", StringComparison.Ordinal) && Profiles.CanOperate)
        {
            await Profiles.ContinueOperationCommand.ExecuteAsync(null);
        }
    }

    [RelayCommand]
    private async Task RefreshPageAsync()
    {
        if (string.Equals(SelectedPage, "Live Fleet", StringComparison.Ordinal))
        {
            await RefreshFleetAsync();
        }
    }

    [RelayCommand]
    private void CancelPageAction()
    {
        if (string.Equals(SelectedPage, "Live Fleet", StringComparison.Ordinal))
        {
            if (!string.IsNullOrWhiteSpace(LiveFleetSearchText))
            {
                LiveFleetSearchText = string.Empty;
            }
            else
            {
                ClearLiveMemberSelection();
            }

            return;
        }

        Profiles.HideDryRunCommand.Execute(null);
    }

    [RelayCommand]
    private void UndoPageAction()
    {
        if (!string.Equals(SelectedPage, "Live Fleet", StringComparison.Ordinal))
        {
            Profiles.HideDryRunCommand.Execute(null);
            return;
        }

        var undone = pendingLiveChanges.UndoLastQueuedChange();
        if (undone is not null)
        {
            LiveCommandStatus = $"Undid: {undone}.";
            RefreshPendingLiveChangeState();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefreshFleet))]
    private async Task RefreshFleetAsync()
    {
        secondsUntilFleetRefresh = Profiles.FleetPollingSeconds;
        IsFleetBusy = true;
        LiveFleetStatusTitle = "Reading the current EVE fleet";
        LiveFleetStatusDetail = "Checking fleet identity, settings, members, wings, and squads…";

        try
        {
            var result = await liveFleetService.RefreshCurrentAsync();
            ApplyLiveFleetResult(result);
        }
        catch (Exception exception)
        {
            IsLiveFleetReady = false;
            LiveFleetStatusTitle = "Live fleet refresh failed";
            LiveFleetStatusDetail = exception.Message;
            await RecordWorkflowFailureAsync("live-fleet-refresh", exception);
        }
        finally
        {
            IsFleetBusy = false;
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        Directory.CreateDirectory(paths.RootDirectory);
        Process.Start(new ProcessStartInfo
        {
            FileName = paths.RootDirectory,
            UseShellExecute = true,
        });
    }

    [RelayCommand]
    private async Task ExportDiagnosticsAsync()
    {
        var path = fileDialogs.ChooseSavePath(
            "Export redacted Fleet Desk diagnostics",
            $"FleetDesk-diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip",
            "ZIP archive (*.zip)|*.zip",
            ".zip");
        if (path is null)
        {
            return;
        }

        try
        {
            MaintenanceStatus = "Building redacted support bundle…";
            await diagnosticExportService.ExportAsync(path);
            MaintenanceStatus = $"Exported redacted diagnostics to {path}";
        }
        catch (Exception exception)
        {
            MaintenanceStatus = $"Diagnostics could not be exported: {exception.Message}";
        }
    }

    private bool CanCheckForUpdates() => !IsUpdateCheckBusy;

    [RelayCommand(CanExecute = nameof(CanCheckForUpdates))]
    private async Task CheckForUpdatesAsync()
    {
        IsUpdateCheckBusy = true;
        AvailableUpdateUrl = null;
        HasAvailableUpdate = false;
        UpdateStatus = "Checking the latest stable GitHub release…";
        try
        {
            var result = await updateCheckService.CheckAsync();
            UpdateStatus = result.UserMessage;
            AvailableUpdateUrl = result.IsUpdateAvailable ? result.ReleaseUrl : null;
            HasAvailableUpdate = result.IsUpdateAvailable &&
                Uri.TryCreate(result.ReleaseUrl, UriKind.Absolute, out _);
        }
        catch (Exception exception)
        {
            UpdateStatus = $"Update check failed: {exception.Message}";
        }
        finally
        {
            IsUpdateCheckBusy = false;
        }
    }

    private bool CanOpenAvailableUpdate() =>
        Uri.TryCreate(AvailableUpdateUrl, UriKind.Absolute, out _);

    [RelayCommand(CanExecute = nameof(CanOpenAvailableUpdate))]
    private void OpenAvailableUpdate()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = AvailableUpdateUrl!,
            UseShellExecute = true,
        });
    }

    private bool CanResetLocalData() =>
        string.Equals(ResetConfirmationText.Trim(), "RESET", StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanResetLocalData))]
    private async Task ResetLocalDataAsync()
    {
        if (!userInteraction.Confirm(
            "Reset Fleet Desk local data",
            "Delete every local Fleet Desk template, operation, preference, crash log, and encrypted EVE sign-in?\n\nThis cannot be undone and does not change anything inside EVE.",
            UserConfirmationKind.Warning))
        {
            return;
        }

        try
        {
            await authenticationService.SignOutAsync();
            await localDataService.ResetAsync();
            userInteraction.Inform(
                "Fleet Desk reset complete",
                "Local Fleet Desk data was removed. The app will now close; run it again to create a clean database.");
            Application.Current.Shutdown();
        }
        catch (Exception exception)
        {
            MaintenanceStatus = $"Local data could not be reset: {exception.Message}";
        }
    }

    [RelayCommand]
    private async Task PreviewRestoreAsync()
    {
        if (await Profiles.PrepareRestorePreviewAsync())
        {
            SelectedPage = "Live Fleet";
        }
    }

    [RelayCommand]
    private void DismissAttention() => HasAttentionBanner = false;

    private void ApplyAuthenticationState(AuthenticatedCharacter? character)
    {
        AuthenticatedCharacterName = character?.CharacterName;
        IsAuthenticated = character is not null;

        if (character is not null && !HasDetectedFleet)
        {
            LiveFleetStatusTitle = $"Ready to detect {character.CharacterName}'s fleet";
            LiveFleetStatusDetail = "Create a fleet in EVE and make this character fleet boss, then open Live Fleet.";
        }
    }

    public void SetFleetPollingEnabled(bool enabled)
    {
        isFleetPollingEnabled = enabled;
    }

    public void Dispose()
    {
        fleetRefreshTimer.Stop();
        fleetRefreshTimer.Tick -= OnFleetRefreshTimerTick;
        Profiles.PropertyChanged -= OnProfilesPropertyChanged;
        Profiles.AttentionRequested -= OnAttentionRequested;
        GC.SuppressFinalize(this);
    }

    private async void OnFleetRefreshTimerTick(object? sender, EventArgs e)
    {
        if (!isFleetPollingEnabled)
        {
            return;
        }

        if (Profiles.IsWaitingForInvites)
        {
            secondsUntilAutomaticCheck--;
            AutomaticCheckCountdownText =
                $"Automatic acceptance check in {secondsUntilAutomaticCheck} second{(secondsUntilAutomaticCheck == 1 ? string.Empty : "s")}";
            if (secondsUntilAutomaticCheck <= 0)
            {
                secondsUntilAutomaticCheck = Profiles.InvitationCheckSeconds;
                await Profiles.AutoContinueOperationAsync();
            }
        }
        else
        {
            secondsUntilAutomaticCheck = Profiles.InvitationCheckSeconds;
            AutomaticCheckCountdownText = "Automatic acceptance check starts when invitations are sent";
        }

        secondsUntilFleetRefresh--;
        LiveFleetRefreshCountdownText = string.Equals(SelectedPage, "Live Fleet", StringComparison.Ordinal)
            ? $"Automatically checking again in {Math.Max(0, secondsUntilFleetRefresh)} seconds"
            : "Automatic fleet check starts when this page is open";
        if (secondsUntilFleetRefresh <= 0 &&
            string.Equals(SelectedPage, "Live Fleet", StringComparison.Ordinal) &&
            CanRefreshFleet())
        {
            secondsUntilFleetRefresh = Profiles.FleetPollingSeconds;
            await RefreshFleetAsync();
        }
    }

    private void OnProfilesPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.PropertyName == nameof(ProfilesViewModel.IsWaitingForInvites))
        {
            secondsUntilAutomaticCheck = Profiles.InvitationCheckSeconds;
        }

        if (e.PropertyName == nameof(ProfilesViewModel.FleetPollingSeconds))
        {
            secondsUntilFleetRefresh = Profiles.FleetPollingSeconds;
        }

        if (e.PropertyName == nameof(ProfilesViewModel.InvitationCheckSeconds))
        {
            secondsUntilAutomaticCheck = Profiles.InvitationCheckSeconds;
        }
    }

    private void OnAttentionRequested(object? sender, FleetAttentionEventArgs e)
    {
        _ = sender;
        AttentionBannerText = e.Message;
        AttentionIsUrgent = e.IsUrgent;
        HasAttentionBanner = true;
        if (Profiles.AttentionSoundsEnabled)
        {
            if (e.IsUrgent)
            {
                SystemSounds.Exclamation.Play();
            }
            else
            {
                SystemSounds.Asterisk.Play();
            }
        }
    }

}
