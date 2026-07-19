using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;
using FleetOrganizer.Infrastructure.Esi;

namespace FleetOrganizer.Infrastructure.Tests.Esi;

public sealed class EveFleetInvitationServiceTests
{
    private static readonly FleetInvitationCandidate[] TwoCharacters =
    [
        new(9002, "First Pilot"),
        new(9003, "Second Pilot"),
    ];

    [Fact]
    public async Task FleetBossMayInviteWhileOccupyingWingCommand()
    {
        var writer = new StubWriteService();
        var service = new EveFleetInvitationService(
            new StubLiveFleetService(CreateReadyResult()),
            writer);

        var result = await service.InviteAsync(
            7001,
            10,
            20,
            "Wing 1 / Squad 1",
            TwoCharacters);

        Assert.True(result.IsComplete);
        Assert.Equal(2, result.Sent.Count);
        Assert.Empty(result.Unsent);
        Assert.Equal(2, writer.Targets.Count);
        Assert.All(writer.Targets, target =>
            Assert.Equal(DesiredFleetRole.SquadMember, target.DesiredRole));
    }

    [Fact]
    public async Task FreshFleetBossFailureStopsBeforeAnyInviteWrite()
    {
        var writer = new StubWriteService();
        var service = new EveFleetInvitationService(
            new StubLiveFleetService(CreateReadyResult() with
            {
                Status = LiveFleetLoadStatus.NotFleetBoss,
                UserMessage = "Another character is fleet boss.",
            }),
            writer);

        var result = await service.InviteAsync(
            7001,
            10,
            20,
            "Wing 1 / Squad 1",
            TwoCharacters);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Sent);
        Assert.Empty(writer.Targets);
    }

    [Fact]
    public async Task BatchStopsAfterFailureWithoutReplayingAcceptedInvites()
    {
        var writer = new StubWriteService(failOnCall: 2);
        var service = new EveFleetInvitationService(
            new StubLiveFleetService(CreateReadyResult()),
            writer);

        var result = await service.InviteAsync(
            7001,
            10,
            20,
            "Wing 1 / Squad 1",
            TwoCharacters);

        Assert.False(result.IsComplete);
        Assert.Equal("First Pilot", Assert.Single(result.Sent).CharacterName);
        Assert.Equal("Second Pilot", Assert.Single(result.Unsent).CharacterName);
        Assert.Equal(2, writer.Targets.Count);
    }

    private static LiveFleetLoadResult CreateReadyResult()
    {
        var snapshot = new LiveFleetSnapshot(
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
            [CreateMember(9001, "wing_commander", 10, -1)]);
        return new(
            LiveFleetLoadStatus.Ready,
            snapshot,
            "Ready",
            null,
            DateTimeOffset.UtcNow);
    }

    private static LiveFleetMember CreateMember(
        long characterId,
        string role,
        long wingId,
        long squadId) =>
        new(
            characterId,
            "Fleet Boss",
            role,
            "Wing Commander",
            587,
            "Rifter",
            30_000_142,
            "Jita",
            null,
            null,
            wingId,
            squadId,
            DateTimeOffset.UtcNow,
            true);

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

    private sealed class StubWriteService(int? failOnCall = null) : IFleetWriteService
    {
        public List<FleetOperationTarget> Targets { get; } = [];

        public Task<FleetWriteResult> InviteAsync(
            long fleetId,
            FleetOperationTarget target,
            CancellationToken cancellationToken = default)
        {
            _ = fleetId;
            cancellationToken.ThrowIfCancellationRequested();
            Targets.Add(target);
            return Task.FromResult(failOnCall == Targets.Count
                ? Failed("EVE rejected the invitation.")
                : Succeeded());
        }

        public Task<FleetWriteResult> CreateWingAsync(long fleetId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FleetWriteResult> RenameWingAsync(long fleetId, FleetOperationTarget target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FleetWriteResult> CreateSquadAsync(long fleetId, FleetOperationTarget target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FleetWriteResult> RenameSquadAsync(long fleetId, FleetOperationTarget target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FleetWriteResult> PlaceAsync(long fleetId, FleetOperationTarget target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<FleetWriteResult> PromoteCommanderAsync(long fleetId, FleetOperationTarget target, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        private static FleetWriteResult Succeeded() =>
            new(true, FleetWriteFailureKind.None, "Sent", null, null);

        private static FleetWriteResult Failed(string message) =>
            new(false, FleetWriteFailureKind.Validation, message, null, null);
    }
}
