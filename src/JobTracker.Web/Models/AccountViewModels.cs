using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace JobTracker.Web.Models;

public sealed class LoginViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }

    public string? ReturnUrl { get; set; }

    [BindNever]
    public bool EmailVerificationRequired { get; set; }
}

public sealed class RegisterViewModel
{
    [Required]
    [MaxLength(120)]
    [Display(Name = "Your name")]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [StringLength(128, MinimumLength = 12)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    [Display(Name = "Confirm password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Range(typeof(bool), "true", "true", ErrorMessage = "You must agree to the Terms of Service and acknowledge the Privacy Policy.")]
    [Display(Name = "I agree to the Terms of Service and acknowledge the Privacy Policy")]
    public bool AcceptPolicies { get; set; }
}

public sealed class EmailVerificationViewModel
{
    [Required]
    public Guid ChallengeId { get; set; }

    [Required]
    [Display(Name = "Verification code")]
    [RegularExpression(@"^[0-9]{6}$", ErrorMessage = "Enter the six-digit code from your email.")]
    public string Code { get; set; } = string.Empty;

    [BindNever]
    public string Email { get; set; } = string.Empty;

    [BindNever]
    public int ResendAvailableInSeconds { get; set; }

    [BindNever]
    public DateTimeOffset? CodeExpiresAt { get; set; }
}

public sealed class EmailActionViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public sealed class ResetPasswordViewModel
{
    [Required]
    public string UserId { get; set; } = string.Empty;

    [Required]
    public string Code { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [MinLength(12)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public sealed record AccountResultViewModel(
    string Title,
    string Message,
    bool IsSuccess);
