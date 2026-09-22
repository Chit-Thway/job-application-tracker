namespace JobTracker.Web.Data;

public sealed class EmailVerificationMonthlyUsage
{
    public DateTimeOffset MonthStart { get; set; }

    public int SentCount { get; set; }
}
