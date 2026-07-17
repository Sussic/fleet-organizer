using System.Globalization;
using System.Text.Json;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Operations;
using FleetOrganizer.Core.Profiles;
using Microsoft.Data.Sqlite;

namespace FleetOrganizer.Infrastructure.Persistence;

internal sealed class FleetProfileRepository(
    IAppDataPaths paths,
    TimeProvider timeProvider) : IFleetProfileRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<FleetProfile[]> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

        var profiles = await LoadProfileHeadersAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        foreach (var profile in profiles)
        {
            await LoadHierarchyAsync(connection, profile, cancellationToken).ConfigureAwait(false);
            await LoadAssignmentsAsync(connection, profile, cancellationToken).ConfigureAwait(false);
        }

        return profiles
            .Select(profile => profile.Build())
            .OrderBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task SaveAsync(
        FleetProfile profile,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);

        var validationErrors = ProfileValidator.Validate(profile);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(validationErrors[0].Message);
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        var nowText = timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture);
        await UpsertProfileAsync(connection, transaction, profile, nowText, cancellationToken)
            .ConfigureAwait(false);
        await DeleteProfileContentsAsync(connection, transaction, profile.Id, cancellationToken)
            .ConfigureAwait(false);
        await InsertHierarchyAsync(connection, transaction, profile, cancellationToken)
            .ConfigureAwait(false);
        await InsertAssignmentsAsync(
            connection,
            transaction,
            profile,
            nowText,
            cancellationToken).ConfigureAwait(false);

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(
        Guid profileId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM active_operations
            WHERE profile_id = $id AND state IN ($complete, $cancelled);

            DELETE FROM fleet_profiles WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", profileId.ToString("D"));
        command.Parameters.AddWithValue("$complete", (int)OperationState.Complete);
        command.Parameters.AddWithValue("$cancelled", (int)OperationState.Cancelled);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWrite,
        }.ToString();
        return new SqliteConnection(connectionString);
    }

    private static async Task EnableForeignKeysAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task<List<ProfileBuilder>> LoadProfileHeadersAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var profiles = new List<ProfileBuilder>();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT id, name FROM fleet_profiles ORDER BY name COLLATE NOCASE;";
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            profiles.Add(new ProfileBuilder(
                Guid.Parse(reader.GetString(0)),
                reader.GetString(1)));
        }

        return profiles;
    }

    private static async Task LoadHierarchyAsync(
        SqliteConnection connection,
        ProfileBuilder profile,
        CancellationToken cancellationToken)
    {
        await using (var wingCommand = connection.CreateCommand())
        {
            wingCommand.CommandText =
                "SELECT id, name, sort_order FROM profile_wings WHERE profile_id = $profileId ORDER BY sort_order, name COLLATE NOCASE;";
            wingCommand.Parameters.AddWithValue("$profileId", profile.Id.ToString("D"));
            await using var reader = await wingCommand
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                profile.Wings.Add(new WingBuilder(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetInt32(2)));
            }
        }

        foreach (var wing in profile.Wings)
        {
            await using var squadCommand = connection.CreateCommand();
            squadCommand.CommandText =
                "SELECT id, name, sort_order FROM profile_squads WHERE wing_id = $wingId ORDER BY sort_order, name COLLATE NOCASE;";
            squadCommand.Parameters.AddWithValue("$wingId", wing.Id.ToString("D"));
            await using var reader = await squadCommand
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                wing.Squads.Add(new ProfileSquad(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetInt32(2)));
            }
        }
    }

    private static async Task LoadAssignmentsAsync(
        SqliteConnection connection,
        ProfileBuilder profile,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT a.character_id,
                   r.canonical_name,
                   a.target_squad_id,
                   a.desired_role,
                   r.tags_json
            FROM profile_assignments a
            JOIN roster_characters r ON r.character_id = a.character_id
            WHERE a.profile_id = $profileId
            ORDER BY r.canonical_name COLLATE NOCASE;
            """;
        command.Parameters.AddWithValue("$profileId", profile.Id.ToString("D"));
        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var roleValue = reader.GetInt32(3);
            if (!Enum.IsDefined(typeof(DesiredFleetRole), roleValue))
            {
                throw new InvalidOperationException(
                    $"Profile '{profile.Name}' contains unsupported role value {roleValue}.");
            }

            profile.Assignments.Add(new ProfileAssignment(
                reader.GetInt64(0),
                reader.GetString(1),
                Guid.Parse(reader.GetString(2)),
                (DesiredFleetRole)roleValue)
            {
                Tags = DeserializeTags(reader.GetString(4)),
            });
        }
    }

    private static async Task UpsertProfileAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FleetProfile profile,
        string nowText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO fleet_profiles (id, name, schema_version, created_utc, updated_utc)
            VALUES ($id, $name, 1, $now, $now)
            ON CONFLICT(id) DO UPDATE SET
                name = excluded.name,
                schema_version = excluded.schema_version,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$id", profile.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", profile.Name.Trim());
        command.Parameters.AddWithValue("$now", nowText);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DeleteProfileContentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            DELETE FROM profile_assignments WHERE profile_id = $profileId;
            DELETE FROM profile_wings WHERE profile_id = $profileId;
            """;
        command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task InsertHierarchyAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FleetProfile profile,
        CancellationToken cancellationToken)
    {
        for (var wingIndex = 0; wingIndex < profile.Wings.Count; wingIndex++)
        {
            var wing = profile.Wings[wingIndex];
            await using (var wingCommand = connection.CreateCommand())
            {
                wingCommand.Transaction = transaction;
                wingCommand.CommandText =
                    "INSERT INTO profile_wings (id, profile_id, name, sort_order) VALUES ($id, $profileId, $name, $sortOrder);";
                wingCommand.Parameters.AddWithValue("$id", wing.Id.ToString("D"));
                wingCommand.Parameters.AddWithValue("$profileId", profile.Id.ToString("D"));
                wingCommand.Parameters.AddWithValue("$name", wing.Name.Trim());
                wingCommand.Parameters.AddWithValue("$sortOrder", wingIndex);
                await wingCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }

            for (var squadIndex = 0; squadIndex < wing.Squads.Count; squadIndex++)
            {
                var squad = wing.Squads[squadIndex];
                await using var squadCommand = connection.CreateCommand();
                squadCommand.Transaction = transaction;
                squadCommand.CommandText =
                    "INSERT INTO profile_squads (id, wing_id, name, sort_order) VALUES ($id, $wingId, $name, $sortOrder);";
                squadCommand.Parameters.AddWithValue("$id", squad.Id.ToString("D"));
                squadCommand.Parameters.AddWithValue("$wingId", wing.Id.ToString("D"));
                squadCommand.Parameters.AddWithValue("$name", squad.Name.Trim());
                squadCommand.Parameters.AddWithValue("$sortOrder", squadIndex);
                await squadCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task InsertAssignmentsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FleetProfile profile,
        string nowText,
        CancellationToken cancellationToken)
    {
        foreach (var assignment in profile.Assignments)
        {
            await UpsertRosterCharacterAsync(
                connection,
                transaction,
                assignment,
                nowText,
                cancellationToken).ConfigureAwait(false);

            await using var assignmentCommand = connection.CreateCommand();
            assignmentCommand.Transaction = transaction;
            assignmentCommand.CommandText =
                """
                INSERT INTO profile_assignments (
                    profile_id,
                    character_id,
                    target_squad_id,
                    desired_role)
                VALUES ($profileId, $characterId, $targetSquadId, $desiredRole);
                """;
            assignmentCommand.Parameters.AddWithValue("$profileId", profile.Id.ToString("D"));
            assignmentCommand.Parameters.AddWithValue("$characterId", assignment.CharacterId);
            assignmentCommand.Parameters.AddWithValue(
                "$targetSquadId",
                assignment.TargetSquadId.ToString("D"));
            assignmentCommand.Parameters.AddWithValue("$desiredRole", (int)assignment.DesiredRole);
            await assignmentCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task UpsertRosterCharacterAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ProfileAssignment assignment,
        string nowText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO roster_characters (
                character_id,
                canonical_name,
                tags_json,
                last_resolved_utc)
            VALUES ($characterId, $name, $tags, $now)
            ON CONFLICT(character_id) DO UPDATE SET
                canonical_name = excluded.canonical_name,
                tags_json = excluded.tags_json,
                last_resolved_utc = excluded.last_resolved_utc;
            """;
        command.Parameters.AddWithValue("$characterId", assignment.CharacterId);
        command.Parameters.AddWithValue("$name", assignment.CharacterName.Trim());
        command.Parameters.AddWithValue(
            "$tags",
            JsonSerializer.Serialize(assignment.Tags, SerializerOptions));
        command.Parameters.AddWithValue("$now", nowText);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string[] DeserializeTags(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json, SerializerOptions) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private sealed class ProfileBuilder(Guid id, string name)
    {
        public Guid Id { get; } = id;

        public string Name { get; } = name;

        public List<WingBuilder> Wings { get; } = [];

        public List<ProfileAssignment> Assignments { get; } = [];

        public FleetProfile Build() => new(
            Id,
            Name,
            Wings.Select(wing => wing.Build()).ToArray(),
            Assignments.ToArray());
    }

    private sealed class WingBuilder(Guid id, string name, int sortOrder)
    {
        public Guid Id { get; } = id;

        public string Name { get; } = name;

        public int SortOrder { get; } = sortOrder;

        public List<ProfileSquad> Squads { get; } = [];

        public ProfileWing Build() => new(Id, Name, SortOrder, Squads.ToArray());
    }
}
