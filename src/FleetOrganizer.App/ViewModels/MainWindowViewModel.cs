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
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Authentication;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Profiles;
using FleetOrganizer.Infrastructure.Authentication;
using FleetOrganizer.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Win32;

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
    private readonly ILocalDataService localDataService;
    private readonly IUpdateCheckService updateCheckService;
    private readonly string applicationVersion;
    private readonly string requiredScopes;
    private readonly DispatcherTimer fleetRefreshTimer;

    private bool isFleetPollingEnabled = true;
    private int secondsUntilAutomaticCheck = 30;
    private int secondsUntilFleetRefresh = 30;
    private LiveFleetSnapshot? currentSnapshot;
    private bool isApplyingFleetSettings;
    private long? liveSelectionAnchorCharacterId;

    [ObservableProperty]
    public partial string LiveFleetSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial LiveFleetSquadTargetViewModel? SelectedBulkLiveTarget { get; set; }

    [ObservableProperty]
    public partial string LiveInviteText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial LiveFleetSquadTargetViewModel? SelectedInviteTarget { get; set; }

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
    [NotifyPropertyChangedFor(nameof(StatusTitle))]
    [NotifyPropertyChangedFor(nameof(StatusDetail))]
    [NotifyPropertyChangedFor(nameof(SignedInCharacter))]
    [NotifyPropertyChangedFor(nameof(FcReadinessSummary))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshFleetCommand))]
    public partial bool IsAuthenticated { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTitle))]
    [NotifyPropertyChangedFor(nameof(StatusDetail))]
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
    [NotifyPropertyChangedFor(nameof(FcReadinessSummary))]
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

    public ObservableCollection<StagedLiveMoveViewModel> StagedLiveMoves { get; } = [];

    public ObservableCollection<StagedLiveInviteViewModel> StagedLiveInvites { get; } = [];

    public ObservableCollection<LiveFleetSquadTargetViewModel> LiveFleetSquadTargets { get; } = [];

    public ObservableCollection<LiveFleetSquadTargetViewModel> LiveBulkMoveTargets { get; } = [];

    public ObservableCollection<LiveFleetSquadTargetViewModel> LiveInviteTargets { get; } = [];

    public bool HasStagedLiveMoves => StagedLiveMoves.Count > 0;

    public bool HasStagedLiveInvites => StagedLiveInvites.Count > 0;

    public bool HasPendingLiveChanges => HasStagedLiveMoves;

    public int PendingLiveChangeCount => StagedLiveMoves.Count;

    public string StagedLiveMovesSummary => HasStagedLiveMoves
        ? $"{StagedLiveMoves.Count} pending move{(StagedLiveMoves.Count == 1 ? string.Empty : "s")} — no ESI write yet"
        : "Drag a member to another squad to stage a move.";

    public string PendingLiveChangesSummary => HasPendingLiveChanges
        ? $"{PendingLiveChangeCount} queued fleet change{(PendingLiveChangeCount == 1 ? string.Empty : "s")} • nothing sent yet"
        : "No queued moves or role changes.";

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
        ILocalDataService localDataService,
        IUpdateCheckService updateCheckService,
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
        this.localDataService = localDataService;
        this.updateCheckService = updateCheckService;
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

    public string FcReadinessSummary =>
        $"{(IsAuthenticated ? "✓ EVE sign-in" : "○ Sign in")}" +
        $"  •  {(HasDetectedFleet ? "✓ Fleet detected" : "○ Open fleet")}" +
        $"  •  {(Profiles.SelectedProfile is null ? "○ Choose template" : $"✓ {Profiles.SelectedProfile.Name}")}" +
        $"  •  {Profiles.RunModeOptions.Single(option => option.Value == Profiles.SelectedRunMode).DisplayName}";

    public string RedirectUri => developerOptions.RedirectUri;

    public string RequiredScopes => requiredScopes;

    public string StatusTitle
    {
        get
        {
            if (!developerOptions.IsClientIdConfigured)
            {
                return "Complete the EVE developer setup";
            }

            if (IsAuthenticationBusy)
            {
                return "Connecting securely to EVE SSO";
            }

            return IsAuthenticated
                ? $"Signed in as {AuthenticatedCharacterName}"
                : "Ready to sign in with EVE";
        }
    }

    public string StatusDetail
    {
        get
        {
            if (!developerOptions.IsClientIdConfigured)
            {
                return "Paste only the public client ID into appsettings.Local.json. " +
                    "The desktop app never uses or stores the developer secret.";
            }

            if (IsAuthenticationBusy)
            {
                return "Complete the authorization in your browser. Fleet Organizer validates the callback, token signature, application audience, and fleet scopes.";
            }

            return IsAuthenticated
                ? "Authorization is encrypted for this Windows user. Live Fleet can stage and run reviewed ESI changes; high-impact actions require an explicit unlock and confirmation."
                : "Open Settings and choose Sign in with EVE. Authorize the character that will be fleet boss.";
        }
    }

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
        var dialog = new SaveFileDialog
        {
            Title = "Export redacted Fleet Desk diagnostics",
            Filter = "ZIP archive (*.zip)|*.zip",
            FileName = $"FleetDesk-diagnostics-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.zip",
            AddExtension = true,
            DefaultExt = ".zip",
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            MaintenanceStatus = "Building redacted support bundle…";
            await diagnosticExportService.ExportAsync(dialog.FileName);
            MaintenanceStatus = $"Exported redacted diagnostics to {dialog.FileName}";
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
        var answer = MessageBox.Show(
            "Delete every local Fleet Desk template, operation, preference, crash log, and encrypted EVE sign-in?\n\nThis cannot be undone and does not change anything inside EVE.",
            "Reset Fleet Desk local data",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await authenticationService.SignOutAsync();
            await localDataService.ResetAsync();
            MessageBox.Show(
                "Local Fleet Desk data was removed. The app will now close; run it again to create a clean database.",
                "Fleet Desk reset complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

    [RelayCommand]
    private void ClearStagedLiveMoves()
    {
        StagedLiveMoves.Clear();
        RefreshPendingLiveChangeState();
    }

    [RelayCommand]
    private void ClearPendingLiveChanges()
    {
        StagedLiveMoves.Clear();
        Profiles.HideDryRunCommand.Execute(null);
        LiveCommandStatus = "Queued fleet changes cleared. Sent invitations are still being tracked.";
        LiveApplyFeedback = string.Empty;
        RefreshPendingLiveChangeState();
    }

    [RelayCommand]
    private void SelectAllVisibleLiveMembers()
    {
        foreach (var member in GetBoardMembers().Where(member => member.IsVisible && member.CanStage))
        {
            member.IsSelected = true;
        }

        RefreshLiveSelectionSummary();
    }

    [RelayCommand]
    private void ClearLiveFleetFilter() => LiveFleetSearchText = string.Empty;

    [RelayCommand]
    private void ClearLiveMemberSelection()
    {
        foreach (var member in GetBoardMembers())
        {
            member.IsSelected = false;
        }

        RefreshLiveSelectionSummary();
    }

    [RelayCommand]
    private void StageSelectedLiveMembers()
    {
        if (SelectedBulkLiveTarget is not { } target)
        {
            LiveFleetStatusDetail = "Choose a target squad for the selected live members.";
            return;
        }

        var selected = GetBoardMembers()
            .Where(member => member.IsSelected && member.CanStage)
            .Select(member => member.CharacterId)
            .ToArray();
        if (selected.Length == 0)
        {
            LiveCommandStatus = "Select at least one movable fleet member first.";
            return;
        }

        StageLiveMembers(selected, target.WingId, target.SquadId, target.DesiredRole);
    }

    [RelayCommand]
    private void CancelStagedLiveMember(LiveFleetBoardMemberViewModel? member)
    {
        if (member?.StagedMove is null)
        {
            return;
        }

        StagedLiveMoves.Remove(member.StagedMove);
        LiveCommandStatus = $"Cancelled the staged change for {member.CharacterName}.";
        LiveApplyFeedback = HasPendingLiveChanges
            ? "Queued changes updated. Apply when ready."
            : string.Empty;
        RefreshPendingLiveChangeState();
    }

    [RelayCommand]
    private void RemoveStagedLiveMove(StagedLiveMoveViewModel? move)
    {
        if (move is null)
        {
            return;
        }

        StagedLiveMoves.Remove(move);
        LiveApplyFeedback = HasPendingLiveChanges
            ? "Queued changes updated. Apply when ready."
            : string.Empty;
        RefreshPendingLiveChangeState();
    }

    [RelayCommand]
    private void RemoveStagedLiveInvite(StagedLiveInviteViewModel? invite)
    {
        if (invite is null)
        {
            return;
        }

        StagedLiveInvites.Remove(invite);
        LiveCommandStatus =
            $"Stopped tracking {invite.CharacterName}. The invitation already sent in EVE cannot be recalled.";
        RefreshPendingLiveChangeState();
    }

    [RelayCommand]
    private Task StageLiveInvitesAsync() => InviteNowAsync();

    [RelayCommand]
    private async Task InviteNowAsync()
    {
        var snapshot = currentSnapshot;
        if (snapshot is null || SelectedInviteTarget is not { } target)
        {
            LiveCommandStatus = "Load a fleet and choose the invitation target squad first.";
            return;
        }

        var entries = RosterPasteParser.Parse(LiveInviteText);
        if (entries.Length == 0)
        {
            LiveCommandStatus = "Paste at least one exact EVE character name.";
            return;
        }

        IsFleetBusy = true;
        LiveCommandStatus = $"Resolving {entries.Length} exact character name{(entries.Length == 1 ? string.Empty : "s")}…";
        try
        {
            var resolution = await characterNameResolver.ResolveAsync(
                entries.Select(entry => entry.CharacterName).ToArray());
            var liveIds = snapshot.Members.Select(member => member.CharacterId).ToHashSet();
            var trackedIds = StagedLiveInvites.Select(invite => invite.CharacterId).ToHashSet();
            var candidates = resolution.Resolved
                .Where(character =>
                    !liveIds.Contains(character.CharacterId) &&
                    !trackedIds.Contains(character.CharacterId))
                .Select(character => new FleetInvitationCandidate(
                    character.CharacterId,
                    character.CharacterName))
                .ToArray();
            if (candidates.Length == 0)
            {
                LiveInviteText = string.Join(Environment.NewLine, resolution.UnresolvedNames);
                LiveCommandStatus = resolution.UnresolvedNames.Length > 0
                    ? "No invitation was sent. Check the exact character name shown in the box."
                    : "Everyone listed is already in the fleet or already has a tracked invitation.";
                return;
            }

            LiveCommandStatus = $"Sending {candidates.Length} invitation{(candidates.Length == 1 ? string.Empty : "s")}…";
            var result = await fleetInvitationService.InviteAsync(
                snapshot.FleetId,
                target.WingId,
                target.SquadId,
                target.DisplayName,
                target.DesiredRole,
                candidates);
            foreach (var character in result.Sent)
            {
                StagedLiveInvites.Add(new StagedLiveInviteViewModel(
                    character.CharacterId,
                    character.CharacterName,
                    target.WingId,
                    target.SquadId,
                    target.DisplayName,
                    target.DesiredRole));
            }

            LiveInviteText = string.Join(
                Environment.NewLine,
                resolution.UnresolvedNames.Concat(result.Unsent.Select(character => character.CharacterName)));
            var remainingCount = resolution.UnresolvedNames.Length + result.Unsent.Count;
            LiveCommandStatus = remainingCount == 0
                ? result.UserMessage
                : $"{result.UserMessage} {remainingCount} name{(remainingCount == 1 ? " remains" : "s remain")} in the box.";
            RefreshPendingLiveChangeState();
        }
        catch (Exception exception)
        {
            LiveCommandStatus = $"Invitations could not be sent: {exception.Message}";
        }
        finally
        {
            IsFleetBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReviewPendingLiveChangesAsync()
    {
        if (currentSnapshot is null || !HasPendingLiveChanges)
        {
            LiveCommandStatus = "Stage at least one move, role change, or invitation first.";
            return;
        }

        if (await Profiles.PrepareLiveDeskChangesAsync(
            currentSnapshot,
            StagedLiveMoves.ToArray(),
            []))
        {
            LiveCommandStatus = Profiles.CanStartReviewedOperation
                ? "Fleet changes are ready for one confirmation."
                : Profiles.DryRunBlockingDetails;
        }
    }

    [RelayCommand]
    private async Task ApplyPendingLiveChangesAsync()
    {
        if (currentSnapshot is null || !HasPendingLiveChanges)
        {
            LiveCommandStatus = "Drag or select at least one fleet member to queue a change first.";
            LiveApplyFeedback = LiveCommandStatus;
            return;
        }

        if (IsFleetBusy)
        {
            LiveApplyFeedback = "Fleet Desk is finishing another fleet check. Apply again when the button becomes available.";
            return;
        }

        IsFleetBusy = true;
        LiveApplyFeedback = "Preparing the exact change list and safety checks…";
        LiveCommandStatus = LiveApplyFeedback;
        try
        {
            if (!await Profiles.PrepareLiveDeskChangesAsync(
                currentSnapshot,
                StagedLiveMoves.ToArray(),
                []))
            {
                LiveApplyFeedback = Profiles.StatusMessage;
                LiveCommandStatus = LiveApplyFeedback;
                return;
            }

            if (!Profiles.CanStartReviewedOperation)
            {
                LiveApplyFeedback = Profiles.HasActiveOperation
                    ? "A previous fleet run is still active. Open Activity to finish, retry, or cancel it before applying another change."
                    : Profiles.IsBusy
                        ? "Fleet Desk is finishing another saved operation. Try Apply again when it completes."
                        : string.IsNullOrWhiteSpace(Profiles.DryRunBlockingDetails)
                            ? Profiles.StatusMessage
                            : Profiles.DryRunBlockingDetails;
                LiveCommandStatus = LiveApplyFeedback;
                return;
            }

            LiveApplyFeedback = "Confirmation opened. Nothing is sent unless you choose Yes.";
            if (await Profiles.StartPreparedOperationAsync())
            {
                StagedLiveMoves.Clear();
                Profiles.HideDryRunCommand.Execute(null);
                await RefreshFleetAsync();
            }

            LiveApplyFeedback = Profiles.StatusMessage;
            LiveCommandStatus = Profiles.StatusMessage;
        }
        catch (Exception exception)
        {
            LiveApplyFeedback = $"Fleet changes could not be prepared: {exception.Message}";
            LiveCommandStatus = LiveApplyFeedback;
        }
        finally
        {
            IsFleetBusy = false;
            RefreshPendingLiveChangeState();
        }
    }

    [RelayCommand]
    private Task ReviewStagedLiveMovesAsync() => ReviewPendingLiveChangesAsync();

    [RelayCommand]
    private async Task PreviewSelectedTemplateAsync()
    {
        await Profiles.PrepareFleetCommand.ExecuteAsync(null);
        LiveCommandStatus = Profiles.StatusMessage;
    }

    public void StageLiveMemberMove(
        long characterId,
        long targetWingId,
        long targetSquadId,
        DesiredFleetRole desiredRole)
    {
        var snapshot = currentSnapshot;
        if (snapshot is null)
        {
            return;
        }

        var member = snapshot.Members.FirstOrDefault(candidate => candidate.CharacterId == characterId);
        var wing = snapshot.Wings.FirstOrDefault(candidate => candidate.WingId == targetWingId);
        var squad = targetSquadId > 0
            ? wing?.Squads.FirstOrDefault(candidate => candidate.SquadId == targetSquadId)
            : null;
        var targetIsValid = desiredRole == DesiredFleetRole.WingCommander
            ? wing is not null
            : wing is not null && squad is not null;
        if (member is null || !targetIsValid)
        {
            LiveFleetStatusDetail = "That character or target position is no longer in the current fleet. Refresh and try again.";
            return;
        }

        if (desiredRole == DesiredFleetRole.FleetCommander)
        {
            LiveCommandStatus = "Fleet-boss transfer is a separately locked high-impact action.";
            return;
        }

        if (desiredRole is DesiredFleetRole.WingCommander or DesiredFleetRole.SquadCommander &&
            IsCommandSeatReservedForAnother(
                characterId,
                targetWingId,
                targetSquadId,
                desiredRole))
        {
            LiveCommandStatus = desiredRole == DesiredFleetRole.WingCommander
                ? $"{wing!.Name} already has a wing commander. Stage that commander out first."
                : $"{wing!.Name} / {squad!.Name} already has a squad commander. Stage that commander out first.";
            LiveApplyFeedback = LiveCommandStatus;
            return;
        }

        var existingMove = StagedLiveMoves.FirstOrDefault(move => move.CharacterId == characterId);
        if (existingMove is not null)
        {
            StagedLiveMoves.Remove(existingMove);
        }
        if (member.WingId == targetWingId &&
            (desiredRole == DesiredFleetRole.WingCommander || member.SquadId == targetSquadId) &&
            DesiredRoleForEsiRole(member.Role) == desiredRole)
        {
            RefreshPendingLiveChangeState();
            LiveCommandStatus = $"{member.CharacterName} is already correct.";
            return;
        }

        var targetCount = snapshot.Members.Count(candidate =>
        {
            var staged = StagedLiveMoves.FirstOrDefault(move => move.CharacterId == candidate.CharacterId);
            return staged is not null
                ? staged.TargetWingId == targetWingId && staged.TargetSquadId == targetSquadId
                : candidate.WingId == targetWingId && candidate.SquadId == targetSquadId;
        });
        var isAlreadyInTarget = member.WingId == targetWingId && member.SquadId == targetSquadId;
        if (desiredRole != DesiredFleetRole.WingCommander &&
            !isAlreadyInTarget &&
            targetCount >= FleetOrganizer.Core.Profiles.ProfileValidator.MaximumCharactersPerSquad)
        {
            RefreshPendingLiveChangeState();
            LiveCommandStatus =
                $"{wing!.Name} / {squad!.Name} is already at EVE's {FleetOrganizer.Core.Profiles.ProfileValidator.MaximumCharactersPerSquad}-pilot squad limit.";
            return;
        }

        var sourceName = GetLivePlacementName(snapshot, member);
        var targetName = desiredRole == DesiredFleetRole.WingCommander
            ? $"{wing!.Name} / Wing Command"
            : desiredRole == DesiredFleetRole.SquadCommander
                ? $"{wing!.Name} / {squad!.Name} / Squad Commander"
                : $"{wing!.Name} / {squad!.Name}";

        StagedLiveMoves.Add(new StagedLiveMoveViewModel(
            characterId,
            member.CharacterName,
            member.WingId,
            member.SquadId,
            sourceName,
            targetWingId,
            targetSquadId,
            targetName,
            desiredRole,
            member.Role));
        RefreshPendingLiveChangeState();
        SelectedLiveActionTabIndex = 2;
        LiveCommandStatus = $"Staged {member.CharacterName} → {targetName}.";
        LiveApplyFeedback = "Ready to apply. The next click prepares one confirmation; no ESI write happens before Yes.";
    }

    private bool IsCommandSeatReservedForAnother(
        long characterId,
        long targetWingId,
        long targetSquadId,
        DesiredFleetRole desiredRole)
    {
        if (currentSnapshot is null)
        {
            return true;
        }

        var occupiedByMember = currentSnapshot.Members
            .Where(member => member.CharacterId != characterId)
            .Any(member =>
            {
                var staged = StagedLiveMoves.FirstOrDefault(move =>
                    move.CharacterId == member.CharacterId);
                var role = staged?.DesiredRole ?? DesiredRoleForEsiRole(member.Role);
                var wingId = staged?.TargetWingId ?? member.WingId;
                var squadId = staged?.TargetSquadId ?? member.SquadId;
                return role == desiredRole &&
                    wingId == targetWingId &&
                    (desiredRole == DesiredFleetRole.WingCommander ||
                        squadId == targetSquadId);
            });
        var reservedByInvite = StagedLiveInvites.Any(invite =>
            invite.CharacterId != characterId &&
            invite.DesiredRole == desiredRole &&
            invite.TargetWingId == targetWingId &&
            (desiredRole == DesiredFleetRole.WingCommander ||
                invite.TargetSquadId == targetSquadId));
        return occupiedByMember || reservedByInvite;
    }

    public void StageDraggedLiveMembers(
        long draggedCharacterId,
        long targetWingId,
        long targetSquadId,
        DesiredFleetRole desiredRole)
    {
        var dragged = GetBoardMembers().FirstOrDefault(member => member.CharacterId == draggedCharacterId);
        var characterIds = dragged is { IsSelected: true }
            ? GetBoardMembers()
                .Where(member => member.IsSelected && member.CanStage)
                .Select(member => member.CharacterId)
                .ToArray()
            : [draggedCharacterId];
        StageLiveMembers(characterIds, targetWingId, targetSquadId, desiredRole);
    }

    public void SelectLiveMember(long characterId, bool extendRange, bool toggle)
    {
        var members = GetBoardMembers().Where(member => member.IsVisible && member.CanStage).ToArray();
        var clickedIndex = Array.FindIndex(members, member => member.CharacterId == characterId);
        if (clickedIndex < 0)
        {
            return;
        }

        if (extendRange && liveSelectionAnchorCharacterId is long anchorId)
        {
            var anchorIndex = Array.FindIndex(members, member => member.CharacterId == anchorId);
            if (anchorIndex >= 0)
            {
                if (!toggle)
                {
                    foreach (var member in GetBoardMembers())
                    {
                        member.IsSelected = false;
                    }
                }

                var start = Math.Min(anchorIndex, clickedIndex);
                var end = Math.Max(anchorIndex, clickedIndex);
                for (var index = start; index <= end; index++)
                {
                    members[index].IsSelected = true;
                }

                RefreshLiveSelectionSummary();
                return;
            }
        }

        if (toggle)
        {
            members[clickedIndex].IsSelected = !members[clickedIndex].IsSelected;
        }
        else
        {
            foreach (var member in GetBoardMembers())
            {
                member.IsSelected = member.CharacterId == characterId;
            }
        }

        liveSelectionAnchorCharacterId = characterId;
        RefreshLiveSelectionSummary();
    }

    private void StageLiveMembers(
        long[] characterIds,
        long targetWingId,
        long targetSquadId,
        DesiredFleetRole? desiredRole)
    {
        if (desiredRole is DesiredFleetRole role &&
            !FleetCommandSeatRules.AcceptsPilotCount(role, characterIds.Length))
        {
            LiveCommandStatus = "A commander seat accepts exactly one pilot. Select one pilot, or choose a squad-member destination for the group.";
            LiveApplyFeedback = LiveCommandStatus;
            return;
        }

        foreach (var characterId in characterIds)
        {
            var member = currentSnapshot?.Members.FirstOrDefault(candidate => candidate.CharacterId == characterId);
            StageLiveMemberMove(
                characterId,
                targetWingId,
                targetSquadId,
                desiredRole ?? DesiredRoleForEsiRole(member?.Role));
        }

        ClearLiveMemberSelection();
    }

    private static DesiredFleetRole DesiredRoleForEsiRole(string? role) => role switch
    {
        "squad_commander" => DesiredFleetRole.SquadCommander,
        "wing_commander" => DesiredFleetRole.WingCommander,
        "fleet_commander" => DesiredFleetRole.FleetCommander,
        _ => DesiredFleetRole.SquadMember,
    };

    private static bool StagedMoveIsApplied(
        StagedLiveMoveViewModel move,
        LiveFleetMember member) =>
        member.CharacterId == move.CharacterId &&
        member.WingId == move.TargetWingId &&
        DesiredRoleForEsiRole(member.Role) == move.DesiredRole &&
        (move.DesiredRole == DesiredFleetRole.WingCommander ||
            member.SquadId == move.TargetSquadId);

    [RelayCommand]
    private async Task ApplyFleetSettingsAsync()
    {
        if (DetectedFleetId is not long fleetId || !HasFleetSettingsChanges)
        {
            LiveCommandStatus = "Change free move or the fleet MOTD first.";
            return;
        }

        var answer = MessageBox.Show(
            $"Apply these fleet settings?\n\nFree move: {(FleetSettingsFreeMove ? "On" : "Off")}\nMOTD: {FleetSettingsMotd}",
            "Apply fleet settings",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        if (await RunAdministrativeActionAsync(() =>
            fleetAdministrationService.UpdateFleetSettingsAsync(
                fleetId,
                FleetSettingsFreeMove,
                FleetSettingsMotd)))
        {
            HasFleetSettingsChanges = false;
        }
    }

    [RelayCommand]
    private async Task KickSelectedLiveMembersAsync()
    {
        var selected = GetBoardMembers()
            .Where(member => member.IsSelected)
            .ToArray();
        if (!CanStartHighImpactAction(selected.Length > 0))
        {
            LiveCommandStatus = selected.Length == 0
                ? "Select the fleet members to kick first."
                : "Tick ‘Unlock high-impact actions’ first.";
            return;
        }

        var names = string.Join(", ", selected.Take(8).Select(member => member.CharacterName));
        if (selected.Length > 8)
        {
            names += $" and {selected.Length - 8} more";
        }

        var answer = MessageBox.Show(
            $"Kick {selected.Length} selected fleet member{(selected.Length == 1 ? string.Empty : "s")} immediately?\n\n{names}\n\nThis is not a staged move: accepted kicks take effect immediately and are not automatically undone.",
            "Confirm fleet kicks",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunAdministrativeActionAsync(() => fleetAdministrationService.KickMembersAsync(
            DetectedFleetId!.Value,
            selected.Select(member => member.CharacterId).ToArray()));
    }

    [RelayCommand]
    private async Task TransferFleetBossToSelectedAsync()
    {
        var selected = GetBoardMembers().Where(member => member.IsSelected).ToArray();
        if (!CanStartHighImpactAction(selected.Length == 1))
        {
            LiveCommandStatus = selected.Length != 1
                ? "Select exactly one character to receive fleet boss."
                : "Tick ‘Unlock high-impact actions’ first.";
            return;
        }

        var target = selected[0];
        var answer = MessageBox.Show(
            $"Transfer fleet boss to {target.CharacterName}?\n\nThe signed-in character will immediately lose write access. Any other pending Fleet Desk work should be completed first.",
            "Confirm fleet-boss transfer",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunAdministrativeActionAsync(() => fleetAdministrationService.TransferFleetBossAsync(
            DetectedFleetId!.Value,
            target.CharacterId));
    }

    [RelayCommand]
    private async Task DeleteLiveSquadAsync(LiveFleetBoardSquadViewModel? squad)
    {
        if (squad is null || !squad.IsLiveStructure || !CanStartHighImpactAction(squad.IsLiveEmpty))
        {
            LiveCommandStatus = squad is { IsLiveEmpty: false }
                ? "That squad is not empty. Move or kick its members first."
                : "Unlock high-impact actions before deleting an empty squad.";
            return;
        }

        var answer = MessageBox.Show(
            $"Delete the empty squad {squad.WingName} / {squad.Name}?\n\nThis removes the live EVE hierarchy item immediately.",
            "Confirm squad deletion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunAdministrativeActionAsync(() => fleetAdministrationService.DeleteSquadAsync(
            DetectedFleetId!.Value,
            squad.SquadId));
    }

    [RelayCommand]
    private async Task DeleteLiveWingAsync(LiveFleetBoardWingViewModel? wing)
    {
        if (wing is null || !wing.IsLiveStructure || !CanStartHighImpactAction(wing.IsLiveEmpty))
        {
            LiveCommandStatus = wing is { IsLiveEmpty: false }
                ? "That wing is not empty. Move or kick its members first."
                : "Unlock high-impact actions before deleting an empty wing.";
            return;
        }

        var answer = MessageBox.Show(
            $"Delete the empty wing {wing.Name}?\n\nThis removes the wing and its empty squads from the live EVE fleet immediately.",
            "Confirm wing deletion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        await RunAdministrativeActionAsync(() => fleetAdministrationService.DeleteWingAsync(
            DetectedFleetId!.Value,
            wing.WingId));
    }

    [RelayCommand]
    private async Task CleanRebuildFleetAsync()
    {
        var snapshot = currentSnapshot;
        var profile = Profiles.GetSelectedProfileSnapshot();
        if (snapshot is null || DetectedFleetId is null)
        {
            LiveCommandStatus = "Load the live fleet before starting a clean rebuild.";
            return;
        }

        if (profile is null)
        {
            LiveCommandStatus = "Choose a saved setup before starting a clean rebuild.";
            return;
        }

        if (Profiles.HasUnsavedChanges)
        {
            LiveCommandStatus = "Save the setup first so the exact rebuild plan has a durable local record.";
            return;
        }

        if (HasPendingLiveChanges)
        {
            LiveCommandStatus = "Apply or cancel the currently staged moves before starting a clean rebuild.";
            return;
        }

        if (!CanStartHighImpactAction(targetIsValid: true))
        {
            LiveCommandStatus = "Unlock high-impact actions before starting a clean rebuild.";
            return;
        }

        var effectiveProfile = ShipRuleResolver.Resolve(profile, snapshot).EffectiveProfile;
        var knownIds = effectiveProfile.Assignments.Select(assignment => assignment.CharacterId).ToHashSet();
        var unknownCount = snapshot.Members.Count(member => !knownIds.Contains(member.CharacterId));
        var invitationCount = effectiveProfile.Assignments.Count(assignment =>
            snapshot.Members.All(member => member.CharacterId != assignment.CharacterId));
        var squadCount = profile.Wings.Sum(wing => wing.Squads.Count);
        var answer = MessageBox.Show(
            $"Clean-rebuild fleet {snapshot.FleetId} from '{profile.Name}'?\n\n" +
            $"1. Move and temporarily demote {snapshot.Members.Length} live pilots into Unknown.\n" +
            "2. Delete the now-empty old wings and squads.\n" +
            $"3. Create {profile.Wings.Count} wings and {squadCount} squads.\n" +
            $"4. Place known and ship-rule pilots, restore commanders, and send {invitationCount} invitation{(invitationCount == 1 ? string.Empty : "s")}.\n" +
            $"5. Leave {unknownCount} unmatched pilot{(unknownCount == 1 ? string.Empty : "s")} safely in Unknown.\n\n" +
            "This is deliberately destructive and may take a while. If ESI stops a write, run Clean rebuild again after refreshing; Fleet Desk will reuse Unknown and continue from the live state.",
            "Confirm clean fleet rebuild",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        IsFleetBusy = true;
        UnlockHighImpactActions = false;
        LiveCommandStatus = "Starting guarded clean rebuild…";
        try
        {
            var progress = new Progress<FleetRebuildProgress>(update =>
            {
                LiveCommandStatus = $"Clean rebuild • {update.CompletedWrites} writes • {update.Message}";
            });
            var result = await fleetRebuildService.RebuildAsync(
                snapshot.FleetId,
                profile,
                progress);
            LiveCommandStatus = result.UserMessage;
        }
        catch (Exception exception)
        {
            LiveCommandStatus = $"Clean rebuild stopped: {exception.Message}";
        }
        finally
        {
            IsFleetBusy = false;
            await RefreshFleetAsync();
        }
    }

    private bool CanStartHighImpactAction(bool targetIsValid) =>
        targetIsValid && UnlockHighImpactActions && DetectedFleetId.HasValue && !IsFleetBusy;

    private async Task<bool> RunAdministrativeActionAsync(
        Func<Task<FleetAdministrationResult>> action)
    {
        IsFleetBusy = true;
        LiveCommandStatus = "Re-checking fleet identity and fleet-boss authority…";
        try
        {
            var result = await action();
            LiveCommandStatus = result.UserMessage;
            UnlockHighImpactActions = false;
            await RefreshFleetAsync();
            return result.IsSuccess;
        }
        catch (Exception exception)
        {
            LiveCommandStatus = $"Fleet action failed: {exception.Message}";
            return false;
        }
        finally
        {
            IsFleetBusy = false;
        }
    }

    partial void OnLiveFleetSearchTextChanged(string value)
    {
        foreach (var member in GetBoardMembers())
        {
            member.IsVisible = member.Matches(value);
        }

        ApplyLiveFleetFilter();
        RefreshLiveSelectionSummary();
    }

    partial void OnLiveInviteTextChanged(string value)
    {
        _ = value;
        RefreshLiveInviteTargets();
    }

    partial void OnFleetSettingsFreeMoveChanged(bool value)
    {
        _ = value;
        RefreshFleetSettingsChangeFlag();
    }

    partial void OnFleetSettingsMotdChanged(string value)
    {
        _ = value;
        RefreshFleetSettingsChangeFlag();
    }

    private void RefreshFleetSettingsChangeFlag()
    {
        if (isApplyingFleetSettings || currentSnapshot is null)
        {
            return;
        }

        HasFleetSettingsChanges = FleetSettingsFreeMove != currentSnapshot.IsFreeMove ||
            !string.Equals(FleetSettingsMotd, currentSnapshot.Motd, StringComparison.Ordinal);
    }

    private void ApplySnapshotFleetSettings(LiveFleetSnapshot snapshot)
    {
        if (HasFleetSettingsChanges)
        {
            return;
        }

        isApplyingFleetSettings = true;
        FleetSettingsFreeMove = snapshot.IsFreeMove;
        FleetSettingsMotd = snapshot.Motd;
        HasFleetSettingsChanges = false;
        isApplyingFleetSettings = false;
    }

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
        if (e.PropertyName is nameof(ProfilesViewModel.SelectedProfile) or
            nameof(ProfilesViewModel.SelectedRunMode) or
            nameof(ProfilesViewModel.HasUnsavedChanges))
        {
            OnPropertyChanged(nameof(FcReadinessSummary));
        }

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

    private void ApplyLiveFleetResult(LiveFleetLoadResult result)
    {
        if (result.Status != LiveFleetLoadStatus.Failed || currentSnapshot is null)
        {
            FleetHierarchy.Clear();
        }
        IsLiveFleetReady = result.Status == LiveFleetLoadStatus.Ready;

        var snapshot = result.Snapshot;
        if (snapshot is null)
        {
            if (result.Status == LiveFleetLoadStatus.Failed && currentSnapshot is not null)
            {
                LiveFleetFreshness =
                    $"Last confirmed {currentSnapshot.ConfirmedAtUtc.ToLocalTime():t} • automatic retry pending";
            }
            else
            {
                currentSnapshot = null;
                StagedLiveMoves.Clear();
                StagedLiveInvites.Clear();
                ClearFleetBoard();
                LiveFleetSquadTargets.Clear();
                LiveBulkMoveTargets.Clear();
                LiveInviteTargets.Clear();
                DetectedFleetId = null;
                LiveFleetBoss = "Not detected";
                LiveFleetSummary = "No fleet data loaded";
                LiveFleetFreshness = result.CheckedAtUtc is DateTimeOffset checkedAtUtc
                    ? $"Checked {checkedAtUtc.ToLocalTime():t} • automatic every {Profiles.FleetPollingSeconds} seconds while open"
                    : "Not refreshed yet";
                RefreshPendingLiveChangeState();
            }
        }
        else
        {
            if (currentSnapshot?.FleetId != snapshot.FleetId)
            {
                StagedLiveMoves.Clear();
                StagedLiveInvites.Clear();
                HasFleetSettingsChanges = false;
            }

            currentSnapshot = snapshot;
            ApplySnapshotFleetSettings(snapshot);
            var liveCharacterIds = snapshot.Members.Select(member => member.CharacterId).ToHashSet();
            var acceptedInvites = StagedLiveInvites
                .Where(invite => liveCharacterIds.Contains(invite.CharacterId))
                .ToArray();
            foreach (var acceptedInvite in acceptedInvites)
            {
                StagedLiveInvites.Remove(acceptedInvite);
            }
            if (acceptedInvites.Length > 0)
            {
                LiveCommandStatus =
                    $"{acceptedInvites.Length} invited pilot{(acceptedInvites.Length == 1 ? " has" : "s have")} joined the fleet.";
            }
            foreach (var departedMove in StagedLiveMoves
                .Where(move => !liveCharacterIds.Contains(move.CharacterId))
                .ToArray())
            {
                StagedLiveMoves.Remove(departedMove);
            }
            foreach (var completedMove in StagedLiveMoves
                .Where(move => snapshot.Members.Any(member => StagedMoveIsApplied(move, member)))
                .ToArray())
            {
                StagedLiveMoves.Remove(completedMove);
            }
            DetectedFleetId = snapshot.FleetId;
            LiveFleetBoss = $"{snapshot.FleetBossName} ({snapshot.FleetBossId})";
            LiveFleetFreshness =
                $"Confirmed {snapshot.ConfirmedAtUtc.ToLocalTime():t} • automatic every {Profiles.FleetPollingSeconds} seconds while open";
            LiveFleetSummary = result.Status == LiveFleetLoadStatus.Ready
                ? $"{snapshot.Members.Length} members • free move {(snapshot.IsFreeMove ? "on" : "off")} • advert {(snapshot.IsRegistered ? "active" : "off")} • voice {(snapshot.IsVoiceEnabled ? "on" : "off")}"
                : "Fleet detected • detailed hierarchy requires the signed-in character to be fleet boss";
        }

        LiveFleetStatusTitle = result.Status switch
        {
            LiveFleetLoadStatus.Ready => $"Fleet {snapshot!.FleetId} is live",
            LiveFleetLoadStatus.SignedOut => "Sign in to read a fleet",
            LiveFleetLoadStatus.NotInFleet => "No current fleet detected",
            LiveFleetLoadStatus.NotFleetBoss => "Fleet detected — fleet boss access required",
            _ => "Live fleet could not be refreshed",
        };
        LiveFleetStatusDetail = result.RetryAfter is null
            ? result.UserMessage
            : $"{result.UserMessage} Automatic refresh will resume when ESI allows it.";

        if (snapshot is not null && result.Status == LiveFleetLoadStatus.Ready)
        {
            foreach (var node in BuildFleetTree(snapshot))
            {
                FleetHierarchy.Add(node);
            }

            BuildFleetBoard(snapshot);
            RefreshPendingLiveChangeState();
        }
    }

    private void ClearLiveFleet()
    {
        FleetHierarchy.Clear();
        ClearFleetBoard();
        LiveFleetSquadTargets.Clear();
        LiveBulkMoveTargets.Clear();
        LiveInviteTargets.Clear();
        StagedLiveMoves.Clear();
        StagedLiveInvites.Clear();
        currentSnapshot = null;
        DetectedFleetId = null;
        IsLiveFleetReady = false;
        LiveFleetBoss = "Not detected";
        LiveFleetSummary = "No fleet data loaded";
        LiveFleetFreshness = "Not refreshed yet";
        LiveFleetStatusTitle = "Sign in to read a fleet";
        LiveFleetStatusDetail = "Authorize the character that will be fleet boss.";
        RefreshPendingLiveChangeState();
    }

    private void BuildFleetBoard(LiveFleetSnapshot snapshot)
    {
        var selectedIds = GetBoardMembers()
            .Where(member => member.IsSelected)
            .Select(member => member.CharacterId)
            .ToHashSet();
        ClearFleetBoard();
        LiveFleetSquadTargets.Clear();

        var fleetCommandMembers = CreateBoardMembers(
            snapshot.Members.Where(member =>
                GetEffectivePosition(member).WingId < 0),
            "Fleet Command",
            "Fleet Command",
            selectedIds);
        if (fleetCommandMembers.Count > 0)
        {
            FleetBoardWings.Add(new LiveFleetBoardWingViewModel(
                -1,
                "Fleet Command",
                isLiveEmpty: false,
                [new LiveFleetBoardSquadViewModel(
                    -1,
                    -1,
                    "Fleet Command",
                    "Command",
                    isLiveEmpty: false,
                    fleetCommandMembers)]));
        }

        foreach (var wing in snapshot.Wings)
        {
            var squads = new ObservableCollection<LiveFleetBoardSquadViewModel>();
            var wingCommandMembers = CreateBoardMembers(
                snapshot.Members.Where(member =>
                {
                    var position = GetEffectivePosition(member);
                    return position.WingId == wing.WingId && position.SquadId < 0;
                }),
                wing.Name,
                "Wing Command",
                selectedIds);
            squads.Add(new LiveFleetBoardSquadViewModel(
                wing.WingId,
                -1,
                wing.Name,
                "Wing Command",
                isLiveEmpty: false,
                wingCommandMembers));
            LiveFleetSquadTargets.Add(new LiveFleetSquadTargetViewModel(
                wing.WingId,
                -1,
                $"{wing.Name} — Wing Commander",
                DesiredFleetRole.WingCommander));

            foreach (var squad in wing.Squads)
            {
                var members = CreateBoardMembers(
                    snapshot.Members.Where(member =>
                    {
                        var position = GetEffectivePosition(member);
                        return position.WingId == wing.WingId &&
                            position.SquadId == squad.SquadId;
                    }),
                    wing.Name,
                    squad.Name,
                    selectedIds);
                squads.Add(new LiveFleetBoardSquadViewModel(
                    wing.WingId,
                    squad.SquadId,
                    wing.Name,
                    squad.Name,
                    isLiveEmpty: snapshot.Members.All(member =>
                        member.SquadId != squad.SquadId),
                    members));
                LiveFleetSquadTargets.Add(new LiveFleetSquadTargetViewModel(
                    wing.WingId,
                    squad.SquadId,
                    $"{wing.Name} / {squad.Name} — Squad Commander",
                    DesiredFleetRole.SquadCommander));
                LiveFleetSquadTargets.Add(new LiveFleetSquadTargetViewModel(
                    wing.WingId,
                    squad.SquadId,
                    $"{wing.Name} / {squad.Name} — Squad members",
                    DesiredFleetRole.SquadMember));
            }

            FleetBoardWings.Add(new LiveFleetBoardWingViewModel(
                wing.WingId,
                wing.Name,
                isLiveEmpty: snapshot.Members.All(member => member.WingId != wing.WingId),
                squads));
        }

        RefreshLiveBulkMoveTargets();
        RefreshLiveInviteTargets();
        ApplyLiveFleetFilter();
        RefreshLiveSelectionSummary();
    }

    private void RefreshLiveInviteTargets()
    {
        var selected = SelectedInviteTarget;
        var nameCount = RosterPasteParser.Parse(LiveInviteText).Length;
        LiveInviteTargets.Clear();

        foreach (var target in LiveFleetSquadTargets.Where(target =>
            target.DesiredRole == DesiredFleetRole.SquadMember ||
            (nameCount == 1 && IsLiveCommandSeatAvailable(target))))
        {
            LiveInviteTargets.Add(target);
        }

        SelectedInviteTarget = LiveInviteTargets.FirstOrDefault(target =>
            selected is not null &&
            target.WingId == selected.WingId &&
            target.SquadId == selected.SquadId &&
            target.DesiredRole == selected.DesiredRole) ?? LiveInviteTargets
                .FirstOrDefault(target => target.DesiredRole == DesiredFleetRole.SquadMember);
        OnPropertyChanged(nameof(LiveInviteTargetHint));
    }

    private void RefreshLiveBulkMoveTargets()
    {
        var selected = SelectedBulkLiveTarget;
        var selectedCount = GetBoardMembers().Count(member => member.IsSelected && member.CanStage);
        LiveBulkMoveTargets.Clear();
        foreach (var target in LiveFleetSquadTargets.Where(target =>
            target.DesiredRole == DesiredFleetRole.SquadMember || selectedCount == 1))
        {
            LiveBulkMoveTargets.Add(target);
        }

        SelectedBulkLiveTarget = LiveBulkMoveTargets.FirstOrDefault(target =>
            selected is not null &&
            target.WingId == selected.WingId &&
            target.SquadId == selected.SquadId &&
            target.DesiredRole == selected.DesiredRole) ?? LiveBulkMoveTargets
                .FirstOrDefault(target => target.DesiredRole == DesiredFleetRole.SquadMember);
    }

    private bool IsLiveCommandSeatAvailable(LiveFleetSquadTargetViewModel target)
    {
        if (!target.IsCommandSeat || currentSnapshot is null)
        {
            return false;
        }

        var isOccupiedInEve = currentSnapshot.Members.Any(member =>
            DesiredRoleForEsiRole(member.Role) == target.DesiredRole &&
            member.WingId == target.WingId &&
            (target.DesiredRole == DesiredFleetRole.WingCommander ||
                member.SquadId == target.SquadId));
        var isReservedByMove = StagedLiveMoves.Any(move =>
            move.DesiredRole == target.DesiredRole &&
            move.TargetWingId == target.WingId &&
            (target.DesiredRole == DesiredFleetRole.WingCommander ||
                move.TargetSquadId == target.SquadId));
        var isReservedByInvite = StagedLiveInvites.Any(invite =>
            invite.DesiredRole == target.DesiredRole &&
            invite.TargetWingId == target.WingId &&
            (target.DesiredRole == DesiredFleetRole.WingCommander ||
                invite.TargetSquadId == target.SquadId));
        return !isOccupiedInEve && !isReservedByMove && !isReservedByInvite;
    }

    private void ApplyLiveFleetFilter()
    {
        var isFiltering = !string.IsNullOrWhiteSpace(LiveFleetSearchText);
        foreach (var wing in FleetBoardWings)
        {
            wing.ApplyFilter(isFiltering);
        }
    }

    private ObservableCollection<LiveFleetBoardMemberViewModel> CreateBoardMembers(
        IEnumerable<LiveFleetMember> source,
        string wingName,
        string squadName,
        HashSet<long> selectedIds)
    {
        var members = new ObservableCollection<LiveFleetBoardMemberViewModel>(
            SortMembers(source.ToArray()).Select(member =>
            {
                var staged = StagedLiveMoves.FirstOrDefault(move =>
                    move.CharacterId == member.CharacterId);
                return new LiveFleetBoardMemberViewModel(
                    member.CharacterId,
                    member.CharacterName,
                    member.Role,
                    member.RoleName,
                    member.ShipTypeName,
                    member.WingId,
                    member.SquadId,
                    wingName,
                    squadName,
                    staged);
            }));
        foreach (var member in members)
        {
            member.IsSelected = selectedIds.Contains(member.CharacterId);
            member.IsVisible = member.Matches(LiveFleetSearchText);
            member.PropertyChanged += OnLiveFleetMemberPropertyChanged;
        }

        return members;
    }

    private (long WingId, long SquadId) GetEffectivePosition(LiveFleetMember member)
    {
        var staged = StagedLiveMoves.FirstOrDefault(move => move.CharacterId == member.CharacterId);
        if (staged is null)
        {
            return (member.WingId, member.SquadId);
        }

        return staged.DesiredRole == DesiredFleetRole.WingCommander
            ? (staged.TargetWingId, -1)
            : (staged.TargetWingId, staged.TargetSquadId);
    }

    private static string GetLivePlacementName(
        LiveFleetSnapshot snapshot,
        LiveFleetMember member)
    {
        if (member.WingId < 0)
        {
            return "Fleet Command";
        }

        var wing = snapshot.Wings.FirstOrDefault(candidate => candidate.WingId == member.WingId);
        if (wing is null)
        {
            return "Unknown position";
        }

        if (member.SquadId < 0)
        {
            return $"{wing.Name} / Wing Command";
        }

        var squad = wing.Squads.FirstOrDefault(candidate => candidate.SquadId == member.SquadId);
        return squad is null
            ? $"{wing.Name} / Unknown squad"
            : $"{wing.Name} / {squad.Name}";
    }

    private IEnumerable<LiveFleetBoardMemberViewModel> GetBoardMembers() =>
        FleetBoardWings.SelectMany(wing => wing.Squads).SelectMany(squad => squad.Members);

    private void ClearFleetBoard()
    {
        foreach (var member in GetBoardMembers())
        {
            member.PropertyChanged -= OnLiveFleetMemberPropertyChanged;
        }

        FleetBoardWings.Clear();
    }

    private void OnLiveFleetMemberPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _ = sender;
        if (e.PropertyName == nameof(LiveFleetBoardMemberViewModel.IsSelected))
        {
            RefreshLiveSelectionSummary();
        }
    }

    private void RefreshLiveSelectionSummary()
    {
        OnPropertyChanged(nameof(LiveFleetSelectedCount));
        OnPropertyChanged(nameof(LiveFleetVisibleCount));
        OnPropertyChanged(nameof(LiveFleetSelectionSummary));
        OnPropertyChanged(nameof(LiveFleetSearchSummary));
        RefreshLiveBulkMoveTargets();
    }

    private void RefreshPendingLiveChangeState()
    {
        OnPropertyChanged(nameof(HasStagedLiveMoves));
        OnPropertyChanged(nameof(HasStagedLiveInvites));
        OnPropertyChanged(nameof(HasPendingLiveChanges));
        OnPropertyChanged(nameof(PendingLiveChangeCount));
        OnPropertyChanged(nameof(StagedLiveMovesSummary));
        OnPropertyChanged(nameof(PendingLiveChangesSummary));
        OnPropertyChanged(nameof(ApplyPendingLiveChangesText));
        OnPropertyChanged(nameof(CanApplyPendingLiveChanges));
        OnPropertyChanged(nameof(SentLiveInvitesSummary));
        if (currentSnapshot is not null)
        {
            BuildFleetBoard(currentSnapshot);
        }
    }

    private static List<LiveFleetTreeNodeViewModel> BuildFleetTree(LiveFleetSnapshot snapshot)
    {
        var roots = new List<LiveFleetTreeNodeViewModel>();
        var placedCharacterIds = new HashSet<long>();

        var commandMembers = SortMembers(snapshot.Members.Where(member => member.WingId < 0).ToArray());
        if (commandMembers.Length > 0)
        {
            foreach (var member in commandMembers)
            {
                placedCharacterIds.Add(member.CharacterId);
            }

            roots.Add(new LiveFleetTreeNodeViewModel(
                "Fleet Command",
                MemberCountText(commandMembers.Length),
                commandMembers.Select(CreateMemberNode).ToArray()));
        }

        foreach (var wing in snapshot.Wings)
        {
            var wingChildren = new List<LiveFleetTreeNodeViewModel>();
            var wingCommanders = SortMembers(snapshot.Members
                .Where(member => member.WingId == wing.WingId && member.SquadId < 0)
                .ToArray());
            foreach (var commander in wingCommanders)
            {
                placedCharacterIds.Add(commander.CharacterId);
                wingChildren.Add(CreateMemberNode(commander));
            }

            foreach (var squad in wing.Squads)
            {
                var squadMembers = SortMembers(snapshot.Members
                    .Where(member => member.WingId == wing.WingId && member.SquadId == squad.SquadId)
                    .ToArray());
                foreach (var member in squadMembers)
                {
                    placedCharacterIds.Add(member.CharacterId);
                }

                wingChildren.Add(new LiveFleetTreeNodeViewModel(
                    squad.Name,
                    MemberCountText(squadMembers.Length),
                    squadMembers.Select(CreateMemberNode).ToArray()));
            }

            var wingMemberCount = snapshot.Members.Count(member => member.WingId == wing.WingId);
            roots.Add(new LiveFleetTreeNodeViewModel(
                wing.Name,
                MemberCountText(wingMemberCount),
                wingChildren.ToArray()));
        }

        var unmatchedMembers = SortMembers(snapshot.Members
            .Where(member => !placedCharacterIds.Contains(member.CharacterId))
            .ToArray());
        if (unmatchedMembers.Length > 0)
        {
            roots.Add(new LiveFleetTreeNodeViewModel(
                "Unassigned / unknown structure",
                MemberCountText(unmatchedMembers.Length),
                unmatchedMembers.Select(CreateMemberNode).ToArray()));
        }

        return roots;
    }

    private static LiveFleetMember[] SortMembers(LiveFleetMember[] members) =>
        members
            .OrderBy(member => RoleOrder(member.Role))
            .ThenBy(member => member.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static LiveFleetTreeNodeViewModel CreateMemberNode(LiveFleetMember member)
    {
        var location = member.StationName ?? member.SolarSystemName;
        return new LiveFleetTreeNodeViewModel(
            member.CharacterName,
            $"{member.RoleName} • {member.ShipTypeName} • {location} • fleet warp {(member.TakesFleetWarp ? "on" : "off")}",
            []);
    }

    private static int RoleOrder(string role) => role switch
    {
        "fleet_commander" => 0,
        "wing_commander" => 1,
        "squad_commander" => 2,
        _ => 3,
    };

    private static string MemberCountText(int count) =>
        $"{count} member{(count == 1 ? string.Empty : "s")}";
}
