using JobTracker.Web.Applications;
using JobTracker.Web.Data;

namespace JobTracker.UnitTests;

public sealed class DashboardCalendarTests
{
    [Fact]
    public void Window_UsesCalendarMonthsAcrossYearBoundary()
    {
        var window = DashboardCalendar.Create(
            new DateTimeOffset(2026, 1, 15, 4, 0, 0, TimeSpan.Zero),
            "Australia/Perth");

        Assert.Equal(new DateOnly(2025, 11, 1), window.StartsOn);
        Assert.Equal(new DateOnly(2026, 1, 31), window.EndsOn);
        Assert.Equal(["Nov 2025", "Dec 2025", "Jan 2026"], window.Months.Select(item => item.Label));
    }

    [Fact]
    public void Window_IncludesLeapDayWithoutUsingNinetyDayApproximation()
    {
        var window = DashboardCalendar.Create(
            new DateTimeOffset(2024, 3, 31, 12, 0, 0, TimeSpan.Zero),
            "Australia/Perth");

        Assert.Equal(new DateOnly(2024, 1, 1), window.StartsOn);
        Assert.Equal(new DateOnly(2024, 3, 31), window.EndsOn);
        Assert.True(window.Contains(new DateOnly(2024, 2, 29)));
        Assert.False(window.Contains(new DateOnly(2023, 12, 31)));
    }

    [Fact]
    public void Window_UsesUsersLocalMonthAtUtcEdge()
    {
        var window = DashboardCalendar.Create(
            new DateTimeOffset(2026, 8, 31, 16, 30, 0, TimeSpan.Zero),
            "Australia/Perth");

        Assert.Equal(new DateOnly(2026, 9, 1), window.Today);
        Assert.Equal(new DateOnly(2026, 7, 1), window.StartsOn);
        Assert.Equal(new DateTimeOffset(2026, 6, 30, 16, 0, 0, TimeSpan.Zero), window.StartsAtUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 16, 0, 0, TimeSpan.Zero), window.EndsAtUtcExclusive);
    }

    [Theory]
    [InlineData(13, GhostingAttentionKind.None)]
    [InlineData(14, GhostingAttentionKind.FollowUp)]
    [InlineData(29, GhostingAttentionKind.FollowUp)]
    [InlineData(30, GhostingAttentionKind.ConfirmGhosted)]
    public void Ghosting_UsesExactLocalCalendarDayBoundaries(
        int daysSinceApplied,
        GhostingAttentionKind expected)
    {
        var today = new DateOnly(2026, 8, 12);

        var result = DashboardCalendar.AssessGhosting(
            today.AddDays(-daysSinceApplied),
            today,
            ApplicationOutcome.Active,
            hasMeaningfulResponse: false);

        Assert.Equal(expected, result.Kind);
        Assert.Equal(daysSinceApplied, result.DaysSinceApplied);
    }

    [Fact]
    public void Ghosting_NeverWarnsAfterResponseOrForClosedOutcome()
    {
        var today = new DateOnly(2026, 8, 12);

        Assert.Equal(
            GhostingAttentionKind.None,
            DashboardCalendar.AssessGhosting(
                today.AddDays(-45),
                today,
                ApplicationOutcome.Active,
                hasMeaningfulResponse: true).Kind);
        Assert.Equal(
            GhostingAttentionKind.None,
            DashboardCalendar.AssessGhosting(
                today.AddDays(-45),
                today,
                ApplicationOutcome.Rejected,
                hasMeaningfulResponse: false).Kind);
    }
}
