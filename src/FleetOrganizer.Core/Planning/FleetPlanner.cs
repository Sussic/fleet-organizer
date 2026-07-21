using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.Core.Planning;

public static class FleetPlanner
{
    public static FleetDryRunPlan Build(
        FleetProfile profile,
        LiveFleetSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(snapshot);

        var validationErrors = ProfileValidator.Validate(profile);
        if (validationErrors.Count > 0)
        {
            throw new InvalidOperationException(
                $"The profile is invalid: {validationErrors[0].Message}");
        }

        var blockers = new List<FleetPlanItem>();
        var structureChanges = new List<FleetPlanItem>();
        var invitations = new List<FleetPlanItem>();
        var moves = new List<FleetPlanItem>();
        var roleChanges = new List<FleetPlanItem>();
        var alreadyCorrect = new List<FleetPlanItem>();

        if (!snapshot.IsFleetBoss)
        {
            blockers.Add(new FleetPlanItem(
                FleetPlanItemKind.Blocked,
                "Fleet-boss access is required",
                "The signed-in character must be the current fleet boss before this plan can run."));
            return CreatePlan(
                profile,
                snapshot,
                blockers,
                ignoredLiveMembers: snapshot.Members.Length);
        }

        var targetResolutions = BuildStructurePlan(
            profile,
            snapshot,
            blockers,
            structureChanges);
        AddUnmanagedCommanderBlockers(
            profile,
            snapshot,
            targetResolutions,
            blockers);
        BuildRosterPlan(
            profile,
            snapshot,
            targetResolutions,
            blockers,
            invitations,
            moves,
            roleChanges,
            alreadyCorrect);

        var profileCharacterIds = profile.Assignments
            .Select(assignment => assignment.CharacterId)
            .ToHashSet();
        var ignoredLiveMembers = snapshot.Members.Count(member =>
            !profileCharacterIds.Contains(member.CharacterId));
        var items = blockers
            .Concat(structureChanges)
            .Concat(invitations)
            .Concat(moves)
            .Concat(roleChanges)
            .Concat(alreadyCorrect)
            .ToArray();

        return new FleetDryRunPlan(
            profile.Id,
            profile.Name,
            snapshot.FleetId,
            snapshot.FleetBossName,
            items,
            ignoredLiveMembers);
    }

    private static Dictionary<Guid, TargetResolution> BuildStructurePlan(
        FleetProfile profile,
        LiveFleetSnapshot snapshot,
        List<FleetPlanItem> blockers,
        List<FleetPlanItem> structureChanges)
    {
        var resolutions = new Dictionary<Guid, TargetResolution>();
        var profileCharacterIds = profile.Assignments
            .Select(assignment => assignment.CharacterId)
            .ToHashSet();
        var exactWingIds = profile.Wings
            .SelectMany(profileWing => snapshot.Wings
                .Where(liveWing => NamesMatch(liveWing.Name, profileWing.Name)))
            .Select(liveWing => liveWing.WingId)
            .ToHashSet();
        var usedWingIds = new HashSet<long>();

        foreach (var profileWing in profile.Wings.OrderBy(wing => wing.SortOrder))
        {
            var liveWingMatches = snapshot.Wings
                .Where(wing => NamesMatch(wing.Name, profileWing.Name))
                .ToArray();
            if (liveWingMatches.Length > 1)
            {
                blockers.Add(new FleetPlanItem(
                    FleetPlanItemKind.Blocked,
                    $"Wing '{profileWing.Name}' is ambiguous",
                    "More than one live wing has this name, so Fleet Organizer cannot choose safely."));
                AddBlockedSquads(profileWing, resolutions);
                continue;
            }

            LiveFleetWing? liveWing = liveWingMatches.SingleOrDefault();
            if (liveWing is null)
            {
                liveWing = snapshot.Wings.FirstOrDefault(candidate =>
                    !usedWingIds.Contains(candidate.WingId) &&
                    !exactWingIds.Contains(candidate.WingId) &&
                    CanRenameWing(candidate, snapshot, profileCharacterIds));
                if (liveWing is not null)
                {
                    structureChanges.Add(new FleetPlanItem(
                        FleetPlanItemKind.RenameWing,
                        $"Rename wing '{liveWing.Name}' to '{profileWing.Name}'",
                        "This live wing is the next unmatched safe wing by fleet order. No unmanaged member is inside it.",
                        LiveWingId: liveWing.WingId,
                        TargetWingName: profileWing.Name,
                        PreviousName: liveWing.Name));
                }
            }

            if (liveWing is null)
            {
                structureChanges.Add(new FleetPlanItem(
                    FleetPlanItemKind.CreateWing,
                    $"Create wing '{profileWing.Name}'",
                    "No safe live wing can be matched. EVE will create it, then Fleet Organizer will name it.",
                    TargetWingName: profileWing.Name));
                AddPlannedSquads(profileWing, resolutions, structureChanges);
                continue;
            }

            usedWingIds.Add(liveWing.WingId);
            BuildSquadPlan(
                profileWing,
                liveWing,
                snapshot,
                profileCharacterIds,
                resolutions,
                blockers,
                structureChanges);
        }

        return resolutions;
    }

