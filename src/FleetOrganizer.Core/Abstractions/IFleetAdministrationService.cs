namespace FleetOrganizer.Core.Abstractions;

public sealed record FleetAdministrationResult(
    bool IsSuccess,
    int CompletedCount,
    string UserMessage);

public interface IFleetAdministrationService
{
    Task<FleetAdministrationResult> UpdateFleetSettingsAsync(
        long expectedFleetId,
        bool isFreeMove,
        string motd,
        CancellationToken cancellationToken = default);

    Task<FleetAdministrationResult> KickMembersAsync(
        long expectedFleetId,
        long[] characterIds,
        CancellationToken cancellationToken = default);

    Task<FleetAdministrationResult> TransferFleetBossAsync(
        long expectedFleetId,
        long characterId,
        CancellationToken cancellationToken = default);

    Task<FleetAdministrationResult> DeleteSquadAsync(
        long expectedFleetId,
        long squadId,
        CancellationToken cancellationToken = default);

    Task<FleetAdministrationResult> DeleteWingAsync(
        long expectedFleetId,
        long wingId,
        CancellationToken cancellationToken = default);
}
