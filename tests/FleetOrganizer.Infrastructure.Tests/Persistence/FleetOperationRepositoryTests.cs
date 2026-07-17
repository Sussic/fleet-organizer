using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;
using FleetOrganizer.Core.Planning;
using FleetOrganizer.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetOrganizer.Infrastructure.Tests.Persistence;

public sealed class FleetOperationRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);

    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "FleetOrganizer.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task OperationAndTypedStepPayloadRoundTrip()
    {
        var paths = new TestAppDataPaths(testRoot);
        var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync(CancellationToken.None);

        var squadId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var profile = new FleetProfile(
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
                    DesiredFleetRole.SquadMember),
            ]);
        var profileRepository = new FleetProfileRepository(paths, TimeProvider.System);
        await profileRepository.SaveAsync(profile, CancellationToken.None);

        var snapshot = CreateSnapshot();
        var plan = FleetPlanner.Build(profile, snapshot);
        var operation = FleetOperationFactory.Create(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            profile,
            snapshot,
            plan,
            Now);
        var repository = new FleetOperationRepository(paths);

        await repository.SaveAsync(operation, snapshot, CancellationToken.None);
        var loaded = await repository.LoadLatestResumableAsync(CancellationToken.None);

        var saved = Assert.IsType<FleetOperation>(loaded);
        Assert.Equal(operation.Id, saved.Id);
        Assert.Equal("Test Fleet", saved.ProfileName);
        Assert.Equal(OperationState.InviteMissing, saved.State);
        Assert.Collection(
            saved.Steps,
            step =>
            {
                Assert.Equal(FleetOperationStepType.Invite, step.Type);
                Assert.Equal("Second Pilot", step.Target.CharacterName);
                Assert.Equal(20, step.Target.SquadId);
            },
            step => Assert.Equal(FleetOperationStepType.Place, step.Type));
    }

    [Fact]
    public async Task RecentHistoryIsNewestFirstAndHonoursLimit()
    {
        var paths = new TestAppDataPaths(testRoot);
        var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync(CancellationToken.None);

        var squadId = Guid.NewGuid();
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "History Fleet",
            [new ProfileWing(Guid.NewGuid(), "Main", 0, [new ProfileSquad(squadId, "Squad 1", 0)])],
            [new ProfileAssignment(9002, "Second Pilot", squadId, DesiredFleetRole.SquadMember)]);
        await new FleetProfileRepository(paths, TimeProvider.System)
            .SaveAsync(profile, CancellationToken.None);

        var snapshot = CreateSnapshot();
        var plan = FleetPlanner.Build(profile, snapshot);
        var repository = new FleetOperationRepository(paths);
        var oldest = FleetOperationFactory.Create(Guid.NewGuid(), profile, snapshot, plan, Now);
        var middle = FleetOperationFactory.Create(Guid.NewGuid(), profile, snapshot, plan, Now.AddMinutes(1));
        var newest = FleetOperationFactory.Create(Guid.NewGuid(), profile, snapshot, plan, Now.AddMinutes(2));
        await repository.SaveAsync(oldest, snapshot, CancellationToken.None);
        await repository.SaveAsync(middle, snapshot, CancellationToken.None);
        await repository.SaveAsync(newest, snapshot, CancellationToken.None);

        var recent = await repository.LoadRecentAsync(2, CancellationToken.None);

        Assert.Collection(
            recent,
            operation => Assert.Equal(newest.Id, operation.Id),
            operation => Assert.Equal(middle.Id, operation.Id));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static LiveFleetSnapshot CreateSnapshot() =>
        new(
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
                    Now,
                    TakesFleetWarp: true),
            ]);

    private sealed class TestAppDataPaths(string rootDirectory) : IAppDataPaths
    {
        public string RootDirectory { get; } = rootDirectory;

        public string DatabasePath { get; } = Path.Combine(rootDirectory, "test.db");

        public string LogsDirectory { get; } = Path.Combine(rootDirectory, "logs");
    }
}
