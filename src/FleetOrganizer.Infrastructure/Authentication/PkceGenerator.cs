using System.Security.Cryptography;

namespace FleetOrganizer.Infrastructure.Authentication;

internal static class PkceGenerator
{
    public static PkceValues Create()
    {
        var verifierBytes = RandomNumberGenerator.GetBytes(32);

        try
        {
            var verifier = ToBase64Url(verifierBytes);
            var challengeBytes = SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier));

            try
            {
                return new PkceValues(verifier, ToBase64Url(challengeBytes));
            }
            finally
            {
                CryptographicOperations.ZeroMemory(challengeBytes);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(verifierBytes);
        }
    }

    public static string CreateState()
    {
        var stateBytes = RandomNumberGenerator.GetBytes(32);

        try
        {
            return ToBase64Url(stateBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(stateBytes);
        }
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}

internal sealed record PkceValues(string Verifier, string Challenge);
