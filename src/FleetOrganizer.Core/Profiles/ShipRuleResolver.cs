using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;

namespace FleetOrganizer.Core.Profiles;

public static class ShipRuleResolver
{
    public static ShipRuleResolution Resolve(
        FleetProfile profile,
        LiveFleetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(snapshot);

        var assignments = profile.Assignments.ToList();
        var assignedCharacterIds = assignments
            .Select(assignment => assignment.CharacterId)
            .ToHashSet();
        var rulesByShip = profile.ShipRules
            .OrderBy(rule => rule.SortOrder)
            .Where(rule => !string.IsNullOrWhiteSpace(rule.ShipTypeName))
            .GroupBy(rule => rule.ShipTypeName.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var matchedCharacters = new List<ShipRuleMatch>();

        foreach (var member in snapshot.Members
            .OrderBy(member => member.CharacterName, StringComparer.OrdinalIgnoreCase))
        {
            if (member.CharacterId == snapshot.FleetBossId ||
                !assignedCharacterIds.Add(member.CharacterId) ||
                !rulesByShip.TryGetValue(member.ShipTypeName.Trim(), out var rule))
            {
                continue;
            }

            assignments.Add(new ProfileAssignment(
                member.CharacterId,
                member.CharacterName,
                rule.TargetSquadId,
                DesiredFleetRole.SquadMember)
            {
                Tags = ["ship rule"],
            });
            matchedCharacters.Add(new ShipRuleMatch(
                member.CharacterId,
                member.CharacterName,
                member.ShipTypeName,
                rule.TargetSquadId));
        }

        return new ShipRuleResolution(
            profile with { Assignments = assignments },
            matchedCharacters);
    }
}

public sealed record ShipRuleResolution(
    FleetProfile EffectiveProfile,
    IReadOnlyList<ShipRuleMatch> Matches);

public sealed record ShipRuleMatch(
    long CharacterId,
    string CharacterName,
    string ShipTypeName,
    Guid TargetSquadId);
