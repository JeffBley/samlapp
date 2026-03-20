using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace SamlSample.Web.Controllers;

[Authorize(Roles = "user,admin")]
public class UserController : Controller
{
    public IActionResult Index()
    {
        var claims = User.Claims
            .OrderBy(c => c.Type)
            .Select(c => new UserClaimViewModel(c.Type, c.Value))
            .ToList();

        var certThumbprint = User.FindFirstValue("urn:samlsample:cert-thumbprint");

        var viewModel = new UserProfileViewModel(
            User.Identity?.Name ?? User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown",
            certThumbprint,
            claims);

        return View(viewModel);
    }
}

public record UserClaimViewModel(string Type, string Value);
public record UserProfileViewModel(string DisplayName, string? CertThumbprint, IReadOnlyList<UserClaimViewModel> Claims);
