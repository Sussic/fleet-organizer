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
    public async Task InitializeCreatesCurrentSchema()
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

        Assert.Equal(2, version);

        await using var columnCommand = connection.CreateCommand();
        columnCommand.CommandText =
            "SELECT COUNT(*) FROM pragma_table_info('operation_steps') WHERE name IN ('sort_order', 'payload_json', 'retry_after_utc');";
        var operationColumnCount = Convert.ToInt64(
            await columnCommand.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(3, operationColumnCount);
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
        command.CommandText = "SELECT COUNT(*) FROM schema_migrations WHERE version IN (1, 2);";

        var migrationCount = Convert.ToInt64(
            await command.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(2, migrationCount);
    }

    [Fact]
    public async Task ExistingVersionOneDatabaseUpgradesToVersionTwo()
    {
        var paths = new TestAppDataPaths(testRoot);
        Directory.CreateDirectory(paths.RootDirectory);
        var initializer = new SqliteDatabaseInitializer(
            paths,
            NullLogger<SqliteDatabaseInitializer>.Instance);

        await using (var connection = new SqliteConnection($"Data Source={paths.DatabasePath}"))
        {
            await connection.OpenAsync(CancellationToken.None);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TABLE schema_migrations (
                    version INTEGER NOT NULL PRIMARY KEY,
                    applied_utc TEXT NOT NULL
                );
                INSERT INTO schema_migrations (version, applied_utc)
                VALUES (1, '2026-07-15T00:00:00.0000000+00:00');

                CREATE TABLE active_operations (
                    id TEXT NOT NULL PRIMARY KEY,
                    updated_utc TEXT NOT NULL
                );
                CREATE TABLE operation_steps (
                    operation_id TEXT NOT NULL,
                    step_key TEXT NOT NULL,
                    step_type TEXT NOT NULL,
                    target_id TEXT NULL,
                    state INTEGER NOT NULL,
                    attempts INTEGER NOT NULL DEFAULT 0,
                    last_failure_kind TEXT NULL,
                    last_failure_message TEXT NULL,
                    updated_utc TEXT NOT NULL,
                    PRIMARY KEY (operation_id, step_key)
                );
                """;
            await command.ExecuteNonQueryAsync(CancellationToken.None);
        }

        await initializer.InitializeAsync(CancellationToken.None);

        await using var upgradedConnection = new SqliteConnection($"Data Source={paths.DatabasePath}");
        await upgradedConnection.OpenAsync(CancellationToken.None);
        await using var versionCommand = upgradedConnection.CreateCommand();
        versionCommand.CommandText = "SELECT MAX(version) FROM schema_migrations;";
        var version = Convert.ToInt64(
            await versionCommand.ExecuteScalarAsync(CancellationToken.None),
            System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(2, version);
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
