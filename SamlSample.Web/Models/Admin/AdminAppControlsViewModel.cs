using System.ComponentModel.DataAnnotations;

namespace SamlSample.Web.Models.Admin;

public class AdminAppControlsViewModel
{
    [Range(1, 3650)]
    [Display(Name = "Log retention (days)")]
    public int LogRetentionDays { get; set; } = 14;

    public bool BootstrapLoginEnabled { get; set; }
}
