using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SamlSample.Web.Services;

namespace SamlSample.Web.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "admin")]
public class SignInLogsController(ISamlConfigurationService configurationService) : Controller
{
    [HttpGet]
    public async Task<IActionResult> Index([FromQuery] string? sort = "desc", CancellationToken cancellationToken = default)
    {
        var newestFirst = !string.Equals(sort, "asc", StringComparison.OrdinalIgnoreCase);
        var logs = await configurationService.GetSignInLogsAsync(newestFirst, cancellationToken);
        ViewData["Sort"] = newestFirst ? "desc" : "asc";
        return View(logs);
    }
}
