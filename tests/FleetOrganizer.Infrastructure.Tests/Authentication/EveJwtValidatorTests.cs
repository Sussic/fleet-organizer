using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FleetOrganizer.Infrastructure.Authentication;
using Microsoft.IdentityModel.Tokens;

namespace FleetOrganizer.Infrastructure.Tests.Authentication;

public sealed class EveJwtValidatorTests
{
    private const string ClientId = "fleet-organizer-test-client";
    private const string KeyId = "test-signing-key";

    [Fact]
    public async Task ValidateAcceptsSignedTokenForClientAndRequiredScopes()
    {
        var fixture = CreateTokenFixture(EveDeveloperScopes());
        using var httpClient = new HttpClient(new StaticJsonHandler(fixture.JwksJson));
        var validator = new EveJwtValidator(httpClient, TimeProvider.System);

        var result = await validator.ValidateAsync(
            fixture.AccessToken,
            CreateMetadata(),
            ClientId,
            EveDeveloperScopes(),
            CancellationToken.None);

        Assert.Equal(2_123_456_789L, result.Character.CharacterId);
        Assert.Equal("Fleet Boss", result.Character.CharacterName);
        Assert.Equal(EveDeveloperScopes(), result.Character.GrantedScopes.ToArray());
        Assert.True(result.ExpiresUtc > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ValidateRejectsTokenMissingWriteScope()
    {
        var fixture = CreateTokenFixture(["esi-fleets.read_fleet.v1"]);
        using var httpClient = new HttpClient(new StaticJsonHandler(fixture.JwksJson));
        var validator = new EveJwtValidator(httpClient, TimeProvider.System);

        await Assert.ThrowsAsync<SecurityTokenValidationException>(() => validator.ValidateAsync(
            fixture.AccessToken,
            CreateMetadata(),
            ClientId,
            EveDeveloperScopes(),
            CancellationToken.None));
    }

    private static TokenFixture CreateTokenFixture(string[] scopes)
    {
        using var rsa = RSA.Create();
        rsa.KeySize = 2_048;
        var securityKey = new RsaSecurityKey(rsa)
        {
            KeyId = KeyId,
        };
        var descriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtRegisteredClaimNames.Aud] = new[] { "EVE Online", ClientId },
                [JwtRegisteredClaimNames.Sub] = "CHARACTER:EVE:2123456789",
                ["name"] = "Fleet Boss",
                ["scp"] = scopes,
            },
            Expires = DateTime.UtcNow.AddMinutes(20),
            Issuer = "https://login.eveonline.com",
            SigningCredentials = new SigningCredentials(
                securityKey,
                SecurityAlgorithms.RsaSha256),
        };
        var handler = new JwtSecurityTokenHandler();
        var accessToken = handler.WriteToken(handler.CreateToken(descriptor));
        var publicParameters = rsa.ExportParameters(includePrivateParameters: false);
        var jwksJson = JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = KeyId,
                    alg = "RS256",
                    n = Base64UrlEncoder.Encode(
                        publicParameters.Modulus ?? throw new InvalidOperationException()),
                    e = Base64UrlEncoder.Encode(
                        publicParameters.Exponent ?? throw new InvalidOperationException()),
                },
            },
        });

        return new TokenFixture(accessToken, jwksJson);
    }

    private static string[] EveDeveloperScopes() =>
    [
        "esi-fleets.read_fleet.v1",
        "esi-fleets.write_fleet.v1",
    ];

    private static EveSsoMetadata CreateMetadata() => new(
        new Uri("https://login.eveonline.com/v2/oauth/authorize"),
        new Uri("https://login.eveonline.com/v2/oauth/token"),
        new Uri("https://login.eveonline.com/oauth/jwks"));

    private sealed record TokenFixture(string AccessToken, string JwksJson);

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
