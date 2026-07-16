using System.Diagnostics;
using System.Net;
using System.Text.Json;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Authentication;
using FleetOrganizer.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace FleetOrganizer.Infrastructure.Authentication;

internal sealed class EveAuthenticationService : IEveAuthenticationService, IDisposable
{
    private static readonly Uri MetadataEndpoint =
        new("https://login.eveonline.com/.well-known/oauth-authorization-server");

    private readonly HttpClient httpClient;
    private readonly EveDeveloperOptions options;
    private readonly ISecretProtector secretProtector;
    private readonly AuthenticatedCharacterRepository repository;
    private readonly EveJwtValidator jwtValidator;
    private readonly TimeProvider timeProvider;
    private readonly SemaphoreSlim sessionGate = new(1, 1);

    private string? accessToken;
    private DateTimeOffset accessTokenExpiresUtc;
    private AuthenticatedCharacter? currentCharacter;

    public EveAuthenticationService(
        HttpClient httpClient,
        IOptions<EveDeveloperOptions> options,
        ISecretProtector secretProtector,
        AuthenticatedCharacterRepository repository,
        EveJwtValidator jwtValidator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secretProtector);
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(jwtValidator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.httpClient = httpClient;
        this.options = options.Value;
        this.secretProtector = secretProtector;
        this.repository = repository;
        this.jwtValidator = jwtValidator;
        this.timeProvider = timeProvider;
    }

    public AuthenticatedCharacter? CurrentCharacter => currentCharacter;

    public async Task<AuthenticatedCharacter?> RestoreSessionAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigurationIsValid();
        await sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var storedCharacter = await repository
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
            if (storedCharacter is null)
            {
                return null;
            }

            var refreshToken = secretProtector.Unprotect(storedCharacter.EncryptedRefreshToken);
            var refreshedSession = await RefreshAsync(refreshToken, cancellationToken)
                .ConfigureAwait(false);

            if (refreshedSession.ValidatedToken.Character.CharacterId != storedCharacter.CharacterId)
            {
                throw new InvalidOperationException(
                    "The saved EVE authorization returned a different character. Sign in again.");
            }

