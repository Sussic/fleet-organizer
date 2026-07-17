using FleetOrganizer.Core.Operations;

namespace FleetOrganizer.Core.Abstractions;

public interface IFleetWriteService
{
    Task<FleetWriteResult> CreateWingAsync(
        long fleetId,
        CancellationToken cancellationToken = default);

    Task<FleetWriteResult> RenameWingAsync(
        long fleetId,
        FleetOperationTarget target,
        CancellationToken cancellationToken = default);

    Task<FleetWriteResult> CreateSquadAsync(
        long fleetId,
        FleetOperationTarget target,
        CancellationToken cancellationToken = default);

    Task<FleetWriteResult> RenameSquadAsync(
        long fleetId,
        FleetOperationTarget target,
        CancellationToken cancellationToken = default);

    Task<FleetWriteResult> InviteAsync(
        long fleetId,
        FleetOperationTarget target,
        CancellationToken cancellationToken = default);

    Task<FleetWriteResult> PlaceAsync(
        long fleetId,
        FleetOperationTarget target,
        CancellationToken cancellationToken = default);

    Task<FleetWriteResult> PromoteCommanderAsync(
        long fleetId,
        FleetOperationTarget target,
        CancellationToken cancellationToken = default);
}
