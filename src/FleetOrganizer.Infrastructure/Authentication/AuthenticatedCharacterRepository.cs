using System.Globalization;
using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Authentication;
using Microsoft.Data.Sqlite;

namespace FleetOrganizer.Infrastructure.Authentication;

internal sealed class AuthenticatedCharacterRepository(IAppDataPaths paths)
{
    public async Task<StoredAuthenticatedCharacter?> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT character_id,
                   character_name,
                   encrypted_refresh_token,
                   granted_scopes,
                   last_validated_utc
            FROM authenticated_characters
            ORDER BY last_validated_utc DESC
            LIMIT 1;
            """;

        await using var reader = await command
            .ExecuteReaderAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var scopes = reader.GetString(3)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return new StoredAuthenticatedCharacter(
            reader.GetInt64(0),
            reader.GetString(1),
            (byte[])reader.GetValue(2),
            scopes,
            DateTimeOffset.Parse(
                reader.GetString(4),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));
    }

    public async Task SaveAsync(
        AuthenticatedCharacter character,
        byte[] encryptedRefreshToken,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(character);
        ArgumentNullException.ThrowIfNull(encryptedRefreshToken);

        if (encryptedRefreshToken.Length == 0)
        {
            throw new ArgumentException(
                "The encrypted refresh token cannot be empty.",
                nameof(encryptedRefreshToken));
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var transaction = connection.BeginTransaction();

        await using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM authenticated_characters;";
            await deleteCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        await using (var insertCommand = connection.CreateCommand())
        {
            insertCommand.Transaction = transaction;
            insertCommand.CommandText =
                """
                INSERT INTO authenticated_characters (
                    character_id,
                    character_name,
                    encrypted_refresh_token,
                    granted_scopes,
                    last_validated_utc)
                VALUES (
                    $characterId,
                    $characterName,
                    $encryptedRefreshToken,
                    $grantedScopes,
                    $lastValidatedUtc);
                """;
            insertCommand.Parameters.AddWithValue("$characterId", character.CharacterId);
            insertCommand.Parameters.AddWithValue("$characterName", character.CharacterName);
            insertCommand.Parameters.AddWithValue("$encryptedRefreshToken", encryptedRefreshToken);
            insertCommand.Parameters.AddWithValue(
                "$grantedScopes",
                string.Join(' ', character.GrantedScopes.Order(StringComparer.Ordinal)));
            insertCommand.Parameters.AddWithValue(
                "$lastValidatedUtc",
                character.LastValidatedUtc.ToString("O", CultureInfo.InvariantCulture));

            await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        transaction.Commit();
    }

    public async Task DeleteAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM authenticated_characters;";
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private SqliteConnection CreateConnection()
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = paths.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString();

        return new SqliteConnection(connectionString);
    }
}
