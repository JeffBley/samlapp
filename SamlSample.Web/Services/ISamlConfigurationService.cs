using ITfoxtec.Identity.Saml2;
using SamlSample.Web.Models;

namespace SamlSample.Web.Services;

public interface ISamlConfigurationService
{
    // ── App management ──────────────────────────────────────────────────────
    Task<IReadOnlyList<SamlSettings>> GetAllAppsAsync(CancellationToken cancellationToken = default);
    Task<SamlSettings> GetAppAsync(int appId, CancellationToken cancellationToken = default);
    Task<SamlSettings?> GetAppBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<SamlSettings> CreateAppAsync(string appName, CancellationToken cancellationToken = default);
    Task SaveAppAsync(SamlSettings settings, CancellationToken cancellationToken = default);
    Task DeleteAppAsync(int appId, CancellationToken cancellationToken = default);

    // ── Global settings ──────────────────────────────────────────────────────
    Task<GlobalSettings> GetGlobalSettingsAsync(CancellationToken cancellationToken = default);
    Task SaveAppControlsAsync(int logRetentionDays, CancellationToken cancellationToken = default);

    // ── Logs ──────────────────────────────────────────────────────────────────
    Task<IReadOnlyList<ConfigChangeLog>> GetConfigurationLogsAsync(bool newestFirst = true, CancellationToken cancellationToken = default);
    Task<int> PurgeConfigurationLogsAsync(CancellationToken cancellationToken = default);
    Task LogSignInAsync(string userName, string certThumbprint, string appName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SignInLog>> GetSignInLogsAsync(bool newestFirst = true, CancellationToken cancellationToken = default);

    // ── Certificates ──────────────────────────────────────────────────────────
    Task<IReadOnlyList<SamlCertificate>> GetCertificatesAsync(int appId, CancellationToken cancellationToken = default);
    Task<SamlCertificate?> GetCertificateAsync(Guid id, CancellationToken cancellationToken = default);
    Task ImportMetadataAsync(int appId, string metadataUrl, CancellationToken cancellationToken = default);
    Task DeleteCertificateAsync(Guid id, CancellationToken cancellationToken = default);

    // ── SAML ──────────────────────────────────────────────────────────────────
    Task<Saml2Configuration> BuildSamlConfigurationAsync(int appId, CancellationToken cancellationToken = default);
}
