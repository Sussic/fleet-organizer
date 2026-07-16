namespace FleetOrganizer.Infrastructure.Configuration;

public sealed class EveDeveloperOptions
{
    public const string SectionName = "EveDeveloper";

    public static IReadOnlyList<string> RequiredScopes { get; } =
    [
        "esi-fleets.read_fleet.v1",
        "esi-fleets.write_fleet.v1",
    ];

    public string ClientId { get; init; } = string.Empty;

    public string RedirectUri { get; init; } = "http://127.0.0.1:42873/callback";

    public string CompatibilityDate { get; init; } = "2026-07-15";

    public bool IsClientIdConfigured =>
        !string.IsNullOrWhiteSpace(ClientId) &&
        !ClientId.Contains("PASTE", StringComparison.OrdinalIgnoreCase);

    public Uri GetValidatedRedirectUri()
    {
        if (!Uri.TryCreate(RedirectUri, UriKind.Absolute, out var redirectUri) ||
            redirectUri.Scheme != Uri.UriSchemeHttp ||
            redirectUri.Host != "127.0.0.1" ||
            redirectUri.IsDefaultPort ||
            !string.Equals(redirectUri.AbsolutePath, "/callback", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The EVE callback must use http://127.0.0.1:<port>/callback.");
        }

        return redirectUri;
    }
}
