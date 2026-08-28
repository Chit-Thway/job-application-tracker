using Microsoft.AspNetCore.Identity;

namespace JobTracker.Web.Data;

public sealed class ApplicationUser : IdentityUser
{
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastLoginAt { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string TimeZoneId { get; set; } = "Australia/Perth";

    public AccountTier AccountTier { get; set; } = AccountTier.Tier1;

    public int RetentionMonths { get; set; } = 3;

    public int DeletionGraceDays { get; set; } = 14;
}
