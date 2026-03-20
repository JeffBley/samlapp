namespace SamlSample.Web.Models;

public class SamlCertificate
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int AppId { get; set; }
    public string Thumbprint { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public DateTimeOffset NotBeforeUtc { get; set; }
    public DateTimeOffset NotAfterUtc { get; set; }
    public string Base64Der { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
