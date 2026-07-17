using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;
using FleetOrganizer.Core.Planning;
using FleetOrganizer.Infrastructure.Operations;

namespace FleetOrganizer.Infrastructure.Tests.Operations;

public sealed class FleetOperationServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SentInviteIsNotRepeatedAndAcceptanceCompletesPlacement()
    {
        var profile = CreateProfile();
        var absentSnapshot = CreateSnapshot(includePilot: false);
        var liveService = new TestLiveFleetService(absentSnapshot);
        var writeService = new TestWriteService();
        var repository = new InMemoryOperationRepository();
        using var service = new FleetOperationService(
            repository,
            liveService,
            writeService,
            TimeProvider.System);
        var reviewedPlan = FleetPlanner.Build(profile, absentSnapshot);

        var started = await service.StartAsync(
            profile,
            reviewedPlan,
            CancellationToken.None);

        Assert.True(started.Started);
        var startedOperation = Assert.IsType<FleetOperation>(started.Operation);
        Assert.Equal(OperationState.AwaitAcceptance, startedOperation.State);
        Assert.Equal(1, writeService.InviteCount);
        Assert.Equal(0, writeService.PlaceCount);

        liveService.Snapshot = CreateSnapshot(includePilot: true);
        var completed = await service.ContinueAsync(
            startedOperation.Id,
            CancellationToken.None);

        Assert.Equal(OperationState.Complete, completed.State);
        Assert.Equal(1, writeService.InviteCount);
        Assert.Equal(0, writeService.PlaceCount);
        Assert.All(
            completed.Steps.Where(step => step.Type != FleetOperationStepType.DeferredCommander),
            step => Assert.Equal(FleetOperationStepState.Succeeded, step.State));
    }

    [Fact]
    public async Task ChangedLivePlanRequiresReviewBeforeAnyWrite()
    {
        var profile = CreateProfile();
        var reviewedPlan = FleetPlanner.Build(profile, CreateSnapshot(includePilot: false));
        var liveService = new TestLiveFleetService(CreateSnapshot(includePilot: true));
        var writeService = new TestWriteService();
        var repository = new InMemoryOperationRepository();
        using var service = new FleetOperationService(
            repository,
            liveService,
            writeService,
            TimeProvider.System);

        var result = await service.StartAsync(
            profile,
            reviewedPlan,
            CancellationToken.None);

        Assert.False(result.Started);
        Assert.Null(result.Operation);
        Assert.Equal(0, writeService.InviteCount);
        Assert.Equal(0, writeService.PlaceCount);
        Assert.Null(await repository.LoadLatestResumableAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CommanderWriteWaitsForLiveConfirmationAndIsNotReplayed()
    {
        var profile = CreateProfile(DesiredFleetRole.SquadCommander);
        var ordinarySnapshot = CreateSnapshot(includePilot: true);
        var liveService = new TestLiveFleetService(ordinarySnapshot);
        var writeService = new TestWriteService();
        var repository = new InMemoryOperationRepository();
        using var service = new FleetOperationService(
            repository,
            liveService,
            writeService,
            TimeProvider.System);

        var started = await service.StartAsync(
            profile,
            FleetPlanner.Build(profile, ordinarySnapshot),
            CancellationToken.None);

        Assert.True(started.Started);
        var operation = Assert.IsType<FleetOperation>(started.Operation);
        Assert.Equal(OperationState.AssignCommanders, operation.State);
        Assert.Equal(1, writeService.PromoteCount);

        liveService.Snapshot = CreateSnapshot(
            includePilot: true,
            pilotRole: "squad_commander");
        var completed = await service.ContinueAsync(operation.Id, CancellationToken.None);

        Assert.Equal(OperationState.Complete, completed.State);
        Assert.Equal(1, writeService.PromoteCount);
    }

    [Fact]
    public async Task MissingStructureIsCreatedNamedAndVerifiedInOneRun()
    {
        var squadId = Guid.NewGuid();
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Structure repair",
            [
                new ProfileWing(
                    Guid.NewGuid(),
                    "Main Wing",
                    0,
                    [new ProfileSquad(squadId, "Squad 1", 0)]),
            ],
            []);
        var initial = CreateSnapshot(includePilot: false) with { Wings = [] };
        var liveService = new TestLiveFleetService(initial);
        var writeService = new TestWriteService(liveService);
        using var service = new FleetOperationService(
            new InMemoryOperationRepository(),
            liveService,
            writeService,
            TimeProvider.System);

        var result = await service.StartAsync(
            profile,
            FleetPlanner.Build(profile, initial),
            CancellationToken.None);

        var operation = Assert.IsType<FleetOperation>(result.Operation);
        Assert.Equal(OperationState.Complete, operation.State);
        var wing = Assert.Single(liveService.Snapshot.Wings);
        Assert.Equal("Main Wing", wing.Name);
        Assert.Equal("Squad 1", Assert.Single(wing.Squads).Name);
        Assert.Equal(4, writeService.StructureWriteCount);
    }

    private static FleetProfile CreateProfile(
        DesiredFleetRole role = DesiredFleetRole.SquadMember)
    {
        var squadId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        return new FleetProfile(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "Test Fleet",
            [
                new ProfileWing(
                    Guid.NewGuid(),
                    "Main Wing",
                    0,
                    [new ProfileSquad(squadId, "Squad 1", 0)]),
            ],
            [
                new ProfileAssignment(
                    9002,
                    "Second Pilot",
                    squadId,
                    role),
            ]);
    }

    private static LiveFleetSnapshot CreateSnapshot(
        bool includePilot,
        string pilotRole = "squad_member")
    {
        var members = new List<LiveFleetMember>
        {
            CreateMember(9001, "Fleet Boss", "fleet_commander", -1, -1),
        };
        if (includePilot)
        {
            members.Add(CreateMember(9002, "Second Pilot", pilotRole, 10, 20));
        }

        return new LiveFleetSnapshot(
            7001,
            9001,
            "Fleet Boss",
            IsFleetBoss: true,
            IsFreeMove: false,
            IsRegistered: false,
            IsVoiceEnabled: false,
            Motd: string.Empty,
            Now,
            Now.AddSeconds(5),
            [new LiveFleetWing(10, "Main Wing", [new LiveFleetSquad(20, "Squad 1")])],
            members.ToArray());
    }

    private static LiveFleetMember CreateMember(
        long characterId,
        string name,
        string role,
        long wingId,
        long squadId) =>
        new(
            characterId,
            name,
            role,
            role.Replace('_', ' '),
            587,
            "Rifter",
            30000142,
            "Jita",
            null,
            null,
            wingId,
            squadId,
            Now,
            TakesFleetWarp: true);

    private sealed class TestLiveFleetService(LiveFleetSnapshot snapshot) : ILiveFleetService
    {
        public LiveFleetSnapshot Snapshot { get; set; } = snapshot;

        public Task<LiveFleetLoadResult> LoadCurrentAsync(
            CancellationToken cancellationToken = default) =>
            CreateResultAsync(cancellationToken);

        public Task<LiveFleetLoadResult> RefreshCurrentAsync(
            CancellationToken cancellationToken = default) =>
            CreateResultAsync(cancellationToken);

        private Task<LiveFleetLoadResult> CreateResultAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new LiveFleetLoadResult(
                LiveFleetLoadStatus.Ready,
                Snapshot,
                "Ready",
                null,
                Snapshot.ConfirmedAtUtc));
        }
    }

    private sealed class TestWriteService(
        TestLiveFleetService? liveService = null) : IFleetWriteService
    {
        public int InviteCount { get; private set; }

        public int PlaceCount { get; private set; }

        public int PromoteCount { get; private set; }

        public int StructureWriteCount { get; private set; }

        public Task<FleetWriteResult> CreateWingAsync(
            long fleetId,
            CancellationToken cancellationToken = default)
        {
            StructureWriteCount++;
            if (liveService is not null)
            {
                liveService.Snapshot = liveService.Snapshot with
                {
                    Wings = liveService.Snapshot.Wings
                        .Append(new LiveFleetWing(30, "Wing 1", []))
                        .ToArray(),
                };
            }

            return CreateSuccessAsync(fleetId, cancellationToken, createdId: 30);
        }

        public Task<FleetWriteResult> RenameWingAsync(
            long fleetId,
            FleetOperationTarget target,
            CancellationToken cancellationToken = default)
        {
            StructureWriteCount++;
            if (liveService is not null)
            {
                liveService.Snapshot = liveService.Snapshot with
                {
                    Wings = liveService.Snapshot.Wings
                        .Select(wing => wing.WingId == target.WingId
                            ? wing with { Name = target.WingName }
                            : wing)
                        .ToArray(),
                };
            }

            return CreateSuccessAsync(fleetId, cancellationToken);
        }

        public Task<FleetWriteResult> CreateSquadAsync(
            long fleetId,
            FleetOperationTarget target,
            CancellationToken cancellationToken = default)
        {
            StructureWriteCount++;
            if (liveService is not null)
            {
                liveService.Snapshot = liveService.Snapshot with
                {
                    Wings = liveService.Snapshot.Wings
                        .Select(wing => wing.WingId == target.WingId
                            ? wing with
                            {
                                Squads = wing.Squads
                                    .Append(new LiveFleetSquad(40, "Squad 1"))
                                    .ToArray(),
                            }
                            : wing)
                        .ToArray(),
                };
            }

            return CreateSuccessAsync(fleetId, cancellationToken, createdId: 40);
        }

        public Task<FleetWriteResult> RenameSquadAsync(
            long fleetId,
            FleetOperationTarget target,
            CancellationToken cancellationToken = default)
        {
            StructureWriteCount++;
            if (liveService is not null)
            {
                liveService.Snapshot = liveService.Snapshot with
                {
                    Wings = liveService.Snapshot.Wings
                        .Select(wing => wing.WingId == target.WingId
                            ? wing with
                            {
                                Squads = wing.Squads
                                    .Select(squad => squad.SquadId == target.SquadId
                                        ? squad with { Name = target.SquadName }
                                        : squad)
                                    .ToArray(),
                            }
                            : wing)
                        .ToArray(),
                };
            }

            return CreateSuccessAsync(fleetId, cancellationToken);
        }

        public Task<FleetWriteResult> InviteAsync(
            long fleetId,
            FleetOperationTarget target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(7001, fleetId);
            Assert.Equal(9002, target.CharacterId);
            InviteCount++;
            return Task.FromResult(new FleetWriteResult(
                IsSuccess: true,
                FleetWriteFailureKind.None,
                "Accepted",
                null,
                null));
        }

        public Task<FleetWriteResult> PlaceAsync(
            long fleetId,
            FleetOperationTarget target,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(7001, fleetId);
            Assert.Equal(9002, target.CharacterId);
            PlaceCount++;
            return Task.FromResult(new FleetWriteResult(
                IsSuccess: true,
                FleetWriteFailureKind.None,
                "Accepted",
                null,
                null));
        }

        public Task<FleetWriteResult> PromoteCommanderAsync(
            long fleetId,
            FleetOperationTarget target,
            CancellationToken cancellationToken = default)
        {
            PromoteCount++;
            return CreateSuccessAsync(fleetId, cancellationToken);
        }

        private static Task<FleetWriteResult> CreateSuccessAsync(
            long fleetId,
            CancellationToken cancellationToken,
            long? createdId = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(7001, fleetId);
            return Task.FromResult(new FleetWriteResult(
                IsSuccess: true,
                FleetWriteFailureKind.None,
                "Accepted",
                null,
                null,
                createdId));
        }
    }

    private sealed class InMemoryOperationRepository : IFleetOperationRepository
    {
        private FleetOperation? operation;
        private LiveFleetSnapshot? snapshot;

        public Task<FleetOperation?> LoadAsync(
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(operation?.Id == operationId ? operation : null);
        }

        public Task<FleetOperation?> LoadLatestResumableAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(operation is { IsTerminal: false } ? operation : null);
        }

        public Task<LiveFleetSnapshot?> LoadInitialSnapshotAsync(
            Guid operationId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(operation?.Id == operationId ? snapshot : null);
        }

        public Task SaveAsync(
            FleetOperation value,
            LiveFleetSnapshot? initialSnapshot = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot ??= initialSnapshot;
            operation = value;
            return Task.CompletedTask;
        }
    }
}
