using System.Net;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Authentication;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Infrastructure.Authentication;
using FleetOrganizer.Infrastructure.Configuration;
using FleetOrganizer.Infrastructure.Esi;
using Microsoft.Extensions.Options;

namespace FleetOrganizer.Infrastructure.Tests.Esi;

public sealed class EveFleetAdministrationServiceTests
{
    [Fact]
    public async Task KickStopsBeforeWriteWhenFreshFleetBossCheckFails()
    {
        using var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var service = new EveFleetAdministrationService(
            client,
            new StubLiveFleetService(new LiveFleetLoadResult(
                LiveFleetLoadStatus.NotFleetBoss,
                null,
                "Another character is fleet boss.",
                null,
                DateTimeOffset.UtcNow)));

        var result = await service.KickMembersAsync(7001, [9002]);

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task KickUsesFreshSameFleetSnapshotBeforeDelete()
    {
        using var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var snapshot = CreateSnapshot();
        var service = new EveFleetAdministrationService(
            client,
            new StubLiveFleetService(new LiveFleetLoadResult(
                LiveFleetLoadStatus.Ready,
                snapshot,
                "Ready",
                null,
                snapshot.ConfirmedAtUtc)));

        var result = await service.KickMembersAsync(7001, [9002]);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(1, handler.CallCount);
        Assert.Equal(HttpMethod.Delete, handler.LastMethod);
        Assert.Equal("/fleets/7001/members/9002", handler.LastPath);
    }

    [Fact]
    public async Task EmptyStructureGuardRejectsDeletingOccupiedSquad()
    {
        using var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var snapshot = CreateSnapshot();
        var service = new EveFleetAdministrationService(
            client,
            new StubLiveFleetService(new LiveFleetLoadResult(
                LiveFleetLoadStatus.Ready,
                snapshot,
                "Ready",
                null,
                snapshot.ConfirmedAtUtc)));

        var result = await service.DeleteSquadAsync(7001, 20);

        Assert.False(result.IsSuccess);
        Assert.Contains("not empty", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.CallCount);
    }

    private static EveEsiClient CreateClient(HttpClient httpClient) =>
        new(
            httpClient,
            new TestAuthenticationService(),
            Options.Create(new EveDeveloperOptions
            {
                ClientId = "test-client",
                CompatibilityDate = "2026-07-15",
            }),
            TimeProvider.System);

    private static LiveFleetSnapshot CreateSnapshot() => new(
        7001,
        9001,
        "Fleet Boss",
        IsFleetBoss: true,
        IsFreeMove: false,
        IsRegistered: false,
        IsVoiceEnabled: false,
        Motd: string.Empty,
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddSeconds(5),
        [new LiveFleetWing(10, "Wing 1", [new LiveFleetSquad(20, "Squad 1")])],
        [
            new LiveFleetMember(
                9001,
                "Fleet Boss",
                "fleet_commander",
                "Fleet Commander",
                587,
                "Rifter",
                30000142,
                "Jita",
                null,
                null,
                -1,
                -1,
                DateTimeOffset.UtcNow,
                true),
            new LiveFleetMember(
                9002,
                "Second Pilot",
                "squad_member",
                "Squad Member",
                587,
                "Rifter",
                30000142,
                "Jita",
                null,
                null,
                10,
                20,
                DateTimeOffset.UtcNow,
                true),
        ]);

    private sealed class StubLiveFleetService(LiveFleetLoadResult result) : ILiveFleetService
    {
        public Task<LiveFleetLoadResult> LoadCurrentAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(result);
        }

        public Task<LiveFleetLoadResult> RefreshCurrentAsync(
            CancellationToken cancellationToken = default) =>
            LoadCurrentAsync(cancellationToken);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public HttpMethod? LastMethod { get; private set; }

        public string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            LastMethod = request.Method;
            LastPath = request.RequestUri?.AbsolutePath;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class TestAuthenticationService : IEveAuthenticationService
    {
        public AuthenticatedCharacter? CurrentCharacter { get; } = new(
            9001,
            "Fleet Boss",
            ["esi-fleets.read_fleet.v1", "esi-fleets.write_fleet.v1"],
            DateTimeOffset.UtcNow);

        public Task<AuthenticatedCharacter?> RestoreSessionAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentCharacter);

        public Task<AuthenticatedCharacter> SignInAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentCharacter!);

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("access-token");

        public Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("refreshed-token");
    }
}
