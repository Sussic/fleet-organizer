using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.Infrastructure.Esi;

internal sealed class EveFleetRebuildService(
    EveEsiClient esiClient,
    ILiveFleetService liveFleetService) : IFleetRebuildService
{
    private const string StagingName = "Unknown";
    private static readonly TimeSpan WritePacingDelay = TimeSpan.FromMilliseconds(150);

    public async Task<FleetRebuildResult> RebuildAsync(
        long expectedFleetId,
        FleetProfile profile,
        IProgress<FleetRebuildProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var errors = ProfileValidator.Validate(profile);
        if (errors.Count > 0)
        {
            return Failed(0, 0, $"The saved setup is invalid: {errors[0].Message}");
        }

        if (profile.Wings.Any(wing =>
                string.Equals(wing.Name, StagingName, StringComparison.OrdinalIgnoreCase) ||
                wing.Squads.Any(squad => string.Equals(
                    squad.Name,
                    StagingName,
                    StringComparison.OrdinalIgnoreCase))))
        {
            return Failed(0, 0, $"'{StagingName}' is reserved for the temporary rebuild staging area. Rename it in the saved setup first.");
        }

        if (profile.Wings.Count >= ProfileValidator.MaximumWings)
        {
            return Failed(
                0,
                0,
                $"Clean rebuild reserves one temporary '{StagingName}' wing, so the saved setup may contain at most {ProfileValidator.MaximumWings - 1} wings.");
        }

        var liveResult = await liveFleetService.RefreshCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (!TryRequireBoss(liveResult, expectedFleetId, out var snapshot, out var failure))
        {
            return Failed(0, 0, failure);
        }

        var currentSnapshot = snapshot!;
        var effectiveProfile = ShipRuleResolver.Resolve(profile, currentSnapshot).EffectiveProfile;
        var assignments = effectiveProfile.Assignments.ToDictionary(assignment => assignment.CharacterId);
        var unknownMembers = currentSnapshot.Members.Count(member => !assignments.ContainsKey(member.CharacterId));
        var unsafeFleetCommander = effectiveProfile.Assignments.FirstOrDefault(assignment =>
            assignment.DesiredRole == DesiredFleetRole.FleetCommander &&
            assignment.CharacterId != currentSnapshot.FleetBossId);
        if (unsafeFleetCommander is not null)
        {
            return Failed(
                0,
                unknownMembers,
                $"{unsafeFleetCommander.CharacterName} is saved as Fleet Commander, but {currentSnapshot.FleetBossName} is the current fleet boss. Clean rebuild never transfers fleet boss implicitly.");
        }

        var completedWrites = 0;
        progress?.Report(new FleetRebuildProgress(
            completedWrites,
            $"Preparing the {StagingName} staging area…"));

        async Task<EsiResult<T>> WriteAsync<T>(
            Func<Task<EsiResult<T>>> action,
            string successMessage)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await action().ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                throw new FleetRebuildWriteException(
                    result.UserMessage ?? "EVE rejected a clean-rebuild write.");
            }

            completedWrites++;
            progress?.Report(new FleetRebuildProgress(completedWrites, successMessage));
            await Task.Delay(WritePacingDelay, cancellationToken).ConfigureAwait(false);
            return result;
        }

        try
        {
            var stagingWingMatches = currentSnapshot.Wings.Where(wing =>
                string.Equals(wing.Name, StagingName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (stagingWingMatches.Length > 1)
            {
                return Failed(completedWrites, unknownMembers, "More than one live wing is named Unknown. Rename all but one in EVE, refresh, and try again.");
            }

            var stagingWing = stagingWingMatches.SingleOrDefault();
            long stagingWingId;
            if (stagingWing is null)
            {
                if (currentSnapshot.Wings.Length >= ProfileValidator.MaximumWings)
                {
                    stagingWing = currentSnapshot.Wings
                        .OrderBy(wing => currentSnapshot.Members.Count(member => member.WingId == wing.WingId))
                        .ThenBy(wing => wing.Name, StringComparer.OrdinalIgnoreCase)
                        .First();
                    stagingWingId = stagingWing.WingId;
                }
                else
                {
                    var created = await WriteAsync(
                        () => esiClient.CreateFleetWingAsync(expectedFleetId, cancellationToken),
                        $"Created the {StagingName} wing.").ConfigureAwait(false);
                    stagingWingId = created.Value!.WingId;
                }

                await WriteAsync(
                    () => esiClient.RenameFleetWingAsync(
                        expectedFleetId,
                        stagingWingId,
                        new RenameFleetStructureRequest(StagingName),
                        cancellationToken),
                    $"Named the staging wing {StagingName}.").ConfigureAwait(false);
            }
            else
            {
                stagingWingId = stagingWing.WingId;
            }

            var stagingSquadMatches = stagingWing?.Squads.Where(squad =>
                string.Equals(squad.Name, StagingName, StringComparison.OrdinalIgnoreCase)).ToArray() ?? [];
            if (stagingSquadMatches.Length > 1)
            {
                return Failed(completedWrites, unknownMembers, "More than one squad in the staging wing is named Unknown. Rename all but one in EVE, refresh, and try again.");
            }

            var stagingSquad = stagingSquadMatches.SingleOrDefault();
            long stagingSquadId;
            if (stagingSquad is null)
            {
                if (stagingWing is not null &&
                    stagingWing.Squads.Length >= ProfileValidator.MaximumSquadsPerWing)
                {
                    stagingSquad = stagingWing.Squads
                        .OrderBy(squad => currentSnapshot.Members.Count(member => member.SquadId == squad.SquadId))
                        .ThenBy(squad => squad.Name, StringComparer.OrdinalIgnoreCase)
                        .First();
                    stagingSquadId = stagingSquad.SquadId;
                }
                else
                {
                    var created = await WriteAsync(
                        () => esiClient.CreateFleetSquadAsync(
                            expectedFleetId,
                            stagingWingId,
                            cancellationToken),
                        $"Created the {StagingName} squad.").ConfigureAwait(false);
                    stagingSquadId = created.Value!.SquadId;
                }

                await WriteAsync(
                    () => esiClient.RenameFleetSquadAsync(
                        expectedFleetId,
                        stagingSquadId,
                        new RenameFleetStructureRequest(StagingName),
                        cancellationToken),
                    $"Named the staging squad {StagingName}.").ConfigureAwait(false);
            }
            else
            {
                stagingSquadId = stagingSquad.SquadId;
            }

            foreach (var member in currentSnapshot.Members.OrderBy(member =>
                member.CharacterId == currentSnapshot.FleetBossId ? 1 : 0))
            {
                if (member.WingId == stagingWingId &&
                    member.SquadId == stagingSquadId &&
                    string.Equals(member.Role, "squad_member", StringComparison.Ordinal))
                {
                    continue;
                }

                await WriteAsync(
                    () => esiClient.MoveFleetMemberAsync(
                        expectedFleetId,
                        member.CharacterId,
                        new MoveFleetMemberRequest("squad_member", stagingSquadId, stagingWingId),
                        cancellationToken),
                    $"Staged {member.CharacterName} in {StagingName}.").ConfigureAwait(false);
            }

            liveResult = await liveFleetService.RefreshCurrentAsync(cancellationToken).ConfigureAwait(false);
            if (!TryRequireBoss(liveResult, expectedFleetId, out snapshot, out failure))
            {
                return Failed(completedWrites, unknownMembers, failure);
            }

            currentSnapshot = snapshot!;

            if (currentSnapshot.Members.Any(member =>
                member.WingId != stagingWingId || member.SquadId != stagingSquadId))
            {
                return Failed(
                    completedWrites,
                    unknownMembers,
                    $"Not every pilot reached {StagingName}. The rebuild stopped before deleting any occupied structure; refresh and run it again.");
            }

            foreach (var squad in currentSnapshot.Wings
                .Single(wing => wing.WingId == stagingWingId)
                .Squads
                .Where(squad => squad.SquadId != stagingSquadId))
            {
                await WriteAsync(
                    () => esiClient.DeleteFleetSquadAsync(
                        expectedFleetId,
                        squad.SquadId,
                        cancellationToken),
                    $"Deleted empty squad {squad.Name}.").ConfigureAwait(false);
            }

            foreach (var wing in currentSnapshot.Wings.Where(wing => wing.WingId != stagingWingId))
            {
                await WriteAsync(
                    () => esiClient.DeleteFleetWingAsync(
                        expectedFleetId,
                        wing.WingId,
                        cancellationToken),
                    $"Deleted empty wing {wing.Name}.").ConfigureAwait(false);
            }

            var squadIds = new Dictionary<Guid, (long WingId, long SquadId)>();
            foreach (var wing in effectiveProfile.Wings.OrderBy(wing => wing.SortOrder))
            {
                var createdWing = await WriteAsync(
                    () => esiClient.CreateFleetWingAsync(expectedFleetId, cancellationToken),
                    $"Created target wing {wing.Name}.").ConfigureAwait(false);
                var wingId = createdWing.Value!.WingId;
                await WriteAsync(
                    () => esiClient.RenameFleetWingAsync(
                        expectedFleetId,
                        wingId,
                        new RenameFleetStructureRequest(wing.Name),
                        cancellationToken),
                    $"Named target wing {wing.Name}.").ConfigureAwait(false);

                foreach (var squad in wing.Squads.OrderBy(squad => squad.SortOrder))
                {
                    var createdSquad = await WriteAsync(
                        () => esiClient.CreateFleetSquadAsync(
                            expectedFleetId,
                            wingId,
                            cancellationToken),
                        $"Created {wing.Name} / {squad.Name}.").ConfigureAwait(false);
                    var squadId = createdSquad.Value!.SquadId;
                    await WriteAsync(
                        () => esiClient.RenameFleetSquadAsync(
                            expectedFleetId,
                            squadId,
                            new RenameFleetStructureRequest(squad.Name),
                            cancellationToken),
                        $"Named {wing.Name} / {squad.Name}.").ConfigureAwait(false);
                    squadIds[squad.Id] = (wingId, squadId);
                }
            }

            var liveCharacterIds = currentSnapshot.Members.Select(member => member.CharacterId).ToHashSet();
            foreach (var assignment in effectiveProfile.Assignments
                .Where(assignment =>
                    liveCharacterIds.Contains(assignment.CharacterId) &&
                    assignment.DesiredRole != DesiredFleetRole.FleetCommander)
                .OrderBy(assignment => assignment.CharacterName, StringComparer.OrdinalIgnoreCase))
            {
                var target = squadIds[assignment.TargetSquadId];
                await WriteAsync(
                    () => esiClient.MoveFleetMemberAsync(
                        expectedFleetId,
                        assignment.CharacterId,
                        new MoveFleetMemberRequest("squad_member", target.SquadId, target.WingId),
                        cancellationToken),
                    $"Placed {assignment.CharacterName}.").ConfigureAwait(false);
            }

            foreach (var assignment in effectiveProfile.Assignments
                .Where(assignment => liveCharacterIds.Contains(assignment.CharacterId))
                .Where(assignment => assignment.DesiredRole != DesiredFleetRole.SquadMember)
                .OrderBy(assignment => CommanderOrder(assignment.DesiredRole)))
            {
                var target = squadIds[assignment.TargetSquadId];
                var placement = GetPlacement(assignment.DesiredRole, target.WingId, target.SquadId);
                await WriteAsync(
                    () => esiClient.MoveFleetMemberAsync(
                        expectedFleetId,
                        assignment.CharacterId,
                        new MoveFleetMemberRequest(placement.Role, placement.SquadId, placement.WingId),
                        cancellationToken),
                    $"Assigned {assignment.CharacterName} as {RoleName(assignment.DesiredRole)}.").ConfigureAwait(false);
            }

            foreach (var assignment in effectiveProfile.Assignments
                .Where(assignment => !liveCharacterIds.Contains(assignment.CharacterId))
                .OrderBy(assignment => assignment.CharacterName, StringComparer.OrdinalIgnoreCase))
            {
                var target = squadIds[assignment.TargetSquadId];
                var placement = GetPlacement(assignment.DesiredRole, target.WingId, target.SquadId);
                await WriteAsync(
                    () => esiClient.InviteFleetMemberAsync(
                        expectedFleetId,
                        new InviteFleetMemberRequest(
                            assignment.CharacterId,
                            placement.Role,
                            placement.SquadId,
                            placement.WingId),
                        cancellationToken),
                    $"Invited {assignment.CharacterName} as {RoleName(assignment.DesiredRole)}.").ConfigureAwait(false);
            }

            if (unknownMembers == 0)
            {
                await WriteAsync(
                    () => esiClient.DeleteFleetWingAsync(
                        expectedFleetId,
                        stagingWingId,
                        cancellationToken),
                    $"Removed the empty {StagingName} staging wing.").ConfigureAwait(false);
            }

            return new FleetRebuildResult(
                true,
                completedWrites,
                unknownMembers,
                unknownMembers == 0
                    ? $"Clean rebuild complete after {completedWrites} ESI writes. Every live pilot matched the saved setup."
                    : $"Clean rebuild complete after {completedWrites} ESI writes. {unknownMembers} unmatched pilot{(unknownMembers == 1 ? " remains" : "s remain")} in {StagingName}.");
        }
        catch (FleetRebuildWriteException exception)
        {
            return Failed(
                completedWrites,
                unknownMembers,
                $"Clean rebuild stopped after {completedWrites} accepted write{(completedWrites == 1 ? string.Empty : "s")}: {exception.Message} Refresh the fleet and run Clean rebuild again; already accepted writes are not replayed blindly.");
        }
    }

    private static bool TryRequireBoss(
        LiveFleetLoadResult result,
        long expectedFleetId,
        out LiveFleetSnapshot? snapshot,
        out string failure)
    {
        snapshot = result.Snapshot;
        if (result.Status != LiveFleetLoadStatus.Ready || snapshot is null)
        {
            failure = result.UserMessage;
            return false;
        }

        if (snapshot.FleetId != expectedFleetId)
        {
            failure = "The signed-in character is now in a different fleet. Refresh before rebuilding.";
            return false;
        }

        if (!snapshot.IsFleetBoss)
        {
            failure = "The signed-in character is no longer fleet boss. No rebuild write was sent.";
            return false;
        }

        failure = string.Empty;
        return true;
    }

    private static (string Role, long? SquadId, long? WingId) GetPlacement(
        DesiredFleetRole role,
        long wingId,
        long squadId) => role switch
    {
        DesiredFleetRole.FleetCommander => ("fleet_commander", null, null),
        DesiredFleetRole.WingCommander => ("wing_commander", null, wingId),
        DesiredFleetRole.SquadCommander => ("squad_commander", squadId, wingId),
        _ => ("squad_member", squadId, wingId),
    };

    private static int CommanderOrder(DesiredFleetRole role) => role switch
    {
        DesiredFleetRole.SquadCommander => 0,
        DesiredFleetRole.WingCommander => 1,
        DesiredFleetRole.FleetCommander => 2,
        _ => 3,
    };

    private static string RoleName(DesiredFleetRole role) => role switch
    {
        DesiredFleetRole.FleetCommander => "Fleet Commander",
        DesiredFleetRole.WingCommander => "Wing Commander",
        DesiredFleetRole.SquadCommander => "Squad Commander",
        _ => "Squad Member",
    };

    private static FleetRebuildResult Failed(
        int completedWrites,
        int unknownMembers,
        string message) => new(false, completedWrites, unknownMembers, message);

    private sealed class FleetRebuildWriteException(string message) : Exception(message);
}
