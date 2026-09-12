using Microsoft.AspNetCore.Identity;

namespace JobTracker.Web.Data;

public sealed class ApplicationUser : IdentityUser
{
    public const string DefaultTimeZoneId = "Australia/Perth";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string TimeZoneId { get; set; } = DefaultTimeZoneId;

    public AccountTier AccountTier { get; set; } = AccountTier.Tier1;

    public Guid? EmailVerificationChallengeId { get; set; }

    public string? EmailVerificationCodeHash { get; set; }

    public DateTimeOffset? EmailVerificationCodeExpiresAt { get; set; }

    public DateTimeOffset? EmailVerificationCodeLastSentAt { get; set; }

    public int EmailVerificationFailedAttempts { get; set; }

    public DateTimeOffset? TermsAcceptedAt { get; set; }

    public string? TermsVersion { get; set; }

    public DateTimeOffset? PrivacyAcknowledgedAt { get; set; }

    public string? PrivacyVersion { get; set; }

    public int RetentionMonths { get; set; } = 3;

    public int DeletionGraceDays { get; set; } = 14;
}
