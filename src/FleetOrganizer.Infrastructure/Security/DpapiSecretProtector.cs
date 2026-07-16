using System.Security.Cryptography;
using System.Text;
using FleetOrganizer.Core.Abstractions;

namespace FleetOrganizer.Infrastructure.Security;

public sealed class DpapiSecretProtector : ISecretProtector
{
    private static readonly byte[] OptionalEntropy =
        Encoding.UTF8.GetBytes("FleetOrganizer.RefreshToken.v1");

    public byte[] Protect(string plaintext)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);

        try
        {
            return ProtectedData.Protect(
                plaintextBytes,
                OptionalEntropy,
                DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    public string Unprotect(byte[] protectedBytes)
    {
        ArgumentNullException.ThrowIfNull(protectedBytes);

        if (protectedBytes.Length == 0)
        {
            throw new ArgumentException("Protected data cannot be empty.", nameof(protectedBytes));
        }

        var plaintextBytes = ProtectedData.Unprotect(
            protectedBytes,
            OptionalEntropy,
            DataProtectionScope.CurrentUser);

        try
        {
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }
}
