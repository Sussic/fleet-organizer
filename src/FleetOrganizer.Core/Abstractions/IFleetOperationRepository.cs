using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;

namespace FleetOrganizer.Core.Abstractions;

public interface IFleetOperationRepository
{
    Task<FleetOperation?> LoadAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<FleetOperation?> LoadLatestResumableAsync(
        CancellationToken cancellationToken = default);

    Task<LiveFleetSnapshot?> LoadInitialSnapshotAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        FleetOperation operation,
        LiveFleetSnapshot? initialSnapshot = null,
        CancellationToken cancellationToken = default);
}
