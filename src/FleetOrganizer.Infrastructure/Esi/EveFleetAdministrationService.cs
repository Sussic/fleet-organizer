using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Fleets;

namespace FleetOrganizer.Infrastructure.Esi;

internal sealed class EveFleetAdministrationService(
    EveEsiClient esiClient,
    ILiveFleetService liveFleetService) : IFleetAdministrationService
{
    public async Task<FleetAdministrationResult> UpdateFleetSettingsAsync(
        long expectedFleetId,
        bool isFreeMove,
        string motd,
        CancellationToken cancellationToken = default)
    {
        var snapshotResult = await RequireCurrentBossAsync(expectedFleetId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshotResult.Failure is not null)
        {
            return snapshotResult.Failure;
        }

        var result = await esiClient.UpdateFleetAsync(
            expectedFleetId,
            new UpdateFleetRequest(isFreeMove, motd),
            cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? new FleetAdministrationResult(true, 1, "Fleet free-move setting and MOTD updated.")
            : Failed(result.UserMessage ?? "EVE rejected the fleet settings update.");
    }

    public async Task<FleetAdministrationResult> KickMembersAsync(
        long expectedFleetId,
        long[] characterIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(characterIds);
        var snapshotResult = await RequireCurrentBossAsync(expectedFleetId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshotResult.Failure is not null)
        {
            return snapshotResult.Failure;
        }

        var snapshot = snapshotResult.Snapshot!;
        var targets = characterIds.Distinct().ToArray();
        if (targets.Length == 0)
        {
            return Failed("Select at least one fleet member to kick.");
        }

        if (targets.Contains(snapshot.FleetBossId))
        {
            return Failed("The current fleet boss cannot be kicked. Transfer fleet boss first.");
        }

        var missing = targets.Where(id => snapshot.Members.All(member => member.CharacterId != id)).ToArray();
        if (missing.Length > 0)
        {
            return Failed("The live fleet changed. Refresh before approving the kick again.");
        }

        var completed = 0;
        foreach (var characterId in targets)
        {
            var result = await esiClient
                .KickFleetMemberAsync(expectedFleetId, characterId, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                return new FleetAdministrationResult(
                    false,
                    completed,
                    completed == 0
                        ? result.UserMessage ?? "EVE rejected the kick."
                        : $"Kicked {completed} member{(completed == 1 ? string.Empty : "s")}, then EVE rejected the next write. Refresh before retrying.");
            }

            completed++;
        }

        return new FleetAdministrationResult(
            true,
            completed,
            $"Kicked {completed} fleet member{(completed == 1 ? string.Empty : "s")}.");
    }

    public async Task<FleetAdministrationResult> TransferFleetBossAsync(
        long expectedFleetId,
        long characterId,
        CancellationToken cancellationToken = default)
    {
        var snapshotResult = await RequireCurrentBossAsync(expectedFleetId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshotResult.Failure is not null)
        {
            return snapshotResult.Failure;
        }

        var snapshot = snapshotResult.Snapshot!;
        var member = snapshot.Members.SingleOrDefault(candidate => candidate.CharacterId == characterId);
        if (member is null)
        {
            return Failed("That character is no longer in this fleet. Refresh before trying again.");
        }

        if (characterId == snapshot.FleetBossId)
        {
            return Failed($"{member.CharacterName} is already fleet boss.");
        }

        var result = await esiClient
            .TransferFleetBossAsync(expectedFleetId, characterId, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? new FleetAdministrationResult(
                true,
                1,
                $"Fleet boss transferred to {member.CharacterName}. This sign-in is now read-only until that character returns fleet boss or you sign in as the new boss.")
            : Failed(result.UserMessage ?? "EVE rejected the fleet-boss transfer.");
    }

    public async Task<FleetAdministrationResult> DeleteSquadAsync(
        long expectedFleetId,
        long squadId,
        CancellationToken cancellationToken = default)
    {
        var snapshotResult = await RequireCurrentBossAsync(expectedFleetId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshotResult.Failure is not null)
        {
            return snapshotResult.Failure;
        }

        var snapshot = snapshotResult.Snapshot!;
        var squad = snapshot.Wings
            .SelectMany(wing => wing.Squads.Select(candidate => (Wing: wing, Squad: candidate)))
            .SingleOrDefault(item => item.Squad.SquadId == squadId);
        if (squad.Wing is null || squad.Squad is null)
        {
            return Failed("That squad no longer exists. Refresh the fleet.");
        }

        if (snapshot.Members.Any(member => member.SquadId == squadId))
        {
            return Failed($"{squad.Wing.Name} / {squad.Squad.Name} is not empty. Move or kick its members first.");
        }

        var result = await esiClient
            .DeleteFleetSquadAsync(expectedFleetId, squadId, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? new FleetAdministrationResult(true, 1, $"Deleted empty squad {squad.Wing.Name} / {squad.Squad.Name}.")
            : Failed(result.UserMessage ?? "EVE rejected the squad deletion.");
    }

    public async Task<FleetAdministrationResult> DeleteWingAsync(
        long expectedFleetId,
        long wingId,
        CancellationToken cancellationToken = default)
    {
        var snapshotResult = await RequireCurrentBossAsync(expectedFleetId, cancellationToken)
            .ConfigureAwait(false);
        if (snapshotResult.Failure is not null)
        {
            return snapshotResult.Failure;
        }

        var snapshot = snapshotResult.Snapshot!;
        var wing = snapshot.Wings.SingleOrDefault(candidate => candidate.WingId == wingId);
        if (wing is null)
        {
            return Failed("That wing no longer exists. Refresh the fleet.");
        }

        if (snapshot.Members.Any(member => member.WingId == wingId))
        {
            return Failed($"{wing.Name} is not empty. Move or kick its members first.");
        }

        var result = await esiClient
            .DeleteFleetWingAsync(expectedFleetId, wingId, cancellationToken)
            .ConfigureAwait(false);
        return result.IsSuccess
            ? new FleetAdministrationResult(true, 1, $"Deleted empty wing {wing.Name}.")
            : Failed(result.UserMessage ?? "EVE rejected the wing deletion.");
    }

    private async Task<(LiveFleetSnapshot? Snapshot, FleetAdministrationResult? Failure)> RequireCurrentBossAsync(
        long expectedFleetId,
        CancellationToken cancellationToken)
    {
        var result = await liveFleetService.RefreshCurrentAsync(cancellationToken).ConfigureAwait(false);
        if (result.Status != LiveFleetLoadStatus.Ready || result.Snapshot is null)
        {
            return (null, Failed(result.UserMessage));
        }

        if (result.Snapshot.FleetId != expectedFleetId)
        {
            return (null, Failed("The signed-in character is now in a different fleet. Refresh and review again."));
        }

        if (!result.Snapshot.IsFleetBoss)
        {
            return (null, Failed("The signed-in character is no longer fleet boss. No high-impact write was sent."));
        }

        return (result.Snapshot, null);
    }

    private static FleetAdministrationResult Failed(string message) => new(false, 0, message);
}
