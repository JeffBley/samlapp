using System.Security.Claims;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using SamlSample.Web.Data;
using SamlSample.Web.Models;
using SamlSample.Web.Services;

namespace SamlSample.Web.Controllers;

[Route("bootstrap-admin")]
public class BootstrapAdminController(
    AppDbContext dbContext,
    ISecretHashingService secretHashingService,
    IWebHostEnvironment environment,
    ISamlConfigurationService configurationService,
    IConfiguration configuration) : Controller
{
    [AllowAnonymous]
    [HttpGet("login")]
    public async Task<IActionResult> Login(CancellationToken cancellationToken)
    {
        if (!await IsBootstrapLoginAvailableAsync(cancellationToken))
        {
            // Bootstrap disabled — send straight to the system app SSO
            return Redirect("/saml/saml-launcher/login?returnUrl=" + Uri.EscapeDataString("/Admin/Sso"));
        }

        return View(new BootstrapAdminLoginViewModel());
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("bootstrap-login")]
    public async Task<IActionResult> Login(BootstrapAdminLoginViewModel model, CancellationToken cancellationToken)
    {
        if (!await IsBootstrapLoginAvailableAsync(cancellationToken))
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            model.ErrorMessage = "Username and password are required.";
            return View(model);
        }

        var credential = await dbContext.LocalAdminCredentials
            .SingleOrDefaultAsync(c => c.Username == model.Username && c.IsEnabled, cancellationToken);

        if (credential is null || !secretHashingService.Verify(model.Password, credential.PasswordHash, credential.Salt))
        {
            model.ErrorMessage = "Invalid bootstrap admin credentials.";
            return View(model);
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, credential.Username),
            new(ClaimTypes.Role, "admin"),
            new(ClaimTypes.Role, "user"),
            new("amr", "local-bootstrap")
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);
        await configurationService.LogSignInAsync(credential.Username, "local-bootstrap", "Bootstrap Admin", CancellationToken.None);
        await configurationService.PurgeConfigurationLogsAsync(CancellationToken.None);

        return RedirectToAction("Index", "Sso", new { area = "Admin" });
    }

    [HttpPost("disable")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Disable(CancellationToken cancellationToken)
    {
        var credentials = await dbContext.LocalAdminCredentials.Where(c => c.IsEnabled).ToListAsync(cancellationToken);
        foreach (var credential in credentials)
        {
            credential.IsEnabled = false;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Bootstrap local admin login disabled.";
        return RedirectToAction("Index", "AppControls", new { area = "Admin" });
    }

    [HttpPost("enable")]
    [ValidateAntiForgeryToken]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Enable(CancellationToken cancellationToken)
    {
        var credentials = await dbContext.LocalAdminCredentials.Where(c => !c.IsEnabled).ToListAsync(cancellationToken);
        foreach (var credential in credentials)
        {
            credential.IsEnabled = true;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        TempData["Message"] = "Bootstrap local admin login enabled.";
        return RedirectToAction("Index", "AppControls", new { area = "Admin" });
    }

    private async Task<bool> IsBootstrapLoginAvailableAsync(CancellationToken cancellationToken)
    {
        var overrideEnabled = string.Equals(configuration["BootstrapAdminEnabled"], "true", StringComparison.OrdinalIgnoreCase);
        if (!overrideEnabled && (!environment.IsDevelopment() || !IsLocalRequest()))
        {
            return false;
        }

        var hasEnabledCredential = await dbContext.LocalAdminCredentials.AnyAsync(c => c.IsEnabled, cancellationToken);
        return hasEnabledCredential;
    }

    private bool IsLocalRequest()
    {
        var remoteIp = HttpContext.Connection.RemoteIpAddress;
        return remoteIp is not null && IPAddress.IsLoopback(remoteIp);
    }
}
