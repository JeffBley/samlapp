using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.MvcCore;
using ITfoxtec.Identity.Saml2.Schemas;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SamlSample.Web.Services;

namespace SamlSample.Web.Controllers;

[AllowAnonymous]
[Route("saml")]
public class SamlController(ISamlConfigurationService configurationService, ILogger<SamlController> logger) : Controller
{
    // Legacy single-app redirect — picks the first configured app for backward compat
    [HttpGet("login")]
    public async Task<IActionResult> LoginDefault([FromQuery] string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        var apps = await configurationService.GetAllAppsAsync(cancellationToken);
        if (apps.Count == 0)
        {
            TempData["Error"] = "No SAML apps are configured. Add an app in the admin portal.";
            return RedirectToAction(nameof(LoginFailed));
        }

        var firstApp = apps[0];
        var target = string.IsNullOrWhiteSpace(firstApp.UrlSlug) ? firstApp.Id.ToString() : firstApp.UrlSlug;
        var qs = string.IsNullOrWhiteSpace(returnUrl) ? string.Empty : $"?returnUrl={Uri.EscapeDataString(returnUrl)}";
        return Redirect($"/saml/{target}/login{qs}");
    }

    [HttpGet("{slug}/login")]
    public async Task<IActionResult> LoginBySlug(string slug, [FromQuery] string? returnUrl = null, CancellationToken cancellationToken = default)
    {
        var app = await configurationService.GetAppBySlugAsync(slug, cancellationToken);
        if (app is null)
        {
            TempData["Error"] = $"No app found for slug '{slug}'.";
            return RedirectToAction(nameof(LoginFailed));
        }
        return await LoginInternal(app.Id, returnUrl, slug, cancellationToken);
    }

    [HttpGet("{appId:int}/login")]
    public Task<IActionResult> Login(int appId, [FromQuery] string? returnUrl = null, CancellationToken cancellationToken = default)
        => LoginInternal(appId, returnUrl, null, cancellationToken);

    private async Task<IActionResult> LoginInternal(int appId, string? returnUrl, string? slug, CancellationToken cancellationToken)
    {
        try
        {
            var config = await configurationService.BuildSamlConfigurationAsync(appId, cancellationToken);
            var app = await configurationService.GetAppAsync(appId, cancellationToken);

            // Prefer explicit stored ACS URL, then slug-based, then int-based
            var effectiveSlug = slug ?? (string.IsNullOrWhiteSpace(app.UrlSlug) ? null : app.UrlSlug);
            var acsUrl = string.IsNullOrWhiteSpace(app.AssertionConsumerServiceUrl)
                ? effectiveSlug is not null
                    ? $"{Request.Scheme}://{Request.Host}/saml/{effectiveSlug}/acs"
                    : $"{Request.Scheme}://{Request.Host}/saml/{appId}/acs"
                : app.AssertionConsumerServiceUrl;

            var authnRequest = new Saml2AuthnRequest(config)
            {
                AssertionConsumerServiceUrl = new Uri(acsUrl),
                Destination = config.SingleSignOnDestination
            };

            var binding = new Saml2RedirectBinding();
            var relayState = string.IsNullOrWhiteSpace(returnUrl) ? "/user" : returnUrl;
            binding.SetRelayStateQuery(new Dictionary<string, string> { ["returnUrl"] = relayState });
            return binding.Bind(authnRequest).ToActionResult();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start SAML login flow for app {AppId}.", appId);
            TempData["Error"] = "An error occurred starting the sign-in flow. Please try again.";
            return RedirectToAction(nameof(LoginFailed));
        }
    }

    [HttpPost("{slug}/acs")]
    [EnableRateLimiting("saml-acs")]
    public async Task<IActionResult> AssertionConsumerServiceBySlug(string slug, CancellationToken cancellationToken = default)
    {
        var app = await configurationService.GetAppBySlugAsync(slug, cancellationToken);
        if (app is null)
        {
            logger.LogWarning("SAML ACS: no app found for slug '{Slug}'.", slug);
            return NotFound();
        }
        return await ProcessAssertion(app.Id, cancellationToken);
    }

    [HttpPost("{appId:int}/acs")]
    [EnableRateLimiting("saml-acs")]
    public Task<IActionResult> AssertionConsumerService(int appId, CancellationToken cancellationToken = default)
        => ProcessAssertion(appId, cancellationToken);

