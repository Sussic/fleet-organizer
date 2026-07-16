using FleetOrganizer.Core.Authentication;

namespace FleetOrganizer.Infrastructure.Authentication;

internal sealed record EveSsoMetadata(
    Uri AuthorizationEndpoint,
    Uri TokenEndpoint,
    Uri JwksEndpoint);

internal sealed record EveTokenResponse(
    string AccessToken,
    string? RefreshToken);

internal sealed record ValidatedEveToken(
    AuthenticatedCharacter Character,
    DateTimeOffset ExpiresUtc);
