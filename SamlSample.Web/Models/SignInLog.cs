namespace SamlSample.Web.Models;

public class SignInLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string AppName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string CertThumbprint { get; set; } = string.Empty;
    public DateTime SignedInUtc { get; set; } = DateTime.UtcNow;
}
