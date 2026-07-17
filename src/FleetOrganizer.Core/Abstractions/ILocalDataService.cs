namespace FleetOrganizer.Core.Abstractions;

public interface ILocalDataService
{
    Task ResetAsync(CancellationToken cancellationToken = default);
}
