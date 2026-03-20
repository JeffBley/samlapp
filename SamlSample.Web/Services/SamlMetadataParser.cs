using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;

namespace SamlSample.Web.Services;

public record MetadataParseResult(string? EntityId, string? SingleSignOnUrl, IReadOnlyCollection<X509Certificate2> SigningCertificates);

public class SamlMetadataParser
{
    private static readonly XNamespace MetadataNs = "urn:oasis:names:tc:SAML:2.0:metadata";
    private static readonly XNamespace DsNs = "http://www.w3.org/2000/09/xmldsig#";

    public MetadataParseResult Parse(string xml)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var entityDescriptor = doc.Root?.Name == MetadataNs + "EntityDescriptor"
            ? doc.Root
            : doc.Descendants(MetadataNs + "EntityDescriptor").FirstOrDefault();
        if (entityDescriptor is null)
        {
            throw new InvalidOperationException("Federation metadata did not contain an EntityDescriptor.");
        }

        var idpDescriptor = entityDescriptor.Element(MetadataNs + "IDPSSODescriptor");
        if (idpDescriptor is null)
        {
            throw new InvalidOperationException("Federation metadata did not contain IDPSSODescriptor.");
        }

        var ssoEndpoint = idpDescriptor
            .Elements(MetadataNs + "SingleSignOnService")
            .FirstOrDefault(x => string.Equals(x.Attribute("Binding")?.Value, "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect", StringComparison.OrdinalIgnoreCase))
            ?.Attribute("Location")?.Value;

        var signingCerts = idpDescriptor
            .Elements(MetadataNs + "KeyDescriptor")
            .Where(x =>
            {
                var useValue = x.Attribute("use")?.Value;
                return string.IsNullOrWhiteSpace(useValue) || string.Equals(useValue, "signing", StringComparison.OrdinalIgnoreCase);
            })
            .Elements(DsNs + "KeyInfo")
            .Elements(DsNs + "X509Data")
            .Elements(DsNs + "X509Certificate")
            .Select(x => x.Value?.Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .Select(x => X509CertificateLoader.LoadCertificate(Convert.FromBase64String(x!)))
            .ToList();

        return new MetadataParseResult(
            entityDescriptor.Attribute("entityID")?.Value,
            ssoEndpoint,
            signingCerts);
    }
}
