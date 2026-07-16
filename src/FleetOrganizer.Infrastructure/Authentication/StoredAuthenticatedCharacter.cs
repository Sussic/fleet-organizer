namespace FleetOrganizer.Infrastructure.Authentication;

internal sealed record StoredAuthenticatedCharacter(
    long CharacterId,
    string CharacterName,
    byte[] EncryptedRefreshToken,
    IReadOnlyList<string> GrantedScopes,
    DateTimeOffset LastValidatedUtc);