    private static void BuildSquadPlan(
        ProfileWing profileWing,
        LiveFleetWing liveWing,
        LiveFleetSnapshot snapshot,
        HashSet<long> profileCharacterIds,
        Dictionary<Guid, TargetResolution> resolutions,
        List<FleetPlanItem> blockers,
        List<FleetPlanItem> structureChanges)
    {
        var exactSquadIds = profileWing.Squads
            .SelectMany(profileSquad => liveWing.Squads
                .Where(liveSquad => NamesMatch(liveSquad.Name, profileSquad.Name)))
            .Select(liveSquad => liveSquad.SquadId)
            .ToHashSet();
        var usedSquadIds = new HashSet<long>();

        foreach (var profileSquad in profileWing.Squads.OrderBy(squad => squad.SortOrder))
        {
            var liveSquadMatches = liveWing.Squads
                .Where(squad => NamesMatch(squad.Name, profileSquad.Name))
                .ToArray();
            var targetLabel = $"{profileWing.Name} / {profileSquad.Name}";
            if (liveSquadMatches.Length > 1)
            {
                blockers.Add(new FleetPlanItem(
                    FleetPlanItemKind.Blocked,
                    $"Squad '{targetLabel}' is ambiguous",
                    "More than one live squad has this name under the matched wing."));
                resolutions[profileSquad.Id] = new TargetResolution(
                    profileWing.Name,
                    targetLabel,
                    liveWing.WingId,
                    null,
                    IsPlanned: false,
                    IsBlocked: true);
                continue;
            }

            LiveFleetSquad? liveSquad = liveSquadMatches.SingleOrDefault();
            if (liveSquad is null)
            {
                liveSquad = liveWing.Squads.FirstOrDefault(candidate =>
                    !usedSquadIds.Contains(candidate.SquadId) &&
                    !exactSquadIds.Contains(candidate.SquadId) &&
                    CanRenameSquad(
                        liveWing.WingId,
                        candidate.SquadId,
                        snapshot,
                        profileCharacterIds));
                if (liveSquad is not null)
                {
                    structureChanges.Add(new FleetPlanItem(
                        FleetPlanItemKind.RenameSquad,
                        $"Rename squad '{liveSquad.Name}' to '{profileSquad.Name}'",
                        $"Reuse the next unmatched safe squad under '{liveWing.Name}'. No unmanaged member is inside it.",
                        TargetSquadId: profileSquad.Id,
                        LiveWingId: liveWing.WingId,
                        LiveSquadId: liveSquad.SquadId,
                        TargetWingName: profileWing.Name,
                        TargetSquadName: profileSquad.Name,
                        PreviousName: liveSquad.Name));
                }
            }

            if (liveSquad is null)
            {
                structureChanges.Add(new FleetPlanItem(
                    FleetPlanItemKind.CreateSquad,
                    $"Create squad '{profileSquad.Name}'",
                    $"Create and name it under live wing '{liveWing.Name}'.",
                    TargetSquadId: profileSquad.Id,
                    LiveWingId: liveWing.WingId,
                    TargetWingName: profileWing.Name,
                    TargetSquadName: profileSquad.Name));
                resolutions[profileSquad.Id] = new TargetResolution(
                    profileWing.Name,
                    targetLabel,
                    liveWing.WingId,
                    null,
                    IsPlanned: true,
                    IsBlocked: false);
                continue;
            }

            usedSquadIds.Add(liveSquad.SquadId);
            resolutions[profileSquad.Id] = new TargetResolution(
                profileWing.Name,
                targetLabel,
                liveWing.WingId,
                liveSquad.SquadId,
                IsPlanned: false,
                IsBlocked: false);
        }
    }

