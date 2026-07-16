using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FleetOrganizer.Core.Authentication;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Infrastructure.Authentication;
using FleetOrganizer.Infrastructure.Configuration;
using FleetOrganizer.Infrastructure.Esi;
using Microsoft.Extensions.Options;

namespace FleetOrganizer.Infrastructure.Tests.Esi;

public sealed class EveEsiClientTests
{
    [Fact]
    public async Task FleetDetectionSendsAuthorizationAndCompatibilityDateAndUsesFreshCache()
    {
        var requestCount = 0;
        string? authorization = null;
        string? compatibilityDate = null;
        using var httpClient = new HttpClient(new DelegateHandler(request =>
        {
            requestCount++;
            authorization = request.Headers.Authorization?.ToString();
            compatibilityDate = request.Headers.GetValues("X-Compatibility-Date").Single();
            return CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {
                  "fleet_boss_id": 9001,
                  "fleet_id": 7001,
                  "role": "fleet_commander",
                  "squad_id": -1,
                  "wing_id": -1
                }
                """);
        }));
        var authentication = new TestAuthenticationService();
        using var client = CreateClient(httpClient, authentication);

        var first = await client.GetCharacterFleetAsync(9001, CancellationToken.None);
        var second = await client.GetCharacterFleetAsync(9001, CancellationToken.None);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.True(second.FromCache);
        Assert.Equal(7_001L, second.Value!.FleetId);
        Assert.Equal(1, requestCount);
        Assert.Equal("Bearer access-token", authorization);
        Assert.Equal("2026-07-15", compatibilityDate);
    }

    [Fact]
    public async Task UnauthorizedResponseRefreshesAndRetriesExactlyOnce()
    {
        var requestCount = 0;
        var authorizationValues = new List<string?>();
        using var httpClient = new HttpClient(new DelegateHandler(request =>
        {
            requestCount++;
            authorizationValues.Add(request.Headers.Authorization?.Parameter);
            return requestCount == 1
                ? CreateJsonResponse(HttpStatusCode.Unauthorized, "{\"error\":\"token expired\"}")
                : CreateJsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "fleet_boss_id": 9001,
                      "fleet_id": 7001,
                      "role": "fleet_commander",
                      "squad_id": -1,
                      "wing_id": -1
                    }
                    """);
        }));
        var authentication = new TestAuthenticationService();
        using var client = CreateClient(httpClient, authentication);