            await PersistAndActivateAsync(refreshedSession, refreshToken, cancellationToken)
                .ConfigureAwait(false);
            return currentCharacter;
        }
        finally
        {
            sessionGate.Release();
        }
    }

    public async Task<AuthenticatedCharacter> SignInAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureConfigurationIsValid();
        await sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var metadata = await GetMetadataAsync(cancellationToken).ConfigureAwait(false);
            var redirectUri = options.GetValidatedRedirectUri();
            var pkce = PkceGenerator.Create();
            var state = PkceGenerator.CreateState();
            var authorizationUri = CreateAuthorizationUri(
                metadata,
                redirectUri,
                pkce.Challenge,
                state);

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(TimeSpan.FromMinutes(5));
            var callbackTask = LoopbackAuthorizationListener.WaitForCodeAsync(
                redirectUri,
                state,
                timeoutSource.Token);

            OpenSystemBrowser(authorizationUri);

            string authorizationCode;
            try
            {
                authorizationCode = await callbackTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new InvalidOperationException(
                    "EVE sign-in timed out. Start sign-in again when you are ready.");
            }

            var tokenResponse = await RequestTokenAsync(
                metadata.TokenEndpoint,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["grant_type"] = "authorization_code",
                    ["code"] = authorizationCode,
                    ["client_id"] = options.ClientId,
                    ["code_verifier"] = pkce.Verifier,
                },
                refreshTokenRequired: true,
                cancellationToken).ConfigureAwait(false);

            var validatedToken = await jwtValidator.ValidateAsync(
                tokenResponse.AccessToken,
                metadata,
                options.ClientId,
                EveDeveloperOptions.RequiredScopes,
                cancellationToken).ConfigureAwait(false);
            var session = new AuthenticationSession(tokenResponse, validatedToken);

            await PersistAndActivateAsync(
                session,
                tokenResponse.RefreshToken!,
                cancellationToken).ConfigureAwait(false);
            return currentCharacter!;
        }
        finally
        {
            sessionGate.Release();
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await repository.DeleteAllAsync(cancellationToken).ConfigureAwait(false);
            ClearInMemorySession();
        }
        finally
        {
            sessionGate.Release();
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await sessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var character = currentCharacter;
            var currentAccessToken = accessToken;
            if (character is null || string.IsNullOrWhiteSpace(currentAccessToken))
            {
                throw new InvalidOperationException("Sign in with EVE before accessing the fleet.");
            }

            if (accessTokenExpiresUtc > timeProvider.GetUtcNow().AddMinutes(2))
            {
                return currentAccessToken;
            }

            var storedCharacter = await repository
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false) ?? throw new InvalidOperationException(
                    "The saved EVE authorization is missing. Sign in again.");
            var refreshToken = secretProtector.Unprotect(storedCharacter.EncryptedRefreshToken);
            var refreshedSession = await RefreshAsync(refreshToken, cancellationToken)
                .ConfigureAwait(false);

            if (refreshedSession.ValidatedToken.Character.CharacterId != character.CharacterId)
            {
                ClearInMemorySession();
                throw new InvalidOperationException(
                    "EVE returned an unexpected character while refreshing authorization.");
            }

            await PersistAndActivateAsync(refreshedSession, refreshToken, cancellationToken)
                .ConfigureAwait(false);
            return accessToken!;
        }
        finally
        {
            sessionGate.Release();
        }
    }

    public void Dispose()
    {
        ClearInMemorySession();
        sessionGate.Dispose();
    }

    private async Task<AuthenticationSession> RefreshAsync(
        string existingRefreshToken,
        CancellationToken cancellationToken)
    {
        var metadata = await GetMetadataAsync(cancellationToken).ConfigureAwait(false);
        var tokenResponse = await RequestTokenAsync(
            metadata.TokenEndpoint,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = existingRefreshToken,
                ["client_id"] = options.ClientId,
            },
            refreshTokenRequired: false,
            cancellationToken).ConfigureAwait(false);
        var validatedToken = await jwtValidator.ValidateAsync(
            tokenResponse.AccessToken,
            metadata,
            options.ClientId,
            EveDeveloperOptions.RequiredScopes,
            cancellationToken).ConfigureAwait(false);

        return new AuthenticationSession(tokenResponse, validatedToken);
    }

    private async Task PersistAndActivateAsync(
        AuthenticationSession session,
        string fallbackRefreshToken,
        CancellationToken cancellationToken)
    {
        var refreshToken = string.IsNullOrWhiteSpace(session.TokenResponse.RefreshToken)
            ? fallbackRefreshToken
            : session.TokenResponse.RefreshToken!;
        var encryptedRefreshToken = secretProtector.Protect(refreshToken);

        try
        {
            await repository.SaveAsync(
                session.ValidatedToken.Character,
                encryptedRefreshToken,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(encryptedRefreshToken);
        }

        accessToken = session.TokenResponse.AccessToken;
        accessTokenExpiresUtc = session.ValidatedToken.ExpiresUtc;
        currentCharacter = session.ValidatedToken.Character;
    }

    private async Task<EveSsoMetadata> GetMetadataAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient
            .GetAsync(MetadataEndpoint, cancellationToken)
            .ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"EVE SSO discovery failed ({(int)response.StatusCode}). Try again shortly.");
        }

        await using var contentStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument
            .ParseAsync(contentStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;

        return new EveSsoMetadata(
            GetTrustedEveUri(root, "authorization_endpoint"),
            GetTrustedEveUri(root, "token_endpoint"),
            GetTrustedEveUri(root, "jwks_uri"));
    }

    private async Task<EveTokenResponse> RequestTokenAsync(
        Uri tokenEndpoint,
        Dictionary<string, string> values,
        bool refreshTokenRequired,
        CancellationToken cancellationToken)
    {
        using var content = new FormUrlEncodedContent(values);
        using var response = await httpClient
            .PostAsync(tokenEndpoint, content, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized
                ? "EVE authorization was rejected. Sign in again and approve both fleet permissions."
                : $"EVE SSO token exchange failed ({(int)response.StatusCode}). Try again shortly.";
            throw new InvalidOperationException(message);
        }

        await using var contentStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        using var document = await JsonDocument
            .ParseAsync(contentStream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var root = document.RootElement;
        var returnedAccessToken = GetRequiredString(root, "access_token");
        var returnedRefreshToken = GetOptionalString(root, "refresh_token");

        if (refreshTokenRequired && string.IsNullOrWhiteSpace(returnedRefreshToken))
        {
            throw new InvalidOperationException("EVE SSO did not return a refresh token.");
        }

        return new EveTokenResponse(returnedAccessToken, returnedRefreshToken);
    }

    private Uri CreateAuthorizationUri(
        EveSsoMetadata metadata,
        Uri redirectUri,
        string codeChallenge,
        string state)
    {
        var queryValues = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["response_type"] = "code",
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["client_id"] = options.ClientId,
            ["scope"] = string.Join(' ', EveDeveloperOptions.RequiredScopes),
            ["state"] = state,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
        };
        var query = string.Join(
            '&',
            queryValues.Select(pair =>
                $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}"));
        var builder = new UriBuilder(metadata.AuthorizationEndpoint)
        {
            Query = query,
        };

        return builder.Uri;
    }

    private static Uri GetTrustedEveUri(JsonElement root, string propertyName)
    {
        var value = GetRequiredString(root, propertyName);
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, "login.eveonline.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"EVE SSO discovery returned an invalid {propertyName}.");
        }

        return uri;
    }

    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        var value = GetOptionalString(root, propertyName);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"EVE SSO response omitted {propertyName}.")
            : value;
    }

    private static string? GetOptionalString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static void OpenSystemBrowser(Uri authorizationUri)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = authorizationUri.AbsoluteUri,
            UseShellExecute = true,
        });

        if (process is null)
        {
            throw new InvalidOperationException("The system browser could not be opened.");
        }
    }

    private void EnsureConfigurationIsValid()
    {
        if (!options.IsClientIdConfigured)
        {
            throw new InvalidOperationException(
                "Add the public EVE client ID to appsettings.Local.json before signing in.");
        }

        _ = options.GetValidatedRedirectUri();
    }

    private void ClearInMemorySession()
    {
        accessToken = null;
        accessTokenExpiresUtc = default;
        currentCharacter = null;
    }

    private sealed record AuthenticationSession(
        EveTokenResponse TokenResponse,
        ValidatedEveToken ValidatedToken);
}