    private static void AddPlannedSquads(
        ProfileWing profileWing,
        Dictionary<Guid, TargetResolution> resolutions,
        List<FleetPlanItem> structureChanges)
    {
        foreach (var profileSquad in profileWing.Squads.OrderBy(squad => squad.SortOrder))
        {
            var targetLabel = $"{profileWing.Name} / {profileSquad.Name}";
            structureChanges.Add(new FleetPlanItem(
                FleetPlanItemKind.CreateSquad,
                $"Create squad '{profileSquad.Name}'",
                $"Create it after new wing '{profileWing.Name}'.",
                TargetSquadId: profileSquad.Id,
                TargetWingName: profileWing.Name,
                TargetSquadName: profileSquad.Name));
            resolutions[profileSquad.Id] = new TargetResolution(
                profileWing.Name,
                targetLabel,
                null,
                null,
                IsPlanned: true,
                IsBlocked: false);
        }
    }

    private static void AddBlockedSquads(
        ProfileWing profileWing,
        Dictionary<Guid, TargetResolution> resolutions)
    {
        foreach (var profileSquad in profileWing.Squads)
        {
            resolutions[profileSquad.Id] = new TargetResolution(
                profileWing.Name,
                $"{profileWing.Name} / {profileSquad.Name}",
                null,
                null,
                IsPlanned: false,
                IsBlocked: true);
        }
    }

    private static bool CanRenameWing(
        LiveFleetWing wing,
        LiveFleetSnapshot snapshot,
        HashSet<long> profileCharacterIds) =>
        snapshot.Members
            .Where(member => member.WingId == wing.WingId)
            .All(member => profileCharacterIds.Contains(member.CharacterId));

    private static bool CanRenameSquad(
        long wingId,
        long squadId,
        LiveFleetSnapshot snapshot,
        HashSet<long> profileCharacterIds) =>
        snapshot.Members
            .Where(member => member.WingId == wingId && member.SquadId == squadId)
            .All(member => profileCharacterIds.Contains(member.CharacterId));

    private static void AddUnmanagedCommanderBlockers(
        FleetProfile profile,
        LiveFleetSnapshot snapshot,
        Dictionary<Guid, TargetResolution> targetResolutions,
        List<FleetPlanItem> blockers)
    {
        var managedCharacterIds = profile.Assignments
            .Select(assignment => assignment.CharacterId)
            .ToHashSet();

        foreach (var assignment in profile.Assignments.Where(assignment =>
            assignment.DesiredRole is
                DesiredFleetRole.SquadCommander or DesiredFleetRole.WingCommander))
        {
            var target = targetResolutions[assignment.TargetSquadId];
            if (target.IsBlocked || target.LiveWingId is null)
            {
                continue;
            }

            var occupant = snapshot.Members.FirstOrDefault(member =>
                member.CharacterId != assignment.CharacterId &&
                (assignment.DesiredRole == DesiredFleetRole.WingCommander
                    ? member.WingId == target.LiveWingId &&
                        string.Equals(member.Role, "wing_commander", StringComparison.Ordinal)
                    : target.LiveSquadId is not null &&
                        member.SquadId == target.LiveSquadId &&
                        string.Equals(member.Role, "squad_commander", StringComparison.Ordinal)));
            if (occupant is not null && !managedCharacterIds.Contains(occupant.CharacterId))
            {
                blockers.Add(new FleetPlanItem(
                    FleetPlanItemKind.Blocked,
                    $"Commander slot for {assignment.CharacterName} is occupied",
                    $"{occupant.CharacterName} is not in this profile. Fleet Organizer will not demote an unmanaged live member.",
                    assignment.CharacterId,
                    assignment.TargetSquadId));
            }
        }
    }

