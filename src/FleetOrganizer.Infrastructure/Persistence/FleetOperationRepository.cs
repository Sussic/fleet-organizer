using System.Globalization;
using System.Text.Json;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;
using Microsoft.Data.Sqlite;

namespace FleetOrganizer.Infrastructure.Persistence;

internal sealed class FleetOperationRepository(
    IAppDataPaths paths) : IFleetOperationRepository
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    public async Task<FleetOperation?> LoadAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        return await LoadAsync(connection, operationId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FleetOperation?> LoadLatestResumableAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT id
            FROM active_operations
            WHERE state NOT IN ($complete, $cancelled)
            ORDER BY updated_utc DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$complete", (int)OperationState.Complete);
        command.Parameters.AddWithValue("$cancelled", (int)OperationState.Cancelled);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string id
            ? await LoadAsync(connection, Guid.Parse(id), cancellationToken).ConfigureAwait(false)
            : null;
    }

    public async Task<LiveFleetSnapshot?> LoadInitialSnapshotAsync(
        Guid operationId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT snapshot_json
            FROM fleet_snapshots
            WHERE operation_id = $operationId
            ORDER BY captured_utc
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return value is string json
            ? JsonSerializer.Deserialize<LiveFleetSnapshot>(json, SerializerOptions)
            : null;
    }

    public async Task SaveAsync(
        FleetOperation operation,
        LiveFleetSnapshot? initialSnapshot = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await EnableForeignKeysAsync(connection, cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(cancellationToken)
            .ConfigureAwait(false);

        await UpsertOperationAsync(connection, transaction, operation, cancellationToken)
            .ConfigureAwait(false);
        await ReplaceStepsAsync(connection, transaction, operation, cancellationToken)
            .ConfigureAwait(false);
        if (initialSnapshot is not null)
        {
            await InsertSnapshotAsync(
                connection,
                transaction,
                operation.Id,
                initialSnapshot,
                cancellationToken).ConfigureAwait(false);
        }

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

    private static async Task<FleetOperation?> LoadAsync(
        SqliteConnection connection,
        Guid operationId,
        CancellationToken cancellationToken)
    {
        FleetOperationHeader? header;
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT o.profile_id,
                       p.name,
                       o.fleet_id,
                       o.state,
                       o.created_utc,
                       o.updated_utc,
                       o.cancellation_reason
                FROM active_operations o
                JOIN fleet_profiles p ON p.id = o.profile_id
                WHERE o.id = $id;
                """;
            command.Parameters.AddWithValue("$id", operationId.ToString("D"));
            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            header = await reader.ReadAsync(cancellationToken).ConfigureAwait(false)
                ? new FleetOperationHeader(
                    Guid.Parse(reader.GetString(0)),
                    reader.GetString(1),
                    reader.GetInt64(2),
                    ReadEnum<OperationState>(reader.GetInt32(3), "operation state"),
                    ParseDate(reader.GetString(4)),
                    ParseDate(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : reader.GetString(6))
                : null;
        }

        if (header is null)
        {
            return null;
        }

        var steps = new List<FleetOperationStep>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT step_key,
                       sort_order,
                       step_type,
                       state,
                       attempts,
                       last_failure_kind,
                       last_failure_message,
                       retry_after_utc,
                       updated_utc,
                       payload_json
                FROM operation_steps
                WHERE operation_id = $operationId
                ORDER BY sort_order, step_key;
                """;
            command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
            await using var reader = await command
                .ExecuteReaderAsync(cancellationToken)
                .ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var target = JsonSerializer.Deserialize<FleetOperationTarget>(
                    reader.GetString(9),
                    SerializerOptions) ?? throw new InvalidOperationException(
                        "An operation step contains an empty target payload.");
                steps.Add(new FleetOperationStep(
                    reader.GetString(0),
                    reader.GetInt32(1),
                    ReadEnum<FleetOperationStepType>(reader.GetString(2), "step type"),
                    target,
                    ReadEnum<FleetOperationStepState>(reader.GetInt32(3), "step state"),
                    reader.GetInt32(4),
                    reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7)),
                    ParseDate(reader.GetString(8))));
            }
        }

        return new FleetOperation(
            operationId,
            header.ProfileId,
            header.ProfileName,
            header.FleetId,
            header.State,
            header.CreatedAtUtc,
            header.UpdatedAtUtc,
            header.Message,
            steps.ToArray());
    }

    private static async Task UpsertOperationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FleetOperation operation,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO active_operations (
                id,
                profile_id,
                fleet_id,
                state,
                created_utc,
                updated_utc,
                cancellation_reason)
            VALUES ($id, $profileId, $fleetId, $state, $created, $updated, $message)
            ON CONFLICT(id) DO UPDATE SET
                state = excluded.state,
                updated_utc = excluded.updated_utc,
                cancellation_reason = excluded.cancellation_reason;
            """;
        command.Parameters.AddWithValue("$id", operation.Id.ToString("D"));
        command.Parameters.AddWithValue("$profileId", operation.ProfileId.ToString("D"));
        command.Parameters.AddWithValue("$fleetId", operation.FleetId);
        command.Parameters.AddWithValue("$state", (int)operation.State);
        command.Parameters.AddWithValue("$created", FormatDate(operation.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", FormatDate(operation.UpdatedAtUtc));
        command.Parameters.AddWithValue("$message", (object?)operation.Message ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReplaceStepsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        FleetOperation operation,
        CancellationToken cancellationToken)
    {
        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText =
                "DELETE FROM operation_steps WHERE operation_id = $operationId;";
            deleteCommand.Parameters.AddWithValue("$operationId", operation.Id.ToString("D"));
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var step in operation.Steps)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO operation_steps (
                    operation_id,
                    step_key,
                    step_type,
                    target_id,
                    state,
                    attempts,
                    last_failure_kind,
                    last_failure_message,
                    updated_utc,
                    sort_order,
                    payload_json,
                    retry_after_utc)
                VALUES (
                    $operationId,
                    $stepKey,
                    $stepType,
                    $targetId,
                    $state,
                    $attempts,
                    $failureKind,
                    $failureMessage,
                    $updated,
                    $sortOrder,
                    $payload,
                    $retryAfter);
                """;
            command.Parameters.AddWithValue("$operationId", operation.Id.ToString("D"));
            command.Parameters.AddWithValue("$stepKey", step.StepKey);
            command.Parameters.AddWithValue("$stepType", step.Type.ToString());
            command.Parameters.AddWithValue(
                "$targetId",
                step.Target.CharacterId.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$state", (int)step.State);
            command.Parameters.AddWithValue("$attempts", step.Attempts);
            command.Parameters.AddWithValue(
                "$failureKind",
                (object?)step.LastFailureKind ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$failureMessage",
                (object?)step.Message ?? DBNull.Value);
            command.Parameters.AddWithValue("$updated", FormatDate(step.UpdatedAtUtc));
            command.Parameters.AddWithValue("$sortOrder", step.SortOrder);
            command.Parameters.AddWithValue(
                "$payload",
                JsonSerializer.Serialize(step.Target, SerializerOptions));
            command.Parameters.AddWithValue(
                "$retryAfter",
                step.RetryAfterUtc is DateTimeOffset retryAfter
                    ? FormatDate(retryAfter)
                    : DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task InsertSnapshotAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid operationId,
        LiveFleetSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO fleet_snapshots (
                id,
                operation_id,
                fleet_id,
                captured_utc,
                snapshot_json)
            VALUES ($id, $operationId, $fleetId, $captured, $snapshot);
            """;
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        command.Parameters.AddWithValue("$operationId", operationId.ToString("D"));
        command.Parameters.AddWithValue("$fleetId", snapshot.FleetId);
        command.Parameters.AddWithValue("$captured", FormatDate(snapshot.ConfirmedAtUtc));
        command.Parameters.AddWithValue(
            "$snapshot",
            JsonSerializer.Serialize(snapshot, SerializerOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task EnableForeignKeysAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static TEnum ReadEnum<TEnum>(int value, string label)
        where TEnum : struct, Enum =>
        Enum.IsDefined(typeof(TEnum), value)
            ? (TEnum)Enum.ToObject(typeof(TEnum), value)
            : throw new InvalidOperationException($"Unknown {label} value {value}.");

    private static TEnum ReadEnum<TEnum>(string value, string label)
        where TEnum : struct, Enum =>
        Enum.TryParse<TEnum>(value, ignoreCase: false, out var result)
            ? result
            : throw new InvalidOperationException($"Unknown {label} '{value}'.");

    private static DateTimeOffset ParseDate(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static string FormatDate(DateTimeOffset value) =>
        value.ToString("O", CultureInfo.InvariantCulture);

    private sealed record FleetOperationHeader(
        Guid ProfileId,
        string ProfileName,
        long FleetId,
        OperationState State,
        DateTimeOffset CreatedAtUtc,
        DateTimeOffset UpdatedAtUtc,
        string? Message);
}
