using System.ComponentModel.DataAnnotations;

namespace SamlSample.Web.Models.Admin;

public class AdminSsoListViewModel
{
    public string Search { get; set; } = string.Empty;
    public IReadOnlyList<SamlAppSummaryViewModel> Apps { get; set; } = [];
}

public class SamlAppSummaryViewModel
{
    public int Id { get; set; }
    public string AppName { get; set; } = string.Empty;
    public string SpEntityId { get; set; } = string.Empty;
    public string UrlSlug { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public string FederationMetadataUrl { get; set; } = string.Empty;
    public bool IsConfigured { get; set; }
}

public class AdminSsoEditViewModel
{
    public int Id { get; set; }

    [Required]
    [Display(Name = "App Name")]
    public string AppName { get; set; } = string.Empty;

    [Display(Name = "SP Entity ID")]
    public string SpEntityId { get; set; } = string.Empty;

    [Display(Name = "Assertion Consumer Service URL")]
    [HttpHttpsUrl]
    public string AssertionConsumerServiceUrl { get; set; } = string.Empty;

    [Display(Name = "IdP Entity ID")]
    public string IdpEntityId { get; set; } = string.Empty;

    [Display(Name = "IdP SSO URL")]
    [HttpHttpsUrl]
    public string IdpSsoUrl { get; set; } = string.Empty;

    [Display(Name = "Federation Metadata URL")]
    [HttpHttpsUrl]
    public string FederationMetadataUrl { get; set; } = string.Empty;

    // Read-only — slug is auto-generated and not editable from the UI
    public string UrlSlug { get; set; } = string.Empty;

    public IReadOnlyList<SamlCertificateViewModel> Certificates { get; set; } = [];
}
