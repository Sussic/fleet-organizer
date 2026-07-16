namespace FleetOrganizer.Core.Abstractions;

public interface ISecretProtector
{
    byte[] Protect(string plaintext);

    string Unprotect(byte[] protectedBytes);
}
