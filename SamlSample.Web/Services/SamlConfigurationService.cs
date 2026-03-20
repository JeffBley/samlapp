using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.ServiceModel.Security;
using ITfoxtec.Identity.Saml2;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using SamlSample.Web.Data;
using SamlSample.Web.Models;

namespace SamlSample.Web.Services;

public class SamlConfigurationService(
    AppDbContext dbContext,
    IHttpClientFactory httpClientFactory,
    SamlMetadataParser metadataParser,
    IHttpContextAccessor httpContextAccessor) : ISamlConfigurationService
{
    // ── App management ──────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SamlSettings>> GetAllAppsAsync(CancellationToken cancellationToken = default)
    {
        return await dbContext.SamlSettings
            .OrderBy(s => s.AppName)
            .ToListAsync(cancellationToken);
    }

    public async Task<SamlSettings> GetAppAsync(int appId, CancellationToken cancellationToken = default)
    {
        return await dbContext.SamlSettings.SingleOrDefaultAsync(s => s.Id == appId, cancellationToken)
            ?? throw new KeyNotFoundException($"App with ID {appId} not found.");
    }

    public Task<SamlSettings?> GetAppBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return dbContext.SamlSettings.SingleOrDefaultAsync(
            s => s.UrlSlug == slug, cancellationToken);
    }

    private static string GenerateSlug(string name)
    {
        var slug = name.ToLowerInvariant().Trim();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[\s_]+", "-");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\-]", string.Empty);
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"-{2,}", "-");
        return slug.Trim('-');
    }

    // Finds the lowest positive integer n not already used in urn:samlsample:web:{n}
    private async Task<int> FindLowestAvailableEntitySlotAsync(CancellationToken cancellationToken)
    {
        const string prefix = "urn:samlsample:web:";
        var usedSlots = (await dbContext.SamlSettings
                .Select(s => s.SpEntityId)
                .ToListAsync(cancellationToken))
            .Where(id => id.StartsWith(prefix, StringComparison.Ordinal))
            .Select(id => id[prefix.Length..])
            .Where(suffix => int.TryParse(suffix, out _))
            .Select(int.Parse)
            .ToHashSet();

        for (var n = 1; ; n++)
        {
            if (!usedSlots.Contains(n))
            {
                return n;
            }
        }
    }

    public async Task<SamlSettings> CreateAppAsync(string appName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            throw new InvalidOperationException("App name is required.");
        }

        var app = new SamlSettings { AppName = appName.Trim(), UrlSlug = GenerateSlug(appName) };
        dbContext.SamlSettings.Add(app);
        TrackConfigChange("SSO Configuration", "App Created", "(none)", appName.Trim(), GetActor());
        await dbContext.SaveChangesAsync(cancellationToken);

        // Auto-assign Entity ID using the lowest available slot in the urn:samlsample:web:{n} sequence
        app.SpEntityId = $"urn:samlsample:web:{await FindLowestAvailableEntitySlotAsync(cancellationToken)}";

        var req = httpContextAccessor.HttpContext?.Request;
        if (req is not null)
        {
            app.AssertionConsumerServiceUrl = $"{req.Scheme}://{req.Host}/saml/{app.UrlSlug}/acs";
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return app;
    }

    public async Task SaveAppAsync(SamlSettings settings, CancellationToken cancellationToken = default)
    {
        var existing = await GetAppAsync(settings.Id, cancellationToken);
        var actor = GetActor();
        var section = $"App [{existing.AppName}]";

        TrackConfigChange(section, "App Name", existing.AppName, settings.AppName, actor);
        TrackConfigChange(section, "SP Entity ID", existing.SpEntityId, settings.SpEntityId, actor);
        TrackConfigChange(section, "Assertion Consumer Service URL", existing.AssertionConsumerServiceUrl, settings.AssertionConsumerServiceUrl, actor);
        TrackConfigChange(section, "IdP Entity ID", existing.IdpEntityId, settings.IdpEntityId, actor);
        TrackConfigChange(section, "IdP SSO URL", existing.IdpSsoUrl, settings.IdpSsoUrl, actor);
        TrackConfigChange(section, "Federation Metadata URL", existing.FederationMetadataUrl, settings.FederationMetadataUrl, actor);

        existing.AppName = settings.AppName.Trim();
        existing.SpEntityId = settings.SpEntityId.Trim();
        existing.AssertionConsumerServiceUrl = settings.AssertionConsumerServiceUrl.Trim();
        existing.IdpEntityId = settings.IdpEntityId.Trim();
        existing.IdpSsoUrl = settings.IdpSsoUrl.Trim();
        existing.FederationMetadataUrl = settings.FederationMetadataUrl.Trim();
        // UrlSlug is not editable from the UI — preserved as-is
        existing.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAppAsync(int appId, CancellationToken cancellationToken = default)
    {
        var app = await GetAppAsync(appId, cancellationToken);
        if (app.IsSystem)
        {
            throw new InvalidOperationException("System apps cannot be deleted.");
        }
        var certs = await GetCertificatesAsync(appId, cancellationToken);
        dbContext.SamlCertificates.RemoveRange(certs);
        dbContext.SamlSettings.Remove(app);
        TrackConfigChange("SSO Configuration", "App Deleted", app.AppName, "(deleted)", GetActor());
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ── Global settings ──────────────────────────────────────────────────────

    public async Task<GlobalSettings> GetGlobalSettingsAsync(CancellationToken cancellationToken = default)
    {
        var gs = await dbContext.GlobalSettings.SingleOrDefaultAsync(cancellationToken);
        if (gs is not null)
        {
            return gs;
        }

        gs = new GlobalSettings();
        dbContext.GlobalSettings.Add(gs);
        await dbContext.SaveChangesAsync(cancellationToken);
        return gs;
    }

    public async Task SaveAppControlsAsync(int logRetentionDays, CancellationToken cancellationToken = default)
    {
        if (logRetentionDays < 1)
        {
            throw new InvalidOperationException("Log retention must be at least 1 day.");
        }

        var gs = await GetGlobalSettingsAsync(cancellationToken);
        TrackConfigChange("App Controls", "Log retention (days)", gs.LogRetentionDays.ToString(), logRetentionDays.ToString(), GetActor());
        gs.LogRetentionDays = logRetentionDays;
        gs.UpdatedUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ── Logs ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<ConfigChangeLog>> GetConfigurationLogsAsync(bool newestFirst = true, CancellationToken cancellationToken = default)
    {
        var logs = await dbContext.ConfigChangeLogs.ToListAsync(cancellationToken);
        return newestFirst
            ? logs.OrderByDescending(l => l.ChangedUtc).ToList()
            : logs.OrderBy(l => l.ChangedUtc).ToList();
    }

    public async Task<int> PurgeConfigurationLogsAsync(CancellationToken cancellationToken = default)
    {
        var gs = await GetGlobalSettingsAsync(cancellationToken);
        var retentionDays = gs.LogRetentionDays <= 0 ? 14 : gs.LogRetentionDays;
        var cutoffUtc = DateTime.UtcNow.AddDays(-retentionDays);

        var staleLogs = await dbContext.ConfigChangeLogs
            .Where(l => l.ChangedUtc < cutoffUtc)
            .ToListAsync(cancellationToken);

        var staleSignInLogs = await dbContext.SignInLogs
            .Where(l => l.SignedInUtc < cutoffUtc)
            .ToListAsync(cancellationToken);

        var total = staleLogs.Count + staleSignInLogs.Count;
        if (total == 0)
        {
            return 0;
        }

        dbContext.ConfigChangeLogs.RemoveRange(staleLogs);
        dbContext.SignInLogs.RemoveRange(staleSignInLogs);
        await dbContext.SaveChangesAsync(cancellationToken);
        return total;
    }

    public async Task LogSignInAsync(string userName, string certThumbprint, string appName, CancellationToken cancellationToken = default)
    {
        dbContext.SignInLogs.Add(new SignInLog
        {
            AppName = appName,
            UserName = userName,
            CertThumbprint = certThumbprint,
            SignedInUtc = DateTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SignInLog>> GetSignInLogsAsync(bool newestFirst = true, CancellationToken cancellationToken = default)
    {
        var logs = await dbContext.SignInLogs.ToListAsync(cancellationToken);
        return newestFirst
            ? logs.OrderByDescending(l => l.SignedInUtc).ToList()
            : logs.OrderBy(l => l.SignedInUtc).ToList();
    }

    // ── Certificates ──────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<SamlCertificate>> GetCertificatesAsync(int appId, CancellationToken cancellationToken = default)
    {
        var certs = await dbContext.SamlCertificates
            .Where(c => c.AppId == appId)
            .ToListAsync(cancellationToken);
        return certs.OrderByDescending(c => c.NotAfterUtc).ToList();
    }

    public Task<SamlCertificate?> GetCertificateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return dbContext.SamlCertificates.SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task ImportMetadataAsync(int appId, string metadataUrl, CancellationToken cancellationToken = default)
    {
        await ImportMetadataInternalAsync(appId, metadataUrl, cancellationToken);
    }

    private async Task ImportMetadataInternalAsync(int appId, string metadataUrl, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(metadataUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new InvalidOperationException("A valid metadata URL is required.");
        }

        // SSRF protection: resolve hostname and reject private/loopback addresses
        try
        {
            var addresses = await System.Net.Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken);
            foreach (var address in addresses)
            {
                if (System.Net.IPAddress.IsLoopback(address) || IsPrivateOrLinkLocal(address))
                {
                    throw new InvalidOperationException("Metadata URL must resolve to a public internet address.");
                }
            }
        }
        catch (System.Net.Sockets.SocketException)
        {
            throw new InvalidOperationException("Metadata URL hostname could not be resolved.");
        }

        var client = httpClientFactory.CreateClient("metadata");
        var response = await client.GetAsync(uri, cancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Metadata download failed with HTTP {(int)response.StatusCode}.");
        }

        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var parseResult = metadataParser.Parse(xml);
        if (parseResult.SigningCertificates.Count == 0)
        {
            throw new InvalidOperationException("No signing certificates were found in the federation metadata document.");
        }

        var settings = await GetAppAsync(appId, cancellationToken);
        var actor = GetActor();
        var section = $"App [{settings.AppName}]";

        TrackConfigChange(section, "Federation Metadata URL", settings.FederationMetadataUrl, metadataUrl.Trim(), actor);
        settings.FederationMetadataUrl = metadataUrl.Trim();

        if (!string.IsNullOrWhiteSpace(parseResult.EntityId))
        {
            TrackConfigChange(section, "IdP Entity ID", settings.IdpEntityId, parseResult.EntityId, actor);
            settings.IdpEntityId = parseResult.EntityId;
        }

        if (!string.IsNullOrWhiteSpace(parseResult.SingleSignOnUrl))
        {
            TrackConfigChange(section, "IdP SSO URL", settings.IdpSsoUrl, parseResult.SingleSignOnUrl, actor);
            settings.IdpSsoUrl = parseResult.SingleSignOnUrl;
        }

        var existingCerts = await dbContext.SamlCertificates.AsNoTracking()
            .Where(c => c.AppId == appId)
            .ToListAsync(cancellationToken);

        // Build the set of thumbprints present in the freshly-fetched metadata
        var metadataThumbprints = parseResult.SigningCertificates
            .Select(c => (c.Thumbprint?.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase) ?? string.Empty))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove certs that are no longer in the metadata (full sync)
        foreach (var stale in existingCerts.Where(x => !metadataThumbprints.Contains(x.Thumbprint)))
        {
            var staleCert = await dbContext.SamlCertificates.FindAsync([stale.Id], cancellationToken);
            if (staleCert is not null)
            {
                dbContext.SamlCertificates.Remove(staleCert);
                TrackConfigChange(section, "Signing Certificate", stale.Thumbprint, "(removed — no longer in metadata)", actor);
            }
        }

        // Add certs that are new in the metadata
        foreach (var cert in parseResult.SigningCertificates)
        {
            var thumbprint = cert.Thumbprint?.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(thumbprint))
            {
                continue;
            }

            if (existingCerts.Any(x => string.Equals(x.Thumbprint, thumbprint, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            dbContext.SamlCertificates.Add(new SamlCertificate
            {
                AppId = appId,
                Thumbprint = thumbprint,
                Subject = cert.Subject,
                Issuer = cert.Issuer,
                NotBeforeUtc = DateTime.SpecifyKind(cert.NotBefore, DateTimeKind.Utc),
                NotAfterUtc = DateTime.SpecifyKind(cert.NotAfter, DateTimeKind.Utc),
                Base64Der = Convert.ToBase64String(cert.RawData)
            });

            TrackConfigChange(section, "Signing Certificate", "(not present)", thumbprint, actor);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteCertificateAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var certificate = await dbContext.SamlCertificates.SingleOrDefaultAsync(c => c.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Certificate not found.");

        var app = await dbContext.SamlSettings.SingleOrDefaultAsync(s => s.Id == certificate.AppId, cancellationToken);
        var section = app is not null ? $"App [{app.AppName}]" : "SSO Certificates";

        TrackConfigChange(section, "Signing Certificate", certificate.Thumbprint, "(deleted)", GetActor());
        dbContext.SamlCertificates.Remove(certificate);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // ── SAML ──────────────────────────────────────────────────────────────────

    public async Task<Saml2Configuration> BuildSamlConfigurationAsync(int appId, CancellationToken cancellationToken = default)
    {
        var settings = await GetAppAsync(appId, cancellationToken);
        var certificates = await GetCertificatesAsync(appId, cancellationToken);

        if (string.IsNullOrWhiteSpace(settings.IdpSsoUrl) || string.IsNullOrWhiteSpace(settings.SpEntityId))
        {
            throw new InvalidOperationException($"App '{settings.AppName}' is not fully configured. Set SP Entity ID and IdP SSO URL in the admin portal.");
        }

        if (certificates.Count == 0)
        {
            throw new InvalidOperationException($"No SAML signing certificates configured for app '{settings.AppName}'.");
        }

        var config = new Saml2Configuration
        {
            Issuer = settings.SpEntityId,
            SingleSignOnDestination = new Uri(settings.IdpSsoUrl),
            CertificateValidationMode = X509CertificateValidationMode.None,
            RevocationMode = X509RevocationMode.NoCheck
        };

        foreach (var cert in certificates)
        {
            config.SignatureValidationCertificates.Add(X509CertificateLoader.LoadCertificate(Convert.FromBase64String(cert.Base64Der)));
        }

        config.AllowedAudienceUris.Add(settings.SpEntityId);

        if (Uri.TryCreate(settings.FederationMetadataUrl, UriKind.Absolute, out var metadataUri))
        {
            var query = QueryHelpers.ParseQuery(metadataUri.Query);
            var appIdParam = query.TryGetValue("appid", out var appIdValues) ? appIdValues.ToString() : null;
            if (!string.IsNullOrWhiteSpace(appIdParam))
            {
                config.AllowedAudienceUris.Add($"spn:{appIdParam}");
            }
        }

        return config;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void TrackConfigChange(string section, string fieldName, string? beforeValue, string? afterValue, string actor)
    {
        var before = (beforeValue ?? string.Empty).Trim();
        var after = (afterValue ?? string.Empty).Trim();
        if (string.Equals(before, after, StringComparison.Ordinal))
        {
            return;
        }

        dbContext.ConfigChangeLogs.Add(new ConfigChangeLog
        {
            Section = section,
            FieldName = fieldName,
            BeforeValue = before,
            AfterValue = after,
            ChangedBy = actor,
            ChangedUtc = DateTime.UtcNow
        });
    }

    private string GetActor()
    {
        return httpContextAccessor.HttpContext?.User?.Identity?.Name ?? "system";
    }

    private static bool IsPrivateOrLinkLocal(System.Net.IPAddress address)
    {
        var bytes = address.GetAddressBytes();
        // IPv4 private/link-local ranges
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] == 10 ||
                   (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 169 && bytes[1] == 254); // link-local
        }
        // IPv6 private/link-local
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal ||
                   address.IsIPv6UniqueLocal;
        }
        return false;
    }
}

