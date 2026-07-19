namespace FleetOrganizer.Core.Abstractions;

public interface IFleetInvitationService
{
    Task<FleetInvitationBatchResult> InviteAsync(
        long expectedFleetId,
        long targetWingId,
        long targetSquadId,
        string targetName,
        IReadOnlyList<FleetInvitationCandidate> characters,
        CancellationToken cancellationToken = default);
}

public sealed record FleetInvitationCandidate(
    long CharacterId,
    string CharacterName);

public sealed record FleetInvitationBatchResult(
    int RequestedCount,
    IReadOnlyList<FleetInvitationCandidate> Sent,
    IReadOnlyList<FleetInvitationCandidate> Unsent,
    string UserMessage)
{
    public bool IsComplete => Sent.Count == RequestedCount;
}