        var result = await client.GetCharacterFleetAsync(9001, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, requestCount);
        Assert.Equal(1, authentication.RefreshCount);
        Assert.Collection(
            authorizationValues,
            value => Assert.Equal("access-token", value),
            value => Assert.Equal("refreshed-token", value));
    }

    [Fact]
    public async Task ErrorLimitResponsePausesSubsequentRequestsWithoutCallingEsiAgain()
    {
        var requestCount = 0;
        using var httpClient = new HttpClient(new DelegateHandler(_ =>
        {
            requestCount++;
            var response = CreateJsonResponse(
                (HttpStatusCode)420,
                "{\"error\":\"error limited\"}");
            response.Headers.Add("X-ESI-Error-Limit-Reset", "17");
            return response;
        }));
        using var client = CreateClient(httpClient, new TestAuthenticationService());

        var first = await client.GetCharacterFleetAsync(9001, CancellationToken.None);
        var second = await client.GetCharacterFleetAsync(9001, CancellationToken.None);

        Assert.Equal(EsiFailureKind.ErrorLimited, first.FailureKind);
        Assert.Equal(EsiFailureKind.Paused, second.FailureKind);
        Assert.Equal(1, requestCount);
        Assert.True(second.RetryAfter is { } retryAfter && retryAfter > TimeSpan.Zero);
    }

    [Fact]
    public async Task CharacterNameResolutionUsesPublicUniverseIdsEndpointWithoutAuthorization()
    {
        string? requestBody = null;
        string? authorization = "not-observed";
        string? compatibilityDate = null;
        using var httpClient = new HttpClient(new AsyncDelegateHandler(async (
            request,
            cancellationToken) =>
        {
            requestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            authorization = request.Headers.Authorization?.ToString();
            compatibilityDate = request.Headers.GetValues("X-Compatibility-Date").Single();
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/universe/ids", request.RequestUri?.AbsolutePath);
            return CreateJsonResponse(
                HttpStatusCode.OK,
                """
                {"characters":[{"id":9001,"name":"Exact Pilot"}]}
                """);
        }));
        using var client = CreateClient(httpClient, new TestAuthenticationService());
        var names = new[] { "Exact Pilot", "Missing Pilot" };

        var result = await client.PostUniverseIdsAsync(names, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var character = Assert.Single(result.Value!.Characters!);
        Assert.Equal(9001, character.Id);
        Assert.Equal("Exact Pilot", character.Name);
        Assert.Null(authorization);
        Assert.Equal("2026-07-15", compatibilityDate);
        var capturedBody = Assert.IsType<string>(requestBody);
        Assert.Contains("Exact Pilot", capturedBody, StringComparison.Ordinal);
        Assert.Contains("Missing Pilot", capturedBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FleetServiceBuildsNamedReadOnlySnapshotFromCurrentOpenApiContracts()
    {
        using var httpClient = new HttpClient(new DelegateHandler(request =>
            request.RequestUri?.AbsolutePath switch
            {
                "/characters/9001/fleet" => CreateJsonResponse(
                    HttpStatusCode.OK,
                    """
                    {"fleet_boss_id":9001,"fleet_id":7001,"role":"fleet_commander","squad_id":-1,"wing_id":-1}
                    """),
                "/fleets/7001" => CreateJsonResponse(
                    HttpStatusCode.OK,
                    """
                    {"is_free_move":true,"is_registered":false,"is_voice_enabled":false,"motd":"Welcome"}
                    """),
                "/fleets/7001/members" => CreateJsonResponse(
                    HttpStatusCode.OK,
                    """
                    [
                      {"character_id":9001,"join_time":"2026-07-16T10:00:00Z","role":"fleet_commander","role_name":"Fleet Commander","ship_type_id":587,"solar_system_id":30000142,"squad_id":-1,"takes_fleet_warp":true,"wing_id":-1},
                      {"character_id":9002,"join_time":"2026-07-16T10:01:00Z","role":"squad_member","role_name":"Squad Member","ship_type_id":587,"solar_system_id":30000142,"squad_id":20,"takes_fleet_warp":true,"wing_id":10}
                    ]
                    """),
                "/fleets/7001/wings" => CreateJsonResponse(
                    HttpStatusCode.OK,
                    """
                    [{"id":10,"name":"Main Wing","squads":[{"id":20,"name":"Squad 1"}]}]
                    """),
                "/universe/names" => CreateJsonResponse(
                    HttpStatusCode.OK,
                    """
                    [
                      {"category":"character","id":9002,"name":"Second Pilot"},
                      {"category":"inventory_type","id":587,"name":"Rifter"},
                      {"category":"solar_system","id":30000142,"name":"Jita"}
                    ]
                    """),
                _ => CreateJsonResponse(HttpStatusCode.NotFound, "{\"error\":\"not found\"}"),
            }));
        var authentication = new TestAuthenticationService();
        using var client = CreateClient(httpClient, authentication);
        using var service = new EveFleetService(client, authentication);

        var result = await service.LoadCurrentAsync(CancellationToken.None);

        Assert.Equal(LiveFleetLoadStatus.Ready, result.Status);
        var snapshot = Assert.IsType<LiveFleetSnapshot>(result.Snapshot);
        Assert.True(snapshot.IsFleetBoss);
        Assert.True(snapshot.IsFreeMove);
        Assert.Equal("Fleet Boss", snapshot.FleetBossName);
        Assert.Equal("Main Wing", Assert.Single(snapshot.Wings).Name);
        var secondPilot = Assert.Single(snapshot.Members, member => member.CharacterId == 9002);
        Assert.Equal("Second Pilot", secondPilot.CharacterName);
        Assert.Equal("Rifter", secondPilot.ShipTypeName);
        Assert.Equal("Jita", secondPilot.SolarSystemName);
    }

    private static EveEsiClient CreateClient(
        HttpClient httpClient,
        TestAuthenticationService authenticationService) =>
        new(
            httpClient,
            authenticationService,
            Options.Create(new EveDeveloperOptions
            {
                ClientId = "test-client",
                CompatibilityDate = "2026-07-15",
            }),
            TimeProvider.System);

    private static HttpResponseMessage CreateJsonResponse(
        HttpStatusCode statusCode,
        string json)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        response.Content.Headers.Expires = DateTimeOffset.UtcNow.AddMinutes(1);
        response.Headers.ETag = new EntityTagHeaderValue("\"test-etag\"");
        response.Headers.Add("X-Ratelimit-Group", "fleet");
        response.Headers.Add("X-Ratelimit-Limit", "1800/15m");
        response.Headers.Add("X-Ratelimit-Remaining", "1798");
        response.Headers.Add("X-Ratelimit-Used", "2");
        return response;
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(send(request));
        }
    }

    private sealed class AsyncDelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) :
        HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            send(request, cancellationToken);
    }

    private sealed class TestAuthenticationService : IEveAuthenticationService
    {
        public AuthenticatedCharacter? CurrentCharacter { get; } = new(
            9001,
            "Fleet Boss",
            ["esi-fleets.read_fleet.v1", "esi-fleets.write_fleet.v1"],
            DateTimeOffset.UtcNow);

        public int RefreshCount { get; private set; }

        public Task<AuthenticatedCharacter?> RestoreSessionAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CurrentCharacter);
        }

        public Task<AuthenticatedCharacter> SignInAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(CurrentCharacter!);
        }

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult("access-token");
        }

        public Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefreshCount++;
            return Task.FromResult("refreshed-token");
        }
    }
}
