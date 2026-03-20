using System.Diagnostics;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SamlSample.Web.Data;
using SamlSample.Web.Models;
using SamlSample.Web.Services;

namespace SamlSample.Web.Controllers;

public class HomeController(
    ISamlConfigurationService configurationService,
    AppDbContext dbContext,
    IWebHostEnvironment environment,
    IConfiguration configuration) : Controller
{
    [Authorize(Roles = "user,admin")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var apps = await configurationService.GetAllAppsAsync(cancellationToken);
        return View(apps.Where(a => !a.IsSystem).ToList());
    }

    [AllowAnonymous]
    public async Task<IActionResult> Welcome(CancellationToken cancellationToken)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction(nameof(Index));

        var overrideEnabled = string.Equals(configuration["BootstrapAdminEnabled"], "true", StringComparison.OrdinalIgnoreCase);
        var bootstrapAvailable = (overrideEnabled || (environment.IsDevelopment() && IsLocalRequest()))
            && await dbContext.LocalAdminCredentials.AnyAsync(c => c.IsEnabled, cancellationToken);

        ViewData["BootstrapAvailable"] = bootstrapAvailable;
        return View();
    }

    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }

    private bool IsLocalRequest()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        return remoteIp is not null && IPAddress.IsLoopback(remoteIp);
    }
}
