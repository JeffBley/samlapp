namespace SamlSample.Web.Services;

public interface ISecretHashingService
{
    (string hash, string salt) HashSecret(string secret);
    bool Verify(string secret, string hash, string salt);
}
