using FleetOrganizer.Core.Authentication;

namespace FleetOrganizer.Infrastructure.Authentication;

public interface IEveAuthenticationService
{
    AuthenticatedCharacter? CurrentCharacter { get; }

    Task<AuthenticatedCharacter?> RestoreSessionAsync(CancellationToken cancellationToken = default);

    Task<AuthenticatedCharacter> SignInAsync(CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);

    Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default);
}
