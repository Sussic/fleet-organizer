using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Authentication;
using FleetOrganizer.Core.Fleets;
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
    private readonly string requiredScopes;
    private readonly DispatcherTimer fleetRefreshTimer;

    private bool isFleetPollingEnabled = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTitle))]
    [NotifyPropertyChangedFor(nameof(StatusDetail))]
    [NotifyPropertyChangedFor(nameof(SignedInCharacter))]
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
    public partial long? DetectedFleetId { get; set; }

    [ObservableProperty]
    public partial string LiveFleetBoss { get; set; } = "Not detected";

    [ObservableProperty]
    public partial string LiveFleetSummary { get; set; } = "No fleet data loaded";

    [ObservableProperty]
    public partial string LiveFleetFreshness { get; set; } = "Not refreshed yet";

    [ObservableProperty]
    public partial bool IsLiveFleetReady { get; set; }

    public ObservableCollection<LiveFleetTreeNodeViewModel> FleetHierarchy { get; } = [];

    public MainWindowViewModel(
        IAppDataPaths paths,
        IOptions<EveDeveloperOptions> developerOptions,
        IEveAuthenticationService authenticationService,
        ILiveFleetService liveFleetService,
        ProfilesViewModel profiles)
    {
        ArgumentNullException.ThrowIfNull(profiles);

        this.paths = paths;
        this.developerOptions = developerOptions.Value;
        this.authenticationService = authenticationService;
        this.liveFleetService = liveFleetService;
        Profiles = profiles;
        requiredScopes = string.Join(Environment.NewLine, EveDeveloperOptions.RequiredScopes);
        fleetRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(30),
        };
        fleetRefreshTimer.Tick += OnFleetRefreshTimerTick;
    }

    public string PageSubtitle => SelectedPage switch
    {
        "Home" => "Choose a saved layout, review the exact changes, then organise the fleet.",
        "Profiles" => "Manage reusable layouts and rosters. Advanced structure controls are optional.",
        "Live Fleet" => "Read the current EVE fleet hierarchy, roles, ships, and locations.",
        "Activity" => "See what the current run needs next, then open per-step recovery details.",
        "Settings" => "Configure EVE SSO, storage, polling, and appearance.",
        _ => string.Empty,
    };

    public ProfilesViewModel Profiles { get; }

    public string ConfigurationStatus => developerOptions.IsClientIdConfigured
        ? "EVE client ID configured"
        : "EVE client ID needs configuration";

    public string SignedInCharacter => AuthenticatedCharacterName ?? "Not signed in";

    public bool HasDetectedFleet => DetectedFleetId.HasValue;

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
        GC.SuppressFinalize(this);
    }

    private async void OnFleetRefreshTimerTick(object? sender, EventArgs e)
    {
        if (isFleetPollingEnabled &&
            string.Equals(SelectedPage, "Live Fleet", StringComparison.Ordinal) &&
            CanRefreshFleet())
        {
            await RefreshFleetAsync();
        }
    }

    private void ApplyLiveFleetResult(LiveFleetLoadResult result)
    {
        FleetHierarchy.Clear();
        IsLiveFleetReady = result.Status == LiveFleetLoadStatus.Ready;

        var snapshot = result.Snapshot;
        if (snapshot is null)
        {
            DetectedFleetId = null;
            LiveFleetBoss = "Not detected";
            LiveFleetSummary = "No fleet data loaded";
            LiveFleetFreshness = result.CheckedAtUtc is DateTimeOffset checkedAtUtc
                ? $"Checked {checkedAtUtc.ToLocalTime():t} • auto-refreshes every 30 seconds while open"
                : "Not refreshed yet";
        }
        else
        {
            DetectedFleetId = snapshot.FleetId;
            LiveFleetBoss = $"{snapshot.FleetBossName} ({snapshot.FleetBossId})";
            LiveFleetFreshness =
                $"Confirmed {snapshot.ConfirmedAtUtc.ToLocalTime():t} • auto-refreshes every 30 seconds while open";
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
        }
    }

    private void ClearLiveFleet()
    {
        FleetHierarchy.Clear();
        DetectedFleetId = null;
        IsLiveFleetReady = false;
        LiveFleetBoss = "Not detected";
        LiveFleetSummary = "No fleet data loaded";
        LiveFleetFreshness = "Not refreshed yet";
        LiveFleetStatusTitle = "Sign in to read a fleet";
        LiveFleetStatusDetail = "Authorize the character that will be fleet boss.";
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
