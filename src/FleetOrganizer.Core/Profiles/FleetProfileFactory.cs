using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;

namespace FleetOrganizer.Core.Profiles;

public static class FleetProfileFactory
{
    public static FleetProfile FromLiveFleet(LiveFleetSnapshot snapshot, string name)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var squadIdMap = new Dictionary<long, Guid>();
        var firstSquadByWing = new Dictionary<long, Guid>();
        var profileWings = new List<ProfileWing>();

        for (var wingIndex = 0; wingIndex < snapshot.Wings.Length; wingIndex++)
        {
            var liveWing = snapshot.Wings[wingIndex];
            var profileSquads = new List<ProfileSquad>();
            for (var squadIndex = 0; squadIndex < liveWing.Squads.Length; squadIndex++)
            {
                var liveSquad = liveWing.Squads[squadIndex];
                var profileSquadId = Guid.NewGuid();
                squadIdMap[liveSquad.SquadId] = profileSquadId;
                firstSquadByWing.TryAdd(liveWing.WingId, profileSquadId);
                profileSquads.Add(new ProfileSquad(
                    profileSquadId,
                    liveSquad.Name,
                    squadIndex));
            }

            profileWings.Add(new ProfileWing(
                Guid.NewGuid(),
                liveWing.Name,
                wingIndex,
                profileSquads));
        }

        var fallbackSquadId = profileWings
            .SelectMany(wing => wing.Squads)
            .Select(squad => (Guid?)squad.Id)
            .FirstOrDefault();
        var assignments = new List<ProfileAssignment>();

        foreach (var member in snapshot.Members)
        {
            var targetSquadId = GetTargetSquadId(
                member,
                squadIdMap,
                firstSquadByWing,
                fallbackSquadId);
            if (targetSquadId is null)
            {
                continue;
            }

            assignments.Add(new ProfileAssignment(
                member.CharacterId,
                member.CharacterName,
                targetSquadId.Value,
                ParseRole(member.Role)));
        }

        return new FleetProfile(
            Guid.NewGuid(),
            name.Trim(),
            profileWings,
            assignments);
    }

    public static FleetProfile Duplicate(FleetProfile source, string name)
    {
        ArgumentNullException.ThrowIfNull(source);

        var squadIdMap = new Dictionary<Guid, Guid>();
        var wings = source.Wings
            .OrderBy(wing => wing.SortOrder)
            .Select(wing => new ProfileWing(
                Guid.NewGuid(),
                wing.Name,
                wing.SortOrder,
                wing.Squads
                    .OrderBy(squad => squad.SortOrder)
                    .Select(squad =>
                    {
                        var newId = Guid.NewGuid();
                        squadIdMap[squad.Id] = newId;
                        return new ProfileSquad(newId, squad.Name, squad.SortOrder);
                    })
                    .ToArray()))
            .ToArray();
        var assignments = source.Assignments
            .Where(assignment => squadIdMap.ContainsKey(assignment.TargetSquadId))
            .Select(assignment => new ProfileAssignment(
                assignment.CharacterId,
                assignment.CharacterName,
                squadIdMap[assignment.TargetSquadId],
                assignment.DesiredRole)
            {
                Tags = assignment.Tags.ToArray(),
            })
            .ToArray();

        var shipRules = source.ShipRules
            .Where(rule => squadIdMap.ContainsKey(rule.TargetSquadId))
            .Select(rule => new ProfileShipRule(
                Guid.NewGuid(),
                rule.ShipTypeName,
                squadIdMap[rule.TargetSquadId],
                rule.SortOrder))
            .ToArray();

        return new FleetProfile(Guid.NewGuid(), name.Trim(), wings, assignments)
        {
            ShipRules = shipRules,
        };
    }

    private static Guid? GetTargetSquadId(
        LiveFleetMember member,
        Dictionary<long, Guid> squadIdMap,
        Dictionary<long, Guid> firstSquadByWing,
        Guid? fallbackSquadId)
    {
        if (squadIdMap.TryGetValue(member.SquadId, out var exactSquadId))
        {
            return exactSquadId;
        }

        return firstSquadByWing.TryGetValue(member.WingId, out var wingSquadId)
            ? wingSquadId
            : fallbackSquadId;
    }

    private static DesiredFleetRole ParseRole(string role) => role switch
    {
        "fleet_commander" => DesiredFleetRole.FleetCommander,
        "wing_commander" => DesiredFleetRole.WingCommander,
        "squad_commander" => DesiredFleetRole.SquadCommander,
        _ => DesiredFleetRole.SquadMember,
    };
}
