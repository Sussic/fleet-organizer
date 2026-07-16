using FleetOrganizer.Core.Domain;

namespace FleetOrganizer.Core.Abstractions;

public interface IFleetProfileRepository
{
    Task<FleetProfile[]> LoadAllAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(FleetProfile profile, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid profileId, CancellationToken cancellationToken = default);
}
