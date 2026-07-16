using FleetOrganizer.Core.Abstractions;

namespace FleetOrganizer.Infrastructure.Esi;

internal sealed class EveCharacterNameResolver(EveEsiClient esiClient) : ICharacterNameResolver
{
    public async Task<CharacterResolutionResult> ResolveAsync(
        string[] characterNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(characterNames);

        var requestedNames = characterNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedNames.Length == 0)
        {
            return new CharacterResolutionResult([], [], null);
        }

        var resolvedByName = new Dictionary<string, ResolvedCharacter>(
            StringComparer.OrdinalIgnoreCase);
        string? failureMessage = null;

        foreach (var batch in requestedNames.Chunk(500))
        {
            var result = await esiClient
                .PostUniverseIdsAsync(batch, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess || result.Value is null)
            {
                failureMessage ??= result.UserMessage ??
                    "EVE character names could not be resolved right now.";
                continue;
            }

            foreach (var character in result.Value.Characters ?? [])
            {
                resolvedByName[character.Name] = new ResolvedCharacter(
                    character.Id,
                    character.Name);
            }
        }

        var resolved = requestedNames
            .Where(resolvedByName.ContainsKey)
            .Select(name => resolvedByName[name])
            .ToArray();
        var unresolved = requestedNames
            .Where(name => !resolvedByName.ContainsKey(name))
            .ToArray();

        return new CharacterResolutionResult(resolved, unresolved, failureMessage);
    }
}
