using System.ComponentModel.DataAnnotations;
using JobTracker.Web.Data;

namespace JobTracker.Web.Admin;

public sealed record AdminDashboardViewModel(
    int UserCount,
    int VerifiedUserCount,
    int AdminCount,
    int TierTwoUserCount,
    int AvailableInvitationCount,
    IReadOnlyList<AdminAuditRow> RecentActivity);

public sealed record AdminAuditRow(
    DateTimeOffset OccurredAt,
    string Actor,
    string Action,
    string Details);

public sealed record AdminUserRow(
    string Id,
    string Email,
    string? PhoneNumber,
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

public sealed record AdminInvitationRow(
    Guid Id,
    string RecipientEmail,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt,
    string Status);

public sealed record AdminInvitationsViewModel(
    CreateAdminInvitationInput Input,
    IReadOnlyList<AdminInvitationRow> Invitations);

public sealed class CreateAdminInvitationInput
{
    [Required]
    [EmailAddress]
    [StringLength(320)]
    [Display(Name = "Recipient email")]
    public string Email { get; set; } = string.Empty;

    [Range(1, 30)]
    [Display(Name = "Expires after")]
    public int ValidForDays { get; set; } = 7;
}

public sealed record AdminOperationResult(bool Succeeded, string Message);
