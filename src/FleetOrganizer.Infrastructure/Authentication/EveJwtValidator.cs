using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using FleetOrganizer.Core.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace FleetOrganizer.Infrastructure.Authentication;

internal sealed class EveJwtValidator(HttpClient httpClient, TimeProvider timeProvider)
{
    private static readonly string[] ValidIssuers =
    [
        "https://login.eveonline.com",
        "https://login.eveonline.com/",
        "login.eveonline.com",
    ];

    public async Task<ValidatedEveToken> ValidateAsync(
        string accessToken,
        EveSsoMetadata metadata,
        string clientId,
        IReadOnlyList<string> requiredScopes,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentNullException.ThrowIfNull(requiredScopes);

        using var jwksResponse = await httpClient
            .GetAsync(metadata.JwksEndpoint, cancellationToken)
            .ConfigureAwait(false);

        if (!jwksResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"EVE SSO signing keys could not be loaded ({(int)jwksResponse.StatusCode}).");
        }

        var jwksJson = await jwksResponse.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);
        var signingKeys = new JsonWebKeySet(jwksJson).Keys;

        if (signingKeys.Count == 0)
        {
            throw new InvalidOperationException("EVE SSO returned no signing keys.");
        }

        var validationParameters = new TokenValidationParameters
        {
            ClockSkew = TimeSpan.FromMinutes(2),
            IssuerSigningKeys = signingKeys,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ValidateAudience = false,
            ValidateIssuer = true,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidIssuers = ValidIssuers,
        };

        var handler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false,
        };

        handler.ValidateToken(accessToken, validationParameters, out var validatedToken);

        if (validatedToken is not JwtSecurityToken jwtToken ||
            !string.Equals(jwtToken.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
        {
            throw new SecurityTokenValidationException("EVE SSO returned an unsupported access token.");
        }

        if (!jwtToken.Audiences.Contains("EVE Online", StringComparer.Ordinal) ||
            !jwtToken.Audiences.Contains(clientId, StringComparer.Ordinal))
        {
            throw new SecurityTokenInvalidAudienceException(
                "The EVE access token was issued for a different application.");
        }

        var claims = ReadRequiredClaims(accessToken, requiredScopes);
        var character = new AuthenticatedCharacter(
            claims.CharacterId,
            claims.CharacterName,
            claims.Scopes,
            timeProvider.GetUtcNow());

        return new ValidatedEveToken(
            character,
            new DateTimeOffset(jwtToken.ValidTo, TimeSpan.Zero));
    }

    private static RequiredClaims ReadRequiredClaims(
        string accessToken,
        IReadOnlyList<string> requiredScopes)
    {
        var segments = accessToken.Split('.');
        if (segments.Length != 3)
        {
            throw new SecurityTokenValidationException("EVE SSO returned a malformed access token.");
        }

        using var payload = JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(segments[1]));
        var root = payload.RootElement;
        var subject = GetRequiredString(root, "sub");
        var characterName = GetRequiredString(root, "name");
        var characterId = ParseCharacterId(subject);
        var scopes = GetScopes(root);

        var missingScopes = requiredScopes
            .Where(requiredScope => !scopes.Contains(requiredScope, StringComparer.Ordinal))
            .ToArray();

        if (missingScopes.Length > 0)
        {
            throw new SecurityTokenValidationException(
                $"EVE authorization is missing required fleet scopes: {string.Join(", ", missingScopes)}.");
        }

        return new RequiredClaims(characterId, characterName, scopes);
    }

    private static string GetRequiredString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            throw new SecurityTokenValidationException(
                $"The EVE access token is missing its {propertyName} claim.");
        }

        return property.GetString()!;
    }

    private static long ParseCharacterId(string subject)
    {
        const string prefix = "CHARACTER:EVE:";
        if (!subject.StartsWith(prefix, StringComparison.Ordinal) ||
            !long.TryParse(
                subject.AsSpan(prefix.Length),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var characterId) ||
            characterId <= 0)
        {
            throw new SecurityTokenValidationException(
                "The EVE access token contains an invalid character subject.");
        }

        return characterId;
    }

    private static string[] GetScopes(JsonElement root)
    {
        if (!root.TryGetProperty("scp", out var scopeClaim) ||
            scopeClaim.ValueKind != JsonValueKind.Array)
        {
            throw new SecurityTokenValidationException(
                "The EVE access token is missing its scope claim.");
        }

        var scopes = scopeClaim
            .EnumerateArray()
            .Where(scope => scope.ValueKind == JsonValueKind.String)
            .Select(scope => scope.GetString())
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope!)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (scopes.Length == 0)
        {
            throw new SecurityTokenValidationException(
                "The EVE access token contains no authorized scopes.");
        }

        return scopes;
    }

    private sealed record RequiredClaims(
        long CharacterId,
        string CharacterName,
        IReadOnlyList<string> Scopes);
}
