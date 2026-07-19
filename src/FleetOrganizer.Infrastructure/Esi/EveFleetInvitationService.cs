using FleetOrganizer.Core.Abstractions;
using FleetOrganizer.Core.Domain;
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
        IReadOnlyList<FleetInvitationCandidate> characters,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetName);
        ArgumentNullException.ThrowIfNull(characters);

        if (characters.Count == 0)
        {
            return new(0, [], [], "Add at least one character to invite.");
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
        var targetSquad = targetWing?.Squads.SingleOrDefault(squad => squad.SquadId == targetSquadId);
        if (targetWing is null || targetSquad is null)
        {
            return Failed(
                characters,
                $"{targetName} no longer exists. Refresh and choose a current squad.");
        }

        var sent = new List<FleetInvitationCandidate>();
        for (var index = 0; index < characters.Count; index++)
        {
            var character = characters[index];
            var target = new FleetOperationTarget(
                character.CharacterId,
                character.CharacterName,
                targetWing.Name,
                targetSquad.Name,
                targetWingId,
                targetSquadId,
                DesiredFleetRole.SquadMember);
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
