using JobTracker.Web.Applications;

namespace JobTracker.UnitTests;

public sealed class RetentionPolicyTests
{
    [Theory]
    [InlineData(2026, 1, 31, 2026, 4, 30)]
    [InlineData(2026, 11, 30, 2027, 2, 28)]
    [InlineData(2027, 11, 29, 2028, 2, 29)]
    public void EligibleOn_UsesCalendarMonthArithmetic(
        int year,
        int month,
        int day,
        int expectedYear,
        int expectedMonth,
        int expectedDay)
    {
        var appliedOn = new DateOnly(year, month, day);

        Assert.Equal(
            new DateOnly(expectedYear, expectedMonth, expectedDay),
            RetentionPolicy.EligibleOn(appliedOn));
    }

    [Fact]
    public void IsEligible_UsesTheOwnersLocalCalendarDate()
    {
        var appliedOn = new DateOnly(2026, 5, 14);

        Assert.False(RetentionPolicy.IsEligible(
            appliedOn,
            new DateTimeOffset(2026, 8, 13, 15, 59, 59, TimeSpan.Zero),
            "Australia/Perth"));
        Assert.True(RetentionPolicy.IsEligible(
            appliedOn,
            new DateTimeOffset(2026, 8, 13, 16, 0, 0, TimeSpan.Zero),
            "Australia/Perth"));
    }

    [Fact]
    public void ReconcileSchedule_GrantsFourteenFullDaysAndPreservesExistingGrace()
    {
        var now = new DateTimeOffset(2026, 8, 14, 3, 25, 0, TimeSpan.Zero);
        var appliedOn = new DateOnly(2026, 5, 14);
        var existing = now.AddDays(6);

        Assert.Equal(
            now.AddDays(14),
            RetentionPolicy.ReconcileSchedule(
                appliedOn,
                false,
                null,
                now,
                "Australia/Perth"));
        Assert.Equal(
            existing,
            RetentionPolicy.ReconcileSchedule(
                appliedOn,
                false,
                existing,
                now,
                "Australia/Perth"));
    }

    [Fact]
    public void ReconcileSchedule_SavingOrBecomingRecentClearsTheSchedule()
    {
        var now = new DateTimeOffset(2026, 8, 14, 3, 25, 0, TimeSpan.Zero);
        var schedule = now.AddDays(7);

        Assert.Null(RetentionPolicy.ReconcileSchedule(
            new DateOnly(2026, 5, 14),
            true,
            schedule,
            now,
            "Australia/Perth"));
        Assert.Null(RetentionPolicy.ReconcileSchedule(
            new DateOnly(2026, 8, 1),
            false,
            schedule,
            now,
            "Australia/Perth"));
    }

    [Fact]
    public void ReconcileSchedule_NewlyUnsavedOldRecordGetsAFreshGracePeriod()
    {
        var now = new DateTimeOffset(2026, 8, 14, 3, 25, 0, TimeSpan.Zero);

        Assert.Equal(
            now.AddDays(14),
            RetentionPolicy.ReconcileSchedule(
                new DateOnly(2026, 4, 1),
                false,
                now.AddHours(1),
                now,
                "Australia/Perth",
                startFreshGracePeriod: true));
    }

    [Fact]
    public void Assess_ExplainsAllUserVisibleStates()
    {
        var now = new DateTimeOffset(2026, 8, 14, 4, 0, 0, TimeSpan.Zero);
        var old = new DateOnly(2026, 5, 14);
        var recent = new DateOnly(2026, 8, 1);
        var scheduled = now.AddDays(14);

        Assert.Equal(
            ApplicationRetentionState.Saved,
            RetentionPolicy.Assess(old, true, null, now, "Australia/Perth").State);
        Assert.Equal(
            ApplicationRetentionState.Recent,
            RetentionPolicy.Assess(recent, false, null, now, "Australia/Perth").State);
        Assert.Equal(
            ApplicationRetentionState.Eligible,
            RetentionPolicy.Assess(old, false, null, now, "Australia/Perth").State);
        Assert.Equal(
            ApplicationRetentionState.DeletionScheduled,
            RetentionPolicy.Assess(old, false, scheduled, now, "Australia/Perth").State);
    }
}
