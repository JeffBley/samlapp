using SamlSample.Web.Models;

namespace SamlSample.Web.Models.Admin;

// AdminSsoViewModel replaced by AdminSsoListViewModel / AdminSsoEditViewModel in AdminSsoListViewModel.cs

public class SamlCertificateViewModel
{
    public Guid Id { get; init; }
    public string Thumbprint { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Issuer { get; init; } = string.Empty;
    public DateTimeOffset NotBeforeUtc { get; init; }
    public DateTimeOffset NotAfterUtc { get; init; }
}
