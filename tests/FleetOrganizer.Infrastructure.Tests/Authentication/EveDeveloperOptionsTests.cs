using FleetOrganizer.Infrastructure.Configuration;

namespace FleetOrganizer.Infrastructure.Tests.Authentication;

public sealed class EveDeveloperOptionsTests
{
    [Fact]
    public void PublicClientIdIsConsideredConfigured()
    {
        var options = new EveDeveloperOptions
        {
            ClientId = "public-client-id",
        };

        Assert.True(options.IsClientIdConfigured);
    }

    [Fact]
    public void PlaceholderClientIdIsNotConsideredConfigured()
    {
        var options = new EveDeveloperOptions
        {
            ClientId = "PASTE_YOUR_PUBLIC_EVE_CLIENT_ID_HERE",
        };

        Assert.False(options.IsClientIdConfigured);
    }

    [Theory]
    [InlineData("https://127.0.0.1:42873/callback")]
    [InlineData("http://localhost:42873/callback")]
    [InlineData("http://127.0.0.1:42873/wrong")]
    public void RedirectUriRejectsAnythingOutsideTheRegisteredLoopbackShape(string redirectUri)
    {
        var options = new EveDeveloperOptions
        {
            RedirectUri = redirectUri,
        };

        Assert.Throws<InvalidOperationException>(options.GetValidatedRedirectUri);
    }
}
