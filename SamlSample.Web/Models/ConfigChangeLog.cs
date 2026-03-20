namespace SamlSample.Web.Models;

public class ConfigChangeLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Section { get; set; } = string.Empty;
    public string FieldName { get; set; } = string.Empty;
    public string BeforeValue { get; set; } = string.Empty;
    public string AfterValue { get; set; } = string.Empty;
    public string ChangedBy { get; set; } = string.Empty;
    public DateTime ChangedUtc { get; set; } = DateTime.UtcNow;
}
