using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Authentication;
using FleetOrganizer.Infrastructure.Authentication;
using FleetOrganizer.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FleetOrganizer.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IAppDataPaths paths;
    private readonly EveDeveloperOptions developerOptions;
    private readonly IEveAuthenticationService authenticationService;
    private readonly string requiredScopes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTitle))]
    [NotifyPropertyChangedFor(nameof(StatusDetail))]
    [NotifyPropertyChangedFor(nameof(SignedInCharacter))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
    public partial bool IsAuthenticated { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTitle))]
    [NotifyPropertyChangedFor(nameof(StatusDetail))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    [NotifyCanExecuteChangedFor(nameof(SignOutCommand))]
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

    public MainWindowViewModel(
        IAppDataPaths paths,
        IOptions<EveDeveloperOptions> developerOptions,
        IEveAuthenticationService authenticationService)
    {
        this.paths = paths;
        this.developerOptions = developerOptions.Value;
        this.authenticationService = authenticationService;
        requiredScopes = string.Join(Environment.NewLine, EveDeveloperOptions.RequiredScopes);
    }

    public string PageSubtitle => SelectedPage switch
    {
        "Home" => "The quick path for inviting and organising a saved fleet.",
        "Profiles" => "Create reusable wings, squads, assignments, and placement rules.",
        "Live Fleet" => "Inspect and adjust the current EVE fleet hierarchy.",
        "Activity" => "Review resumable operations and per-character results.",
        "Settings" => "Configure EVE SSO, storage, polling, and appearance.",
        _ => string.Empty,
    };

    public string ConfigurationStatus => developerOptions.IsClientIdConfigured
        ? "EVE client ID configured"
        : "EVE client ID needs configuration";

    public string SignedInCharacter => AuthenticatedCharacterName ?? "Not signed in";

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
                ? "Authorization is encrypted for this Windows user and will be refreshed when the app restarts. Read-only live fleet detection is the next slice."
                : "Open Settings and choose Sign in with EVE. Authorize the character that will be fleet boss.";
        }
    }

    public string DatabasePath => paths.DatabasePath;

    private bool CanSignIn() =>
        developerOptions.IsClientIdConfigured &&
        !IsAuthenticated &&
        !IsAuthenticationBusy;

    private bool CanSignOut() => IsAuthenticated && !IsAuthenticationBusy;

    public async Task InitializeAsync()
    {
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
    private void Navigate(string? page)
    {
        if (!string.IsNullOrWhiteSpace(page))
        {
            SelectedPage = page;
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
    }
}
