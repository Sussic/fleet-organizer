using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace FleetOrganizer.Infrastructure.Tests.Persistence;

public sealed class SqliteDatabaseInitializerTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        "FleetOrganizer.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InitializeCreatesVersionOneSchema()
    {
        var paths = new TestAppDataPaths(testRoot);
        var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);

        await initializer.InitializeAsync(CancellationToken.None);

        Assert.True(File.Exists(paths.DatabasePath));

        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(version) FROM schema_migrations;";

        var version = Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(1, version);
    }

    [Fact]
    public async Task InitializeCanRunTwiceWithoutReapplyingMigration()
    {
        var paths = new TestAppDataPaths(testRoot);
        var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);

        await initializer.InitializeAsync(CancellationToken.None);
        await initializer.InitializeAsync(CancellationToken.None);

        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version = 1;";

        var migrationCount = Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(1, migrationCount);
    }

    [Fact]
    public async Task BundledSqliteContainsTheRequiredSecurityFix()
    {
        var paths = new TestAppDataPaths(testRoot);
        var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);

        await initializer.InitializeAsync(CancellationToken.None);

        await using var connection = new SqliteConnection($"Data Source={paths.DatabasePath}");
        await connection.OpenAsync(CancellationToken.None);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version();";

        var versionText = Convert.ToString(
            await command.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture);

        var version = Version.Parse(
            versionText ?? throw new InvalidOperationException("SQLite did not return its runtime version."));
        Assert.True(
            version >= new Version(3, 50, 2),
            $"Bundled SQLite {version} is vulnerable; version 3.50.2 or later is required.");
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
