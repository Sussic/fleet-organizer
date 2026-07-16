using System.Security.Cryptography;
using System.Text;
using FleetOrganizer.Infrastructure.Authentication;

namespace FleetOrganizer.Infrastructure.Tests.Authentication;

public sealed class PkceGeneratorTests
{
    [Fact]
    public void CreateProducesMatchingS256Challenge()
    {
        var values = PkceGenerator.Create();
        var expectedChallenge = Convert.ToBase64String(
                SHA256.HashData(Encoding.ASCII.GetBytes(values.Verifier)))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        Assert.Equal(43, values.Verifier.Length);
        Assert.DoesNotContain('=', values.Verifier);
        Assert.Equal(expectedChallenge, values.Challenge);
    }

    [Fact]
    public void CreateStateProducesIndependentRandomValues()
    {
        var first = PkceGenerator.CreateState();
        var second = PkceGenerator.CreateState();

        Assert.Equal(43, first.Length);
        Assert.NotEqual(first, second);
    }
}
