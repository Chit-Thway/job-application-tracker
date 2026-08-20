using System.ComponentModel.DataAnnotations;

namespace JobTracker.Web.Identity;

public sealed class AccountEmailOptions
{
    public const string SectionName = "Email";

    [Required]
    public string Endpoint { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string SenderAddress { get; init; } = string.Empty;

    [Required]
    [Url]
    public string PublicBaseUrl { get; init; } = string.Empty;

    [Required]
    [EmailAddress]
    public string SupportAddress { get; init; } = string.Empty;
}
