using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SamlSample.Web.Data;

namespace SamlSample.Web.ViewComponents;

public class SecurityAlertsViewComponent(AppDbContext dbContext) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync()
    {
        if (UserClaimsPrincipal.Identity?.IsAuthenticated != true)
            return Content(string.Empty);

        var launcher = await dbContext.SamlSettings
            .SingleOrDefaultAsync(s => s.IsSystem);

        // SSO is "configured" when the IdP entity ID and at least a login URL are set.
        var ssoConfigured = launcher is not null
            && !string.IsNullOrEmpty(launcher.IdpEntityId)
            && (!string.IsNullOrEmpty(launcher.IdpSsoUrl) || !string.IsNullOrEmpty(launcher.FederationMetadataUrl));

        if (!ssoConfigured)
            return View(new SecurityAlertsModel(SsoNotConfigured: true, BootstrapEnabled: false));

        var bootstrapEnabled = await dbContext.LocalAdminCredentials
            .AnyAsync(c => c.IsEnabled);

        return View(new SecurityAlertsModel(SsoNotConfigured: false, BootstrapEnabled: bootstrapEnabled));
    }
}

public record SecurityAlertsModel(bool SsoNotConfigured, bool BootstrapEnabled);
