namespace FleetOrganizer.Core.Abstractions;

public sealed record ResolvedCharacter(long CharacterId, string CharacterName);

public sealed record CharacterResolutionResult(
    ResolvedCharacter[] Resolved,
    string[] UnresolvedNames,
    string? UserMessage);

public interface ICharacterNameResolver
{
    Task<CharacterResolutionResult> ResolveAsync(
        string[] characterNames,
        CancellationToken cancellationToken = default);
}
