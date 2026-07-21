using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using FleetOrganizer.Infrastructure.Authentication;
using FleetOrganizer.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FleetOrganizer.Infrastructure.Esi;

internal sealed class EveEsiClient : IDisposable
{
    private static readonly Uri EsiBaseUri = new("https://esi.evetech.net/");
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan CharacterFleetCacheDuration = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan LiveFleetCacheDuration = TimeSpan.FromSeconds(5);

    private readonly HttpClient httpClient;
    private readonly IEveAuthenticationService authenticationService;
    private readonly TimeProvider timeProvider;
    private readonly string compatibilityDate;
    private readonly Dictionary<string, EsiCacheEntry> cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, EsiFailureCacheEntry> failureCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim requestGate = new(1, 1);

    private DateTimeOffset? pauseUntilUtc;

    public EveEsiClient(
        HttpClient httpClient,
        IEveAuthenticationService authenticationService,
        IOptions<EveDeveloperOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(authenticationService);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        this.httpClient = httpClient;
        this.authenticationService = authenticationService;
        this.timeProvider = timeProvider;
        compatibilityDate = options.Value.CompatibilityDate;
    }

    public Task<EsiResult<CharacterFleetResponse>> GetCharacterFleetAsync(
        long characterId,
        CancellationToken cancellationToken) =>
        GetAsync<CharacterFleetResponse>(
            $"characters/{characterId}/fleet",
            CharacterFleetCacheDuration,
            cancellationToken);

    public Task<EsiResult<FleetInfoResponse>> GetFleetAsync(
        long fleetId,
        CancellationToken cancellationToken) =>
        GetAsync<FleetInfoResponse>(
            $"fleets/{fleetId}",
            LiveFleetCacheDuration,
            cancellationToken);

    public Task<EsiResult<EsiEmptyResponse>> UpdateFleetAsync(
        long fleetId,
        UpdateFleetRequest settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return SendWriteAsync(
            () => new HttpRequestMessage(
                HttpMethod.Put,
                new Uri(EsiBaseUri, $"fleets/{fleetId}"))
            {
                Content = JsonContent.Create(settings, options: SerializerOptions),
            },
            fleetId,
            cancellationToken);
    }

    public Task<EsiResult<FleetMemberResponse[]>> GetFleetMembersAsync(
        long fleetId,
        CancellationToken cancellationToken) =>
        GetAsync<FleetMemberResponse[]>(
            $"fleets/{fleetId}/members",
            LiveFleetCacheDuration,
            cancellationToken);

    public Task<EsiResult<FleetWingResponse[]>> GetFleetWingsAsync(
        long fleetId,
        CancellationToken cancellationToken) =>
        GetAsync<FleetWingResponse[]>(
            $"fleets/{fleetId}/wings",
            LiveFleetCacheDuration,
            cancellationToken);

