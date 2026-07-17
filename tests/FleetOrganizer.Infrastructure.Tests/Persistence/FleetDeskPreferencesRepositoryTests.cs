using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Planning;
using FleetOrganizer.Core.Profiles;
using FleetOrganizer.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetOrganizer.Infrastructure.Tests.Persistence;

public sealed class FleetDeskPreferencesRepositoryTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "FleetOrganizer.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PreferencesRoundTripThroughExistingSettingsTable()
    {
        var paths = new TestAppDataPaths(testRoot);
        var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);
        await initializer.InitializeAsync(CancellationToken.None);
        var repository = new FleetDeskPreferencesRepository(paths, TimeProvider.System);
        var defaultId = Guid.NewGuid();
        var pinnedId = Guid.NewGuid();
        var preferences = new FleetDeskPreferences
        {
            LastUsedProfileId = pinnedId,
            DefaultProfileId = defaultId,
            PinnedProfileIds = [defaultId, pinnedId],
            RunMode = FleetRunMode.PlacePresent,
            AttentionSoundsEnabled = false,
        };

        await repository.SaveAsync(preferences, CancellationToken.None);
        var loaded = await repository.LoadAsync(CancellationToken.None);

        Assert.Equal(defaultId, loaded.DefaultProfileId);
        Assert.Equal(pinnedId, loaded.LastUsedProfileId);
        Assert.Equal(FleetRunMode.PlacePresent, loaded.RunMode);
        Assert.False(loaded.AttentionSoundsEnabled);
        Assert.Equal([defaultId, pinnedId], loaded.PinnedProfileIds);
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
