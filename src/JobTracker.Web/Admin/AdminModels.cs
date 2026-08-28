using System.ComponentModel.DataAnnotations;
using JobTracker.Web.Data;

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

public sealed record AdminOperationResult(bool Succeeded, string Message);
