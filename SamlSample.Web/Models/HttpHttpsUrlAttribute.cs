using System.ComponentModel.DataAnnotations;

namespace SamlSample.Web.Models;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class HttpHttpsUrlAttribute : ValidationAttribute
{
    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        if (value is null)
        {
            return ValidationResult.Success;
        }

        var input = value.ToString();
        if (string.IsNullOrWhiteSpace(input))
        {
            return ValidationResult.Success;
        }

        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            return new ValidationResult(ErrorMessage ?? $"The {validationContext.DisplayName} field must be a valid absolute URL.");
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return new ValidationResult(ErrorMessage ?? $"The {validationContext.DisplayName} field must use http or https.");
        }

        return ValidationResult.Success;
    }
}
