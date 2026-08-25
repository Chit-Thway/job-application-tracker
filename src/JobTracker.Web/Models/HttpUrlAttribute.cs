using System.ComponentModel.DataAnnotations;

namespace JobTracker.Web.Models;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class HttpUrlAttribute : ValidationAttribute
{
    public HttpUrlAttribute()
        : base("Enter a complete HTTP or HTTPS URL.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is null || value is string { Length: 0 })
        {
            return true;
        }

        return value is string text
            && Uri.TryCreate(text.Trim(), UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrWhiteSpace(uri.Host);
    }
}
