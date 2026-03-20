namespace SamlSample.Web.Models;

public class SamlSettings
{
    public int Id { get; set; }
    public string AppName { get; set; } = "Default";
    public string SpEntityId { get; set; } = string.Empty;
    public string AssertionConsumerServiceUrl { get; set; } = string.Empty;
    public string IdpEntityId { get; set; } = string.Empty;
    public string IdpSsoUrl { get; set; } = string.Empty;
    public string FederationMetadataUrl { get; set; } = string.Empty;
    public string UrlSlug { get; set; } = string.Empty;
    /// <summary>System apps (e.g. SAML Launcher) cannot be deleted.</summary>
    public bool IsSystem { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
