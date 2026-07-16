namespace FleetOrganizer.Core.Authentication;

public sealed record AuthenticatedCharacter(
    long CharacterId,
    string CharacterName,
    IReadOnlyList<string> GrantedScopes,
    DateTimeOffset LastValidatedUtc);
