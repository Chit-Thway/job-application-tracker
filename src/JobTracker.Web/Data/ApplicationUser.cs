using Microsoft.AspNetCore.Identity;

namespace JobTracker.Web.Data;

public sealed class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = string.Empty;

    public string TimeZoneId { get; set; } = "Australia/Perth";

    public int RetentionMonths { get; set; } = 3;

    public int DeletionGraceDays { get; set; } = 14;
}