    private static void BuildRosterPlan(
        FleetProfile profile,
        LiveFleetSnapshot snapshot,
        Dictionary<Guid, TargetResolution> targetResolutions,
        List<FleetPlanItem> blockers,
        List<FleetPlanItem> invitations,
        List<FleetPlanItem> moves,
        List<FleetPlanItem> roleChanges,
        List<FleetPlanItem> alreadyCorrect)
    {
        var liveMembers = snapshot.Members.ToDictionary(member => member.CharacterId);

        foreach (var assignment in profile.Assignments
            .OrderBy(assignment => assignment.CharacterName, StringComparer.OrdinalIgnoreCase))
        {
            if (AddFleetBossSafetyBlocker(assignment, snapshot, blockers))
            {
                continue;
            }

            if (assignment.DesiredRole == DesiredFleetRole.FleetCommander)
            {
                if (!liveMembers.TryGetValue(assignment.CharacterId, out var fleetBoss) ||
                    !RoleMatches(fleetBoss.Role, DesiredFleetRole.FleetCommander))
                {
                    blockers.Add(new FleetPlanItem(
                        FleetPlanItemKind.Blocked,
                        $"Fleet boss {assignment.CharacterName} is not visible in fleet command",
                        "Refresh the live fleet before attempting to use this profile.",
                        assignment.CharacterId,
                        assignment.TargetSquadId));
                }
                else
                {
                    alreadyCorrect.Add(new FleetPlanItem(
                        FleetPlanItemKind.AlreadyCorrect,
                        $"{assignment.CharacterName} is already fleet boss",
                        "Fleet-boss authority will be preserved.",
                        assignment.CharacterId,
                        assignment.TargetSquadId));
                }

                continue;
            }

            var target = targetResolutions[assignment.TargetSquadId];
            if (target.IsBlocked)
            {
                blockers.Add(new FleetPlanItem(
                    FleetPlanItemKind.Blocked,
                    $"Cannot place {assignment.CharacterName}",
                    $"Target '{target.Label}' cannot be matched safely.",
                    assignment.CharacterId,
                    assignment.TargetSquadId));
                continue;
            }

            if (!liveMembers.TryGetValue(assignment.CharacterId, out var liveMember))
            {
                invitations.Add(new FleetPlanItem(
                    FleetPlanItemKind.InviteCharacter,
                    $"Invite {assignment.CharacterName}",
                    $"After acceptance, place in {GetDesiredPlacement(target, assignment.DesiredRole)} as {GetRoleName(assignment.DesiredRole)}.",
                    assignment.CharacterId,
                    assignment.TargetSquadId));
                continue;
            }

            var positionIsCorrect = IsPositionCorrect(liveMember, target, assignment.DesiredRole);
            var roleIsCorrect = RoleMatches(liveMember.Role, assignment.DesiredRole);
            if (!positionIsCorrect)
            {
                moves.Add(new FleetPlanItem(
                    FleetPlanItemKind.MoveCharacter,
                    $"Move {assignment.CharacterName}",
                    $"From {GetLivePlacement(snapshot, liveMember)} to {GetDesiredPlacement(target, assignment.DesiredRole)}.",
                    assignment.CharacterId,
                    assignment.TargetSquadId));
            }

            if (!roleIsCorrect)
            {
                roleChanges.Add(new FleetPlanItem(
                    FleetPlanItemKind.ChangeRole,
                    $"Change {assignment.CharacterName}'s role",
                    $"From {GetLiveRoleName(liveMember)} to {GetRoleName(assignment.DesiredRole)}.",
                    assignment.CharacterId,
                    assignment.TargetSquadId));
            }

            if (positionIsCorrect && roleIsCorrect)
            {
                alreadyCorrect.Add(new FleetPlanItem(
                    FleetPlanItemKind.AlreadyCorrect,
                    $"{assignment.CharacterName} is already correct",
                    $"{GetDesiredPlacement(target, assignment.DesiredRole)} • {GetRoleName(assignment.DesiredRole)}.",
                    assignment.CharacterId,
                    assignment.TargetSquadId));
            }
        }
    }

