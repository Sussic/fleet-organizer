using FleetOrganizer.Core.Fleets;

namespace FleetOrganizer.Core.Abstractions;

public interface ILiveFleetService
{
    Task<LiveFleetLoadResult> LoadCurrentAsync(CancellationToken cancellationToken = default);
}
