using FleetOrganizer.Core.Profiles;

namespace FleetOrganizer.Core.Abstractions;

public interface IFleetDeskPreferencesRepository
{
    Task<FleetDeskPreferences> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(
        FleetDeskPreferences preferences,
        CancellationToken cancellationToken = default);
}
