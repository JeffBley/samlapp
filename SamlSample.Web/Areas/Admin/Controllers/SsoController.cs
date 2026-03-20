using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SamlSample.Web.Models;
using SamlSample.Web.Models.Admin;
using SamlSample.Web.Services;

namespace SamlSample.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "admin")]
public class SsoController(ISamlConfigurationService configurationService) : Controller
{
    // ── App list ──────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Index(string? search, CancellationToken cancellationToken)
    {
        var apps = await configurationService.GetAllAppsAsync(cancellationToken);

        var filtered = (string.IsNullOrWhiteSpace(search)
            ? apps
            : apps.Where(a => a.AppName.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(a => a.IsSystem ? 0 : 1)
            .ThenBy(a => a.AppName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return View(new AdminSsoListViewModel
        {
            Search = search ?? string.Empty,
            Apps = filtered.Select(a => new SamlAppSummaryViewModel
            {
                Id = a.Id,
                AppName = a.AppName,
                SpEntityId = a.SpEntityId,
                UrlSlug = a.UrlSlug,
                IsSystem = a.IsSystem,
                FederationMetadataUrl = a.FederationMetadataUrl,
                IsConfigured = !string.IsNullOrEmpty(a.IdpEntityId)
                    && (!string.IsNullOrEmpty(a.IdpSsoUrl) || !string.IsNullOrEmpty(a.FederationMetadataUrl))
            }).ToList()
        });
    }

    // ── Create app ────────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([FromForm] string appName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            TempData["Error"] = "App name is required.";
            return RedirectToAction(nameof(Index));
        }

        try
        {
            var newApp = await configurationService.CreateAppAsync(appName, cancellationToken);
            return RedirectToAction(nameof(Edit), new { id = newApp.Id });
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToAction(nameof(Index));
        }
    }

    // ── Edit app ──────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        try
        {
            var app = await configurationService.GetAppAsync(id, cancellationToken);
            var certs = await configurationService.GetCertificatesAsync(id, cancellationToken);
            var vm = ToEditViewModel(app, certs);
            if (string.IsNullOrWhiteSpace(vm.AssertionConsumerServiceUrl))
            {
                var effectiveSlug = string.IsNullOrWhiteSpace(vm.UrlSlug) ? vm.Id.ToString() : vm.UrlSlug;
                vm.AssertionConsumerServiceUrl = $"{Request.Scheme}://{Request.Host}/saml/{effectiveSlug}/acs";
            }
            return View(vm);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, AdminSsoEditViewModel viewModel, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            viewModel.Certificates = (await configurationService.GetCertificatesAsync(id, cancellationToken))
                .Select(ToCertificateViewModel).ToList();
            return View(viewModel);
        }

        try
        {
            await configurationService.SaveAppAsync(new SamlSettings
            {
                Id = id,
                AppName = viewModel.AppName,
                SpEntityId = viewModel.SpEntityId,
                AssertionConsumerServiceUrl = viewModel.AssertionConsumerServiceUrl,
                IdpEntityId = viewModel.IdpEntityId,
                IdpSsoUrl = viewModel.IdpSsoUrl,
                FederationMetadataUrl = viewModel.FederationMetadataUrl
            }, cancellationToken);
            TempData["Message"] = "App configuration saved.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    // ── Delete app ────────────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteApp(int id, CancellationToken cancellationToken)
    {
        try
        {
            await configurationService.DeleteAppAsync(id, cancellationToken);
            TempData["Message"] = "App deleted.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    // ── Import metadata ───────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ImportMetadata(int id, [FromForm] string metadataUrl, CancellationToken cancellationToken)
    {
        try
        {
            await configurationService.ImportMetadataAsync(id, metadataUrl, cancellationToken);
            TempData["Message"] = "Metadata imported and certificates updated.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    // ── Delete certificate ────────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCert(int id, [FromForm] Guid certificateId, CancellationToken cancellationToken)
    {
        try
        {
            await configurationService.DeleteCertificateAsync(certificateId, cancellationToken);
            TempData["Message"] = "Certificate deleted.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    // ── Refresh metadata (single app) ─────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshMetadata(int id, CancellationToken cancellationToken)
    {
        try
        {
            var app = await configurationService.GetAppAsync(id, cancellationToken);
            if (string.IsNullOrWhiteSpace(app.FederationMetadataUrl))
            {
                TempData["Error"] = "This app has no Federation Metadata URL configured.";
                return RedirectToAction(nameof(Index));
            }
            await configurationService.ImportMetadataAsync(id, app.FederationMetadataUrl, cancellationToken);
            TempData["Message"] = $"Federation metadata refreshed for {app.AppName}.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }
        return RedirectToAction(nameof(Index));
    }

    // ── Refresh all metadata ───────────────────────────────────────────────────

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RefreshAllMetadata(CancellationToken cancellationToken)
    {
        var apps = await configurationService.GetAllAppsAsync(cancellationToken);
        var eligible = apps.Where(a => !string.IsNullOrWhiteSpace(a.FederationMetadataUrl)).ToList();

        if (eligible.Count == 0)
        {
            TempData["Error"] = "No apps have a Federation Metadata URL configured.";
            return RedirectToAction(nameof(Index));
        }

        var succeeded = 0;
        var failures = new List<string>();
        foreach (var app in eligible)
        {
            try
            {
                await configurationService.ImportMetadataAsync(app.Id, app.FederationMetadataUrl, cancellationToken);
                succeeded++;
            }
            catch (Exception ex)
            {
                failures.Add($"{app.AppName}: {ex.Message}");
            }
        }

        if (failures.Count == 0)
        {
            TempData["Message"] = $"Federation metadata refreshed for {succeeded} app{(succeeded == 1 ? "" : "s")}.";
        }
        else
        {
            var summary = $"{succeeded} succeeded, {failures.Count} failed: {string.Join("; ", failures)}";
            TempData[failures.Count == eligible.Count ? "Error" : "Message"] = summary;
        }

        return RedirectToAction(nameof(Index));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static AdminSsoEditViewModel ToEditViewModel(SamlSettings app, IReadOnlyList<SamlCertificate> certs)
    {
        return new AdminSsoEditViewModel
        {
            Id = app.Id,
            AppName = app.AppName,
            SpEntityId = app.SpEntityId,
            AssertionConsumerServiceUrl = app.AssertionConsumerServiceUrl,
            IdpEntityId = app.IdpEntityId,
            IdpSsoUrl = app.IdpSsoUrl,
            FederationMetadataUrl = app.FederationMetadataUrl,
            UrlSlug = app.UrlSlug,
            Certificates = certs.Select(ToCertificateViewModel).ToList()
        };
    }

    private static SamlCertificateViewModel ToCertificateViewModel(SamlCertificate cert)
    {
        return new SamlCertificateViewModel
        {
            Id = cert.Id,
            Thumbprint = cert.Thumbprint,
            Subject = cert.Subject,
            Issuer = cert.Issuer,
            NotBeforeUtc = cert.NotBeforeUtc,
            NotAfterUtc = cert.NotAfterUtc
        };
    }
}
