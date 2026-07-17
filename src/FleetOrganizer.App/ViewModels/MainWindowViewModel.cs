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
using FleetOrganizer.Core.Fleets;
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

    [ObservableProperty]
    public partial string LiveFleetSearchText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial LiveFleetSquadTargetViewModel? SelectedBulkLiveTarget { get; set; }

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
    public partial string SelectedPage { get; set; } = "Home";

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

    public ObservableCollection<LiveFleetSquadTargetViewModel> LiveFleetSquadTargets { get; } = [];

    public bool HasStagedLiveMoves => StagedLiveMoves.Count > 0;

    public string StagedLiveMovesSummary => HasStagedLiveMoves
        ? $"{StagedLiveMoves.Count} pending move{(StagedLiveMoves.Count == 1 ? string.Empty : "s")} — no ESI write yet"
        : "Drag an ordinary squad member to another squad to stage a move.";

    public int LiveFleetSelectedCount => FleetBoardWings
        .SelectMany(wing => wing.Squads)
        .SelectMany(squad => squad.Members)
        .Count(member => member.IsSelected);

    public string LiveFleetSelectionSummary =>
        $"{LiveFleetSelectedCount} selected • search matches character, ship, role, wing, or squad";

    public MainWindowViewModel(
        IAppDataPaths paths,
        IOptions<EveDeveloperOptions> developerOptions,
        IEveAuthenticationService authenticationService,
        ILiveFleetService liveFleetService,
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
        "Home" => "Choose a saved layout, review the exact changes, then organise the fleet.",
        "Profiles" => "Manage reusable layouts and rosters. Advanced structure controls are optional.",
        "Live Fleet" => "Read the current EVE fleet hierarchy, roles, ships, and locations.",
        "Activity" => "Review the current run, recover individual steps, or reopen a previous fleet run.",
        "Settings" => "Configure EVE SSO, storage, polling, and appearance.",
        _ => string.Empty,
    };

    public string PageTitle => SelectedPage switch
    {
        "Profiles" => "Fleet templates",
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
                ? "Authorization is encrypted for this Windows user. Live Fleet is read-only; reviewed profiles can start guarded repair, invitation, placement, and commander runs."
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
            SelectedPage = "Home";
        }
    }

    [RelayCommand]
    private void DismissAttention() => HasAttentionBanner = false;

    [RelayCommand]
    private void ClearStagedLiveMoves()
    {
        StagedLiveMoves.Clear();
        RefreshStagedMoveState();
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
            LiveFleetStatusDetail = "Select at least one ordinary squad member first.";
            return;
        }

        StageLiveMembers(selected, target.WingId, target.SquadId);
    }

    [RelayCommand]
    private void RemoveStagedLiveMove(StagedLiveMoveViewModel? move)
    {
        if (move is null)
        {
            return;
        }

        StagedLiveMoves.Remove(move);
        RefreshStagedMoveState();
    }

    [RelayCommand]
    private async Task ReviewStagedLiveMovesAsync()
    {
        if (currentSnapshot is null || StagedLiveMoves.Count == 0)
        {
            LiveFleetStatusDetail = "Stage at least one ordinary squad-member move first.";
            return;
        }

        if (await Profiles.PrepareStagedMovesAsync(currentSnapshot, StagedLiveMoves.ToArray()))
        {
            StagedLiveMoves.Clear();
            RefreshStagedMoveState();
            SelectedPage = "Home";
        }
    }

    public void StageLiveMemberMove(long characterId, long targetWingId, long targetSquadId)
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

        if (!string.Equals(member.Role, "squad_member", StringComparison.Ordinal))
        {
            LiveFleetStatusDetail = $"{member.CharacterName} is a commander. Use a reviewed template commander run instead.";
            return;
        }

        var existingMove = StagedLiveMoves.FirstOrDefault(move => move.CharacterId == characterId);
        if (existingMove is not null)
        {
            StagedLiveMoves.Remove(existingMove);
        }
        if (member.WingId == targetWingId && member.SquadId == targetSquadId)
        {
            RefreshStagedMoveState();
            LiveFleetStatusDetail = $"{member.CharacterName} is already in {wing.Name} / {squad.Name}.";
            return;
        }

        var targetCount = snapshot.Members.Count(candidate =>
        {
            var staged = StagedLiveMoves.FirstOrDefault(move => move.CharacterId == candidate.CharacterId);
            return staged is not null
                ? staged.TargetWingId == targetWingId && staged.TargetSquadId == targetSquadId
                : candidate.WingId == targetWingId && candidate.SquadId == targetSquadId;
        });
        if (targetCount >= FleetOrganizer.Core.Profiles.ProfileValidator.MaximumCharactersPerSquad)
        {
            RefreshStagedMoveState();
            LiveFleetStatusDetail = $"{wing.Name} / {squad.Name} is already at the safe 10-character capacity.";
            return;
        }

        StagedLiveMoves.Add(new StagedLiveMoveViewModel(
            characterId,
            member.CharacterName,
            member.WingId,
            member.SquadId,
            targetWingId,
            targetSquadId,
            $"{wing.Name} / {squad.Name}"));
        RefreshStagedMoveState();
        LiveFleetStatusDetail = $"Staged {member.CharacterName} → {wing.Name} / {squad.Name}. Review pending moves when ready.";
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
        StageLiveMembers(characterIds, targetWingId, targetSquadId);
    }

    private void StageLiveMembers(long[] characterIds, long targetWingId, long targetSquadId)
    {
        foreach (var characterId in characterIds)
        {
            StageLiveMemberMove(characterId, targetWingId, targetSquadId);
        }

        ClearLiveMemberSelection();
    }

    partial void OnLiveFleetSearchTextChanged(string value)
    {
        foreach (var member in GetBoardMembers())
        {
            member.IsVisible = member.Matches(value);
        }

        RefreshLiveSelectionSummary();
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
            }

            currentSnapshot = snapshot;
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
            RefreshStagedMoveState();
        }
    }

    private void ClearLiveFleet()
    {
        FleetHierarchy.Clear();
        ClearFleetBoard();
        LiveFleetSquadTargets.Clear();
        StagedLiveMoves.Clear();
        currentSnapshot = null;
        DetectedFleetId = null;
        IsLiveFleetReady = false;
        LiveFleetBoss = "Not detected";
        LiveFleetSummary = "No fleet data loaded";
        LiveFleetFreshness = "Not refreshed yet";
        LiveFleetStatusTitle = "Sign in to read a fleet";
        LiveFleetStatusDetail = "Authorize the character that will be fleet boss.";
        RefreshStagedMoveState();
    }

    private void BuildFleetBoard(LiveFleetSnapshot snapshot)
    {
        var selectedIds = GetBoardMembers()
            .Where(member => member.IsSelected)
            .Select(member => member.CharacterId)
            .ToHashSet();
        ClearFleetBoard();
        LiveFleetSquadTargets.Clear();
        foreach (var wing in snapshot.Wings)
        {
            var squads = new ObservableCollection<LiveFleetBoardSquadViewModel>();
            foreach (var squad in wing.Squads)
            {
                var members = new ObservableCollection<LiveFleetBoardMemberViewModel>(
                    SortMembers(snapshot.Members
                        .Where(member => member.WingId == wing.WingId &&
                            member.SquadId == squad.SquadId)
                        .ToArray())
                    .Select(member =>
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
                            wing.Name,
                            squad.Name,
                            staged?.TargetName);
                    }));
                foreach (var member in members)
                {
                    member.IsSelected = selectedIds.Contains(member.CharacterId);
                    member.IsVisible = member.Matches(LiveFleetSearchText);
                    member.PropertyChanged += OnLiveFleetMemberPropertyChanged;
                }
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
        RefreshLiveSelectionSummary();
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
        OnPropertyChanged(nameof(LiveFleetSelectionSummary));
    }

    private void RefreshStagedMoveState()
    {
        OnPropertyChanged(nameof(HasStagedLiveMoves));
        OnPropertyChanged(nameof(StagedLiveMovesSummary));
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
