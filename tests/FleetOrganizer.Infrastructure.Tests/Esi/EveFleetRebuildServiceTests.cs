using System.Net;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Authentication;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Infrastructure.Authentication;
using FleetOrganizer.Infrastructure.Configuration;
using FleetOrganizer.Infrastructure.Esi;
using Microsoft.Extensions.Options;

namespace FleetOrganizer.Infrastructure.Tests.Esi;

public sealed class EveFleetRebuildServiceTests
{
    [Fact]
    public async Task RebuildStopsBeforeWritesWhenFreshFleetBossCheckFails()
    {
        using var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var service = new EveFleetRebuildService(
            client,
            new StubLiveFleetService(new LiveFleetLoadResult(
                LiveFleetLoadStatus.NotFleetBoss,
                null,
                "Another character is fleet boss.",
                null,
                DateTimeOffset.UtcNow)));

        var result = await service.RebuildAsync(7001, CreateProfile());

        Assert.False(result.IsSuccess);
        Assert.Equal(0, result.CompletedWrites);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ReservedUnknownNameIsRejectedBeforeFleetRead()
    {
        using var handler = new CountingHandler();
        using var httpClient = new HttpClient(handler);
        using var client = CreateClient(httpClient);
        var service = new EveFleetRebuildService(
            client,
            new StubLiveFleetService(new LiveFleetLoadResult(
                LiveFleetLoadStatus.Ready,
                null,
                "unused",
                null,
                DateTimeOffset.UtcNow)));
        var original = CreateProfile();
        var squadId = original.Assignments[0].TargetSquadId;
        var profile = original with
        {
            Wings = [new ProfileWing(Guid.NewGuid(), "Unknown", 0, [
                new ProfileSquad(squadId, "Squad 1", 0),
            ])],
        };

        var result = await service.RebuildAsync(7001, profile);

        Assert.False(result.IsSuccess);
        Assert.Contains("reserved", result.UserMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, handler.CallCount);
    }

    private static FleetProfile CreateProfile()
    {
        var squadId = Guid.NewGuid();
        return new FleetProfile(
            Guid.NewGuid(),
            "Doctrine",
            [new ProfileWing(Guid.NewGuid(), "Wing 1", 0, [
                new ProfileSquad(squadId, "Squad 1", 0),
            ])],
            [new ProfileAssignment(9001, "Fleet Boss", squadId, DesiredFleetRole.SquadMember)]);
    }

    private static EveEsiClient CreateClient(HttpClient httpClient) => new(
        httpClient,
        new TestAuthenticationService(),
        Options.Create(new EveDeveloperOptions
        {
            ClientId = "test-client",
            CompatibilityDate = "2026-07-15",
        }),
        TimeProvider.System);

    private sealed class StubLiveFleetService(LiveFleetLoadResult result) : ILiveFleetService
    {
        public Task<LiveFleetLoadResult> LoadCurrentAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(result);

        public Task<LiveFleetLoadResult> RefreshCurrentAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    private sealed class CountingHandler : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
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
            CancellationToken cancellationToken = default) => Task.FromResult(CurrentCharacter);

        public Task<AuthenticatedCharacter> SignInAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(CurrentCharacter!);

        public Task SignOutAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("access-token");

        public Task<string> RefreshAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("refreshed-token");
    }
}
