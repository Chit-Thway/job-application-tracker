using System.ComponentModel.DataAnnotations;
using JobTracker.Web.Data;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace JobTracker.Web.Admin;

public sealed record AdminDashboardViewModel(
    int UserCount,
    int VerifiedUserCount,
    int AdminCount,
    int TierTwoUserCount,
    IReadOnlyList<AdminAuditRow> RecentActivity);

public sealed record AdminAuditRow(
    DateTimeOffset OccurredAt,
    string Actor,
    string Action,
    string Details);

public sealed record AdminUserRow(
    string Id,
    string Email,
    string DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    bool EmailConfirmed,
    AccountTier AccountTier,
    int ApplicationCount,
    bool IsAdmin,
    bool IsLocked);

public sealed record AdminUsersViewModel(
    string Query,
    IReadOnlyList<AdminUserRow> Users);

public sealed class AdminDeleteAccountViewModel
{
    [BindNever]
    public string UserId { get; set; } = string.Empty;

    [BindNever]
    public string Email { get; set; } = string.Empty;

    [BindNever]
    public string DisplayName { get; set; } = string.Empty;

    [BindNever]
    public int ApplicationCount { get; set; }

    [BindNever]
    public bool IsAdmin { get; set; }

    [Required(ErrorMessage = "Type the account email address to confirm permanent deletion.")]
    [EmailAddress]
    [Display(Name = "Type the account email address to confirm")]
    public string ConfirmationEmail { get; set; } = string.Empty;
}

public sealed record AdminOperationResult(bool Succeeded, string Message);
