using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.App.ViewModels;

public static class LiveFleetProfileComposer
{
    public static FleetProfile Compose(
        Guid profileId,
        LiveFleetSnapshot snapshot,
        IReadOnlyCollection<StagedLiveMoveViewModel> stagedMoves,
        IReadOnlyCollection<StagedLiveInviteViewModel> stagedInvites,
        IReadOnlyCollection<StagedLiveStructureChangeViewModel> stagedStructureChanges)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(stagedMoves);
        ArgumentNullException.ThrowIfNull(stagedInvites);
        ArgumentNullException.ThrowIfNull(stagedStructureChanges);

        var profile = FleetProfileFactory.FromLiveFleet(snapshot, "Fleet Desk live changes (system)") with
        {
            Id = profileId,
        };
        var targetWingIds = new Dictionary<long, Guid>();
        var targetSquadIds = new Dictionary<(long WingId, long SquadId), Guid>();
        foreach (var (liveWing, profileWing) in snapshot.Wings
            .Zip(profile.Wings.OrderBy(wing => wing.SortOrder)))
        {
            targetWingIds[liveWing.WingId] = profileWing.Id;
            foreach (var (liveSquad, profileSquad) in liveWing.Squads
                .Zip(profileWing.Squads.OrderBy(squad => squad.SortOrder)))
            {
                targetSquadIds[(liveWing.WingId, liveSquad.SquadId)] = profileSquad.Id;
            }
        }

        profile = ApplyStructureChanges(
            profile,
            targetWingIds,
            targetSquadIds,
            stagedStructureChanges);

        var movesByCharacterId = stagedMoves.ToDictionary(move => move.CharacterId);
        var invitationAssignments = stagedInvites.Select(invite => new ProfileAssignment(
            invite.CharacterId,
            invite.CharacterName,
            targetSquadIds[(invite.TargetWingId, invite.TargetSquadId)],
            invite.DesiredRole));
        return profile with
        {
            Assignments = profile.Assignments
                .Select(assignment => movesByCharacterId.TryGetValue(
                    assignment.CharacterId,
                    out var move)
                        ? assignment with
                        {
                            TargetSquadId = ResolveTargetSquadId(snapshot, targetSquadIds, move),
                            DesiredRole = move.DesiredRole,
                        }
                        : assignment)
                .Concat(invitationAssignments)
                .ToArray(),
        };
    }

    private static FleetProfile ApplyStructureChanges(
        FleetProfile profile,
        IReadOnlyDictionary<long, Guid> targetWingIds,
        IReadOnlyDictionary<(long WingId, long SquadId), Guid> targetSquadIds,
        IEnumerable<StagedLiveStructureChangeViewModel> changes)
    {
        var wings = profile.Wings.OrderBy(wing => wing.SortOrder).ToList();
        foreach (var change in changes)
        {
            switch (change.Kind)
            {
                case StagedLiveStructureChangeKind.AddWing:
                    wings.Add(new ProfileWing(Guid.NewGuid(), change.NewName, wings.Count, []));
                    break;

                case StagedLiveStructureChangeKind.AddSquad:
                {
                    var wingId = targetWingIds[change.WingId];
                    var index = wings.FindIndex(wing => wing.Id == wingId);
                    var wing = wings[index];
                    var squads = wing.Squads.OrderBy(squad => squad.SortOrder).ToList();
                    squads.Add(new ProfileSquad(Guid.NewGuid(), change.NewName, squads.Count));
                    wings[index] = wing with { Squads = squads };
                    break;
                }

                case StagedLiveStructureChangeKind.RenameWing:
                {
                    var wingId = targetWingIds[change.WingId];
                    var index = wings.FindIndex(wing => wing.Id == wingId);
                    wings[index] = wings[index] with { Name = change.NewName };
                    break;
                }

                case StagedLiveStructureChangeKind.RenameSquad:
                {
                    var wingId = targetWingIds[change.WingId];
                    var squadId = targetSquadIds[(change.WingId, change.SquadId)];
                    var wingIndex = wings.FindIndex(wing => wing.Id == wingId);
                    var wing = wings[wingIndex];
                    var squads = wing.Squads.OrderBy(squad => squad.SortOrder).ToList();
                    var squadIndex = squads.FindIndex(squad => squad.Id == squadId);
                    squads[squadIndex] = squads[squadIndex] with { Name = change.NewName };
                    wings[wingIndex] = wing with { Squads = squads };
                    break;
                }

                default:
                    throw new InvalidOperationException($"Unknown live structure change {change.Kind}.");
            }
        }

        return profile with { Wings = wings };
    }

    private static Guid ResolveTargetSquadId(
        LiveFleetSnapshot snapshot,
        IReadOnlyDictionary<(long WingId, long SquadId), Guid> targetSquadIds,
        StagedLiveMoveViewModel move)
    {
        if (move.TargetSquadId > 0)
        {
            return targetSquadIds[(move.TargetWingId, move.TargetSquadId)];
        }

        var firstSquad = snapshot.Wings
            .Single(wing => wing.WingId == move.TargetWingId)
            .Squads
            .OrderBy(squad => squad.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault() ?? throw new InvalidOperationException(
                $"{move.TargetName} has no squad that can anchor a saved commander placement.");
        return targetSquadIds[(move.TargetWingId, firstSquad.SquadId)];
    }
}