    public Task<EsiResult<EsiEmptyResponse>> InviteFleetMemberAsync(
        long fleetId,
        InviteFleetMemberRequest invitation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(invitation);

        return SendWriteAsync(
            () => new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(EsiBaseUri, $"fleets/{fleetId}/members"))
            {
                Content = JsonContent.Create(invitation, options: SerializerOptions),
            },
            fleetId,
            cancellationToken);
    }

    public Task<EsiResult<EsiEmptyResponse>> MoveFleetMemberAsync(
        long fleetId,
        long characterId,
        MoveFleetMemberRequest placement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(placement);

        return SendWriteAsync(
            () => new HttpRequestMessage(
                HttpMethod.Put,
                new Uri(EsiBaseUri, $"fleets/{fleetId}/members/{characterId}"))
            {
                Content = JsonContent.Create(placement, options: SerializerOptions),
            },
            fleetId,
            cancellationToken);
    }

    public Task<EsiResult<EsiEmptyResponse>> KickFleetMemberAsync(
        long fleetId,
        long characterId,
        CancellationToken cancellationToken) =>
        SendWriteAsync(
            () => new HttpRequestMessage(
                HttpMethod.Delete,
                new Uri(EsiBaseUri, $"fleets/{fleetId}/members/{characterId}")),
            fleetId,
            cancellationToken);

    public Task<EsiResult<EsiEmptyResponse>> TransferFleetBossAsync(
        long fleetId,
        long characterId,
        CancellationToken cancellationToken) =>
        MoveFleetMemberAsync(
            fleetId,
            characterId,
            new MoveFleetMemberRequest("fleet_commander", SquadId: null, WingId: null),
            cancellationToken);

    public Task<EsiResult<CreatedFleetWingResponse>> CreateFleetWingAsync(
        long fleetId,
        CancellationToken cancellationToken) =>
        SendWriteWithResponseAsync<CreatedFleetWingResponse>(
            () => new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(EsiBaseUri, $"fleets/{fleetId}/wings")),
            fleetId,
            cancellationToken);

    public Task<EsiResult<EsiEmptyResponse>> RenameFleetWingAsync(
        long fleetId,
        long wingId,
        RenameFleetStructureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendWriteAsync(
            () => new HttpRequestMessage(
                HttpMethod.Put,
                new Uri(EsiBaseUri, $"fleets/{fleetId}/wings/{wingId}"))
            {
                Content = JsonContent.Create(request, options: SerializerOptions),
            },
            fleetId,
            cancellationToken);
    }

    public Task<EsiResult<CreatedFleetSquadResponse>> CreateFleetSquadAsync(
        long fleetId,
        long wingId,
        CancellationToken cancellationToken) =>
        SendWriteWithResponseAsync<CreatedFleetSquadResponse>(
            () => new HttpRequestMessage(
                HttpMethod.Post,
                new Uri(EsiBaseUri, $"fleets/{fleetId}/wings/{wingId}/squads")),
            fleetId,
            cancellationToken);

    public Task<EsiResult<EsiEmptyResponse>> RenameFleetSquadAsync(
        long fleetId,
        long squadId,
        RenameFleetStructureRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        return SendWriteAsync(
            () => new HttpRequestMessage(
                HttpMethod.Put,
                new Uri(EsiBaseUri, $"fleets/{fleetId}/squads/{squadId}"))
            {
                Content = JsonContent.Create(request, options: SerializerOptions),
            },
            fleetId,
            cancellationToken);
    }

    public Task<EsiResult<EsiEmptyResponse>> DeleteFleetSquadAsync(
        long fleetId,
        long squadId,
        CancellationToken cancellationToken) =>
        SendWriteAsync(
            () => new HttpRequestMessage(
                HttpMethod.Delete,
                new Uri(EsiBaseUri, $"fleets/{fleetId}/squads/{squadId}")),
            fleetId,
            cancellationToken);

    public Task<EsiResult<EsiEmptyResponse>> DeleteFleetWingAsync(
        long fleetId,
        long wingId,
        CancellationToken cancellationToken) =>
        SendWriteAsync(
            () => new HttpRequestMessage(
                HttpMethod.Delete,
                new Uri(EsiBaseUri, $"fleets/{fleetId}/wings/{wingId}")),
            fleetId,
            cancellationToken);

    public async Task<EsiResult<UniverseNameResponse[]>> PostUniverseNamesAsync(
        long[] ids,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);

        if (ids.Length is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ids),
                "ESI name resolution accepts between 1 and 1000 IDs.");
        }

        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendAsync<UniverseNameResponse[]>(
                () => new HttpRequestMessage(HttpMethod.Post, new Uri(EsiBaseUri, "universe/names"))
                {
                    Content = JsonContent.Create(ids, options: SerializerOptions),
                },
                authenticated: false,
                cachedEntry: null,
                fallbackCacheDuration: TimeSpan.Zero,
                allowAutomaticRetry: true,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            requestGate.Release();
        }
    }

    public async Task<EsiResult<UniverseIdsResponse>> PostUniverseIdsAsync(
        string[] names,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (names.Length is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(
                nameof(names),
                "ESI name resolution accepts between 1 and 500 names.");
        }

        if (names.Any(name => string.IsNullOrWhiteSpace(name) || name.Length > 100))
        {
            throw new ArgumentException(
                "Each ESI name must contain between 1 and 100 characters.",
                nameof(names));
        }

        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SendAsync<UniverseIdsResponse>(
                () => new HttpRequestMessage(HttpMethod.Post, new Uri(EsiBaseUri, "universe/ids"))
                {
                    Content = JsonContent.Create(names, options: SerializerOptions),
                },
                authenticated: false,
                cachedEntry: null,
                fallbackCacheDuration: TimeSpan.Zero,
                allowAutomaticRetry: true,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            requestGate.Release();
        }
    }

    public void InvalidateCharacterFleet(long characterId)
    {
        var path = $"characters/{characterId}/fleet";
        cache.Remove(path);
        failureCache.Remove(path);
    }

    public void InvalidateLiveFleet(long fleetId)
    {
        foreach (var path in new[]
        {
            $"fleets/{fleetId}",
            $"fleets/{fleetId}/members",
            $"fleets/{fleetId}/wings",
        })
        {
            cache.Remove(path);
            failureCache.Remove(path);
        }
    }

    public void Dispose()
    {
        requestGate.Dispose();
    }

    private async Task<EsiResult<T>> GetAsync<T>(
        string relativePath,
        TimeSpan fallbackCacheDuration,
        CancellationToken cancellationToken)
    {
        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = timeProvider.GetUtcNow();
            if (GetCachedFailure<T>(relativePath, now) is { } cachedFailure)
            {
                return cachedFailure;
            }

            var cachedEntry = GetTypedCacheEntry<T>(relativePath);
            if (cachedEntry is not null && cachedEntry.ExpiresAtUtc > now)
            {
                return CreateCachedResult<T>(cachedEntry);
            }

            var result = await SendAsync<T>(
                () => CreateGetRequest(relativePath, cachedEntry),
                authenticated: true,
                cachedEntry,
                fallbackCacheDuration,
                allowAutomaticRetry: true,
                cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess && result.Value is not null)
            {
                failureCache.Remove(relativePath);
                cache[relativePath] = new EsiCacheEntry(
                    result.Value,
                    result.FetchedAtUtc,
                    result.ExpiresAtUtc ?? result.FetchedAtUtc.Add(fallbackCacheDuration),
                    result.ETag,
                    result.LastModifiedUtc);
            }
            else if (CanCacheFailure(result.FailureKind))
            {
                cache.Remove(relativePath);
                failureCache[relativePath] = new EsiFailureCacheEntry(
                    result,
                    result.ExpiresAtUtc ?? result.FetchedAtUtc.Add(fallbackCacheDuration));
            }

            return result;
        }
        finally
        {
            requestGate.Release();
        }
    }

    private async Task<EsiResult<EsiEmptyResponse>> SendWriteAsync(
        Func<HttpRequestMessage> requestFactory,
        long fleetId,
        CancellationToken cancellationToken) =>
        await SendWriteWithResponseAsync<EsiEmptyResponse>(
            requestFactory,
            fleetId,
            cancellationToken).ConfigureAwait(false);

    private async Task<EsiResult<T>> SendWriteWithResponseAsync<T>(
        Func<HttpRequestMessage> requestFactory,
        long fleetId,
        CancellationToken cancellationToken)
    {
        await requestGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await SendAsync<T>(
                requestFactory,
                authenticated: true,
                cachedEntry: null,
                fallbackCacheDuration: TimeSpan.Zero,
                allowAutomaticRetry: false,
                cancellationToken).ConfigureAwait(false);
            if (result.IsSuccess)
            {
                InvalidateLiveFleet(fleetId);
            }

            return result;
        }
        finally
        {
            requestGate.Release();
        }
    }

    private async Task<EsiResult<T>> SendAsync<T>(
        Func<HttpRequestMessage> requestFactory,
        bool authenticated,
        EsiCacheEntry? cachedEntry,
        TimeSpan fallbackCacheDuration,
        bool allowAutomaticRetry,
        CancellationToken cancellationToken)
    {
        string? refreshedAccessToken = null;
        var hasRefreshedAuthorization = false;
        var maximumAttempts = allowAutomaticRetry ? 3 : 1;

        for (var attempt = 1; attempt <= maximumAttempts; attempt++)
        {
            var pausedResult = CreatePausedResult<T>();
            if (pausedResult is not null)
            {
                return pausedResult;
            }

            using var request = requestFactory();
            request.Headers.Add("X-Compatibility-Date", compatibilityDate);
            request.Headers.AcceptLanguage.ParseAdd("en");

            if (authenticated)
            {
                string accessToken;
                try
                {
                    accessToken = refreshedAccessToken ?? await authenticationService
                        .GetAccessTokenAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception exception) when (IsAuthenticationFailure(exception))
                {
                    return CreateAuthorizationFailure<T>();
                }

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            }

            HttpResponseMessage response;
            try
            {
                response = await httpClient
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < maximumAttempts)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested && attempt < maximumAttempts)
            {
                await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                continue;
            }
            catch (HttpRequestException)
            {
                return CreateTransportFailure<T>(
                    allowAutomaticRetry
                        ? "ESI could not be reached after three attempts. Check your connection and try again."
                        : "ESI could not be reached. This write was not automatically replayed because its outcome may be unknown; refresh the live fleet before retrying.");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return CreateTransportFailure<T>(
                    allowAutomaticRetry
                        ? "ESI timed out after three attempts. Try again shortly."
                        : "The ESI write timed out and was not automatically replayed. Refresh the live fleet before retrying.");
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized &&
                    authenticated &&
                    !hasRefreshedAuthorization)
                {
                    try
                    {
                        refreshedAccessToken = await authenticationService
                            .RefreshAccessTokenAsync(cancellationToken)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception) when (IsAuthenticationFailure(exception))
                    {
                        return CreateAuthorizationFailure<T>(response);
                    }

                    hasRefreshedAuthorization = true;
                    attempt--;
                    continue;
                }

                if ((int)response.StatusCode >= 500 && attempt < maximumAttempts)
                {
                    await DelayBeforeRetryAsync(attempt, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var now = timeProvider.GetUtcNow();
                var expiresAtUtc = GetExpiresAtUtc(response, now, fallbackCacheDuration);
                var rateState = GetRateState(response);
                var retryAfter = GetRetryAfter(response, now);

                if (response.StatusCode == HttpStatusCode.NotModified && cachedEntry is not null)
                {
                    return new EsiResult<T>(
                        (T)cachedEntry.Value,
                        HttpStatusCode.OK,
                        EsiFailureKind.None,
                        null,
                        GetRequestId(response),
                        now,
                        expiresAtUtc,
                        cachedEntry.ETag,
                        cachedEntry.LastModifiedUtc,
                        null,
                        rateState,
                        FromCache: true);
                }

                if (!response.IsSuccessStatusCode)
                {
                    ApplyPause(response.StatusCode, retryAfter, rateState);
                    return CreateFailure<T>(response, now, expiresAtUtc, retryAfter, rateState);
                }

                try
                {
                    if (typeof(T) == typeof(EsiEmptyResponse))
                    {
                        return new EsiResult<T>(
                            (T)(object)new EsiEmptyResponse(),
                            response.StatusCode,
                            EsiFailureKind.None,
                            null,
                            GetRequestId(response),
                            now,
                            expiresAtUtc,
                            response.Headers.ETag?.Tag,
                            response.Content.Headers.LastModified,
                            null,
                            rateState,
                            FromCache: false);
                    }

                    await using var contentStream = await response.Content
                        .ReadAsStreamAsync(cancellationToken)
                        .ConfigureAwait(false);
                    var value = await JsonSerializer
                        .DeserializeAsync<T>(contentStream, SerializerOptions, cancellationToken)
                        .ConfigureAwait(false);

                    if (value is null)
                    {
                        return CreateInvalidResponse<T>(response, now, expiresAtUtc, rateState);
                    }

                    return new EsiResult<T>(
                        value,
                        response.StatusCode,
                        EsiFailureKind.None,
                        null,
                        GetRequestId(response),
                        now,
                        expiresAtUtc,
                        response.Headers.ETag?.Tag,
                        response.Content.Headers.LastModified,
                        null,
                        rateState,
                        FromCache: false);
                }
                catch (JsonException)
                {
                    return CreateInvalidResponse<T>(response, now, expiresAtUtc, rateState);
                }
            }
        }

        return CreateTransportFailure<T>(
            allowAutomaticRetry
                ? "ESI request failed after three attempts."
                : "The ESI write failed without an automatic replay.");
    }

    private static HttpRequestMessage CreateGetRequest(
        string relativePath,
        EsiCacheEntry? cachedEntry)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, new Uri(EsiBaseUri, relativePath));
        if (cachedEntry is null)
        {
            return request;
        }

        if (!string.IsNullOrWhiteSpace(cachedEntry.ETag) &&
            EntityTagHeaderValue.TryParse(cachedEntry.ETag, out var entityTag))
        {
            request.Headers.IfNoneMatch.Add(entityTag);
        }

        request.Headers.IfModifiedSince = cachedEntry.LastModifiedUtc;
        return request;
    }

    private EsiCacheEntry? GetTypedCacheEntry<T>(string relativePath)
    {
        if (cache.TryGetValue(relativePath, out var entry) && entry.Value is T)
        {
            return entry;
        }

        return null;
    }

    private EsiResult<T>? GetCachedFailure<T>(string relativePath, DateTimeOffset now)
    {
        if (!failureCache.TryGetValue(relativePath, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAtUtc <= now)
        {
            failureCache.Remove(relativePath);
            return null;
        }

        return entry.Result is EsiResult<T> result
            ? result with { FromCache = true }
            : null;
    }

    private static EsiResult<T> CreateCachedResult<T>(EsiCacheEntry entry) =>
        new(
            (T)entry.Value,
            HttpStatusCode.OK,
            EsiFailureKind.None,
            null,
            null,
            entry.FetchedAtUtc,
            entry.ExpiresAtUtc,
            entry.ETag,
            entry.LastModifiedUtc,
            null,
            null,
            FromCache: true);

    private EsiResult<T>? CreatePausedResult<T>()
    {
        var now = timeProvider.GetUtcNow();
        if (pauseUntilUtc is null || pauseUntilUtc <= now)
        {
            pauseUntilUtc = null;
            return null;
        }

        var retryAfter = pauseUntilUtc.Value - now;
        return new EsiResult<T>(
            default,
            (HttpStatusCode)429,
            EsiFailureKind.Paused,
            $"ESI has paused requests for another {Math.Ceiling(retryAfter.TotalSeconds):0} seconds.",
            null,
            now,
            null,
            null,
            null,
            retryAfter,
            null,
            FromCache: false);
    }

    private EsiResult<T> CreateAuthorizationFailure<T>(HttpResponseMessage? response = null)
    {
        var now = timeProvider.GetUtcNow();
        return new EsiResult<T>(
            default,
            HttpStatusCode.Unauthorized,
            EsiFailureKind.Unauthorized,
            "EVE authorization expired and could not be refreshed. Sign in again.",
            response is null ? null : GetRequestId(response),
            now,
            null,
            null,
            null,
            null,
            response is null ? null : GetRateState(response),
            FromCache: false);
    }

    private static EsiResult<T> CreateFailure<T>(
        HttpResponseMessage response,
        DateTimeOffset now,
        DateTimeOffset? expiresAtUtc,
        TimeSpan? retryAfter,
        EsiRateState? rateState)
    {
        var failureKind = MapFailureKind(response.StatusCode);
        return new EsiResult<T>(
            default,
            response.StatusCode,
            failureKind,
            GetUserMessage(failureKind, response.StatusCode, retryAfter),
            GetRequestId(response),
            now,
            expiresAtUtc,
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified,
            retryAfter,
            rateState,
            FromCache: false);
    }

    private EsiResult<T> CreateTransportFailure<T>(string message)
    {
        var now = timeProvider.GetUtcNow();
        return new EsiResult<T>(
            default,
            HttpStatusCode.ServiceUnavailable,
            EsiFailureKind.Network,
            message,
            null,
            now,
            null,
            null,
            null,
            null,
            null,
            FromCache: false);
    }

    private static EsiResult<T> CreateInvalidResponse<T>(
        HttpResponseMessage response,
        DateTimeOffset now,
        DateTimeOffset? expiresAtUtc,
        EsiRateState? rateState) =>
        new(
            default,
            response.StatusCode,
            EsiFailureKind.InvalidResponse,
            "ESI returned an unreadable response. Update Fleet Organizer or try again shortly.",
            GetRequestId(response),
            now,
            expiresAtUtc,
            response.Headers.ETag?.Tag,
            response.Content.Headers.LastModified,
            null,
            rateState,
            FromCache: false);

    private void ApplyPause(
        HttpStatusCode statusCode,
        TimeSpan? retryAfter,
        EsiRateState? rateState)
    {
        if ((int)statusCode is not 420 and not 429)
        {
            return;
        }

        var duration = retryAfter ??
            (rateState?.ErrorLimitResetSeconds is int seconds
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromSeconds(60));
        pauseUntilUtc = timeProvider.GetUtcNow().Add(duration);
    }

    private async Task DelayBeforeRetryAsync(int attempt, CancellationToken cancellationToken)
    {
        var jitterMilliseconds = RandomNumberGenerator.GetInt32(50, 151);
        var delay = TimeSpan.FromMilliseconds(
            (250 * Math.Pow(2, attempt - 1)) + jitterMilliseconds);
        await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
    }

    private static EsiFailureKind MapFailureKind(HttpStatusCode statusCode) =>
        (int)statusCode switch
        {
            401 => EsiFailureKind.Unauthorized,
            403 => EsiFailureKind.Forbidden,
            404 => EsiFailureKind.NotFound,
            420 => EsiFailureKind.ErrorLimited,
            422 => EsiFailureKind.Validation,
            429 => EsiFailureKind.RateLimited,
            >= 500 => EsiFailureKind.Server,
            _ => EsiFailureKind.Client,
        };

    private static bool CanCacheFailure(EsiFailureKind failureKind) =>
        failureKind is
            EsiFailureKind.Forbidden or
            EsiFailureKind.NotFound or
            EsiFailureKind.Validation or
            EsiFailureKind.Client;

    private static bool IsAuthenticationFailure(Exception exception) =>
        exception is
            InvalidOperationException or
            HttpRequestException or
            CryptographicException or
            SecurityTokenException;

    private static string GetUserMessage(
        EsiFailureKind failureKind,
        HttpStatusCode statusCode,
        TimeSpan? retryAfter) =>
        failureKind switch
        {
            EsiFailureKind.Unauthorized =>
                "EVE authorization expired and could not be refreshed. Sign in again.",
            EsiFailureKind.Forbidden =>
                "EVE denied this fleet request. Confirm this character is fleet boss and approved both fleet permissions.",
            EsiFailureKind.NotFound =>
                "The fleet was not found. It may have changed or ended.",
            EsiFailureKind.ErrorLimited =>
                $"ESI paused requests after too many errors. Retrying in {FormatRetryAfter(retryAfter)}.",
            EsiFailureKind.RateLimited =>
                $"ESI is rate limiting requests. Retrying in {FormatRetryAfter(retryAfter)}.",
            EsiFailureKind.Validation =>
                "ESI rejected the request because the current fleet state is no longer valid.",
            EsiFailureKind.Server =>
                "ESI is temporarily unavailable after three attempts. Try again shortly.",
            _ => $"ESI rejected the request ({(int)statusCode}).",
        };

    private static string FormatRetryAfter(TimeSpan? retryAfter) =>
        retryAfter is null
            ? "about 60 seconds"
            : $"{Math.Max(1, Math.Ceiling(retryAfter.Value.TotalSeconds)):0} seconds";

    private static DateTimeOffset GetExpiresAtUtc(
        HttpResponseMessage response,
        DateTimeOffset now,
        TimeSpan fallbackCacheDuration) =>
        response.Content.Headers.Expires ?? now.Add(fallbackCacheDuration);

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
        {
            return delta;
        }

        if (response.Headers.RetryAfter?.Date is DateTimeOffset date)
        {
            return date > now ? date - now : TimeSpan.Zero;
        }

        if (TryGetIntHeader(response, "X-ESI-Error-Limit-Reset") is int resetSeconds)
        {
            return TimeSpan.FromSeconds(resetSeconds);
        }

        return null;
    }

    private static EsiRateState? GetRateState(HttpResponseMessage response)
    {
        var group = GetHeader(response, "X-Ratelimit-Group");
        var limit = GetHeader(response, "X-Ratelimit-Limit");
        var remaining = TryGetIntHeader(response, "X-Ratelimit-Remaining");
        var used = TryGetIntHeader(response, "X-Ratelimit-Used");
        var errorRemaining = TryGetIntHeader(response, "X-ESI-Error-Limit-Remain");
        var errorReset = TryGetIntHeader(response, "X-ESI-Error-Limit-Reset");

        return group is null &&
            limit is null &&
            remaining is null &&
            used is null &&
            errorRemaining is null &&
            errorReset is null
            ? null
            : new EsiRateState(group, limit, remaining, used, errorRemaining, errorReset);
    }

    private static string? GetRequestId(HttpResponseMessage response) =>
        GetHeader(response, "X-Request-ID") ?? GetHeader(response, "X-Esi-Request-Id");

    private static int? TryGetIntHeader(HttpResponseMessage response, string name)
    {
        var value = GetHeader(response, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : null;
    }

    private static string? GetHeader(HttpResponseMessage response, string name)
    {
        if (response.Headers.TryGetValues(name, out var values))
        {
            return values.FirstOrDefault();
        }

        return response.Content.Headers.TryGetValues(name, out values)
            ? values.FirstOrDefault()
            : null;
    }

    private sealed record EsiCacheEntry(
        object Value,
        DateTimeOffset FetchedAtUtc,
        DateTimeOffset ExpiresAtUtc,
        string? ETag,
        DateTimeOffset? LastModifiedUtc);

    private sealed record EsiFailureCacheEntry(
        object Result,
        DateTimeOffset ExpiresAtUtc);
}
