using FleetOrganizer.Core.Domain;

namespace FleetOrganizer.Core.Abstractions;

public sealed record FleetRebuildProgress(
    int CompletedWrites,
    string Message);

public sealed record FleetRebuildResult(
    bool IsSuccess,
    int CompletedWrites,
    int UnknownMembers,
    string UserMessage);

public interface IFleetRebuildService
{
    Task<FleetRebuildResult> RebuildAsync(
        long expectedFleetId,
        FleetProfile profile,
        IProgress<FleetRebuildProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
