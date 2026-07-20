using FleetOrganizer.Core.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace FleetOrganizer.Infrastructure.Persistence;

public sealed partial class SqliteDatabaseInitializer(
    IAppDataPaths paths,
    ILogger<SqliteDatabaseInitializer> logger)
{
    private const long CurrentSchemaVersion = 5;

    static SqliteDatabaseInitializer()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        EnsureDirectoriesExist();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            "PRAGMA foreign_keys = ON; PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 5000;",
            cancellationToken).ConfigureAwait(false);

        await ExecuteAsync(
            connection,
            """
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_utc TEXT NOT NULL
            );
            """,
            cancellationToken).ConfigureAwait(false);

        var currentVersion = await ReadSchemaVersionAsync(connection, cancellationToken)
            .ConfigureAwait(false);

        if (currentVersion > CurrentSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Database schema {currentVersion} is newer than this application supports ({CurrentSchemaVersion}).");
        }

        if (currentVersion < 1)
        {
            ApplyVersionOne(connection);
            LogMigrationApplied(logger, 1);
        }

        if (currentVersion < 2)
        {
            ApplyVersionTwo(connection);
            LogMigrationApplied(logger, 2);
        }

        if (currentVersion < 3)
        {
            ApplyVersionThree(connection);
            LogMigrationApplied(logger, 3);
        }

        if (currentVersion < 4)
        {
            ApplyVersionFour(connection);
            LogMigrationApplied(logger, 4);
        }

        if (currentVersion < 5)
        {
            ApplyVersionFive(connection);
            LogMigrationApplied(logger, 5);
        }

        LogDatabaseReady(logger, CurrentSchemaVersion);
    }

    private void EnsureDirectoriesExist()
    {
        if (paths is AppDataPaths appDataPaths)
        {
            appDataPaths.EnsureDirectoriesExist();
            return;
        }

        Directory.CreateDirectory(paths.RootDirectory);
        Directory.CreateDirectory(paths.LogsDirectory);
    }

    private static async Task<long> ReadSchemaVersionAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_migrations;";
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return Convert.ToInt64(result, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void ApplyVersionOne(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = VersionOneSql;
        command.ExecuteNonQuery();

        using var migrationCommand = connection.CreateCommand();
        migrationCommand.Transaction = transaction;
        migrationCommand.CommandText =
            "INSERT INTO schema_migrations (version, applied_utc) VALUES ($version, $appliedUtc);";
        migrationCommand.Parameters.AddWithValue("$version", 1);
        migrationCommand.Parameters.AddWithValue(
            "$appliedUtc",
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        migrationCommand.ExecuteNonQuery();

        transaction.Commit();
    }

    private static void ApplyVersionTwo(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = VersionTwoSql;
        command.ExecuteNonQuery();

        using var migrationCommand = connection.CreateCommand();
        migrationCommand.Transaction = transaction;
        migrationCommand.CommandText =
            "INSERT INTO schema_migrations (version, applied_utc) VALUES ($version, $appliedUtc);";
        migrationCommand.Parameters.AddWithValue("$version", 2);
        migrationCommand.Parameters.AddWithValue(
            "$appliedUtc",
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        migrationCommand.ExecuteNonQuery();

        transaction.Commit();
    }

    private static void ApplyVersionThree(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = VersionThreeSql;
        command.ExecuteNonQuery();

        using var migrationCommand = connection.CreateCommand();
        migrationCommand.Transaction = transaction;
        migrationCommand.CommandText =
            "INSERT INTO schema_migrations (version, applied_utc) VALUES ($version, $appliedUtc);";
        migrationCommand.Parameters.AddWithValue("$version", 3);
        migrationCommand.Parameters.AddWithValue(
            "$appliedUtc",
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        migrationCommand.ExecuteNonQuery();

        transaction.Commit();
    }

    private static void ApplyVersionFour(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = VersionFourSql;
        command.ExecuteNonQuery();

        using var migrationCommand = connection.CreateCommand();
        migrationCommand.Transaction = transaction;
        migrationCommand.CommandText =
            "INSERT INTO schema_migrations (version, applied_utc) VALUES ($version, $appliedUtc);";
        migrationCommand.Parameters.AddWithValue("$version", 4);
        migrationCommand.Parameters.AddWithValue(
            "$appliedUtc",
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        migrationCommand.ExecuteNonQuery();

        transaction.Commit();
    }

    private static void ApplyVersionFive(SqliteConnection connection)
    {
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = VersionFiveSql;
        command.ExecuteNonQuery();

        using var migrationCommand = connection.CreateCommand();
        migrationCommand.Transaction = transaction;
        migrationCommand.CommandText =
            "INSERT INTO schema_migrations (version, applied_utc) VALUES ($version, $appliedUtc);";
        migrationCommand.Parameters.AddWithValue("$version", 5);
        migrationCommand.Parameters.AddWithValue(
            "$appliedUtc",
            DateTimeOffset.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        migrationCommand.ExecuteNonQuery();

        transaction.Commit();
    }

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Applied database schema migration {SchemaVersion}.")]
    private static partial void LogMigrationApplied(ILogger logger, long schemaVersion);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Fleet Organizer database is ready at schema {SchemaVersion}.")]
    private static partial void LogDatabaseReady(ILogger logger, long schemaVersion);

    private const string VersionOneSql =
        """
        CREATE TABLE settings (
            key TEXT NOT NULL PRIMARY KEY,
            value TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );

        CREATE TABLE authenticated_characters (
            character_id INTEGER NOT NULL PRIMARY KEY,
            character_name TEXT NOT NULL,
            encrypted_refresh_token BLOB NOT NULL,
            granted_scopes TEXT NOT NULL,
            last_validated_utc TEXT NOT NULL
        );

        CREATE TABLE fleet_profiles (
            id TEXT NOT NULL PRIMARY KEY,
            name TEXT NOT NULL COLLATE NOCASE UNIQUE,
            schema_version INTEGER NOT NULL,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL
        );

        CREATE TABLE profile_wings (
            id TEXT NOT NULL PRIMARY KEY,
            profile_id TEXT NOT NULL,
            name TEXT NOT NULL COLLATE NOCASE,
            sort_order INTEGER NOT NULL,
            FOREIGN KEY (profile_id) REFERENCES fleet_profiles(id) ON DELETE CASCADE,
            UNIQUE (profile_id, name)
        );

        CREATE TABLE profile_squads (
            id TEXT NOT NULL PRIMARY KEY,
            wing_id TEXT NOT NULL,
            name TEXT NOT NULL COLLATE NOCASE,
            sort_order INTEGER NOT NULL,
            FOREIGN KEY (wing_id) REFERENCES profile_wings(id) ON DELETE CASCADE,
            UNIQUE (wing_id, name)
        );

        CREATE TABLE roster_characters (
            character_id INTEGER NOT NULL PRIMARY KEY,
            canonical_name TEXT NOT NULL,
            tags_json TEXT NOT NULL DEFAULT '[]',
            last_resolved_utc TEXT NOT NULL
        );

        CREATE TABLE profile_assignments (
            profile_id TEXT NOT NULL,
            character_id INTEGER NOT NULL,
            target_squad_id TEXT NOT NULL,
            desired_role INTEGER NOT NULL,
            PRIMARY KEY (profile_id, character_id),
            FOREIGN KEY (profile_id) REFERENCES fleet_profiles(id) ON DELETE CASCADE,
            FOREIGN KEY (character_id) REFERENCES roster_characters(character_id),
            FOREIGN KEY (target_squad_id) REFERENCES profile_squads(id) ON DELETE CASCADE
        );

        CREATE TABLE active_operations (
            id TEXT NOT NULL PRIMARY KEY,
            profile_id TEXT NOT NULL,
            fleet_id INTEGER NOT NULL,
            state INTEGER NOT NULL,
            created_utc TEXT NOT NULL,
            updated_utc TEXT NOT NULL,
            cancellation_reason TEXT NULL,
            FOREIGN KEY (profile_id) REFERENCES fleet_profiles(id)
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
            PRIMARY KEY (operation_id, step_key),
            FOREIGN KEY (operation_id) REFERENCES active_operations(id) ON DELETE CASCADE
        );

        CREATE TABLE fleet_snapshots (
            id TEXT NOT NULL PRIMARY KEY,
            operation_id TEXT NOT NULL,
            fleet_id INTEGER NOT NULL,
            captured_utc TEXT NOT NULL,
            snapshot_json TEXT NOT NULL,
            FOREIGN KEY (operation_id) REFERENCES active_operations(id) ON DELETE CASCADE
        );

        CREATE INDEX ix_profile_wings_profile_sort
            ON profile_wings(profile_id, sort_order);
        CREATE INDEX ix_profile_squads_wing_sort
            ON profile_squads(wing_id, sort_order);
        CREATE INDEX ix_operation_steps_state
            ON operation_steps(operation_id, state);
        """;

    private const string VersionTwoSql =
        """
        ALTER TABLE operation_steps
            ADD COLUMN sort_order INTEGER NOT NULL DEFAULT 0;

        ALTER TABLE operation_steps
            ADD COLUMN payload_json TEXT NOT NULL DEFAULT '{}';

        ALTER TABLE operation_steps
            ADD COLUMN retry_after_utc TEXT NULL;

        CREATE INDEX ix_active_operations_updated
            ON active_operations(updated_utc DESC);
        """;

    private const string VersionThreeSql =
        """
        CREATE TABLE profile_ship_rules (
            id TEXT NOT NULL PRIMARY KEY,
            profile_id TEXT NOT NULL,
            ship_type_name TEXT NOT NULL COLLATE NOCASE,
            target_squad_id TEXT NOT NULL,
            sort_order INTEGER NOT NULL,
            FOREIGN KEY (profile_id) REFERENCES fleet_profiles(id) ON DELETE CASCADE,
            FOREIGN KEY (target_squad_id) REFERENCES profile_squads(id) ON DELETE CASCADE,
            UNIQUE (profile_id, ship_type_name)
        );

        CREATE INDEX ix_profile_ship_rules_profile_sort
            ON profile_ship_rules(profile_id, sort_order);
        """;

    private const string VersionFourSql =
        """
        ALTER TABLE profile_ship_rules
            ADD COLUMN label TEXT NOT NULL DEFAULT '';

        ALTER TABLE profile_ship_rules
            ADD COLUMN overflow_squad_id TEXT NULL;

        ALTER TABLE profile_ship_rules
            ADD COLUMN max_per_squad INTEGER NOT NULL DEFAULT 10;

        ALTER TABLE profile_ship_rules
            ADD COLUMN balance_targets INTEGER NOT NULL DEFAULT 0;

        ALTER TABLE profile_ship_rules
            ADD COLUMN is_fallback INTEGER NOT NULL DEFAULT 0;
        """;

    private const string VersionFiveSql =
        """
        ALTER TABLE fleet_profiles
            ADD COLUMN is_internal INTEGER NOT NULL DEFAULT 0;

        CREATE INDEX ix_fleet_profiles_internal_name
            ON fleet_profiles(is_internal, name COLLATE NOCASE);
        """;
}
