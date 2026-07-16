using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetOrganizer.Infrastructure.Tests.Persistence;

public sealed class FleetProfileRepositoryTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "FleetOrganizer.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveLoadUpdateAndDeleteRoundTripAProfile()
    {
        var paths = new TestAppDataPaths(testRoot);
        var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync(CancellationToken.None);
        var repository = new FleetProfileRepository(paths, TimeProvider.System);
        var squadId = Guid.NewGuid();
        var squads = new[] { new ProfileSquad(squadId, "Squad 1", 0) };
        var wings = new[] { new ProfileWing(Guid.NewGuid(), "Wing 1", 0, squads) };
        var tags = new[] { "logi", "anchor" };
        var assignments = new[]
        {
            new ProfileAssignment(
                9001,
                "Logi Pilot",
                squadId,
                DesiredFleetRole.SquadCommander)
            {
                Tags = tags,
            },
        };
        var profile = new FleetProfile(
            Guid.NewGuid(),
            "Doctrine Alpha",
            wings,
            assignments);

        await repository.SaveAsync(profile, CancellationToken.None);
        var loaded = Assert.Single(
            await repository.LoadAllAsync(CancellationToken.None));

        Assert.Equal(profile.Id, loaded.Id);
        Assert.Equal("Doctrine Alpha", loaded.Name);
        Assert.Equal("Wing 1", Assert.Single(loaded.Wings).Name);
        var loadedAssignment = Assert.Single(loaded.Assignments);
        Assert.Equal(DesiredFleetRole.SquadCommander, loadedAssignment.DesiredRole);
        Assert.Collection(
            loadedAssignment.Tags,
            tag => Assert.Equal("logi", tag),
            tag => Assert.Equal("anchor", tag));

        var renamed = profile with { Name = "Doctrine Bravo" };
        await repository.SaveAsync(renamed, CancellationToken.None);
        var updated = Assert.Single(
            await repository.LoadAllAsync(CancellationToken.None));
        Assert.Equal("Doctrine Bravo", updated.Name);

        await repository.DeleteAsync(profile.Id, CancellationToken.None);
        Assert.Empty(await repository.LoadAllAsync(CancellationToken.None));
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

    private sealed class TestAppDataPaths(string rootDirectory) : IAppDataPaths
    {
        public string RootDirectory { get; } = rootDirectory;

        public string DatabasePath { get; } = Path.Combine(rootDirectory, "test.db");

        public string LogsDirectory { get; } = Path.Combine(rootDirectory, "logs");
    }
}
