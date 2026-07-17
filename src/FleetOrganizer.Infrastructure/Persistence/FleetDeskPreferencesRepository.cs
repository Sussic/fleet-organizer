using System.Globalization;
using System.Text.Json;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Profiles;
using Microsoft.Data.Sqlite;

namespace FleetOrganizer.Infrastructure.Persistence;

internal sealed class FleetDeskPreferencesRepository(
    IAppDataPaths paths,
    TimeProvider timeProvider) : IFleetDeskPreferencesRepository
{
    private const string SettingsKey = "fleet_desk.preferences.v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<FleetDeskPreferences> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = $key;";
        command.Parameters.AddWithValue("$key", SettingsKey);
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        if (value is not string json)
        {
            return new FleetDeskPreferences();
        }

        try
        {
            return JsonSerializer.Deserialize<FleetDeskPreferences>(json, SerializerOptions) ??
                new FleetDeskPreferences();
        }
        catch (JsonException)
        {
            return new FleetDeskPreferences();
        }
    }

    public async Task SaveAsync(
        FleetDeskPreferences preferences,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO settings (key, value, updated_utc)
            VALUES ($key, $value, $updatedUtc)
            ON CONFLICT(key) DO UPDATE SET
                value = excluded.value,
                updated_utc = excluded.updated_utc;
            """;
        command.Parameters.AddWithValue("$key", SettingsKey);
        command.Parameters.AddWithValue(
            "$value",
            JsonSerializer.Serialize(preferences, SerializerOptions));
        command.Parameters.AddWithValue(
            "$updatedUtc",
            timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
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
}
