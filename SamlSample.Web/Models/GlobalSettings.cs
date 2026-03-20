namespace SamlSample.Web.Models;

public class GlobalSettings
{
    public int Id { get; set; }
    public int LogRetentionDays { get; set; } = 14;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
