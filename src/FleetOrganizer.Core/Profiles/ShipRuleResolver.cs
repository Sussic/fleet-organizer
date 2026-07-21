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
        var matchedCharacters = new List<ShipRuleMatch>();
        var capacitySkipped = new Dictionary<long, ShipRuleCapacitySkip>();
        var targetCounts = profile.Assignments
            .Where(assignment => assignment.DesiredRole is
                DesiredFleetRole.SquadMember or DesiredFleetRole.SquadCommander)
            .GroupBy(assignment => assignment.TargetSquadId)
            .ToDictionary(group => group.Key, group => group.Count());

        foreach (var rule in profile.ShipRules
            .OrderBy(rule => rule.SortOrder)
            .ThenBy(rule => rule.Id))
        {
            var shipTypes = ParseShipTypes(rule.ShipTypeName);
            foreach (var member in snapshot.Members
                .OrderBy(member => member.CharacterName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(member => member.CharacterId))
            {
                if (member.CharacterId == snapshot.FleetBossId ||
                    assignedCharacterIds.Contains(member.CharacterId) ||
                    (!rule.IsFallback && !shipTypes.Contains(
                        member.ShipTypeName.Trim(),
                        StringComparer.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var targetSquadId = ChooseTarget(rule, targetCounts);
                if (targetSquadId is null)
                {
                    capacitySkipped[member.CharacterId] = new ShipRuleCapacitySkip(
                        member.CharacterId,
                        member.CharacterName,
                        member.ShipTypeName,
                        RuleName(rule));
                    continue;
                }

                assignedCharacterIds.Add(member.CharacterId);
                capacitySkipped.Remove(member.CharacterId);
                targetCounts[targetSquadId.Value] = GetCount(targetCounts, targetSquadId.Value) + 1;
                assignments.Add(new ProfileAssignment(
                    member.CharacterId,
                    member.CharacterName,
                    targetSquadId.Value,
                    DesiredFleetRole.SquadMember)
                {
                    Tags = [$"ship rule: {RuleName(rule)}"],
                });
                matchedCharacters.Add(new ShipRuleMatch(
                    member.CharacterId,
                    member.CharacterName,
                    member.ShipTypeName,
                    targetSquadId.Value,
                    RuleName(rule)));
            }
        }

        return new ShipRuleResolution(
            profile with { Assignments = assignments },
            matchedCharacters,
            capacitySkipped.Values
                .OrderBy(skipped => skipped.CharacterName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(skipped => skipped.CharacterId)
                .ToArray());
    }

    public static string[] ParseShipTypes(string value) =>
        value.Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static Guid? ChooseTarget(
        ProfileShipRule rule,
        Dictionary<Guid, int> counts)
    {
        var targets = new[] { (Guid?)rule.TargetSquadId, rule.OverflowSquadId }
            .Where(target => target.HasValue)
            .Select(target => target!.Value)
            .Distinct()
            .Where(target => GetCount(counts, target) < rule.MaximumPerSquad)
            .ToArray();
        if (targets.Length == 0)
        {
            return null;
        }

        return rule.BalanceAcrossTargets
            ? targets.OrderBy(target => GetCount(counts, target)).ThenBy(target => target).First()
            : targets[0];
    }

    private static int GetCount(Dictionary<Guid, int> counts, Guid target) =>
        counts.TryGetValue(target, out var count) ? count : 0;

    private static string RuleName(ProfileShipRule rule) =>
        string.IsNullOrWhiteSpace(rule.Label)
            ? rule.IsFallback ? "Fallback" : rule.ShipTypeName.Trim()
            : rule.Label.Trim();
}

public sealed record ShipRuleResolution(
    FleetProfile EffectiveProfile,
    IReadOnlyList<ShipRuleMatch> Matches,
    IReadOnlyList<ShipRuleCapacitySkip> CapacitySkipped);

public sealed record ShipRuleMatch(
    long CharacterId,
    string CharacterName,
    string ShipTypeName,
    Guid TargetSquadId,
    string RuleName);

public sealed record ShipRuleCapacitySkip(
    long CharacterId,
    string CharacterName,
    string ShipTypeName,
    string RuleName);