    private async Task<IActionResult> ProcessAssertion(int appId, CancellationToken cancellationToken)
    {
        var config = await configurationService.BuildSamlConfigurationAsync(appId, cancellationToken);
        var authnResponse = new Saml2AuthnResponse(config);
        var binding = new Saml2PostBinding();

        try
        {
            binding.ReadSamlResponse(Request.ToGenericHttpRequest(), authnResponse);
            binding.Unbind(Request.ToGenericHttpRequest(), authnResponse);

            if (authnResponse.Status != Saml2StatusCodes.Success)
            {
                logger.LogWarning("SAML response status was not success. Status: {Status}", authnResponse.Status);
                return RedirectToAction(nameof(LoginFailed));
            }

            var incomingPrincipal = authnResponse.ClaimsIdentity is null
                ? new ClaimsPrincipal(new ClaimsIdentity())
                : new ClaimsPrincipal(authnResponse.ClaimsIdentity);

            var app = await configurationService.GetAppAsync(appId, cancellationToken);

            var finalClaims = incomingPrincipal.Claims.ToList();
            var roleSourceClaims = incomingPrincipal.Claims
                .Where(c => c.Type == "roles" || c.Type == "role" || c.Type == "http://schemas.microsoft.com/ws/2008/06/identity/claims/role")
                .Select(c => c.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var role in roleSourceClaims)
            {
                finalClaims.Add(new Claim(ClaimTypes.Role, role));
            }

            if (!roleSourceClaims.Any(r => string.Equals(r, "user", StringComparison.OrdinalIgnoreCase)) &&
                !roleSourceClaims.Any(r => string.Equals(r, "admin", StringComparison.OrdinalIgnoreCase)))
            {
                // Reject login when no recognized role is present — prevents silent privilege escalation
                // via a principal with no role assignment. Configure Entra app roles and claim issuance
                // policy to always emit `roles` claims.
                logger.LogWarning("SAML login rejected for app {AppId}: assertion contained no recognized role claims.", appId);
                TempData["Error"] = "Sign-in failed: your account has no role assigned for this application. Contact your administrator.";
                return RedirectToAction(nameof(LoginFailed));
            }

            // System apps (admin portal) always grant admin access regardless of IdP role claims
            if (app.IsSystem)
            {
                if (!finalClaims.Any(c => c.Type == ClaimTypes.Role && c.Value == "admin"))
                    finalClaims.Add(new Claim(ClaimTypes.Role, "admin"));
                if (!finalClaims.Any(c => c.Type == ClaimTypes.Role && c.Value == "user"))
                    finalClaims.Add(new Claim(ClaimTypes.Role, "user"));
            }

            // Store the app name and signing cert thumbprint in the session cookie
            finalClaims.Add(new Claim("urn:samlsample:app", app.AppName));

            var thumbprint = ExtractSigningCertThumbprint(authnResponse) ?? "(unknown)";
            finalClaims.Add(new Claim("urn:samlsample:cert-thumbprint", thumbprint));

            var identity = new ClaimsIdentity(finalClaims, "saml", ClaimTypes.Name, ClaimTypes.Role);
            var principal = new ClaimsPrincipal(identity);
            await HttpContext.SignInAsync(principal);

            var userName = identity.Name ?? "(unknown)";
            await configurationService.LogSignInAsync(userName, thumbprint, app.AppName, cancellationToken);
            await configurationService.PurgeConfigurationLogsAsync(cancellationToken);

            // Honour returnUrl from relay state (validated to prevent open redirect)
            var relayStateQuery = binding.GetRelayStateQuery();
            if (relayStateQuery.TryGetValue("returnUrl", out var returnUrl)
                && !string.IsNullOrEmpty(returnUrl)
                && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }
            return RedirectToAction("Index", "User");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "SAML ACS processing failed for app {AppId}.", appId);
            return RedirectToAction(nameof(LoginFailed));
        }
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync();
        return RedirectToAction("Welcome", "Home");
    }

    [HttpGet("failed")]
    public IActionResult LoginFailed()
    {
        return View();
    }

    private static string? ExtractSigningCertThumbprint(Saml2AuthnResponse authnResponse)
    {
        try
        {
            var nsMgr = new XmlNamespaceManager(authnResponse.XmlDocument.NameTable);
            nsMgr.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");
            var certNode = authnResponse.XmlDocument.SelectSingleNode("//ds:X509Certificate", nsMgr);
            if (certNode is null) return null;
            var certBytes = Convert.FromBase64String(certNode.InnerText.Trim());
            var cert = X509CertificateLoader.LoadCertificate(certBytes);
            return cert.Thumbprint;
        }
        catch
        {
            return null;
        }
    }
}
