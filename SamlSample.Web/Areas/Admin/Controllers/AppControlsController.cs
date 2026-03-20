using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SamlSample.Web.Data;
using SamlSample.Web.Models.Admin;
using SamlSample.Web.Services;

namespace SamlSample.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "admin")]
public class AppControlsController(ISamlConfigurationService configurationService, AppDbContext dbContext) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var gs = await configurationService.GetGlobalSettingsAsync(cancellationToken);
        var bootstrapEnabled = await dbContext.LocalAdminCredentials.AnyAsync(c => c.IsEnabled, cancellationToken);
        return View(new AdminAppControlsViewModel
        {
            LogRetentionDays = gs.LogRetentionDays <= 0 ? 14 : gs.LogRetentionDays,
            BootstrapLoginEnabled = bootstrapEnabled
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(AdminAppControlsViewModel viewModel, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View("Index", viewModel);
        }

        try
        {
            await configurationService.SaveAppControlsAsync(viewModel.LogRetentionDays, cancellationToken);
            TempData["Message"] = "App controls updated.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Purge(CancellationToken cancellationToken)
    {
        try
        {
            var purged = await configurationService.PurgeConfigurationLogsAsync(cancellationToken);
            TempData["Message"] = purged > 0
                ? $"Purged {purged} log {(purged == 1 ? "entry" : "entries")} older than the retention period."
                : "No log entries were old enough to purge.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index));
    }
}
