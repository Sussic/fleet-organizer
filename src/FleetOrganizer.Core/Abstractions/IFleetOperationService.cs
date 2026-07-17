using FleetOrganizer.Core.Domain;
using FleetOrganizer.Core.Fleets;
using FleetOrganizer.Core.Operations;
using FleetOrganizer.Core.Planning;

namespace FleetOrganizer.Core.Abstractions;

public interface IFleetOperationService
{
    Task<FleetOperation?> LoadLatestResumableAsync(
        CancellationToken cancellationToken = default);

    Task<FleetOperation?> LoadAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<FleetOperation[]> LoadRecentAsync(
        int maximumCount = 50,
        CancellationToken cancellationToken = default);

    Task<LiveFleetSnapshot?> LoadInitialSnapshotAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<FleetOperationStartResult> StartAsync(
        FleetProfile profile,
        FleetDryRunPlan reviewedPlan,
        CancellationToken cancellationToken = default);

    Task<FleetOperation> ContinueAsync(
        Guid operationId,
        CancellationToken cancellationToken = default);

    Task<FleetOperation> RetryStepAsync(
        Guid operationId,
        string stepKey,
        CancellationToken cancellationToken = default);

    Task<FleetOperation> SkipStepAsync(
        Guid operationId,
        string stepKey,
        CancellationToken cancellationToken = default);

    Task<FleetOperation> CancelAsync(
        Guid operationId,
        string reason,
        CancellationToken cancellationToken = default);
}

public sealed record FleetOperationStartResult(
    bool Started,
    FleetOperation? Operation,
    FleetDryRunPlan CurrentPlan,
    string UserMessage);
