namespace JobTracker.Web.Applications;

public enum ApplicationRetentionState
{
    Recent,
    Eligible,
    DeletionScheduled,
    Saved,
}

public sealed record ApplicationRetentionStatus(
    ApplicationRetentionState State,
    DateOnly EligibleOn,
    DateTimeOffset? DeletionScheduledAt)
{
    public bool IsDeletionScheduled => State == ApplicationRetentionState.DeletionScheduled;
}

public static class RetentionPolicy
{
    public const int RetentionMonths = 3;
    public const int GracePeriodDays = 14;

    public static DateOnly EligibleOn(DateOnly appliedOn) =>
        appliedOn.AddMonths(RetentionMonths);

    public static bool IsEligible(
        DateOnly appliedOn,
        DateTimeOffset now,
        string timeZoneId) =>
        LocalDate(now, timeZoneId) >= EligibleOn(appliedOn);

    public static DateTimeOffset? ReconcileSchedule(
        DateOnly appliedOn,
        bool isSavedForever,
        DateTimeOffset? currentSchedule,
        DateTimeOffset now,
        string timeZoneId,
        bool startFreshGracePeriod = false)
    {
        if (isSavedForever || !IsEligible(appliedOn, now, timeZoneId))
        {
            return null;
        }

        if (startFreshGracePeriod || currentSchedule is null)
        {
            return now.AddDays(GracePeriodDays);
        }

        return currentSchedule;
    }

    public static ApplicationRetentionStatus Assess(
        DateOnly appliedOn,
        bool isSavedForever,
        DateTimeOffset? deletionScheduledAt,
        DateTimeOffset now,
        string timeZoneId)
    {
        var eligibleOn = EligibleOn(appliedOn);
        if (isSavedForever)
        {
            return new ApplicationRetentionStatus(
                ApplicationRetentionState.Saved,
                eligibleOn,
                null);
        }

        if (deletionScheduledAt is not null)
        {
            return new ApplicationRetentionStatus(
                ApplicationRetentionState.DeletionScheduled,
                eligibleOn,
                deletionScheduledAt);
        }

        return new ApplicationRetentionStatus(
            IsEligible(appliedOn, now, timeZoneId)
                ? ApplicationRetentionState.Eligible
                : ApplicationRetentionState.Recent,
            eligibleOn,
            null);
    }

    private static DateOnly LocalDate(DateTimeOffset value, string timeZoneId) =>
        DateOnly.FromDateTime(ApplicationTime.ToLocal(value, timeZoneId));
}
