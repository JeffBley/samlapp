using System.Security.Cryptography;

namespace SamlSample.Web.Services;

public class Pbkdf2SecretHashingService : ISecretHashingService
{
    private const int IterationCount = 210_000;
    private const int SaltLength = 16;
    private const int KeyLength = 32;

    public (string hash, string salt) HashSecret(string secret)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltLength);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(secret, saltBytes, IterationCount, HashAlgorithmName.SHA256, KeyLength);
        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    public bool Verify(string secret, string hash, string salt)
    {
        var saltBytes = Convert.FromBase64String(salt);
        var expectedHash = Convert.FromBase64String(hash);
        var incomingHash = Rfc2898DeriveBytes.Pbkdf2(secret, saltBytes, IterationCount, HashAlgorithmName.SHA256, expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(incomingHash, expectedHash);
    }
}
