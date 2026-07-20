using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;

namespace FleetOrganizer.Infrastructure.Esi;

internal sealed class EveFleetInvitationService(
    ILiveFleetService liveFleetService,
    IFleetWriteService writeService) : IFleetInvitationService
{
    public async Task<FleetInvitationBatchResult> InviteAsync(
        long expectedFleetId,
        long targetWingId,
        long targetSquadId,
        string targetName,
        DesiredFleetRole desiredRole,
        IReadOnlyList<FleetInvitationCandidate> characters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentNullException.ThrowIfNull(characters);

        if (characters.Count == 0)
        {
            return new(0, [], [], "Add at least one character to invite.");
        }

        if (!FleetCommandSeatRules.AcceptsPilotCount(desiredRole, characters.Count))
        {
            return Failed(characters, "A commander seat accepts exactly one invitation at a time.");
        }

        if (desiredRole == DesiredFleetRole.FleetCommander)
        {
            return Failed(characters, "Fleet Commander invitation is not exposed here. Transfer fleet boss separately from the guarded high-impact controls.");
        }

        var liveResult = await liveFleetService
            .RefreshCurrentAsync(cancellationToken)
            .ConfigureAwait(false);
        if (liveResult.Status != LiveFleetLoadStatus.Ready || liveResult.Snapshot is null)
        {
            return Failed(characters, liveResult.UserMessage);
        }

        var snapshot = liveResult.Snapshot;
        if (snapshot.FleetId != expectedFleetId)
        {
            return Failed(
                characters,
                "The signed-in character is now in a different fleet. Refresh before inviting.");
        }

        if (!snapshot.IsFleetBoss)
        {
            return Failed(
                characters,
                "The signed-in character is no longer fleet boss. No invitation was sent.");
        }

        var targetWing = snapshot.Wings.SingleOrDefault(wing => wing.WingId == targetWingId);
        var targetSquad = targetSquadId > 0
            ? targetWing?.Squads.SingleOrDefault(squad => squad.SquadId == targetSquadId)
            : null;
        var targetExists = desiredRole == DesiredFleetRole.WingCommander
            ? targetWing is not null
            : targetWing is not null && targetSquad is not null;
        if (!targetExists)
        {
            return Failed(
                characters,
                $"{targetName} no longer exists. Refresh and choose a current fleet position.");
        }

        var occupied = desiredRole switch
        {
            DesiredFleetRole.WingCommander => snapshot.Members.Any(member =>
                member.WingId == targetWingId &&
                string.Equals(member.Role, "wing_commander", StringComparison.Ordinal)),
            DesiredFleetRole.SquadCommander => snapshot.Members.Any(member =>
                member.SquadId == targetSquadId &&
                string.Equals(member.Role, "squad_commander", StringComparison.Ordinal)),
            _ => false,
        };
        if (occupied)
        {
            return Failed(characters, $"{targetName} is occupied. Move or demote the current commander first.");
        }

        var sent = new List<FleetInvitationCandidate>();
        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            var target = new FleetOperationTarget(
                character.CharacterId,
                character.CharacterName,
                targetWing!.Name,
                targetSquad?.Name ?? "Wing Command",
                targetWingId,
                targetSquadId,
                desiredRole,
                InviteDirectlyToRole: true);
            var result = await writeService
                .InviteAsync(expectedFleetId, target, cancellationToken)
                .ConfigureAwait(false);
            if (!result.IsSuccess)
            {
                var unsent = characters.Skip(index).ToArray();
                return new(
                    characters.Count,
                    sent,
                    unsent,
                    sent.Count == 0
                        ? result.UserMessage
                        : $"Sent {sent.Count} invitation{(sent.Count == 1 ? string.Empty : "s")}; stopped before {character.CharacterName}: {result.UserMessage}");
            }

            sent.Add(character);
        }

        return new(
            characters.Count,
            sent,
            [],
            $"Sent {sent.Count} invitation{(sent.Count == 1 ? string.Empty : "s")} to {targetName}. Waiting for acceptance in EVE.");
    }

    private static FleetInvitationBatchResult Failed(
        IReadOnlyList<FleetInvitationCandidate> characters,
        string message) =>
        new(characters.Count, [], characters.ToArray(), message);
}
