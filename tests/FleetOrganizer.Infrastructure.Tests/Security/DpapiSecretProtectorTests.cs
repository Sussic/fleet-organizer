using FleetOrganizer.Infrastructure.Security;

namespace FleetOrganizer.Infrastructure.Tests.Security;

public sealed class DpapiSecretProtectorTests
{
    [Fact]
    public void ProtectAndUnprotectRoundTripsForCurrentWindowsUser()
    {
        const string refreshToken = "example-refresh-token-never-log-this";
        var protector = new DpapiSecretProtector();

        var protectedBytes = protector.Protect(refreshToken);
        var recovered = protector.Unprotect(protectedBytes);

        Assert.NotEmpty(protectedBytes);
        Assert.NotEqual(refreshToken, Convert.ToBase64String(protectedBytes));
        Assert.Equal(refreshToken, recovered);
    }
}