    private static bool AddFleetBossSafetyBlocker(
        ProfileAssignment assignment,
        LiveFleetSnapshot snapshot,
        List<FleetPlanItem> blockers)
    {
        if (assignment.DesiredRole == DesiredFleetRole.FleetCommander &&
            assignment.CharacterId != snapshot.FleetBossId)
        {
            blockers.Add(new FleetPlanItem(
                FleetPlanItemKind.Blocked,
                $"Cannot make {assignment.CharacterName} fleet commander",
                $"{snapshot.FleetBossName} is the authenticated fleet boss. Automatic fleet-boss transfer is outside the safe operation boundary.",
                assignment.CharacterId,
                assignment.TargetSquadId));
            return true;
        }

        return false;
    }

    private static bool IsPositionCorrect(
        LiveFleetMember liveMember,
        TargetResolution target,
        DesiredFleetRole desiredRole)
    {
        if (target.IsPlanned)
        {
            return false;
        }

        return desiredRole == DesiredFleetRole.WingCommander
            ? liveMember.WingId == target.LiveWingId
            : liveMember.SquadId == target.LiveSquadId;
    }

    private static bool RoleMatches(string liveRole, DesiredFleetRole desiredRole) =>
        desiredRole switch
        {
            DesiredFleetRole.SquadMember => string.Equals(
                liveRole,
                "squad_member",
                StringComparison.Ordinal),
            DesiredFleetRole.SquadCommander => string.Equals(
                liveRole,
                "squad_commander",
                StringComparison.Ordinal),
            DesiredFleetRole.WingCommander => string.Equals(
                liveRole,
                "wing_commander",
                StringComparison.Ordinal),
            DesiredFleetRole.FleetCommander => string.Equals(
                liveRole,
                "fleet_commander",
                StringComparison.Ordinal),
            _ => false,
        };

    private static string GetDesiredPlacement(
        TargetResolution target,
        DesiredFleetRole desiredRole) =>
        desiredRole switch
        {
            DesiredFleetRole.FleetCommander => "Fleet Command",
            DesiredFleetRole.WingCommander => $"{target.WingName} / Wing Command",
            _ => target.Label,
        };

    private static string GetLivePlacement(
        LiveFleetSnapshot snapshot,
        LiveFleetMember member)
    {
        if (string.Equals(member.Role, "fleet_commander", StringComparison.Ordinal))
        {
            return "Fleet Command";
        }

        var wing = snapshot.Wings.FirstOrDefault(candidate => candidate.WingId == member.WingId);
        if (string.Equals(member.Role, "wing_commander", StringComparison.Ordinal))
        {
            return wing is null ? "an unknown wing" : $"{wing.Name} / Wing Command";
        }

        var squad = wing?.Squads.FirstOrDefault(candidate => candidate.SquadId == member.SquadId);
        return wing is null || squad is null
            ? "an unknown fleet position"
            : $"{wing.Name} / {squad.Name}";
    }

    private static string GetLiveRoleName(LiveFleetMember member) =>
        string.IsNullOrWhiteSpace(member.RoleName)
            ? member.Role.Replace('_', ' ')
            : member.RoleName;

    private static string GetRoleName(DesiredFleetRole role) => role switch
    {
        DesiredFleetRole.SquadMember => "Squad Member",
        DesiredFleetRole.SquadCommander => "Squad Commander",
        DesiredFleetRole.WingCommander => "Wing Commander",
        DesiredFleetRole.FleetCommander => "Fleet Commander",
        _ => role.ToString(),
    };

    private static bool NamesMatch(string first, string second) =>
        string.Equals(first.Trim(), second.Trim(), StringComparison.OrdinalIgnoreCase);

    private static FleetDryRunPlan CreatePlan(
        FleetProfile profile,
        LiveFleetSnapshot snapshot,
        List<FleetPlanItem> items,
        int ignoredLiveMembers) =>
        new(
            profile.Id,
            profile.Name,
            snapshot.FleetId,
            snapshot.FleetBossName,
            items.ToArray(),
            ignoredLiveMembers);

    private sealed record TargetResolution(
        string WingName,
        string Label,
        long? LiveWingId,
        long? LiveSquadId,
        bool IsPlanned,
        bool IsBlocked);
}
