using System.ComponentModel.DataAnnotations;

namespace SamlSample.Web.Models;

public class BootstrapAdminLoginViewModel
{
    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public string? ErrorMessage { get; set; }
}
