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
    private readonly IFleetAdministrationService fleetAdministrationService;
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

    [ObservableProperty]
    public partial string LiveFleetSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial LiveFleetSquadTargetViewModel? SelectedBulkLiveTarget { get; set; }

    [ObservableProperty]
    public partial DesiredFleetRole SelectedBulkLiveRole { get; set; } = DesiredFleetRole.SquadMember;

    [ObservableProperty]
    public partial string LiveInviteText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial LiveFleetSquadTargetViewModel? SelectedInviteTarget { get; set; }

    [ObservableProperty]
    public partial DesiredFleetRole SelectedInviteRole { get; set; } = DesiredFleetRole.SquadMember;

    [ObservableProperty]
    public partial bool UnlockHighImpactActions { get; set; }

    [ObservableProperty]
    public partial string LiveCommandStatus { get; set; } =
        "Stage normal work freely. Fleet Desk asks once before it writes.";

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

    public bool HasStagedLiveMoves => StagedLiveMoves.Count > 0;

    public bool HasStagedLiveInvites => StagedLiveInvites.Count > 0;

    public bool HasPendingLiveChanges => HasStagedLiveMoves || HasStagedLiveInvites;

    public int PendingLiveChangeCount => StagedLiveMoves.Count + StagedLiveInvites.Count;

    public string StagedLiveMovesSummary => HasStagedLiveMoves
        ? $"{StagedLiveMoves.Count} pending move{(StagedLiveMoves.Count == 1 ? string.Empty : "s")} — no ESI write yet"
        : "Drag a member to another squad to stage a move.";

    public string PendingLiveChangesSummary => HasPendingLiveChanges
        ? $"{PendingLiveChangeCount} pending change{(PendingLiveChangeCount == 1 ? string.Empty : "s")} • no ESI write yet"
        : "No pending changes. Drag members, bulk-place them, invite pilots, or apply a template.";

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
        IFleetAdministrationService fleetAdministrationService,
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
        this.fleetAdministrationService = fleetAdministrationService;
        this.diagnosticExportService = diagnosticExportService;
        this.localDataService = localDataService;
        this.updateCheckService = updateCheckService;
        applicationVersion =
            Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "development";
        Profiles = profiles;
        LiveRoleOptions = Profiles.RoleOptions
            .Where(option => option.Value != DesiredFleetRole.FleetCommander)
            .ToArray();
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
        "Live Fleet" => "Run the fleet from one place: drag, invite, apply a template, review, confirm.",
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

    public DesiredRoleOptionViewModel[] LiveRoleOptions { get; }

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
            var result = await liveFleetService.LoadCurrentAsync();
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
        StagedLiveInvites.Clear();
        Profiles.HideDryRunCommand.Execute(null);
        LiveCommandStatus = "Pending changes cleared. Nothing was written to EVE.";
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

        StageLiveMembers(selected, target.WingId, target.SquadId, SelectedBulkLiveRole);
    }

    [RelayCommand]
    private void RemoveStagedLiveMove(StagedLiveMoveViewModel? move)
    {
        if (move is null)
        {
            return;
        }

        StagedLiveMoves.Remove(move);
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
        RefreshPendingLiveChangeState();
    }

    [RelayCommand]
    private async Task StageLiveInvitesAsync()
    {
        if (currentSnapshot is null || SelectedInviteTarget is not { } target)
        {
            LiveCommandStatus = "Load a fleet and choose the invitation target squad first.";
            return;
        }

        if (SelectedInviteRole == DesiredFleetRole.FleetCommander)
        {
            LiveCommandStatus = "Use the separately locked fleet-boss transfer action after the character joins.";
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
            var liveIds = currentSnapshot.Members.Select(member => member.CharacterId).ToHashSet();
            var stagedIds = StagedLiveInvites.Select(invite => invite.CharacterId).ToHashSet();
            var added = 0;
            foreach (var character in resolution.Resolved)
            {
                if (liveIds.Contains(character.CharacterId) || !stagedIds.Add(character.CharacterId))
                {
                    continue;
                }

                StagedLiveInvites.Add(new StagedLiveInviteViewModel(
                    character.CharacterId,
                    character.CharacterName,
                    target.WingId,
                    target.SquadId,
                    target.DisplayName,
                    SelectedInviteRole));
                added++;
            }

            LiveInviteText = resolution.UnresolvedNames.Length == 0
                ? string.Empty
                : string.Join(Environment.NewLine, resolution.UnresolvedNames);
            LiveCommandStatus = resolution.UnresolvedNames.Length == 0
                ? $"Staged {added} invitation{(added == 1 ? string.Empty : "s")}."
                : $"Staged {added}; {resolution.UnresolvedNames.Length} exact name{(resolution.UnresolvedNames.Length == 1 ? " was" : "s were")} not found.";
            RefreshPendingLiveChangeState();
        }
        catch (Exception exception)
        {
            LiveCommandStatus = $"Invitations could not be staged: {exception.Message}";
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
            StagedLiveInvites.ToArray()))
        {
            LiveCommandStatus = "Review ready below. Confirm once to send the guarded run.";
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
        var squad = wing?.Squads.FirstOrDefault(candidate => candidate.SquadId == targetSquadId);
        if (member is null || wing is null || squad is null)
        {
            LiveFleetStatusDetail = "That character or target squad is no longer in the current fleet. Refresh and try again.";
            return;
        }

        if (desiredRole == DesiredFleetRole.FleetCommander)
        {
            LiveCommandStatus = "Fleet-boss transfer is a separately locked high-impact action.";
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
        if (!isAlreadyInTarget &&
            targetCount >= FleetOrganizer.Core.Profiles.ProfileValidator.MaximumCharactersPerSquad)
        {
            RefreshPendingLiveChangeState();
            LiveCommandStatus =
                $"{wing.Name} / {squad.Name} is already at EVE's {FleetOrganizer.Core.Profiles.ProfileValidator.MaximumCharactersPerSquad}-pilot squad limit.";
            return;
        }

        StagedLiveMoves.Add(new StagedLiveMoveViewModel(
            characterId,
            member.CharacterName,
            member.WingId,
            member.SquadId,
            targetWingId,
            targetSquadId,
            $"{wing.Name} / {squad.Name}",
            desiredRole,
            member.Role));
        RefreshPendingLiveChangeState();
        LiveCommandStatus = $"Staged {member.CharacterName} → {wing.Name} / {squad.Name}.";
    }

    public void StageDraggedLiveMembers(long draggedCharacterId, long targetWingId, long targetSquadId)
    {
        var dragged = GetBoardMembers().FirstOrDefault(member => member.CharacterId == draggedCharacterId);
        var characterIds = dragged is { IsSelected: true }
            ? GetBoardMembers()
                .Where(member => member.IsSelected && member.CanStage)
                .Select(member => member.CharacterId)
                .ToArray()
            : [draggedCharacterId];
        StageLiveMembers(characterIds, targetWingId, targetSquadId, desiredRole: null);
    }

    private void StageLiveMembers(
        long[] characterIds,
        long targetWingId,
        long targetSquadId,
        DesiredFleetRole? desiredRole)
    {
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
        if (squad is null || !squad.IsLiveStructure || !CanStartHighImpactAction(squad.IsEmpty))
        {
            LiveCommandStatus = squad is { IsEmpty: false }
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
        if (wing is null || !wing.IsLiveStructure || !CanStartHighImpactAction(wing.IsEmpty))
        {
            LiveCommandStatus = wing is { IsEmpty: false }
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
        FleetHierarchy.Clear();
        IsLiveFleetReady = result.Status == LiveFleetLoadStatus.Ready;

        var snapshot = result.Snapshot;
        if (snapshot is null)
        {
            currentSnapshot = null;
            StagedLiveMoves.Clear();
            StagedLiveInvites.Clear();
            ClearFleetBoard();
            LiveFleetSquadTargets.Clear();
            DetectedFleetId = null;
            LiveFleetBoss = "Not detected";
            LiveFleetSummary = "No fleet data loaded";
            LiveFleetFreshness = result.CheckedAtUtc is DateTimeOffset checkedAtUtc
                ? $"Checked {checkedAtUtc.ToLocalTime():t} • automatic every {Profiles.FleetPollingSeconds} seconds while open"
                : "Not refreshed yet";
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
            foreach (var acceptedInvite in StagedLiveInvites
                .Where(invite => liveCharacterIds.Contains(invite.CharacterId))
                .ToArray())
            {
                StagedLiveInvites.Remove(acceptedInvite);
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
            snapshot.Members.Where(member => member.WingId < 0),
            "Fleet Command",
            "Fleet Command",
            selectedIds);
        if (fleetCommandMembers.Count > 0)
        {
            FleetBoardWings.Add(new LiveFleetBoardWingViewModel(
                -1,
                "Fleet Command",
                [new LiveFleetBoardSquadViewModel(
                    -1,
                    -1,
                    "Fleet Command",
                    "Command",
                    fleetCommandMembers)]));
        }

        foreach (var wing in snapshot.Wings)
        {
            var squads = new ObservableCollection<LiveFleetBoardSquadViewModel>();
            var wingCommandMembers = CreateBoardMembers(
                snapshot.Members.Where(member =>
                    member.WingId == wing.WingId && member.SquadId < 0),
                wing.Name,
                "Wing Command",
                selectedIds);
            if (wingCommandMembers.Count > 0)
            {
                squads.Add(new LiveFleetBoardSquadViewModel(
                    wing.WingId,
                    -1,
                    wing.Name,
                    "Wing Command",
                    wingCommandMembers));
            }

            foreach (var squad in wing.Squads)
            {
                var members = CreateBoardMembers(
                    snapshot.Members.Where(member =>
                        member.WingId == wing.WingId && member.SquadId == squad.SquadId),
                    wing.Name,
                    squad.Name,
                    selectedIds);
                squads.Add(new LiveFleetBoardSquadViewModel(
                    wing.WingId,
                    squad.SquadId,
                    wing.Name,
                    squad.Name,
                    members));
                LiveFleetSquadTargets.Add(new LiveFleetSquadTargetViewModel(
                    wing.WingId,
                    squad.SquadId,
                    $"{wing.Name} / {squad.Name}"));
            }

            FleetBoardWings.Add(new LiveFleetBoardWingViewModel(wing.WingId, wing.Name, squads));
        }

        SelectedBulkLiveTarget = LiveFleetSquadTargets.FirstOrDefault(target =>
            SelectedBulkLiveTarget is not null &&
            target.WingId == SelectedBulkLiveTarget.WingId &&
            target.SquadId == SelectedBulkLiveTarget.SquadId) ?? LiveFleetSquadTargets.FirstOrDefault();
        SelectedInviteTarget = LiveFleetSquadTargets.FirstOrDefault(target =>
            SelectedInviteTarget is not null &&
            target.WingId == SelectedInviteTarget.WingId &&
            target.SquadId == SelectedInviteTarget.SquadId) ?? LiveFleetSquadTargets.FirstOrDefault();
        ApplyLiveFleetFilter();
        RefreshLiveSelectionSummary();
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
                    staged?.Summary);
            }));
        foreach (var member in members)
        {
            member.IsSelected = selectedIds.Contains(member.CharacterId);
            member.IsVisible = member.Matches(LiveFleetSearchText);
            member.PropertyChanged += OnLiveFleetMemberPropertyChanged;
        }

        return members;
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
    }

    private void RefreshPendingLiveChangeState()
    {
        OnPropertyChanged(nameof(HasStagedLiveMoves));
        OnPropertyChanged(nameof(HasStagedLiveInvites));
        OnPropertyChanged(nameof(HasPendingLiveChanges));
        OnPropertyChanged(nameof(PendingLiveChangeCount));
        OnPropertyChanged(nameof(StagedLiveMovesSummary));
        OnPropertyChanged(nameof(PendingLiveChangesSummary));
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
