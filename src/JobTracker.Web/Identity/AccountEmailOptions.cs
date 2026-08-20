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

}
