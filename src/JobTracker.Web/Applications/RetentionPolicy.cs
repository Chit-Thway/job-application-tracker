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

public sealed record RetentionPreferences(
    string TimeZoneId,
    int RetentionMonths,
    int DeletionGraceDays);

public static class RetentionPolicy
{
    public const int DefaultRetentionMonths = 3;
    public const int DefaultGracePeriodDays = 14;
    public const int RetentionMonths = DefaultRetentionMonths;
    public const int GracePeriodDays = DefaultGracePeriodDays;

    public static IReadOnlyList<int> AllowedRetentionMonths { get; } =
        Array.AsReadOnly([1, 2, 3]);

    public static IReadOnlyList<int> AllowedGracePeriodDays { get; } =
        Array.AsReadOnly([3, 5, 10, 14]);

    public static DateOnly EligibleOn(
        DateOnly appliedOn,
        int retentionMonths = DefaultRetentionMonths) =>
        appliedOn.AddMonths(retentionMonths);

    public static bool IsEligible(
        DateOnly appliedOn,
        DateTimeOffset now,
        string timeZoneId,
        int retentionMonths = DefaultRetentionMonths) =>
        LocalDate(now, timeZoneId) >= EligibleOn(appliedOn, retentionMonths);

    public static DateTimeOffset? ReconcileSchedule(
        DateOnly appliedOn,
        bool isSavedForever,
        DateTimeOffset? currentSchedule,
        DateTimeOffset now,
        string timeZoneId,
        bool startFreshGracePeriod = false,
        int retentionMonths = DefaultRetentionMonths,
        int gracePeriodDays = DefaultGracePeriodDays)
    {
        if (isSavedForever || !IsEligible(appliedOn, now, timeZoneId, retentionMonths))
        {
            return null;
        }

        if (startFreshGracePeriod || currentSchedule is null)
        {
            return now.AddDays(gracePeriodDays);
        }

        return currentSchedule;
    }

    public static ApplicationRetentionStatus Assess(
        DateOnly appliedOn,
        bool isSavedForever,
        DateTimeOffset? deletionScheduledAt,
        DateTimeOffset now,
        string timeZoneId,
        int retentionMonths = DefaultRetentionMonths)
    {
        var eligibleOn = EligibleOn(appliedOn, retentionMonths);
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
            IsEligible(appliedOn, now, timeZoneId, retentionMonths)
                ? ApplicationRetentionState.Eligible
                : ApplicationRetentionState.Recent,
            eligibleOn,
            null);
    }

    private static DateOnly LocalDate(DateTimeOffset value, string timeZoneId) =>
        DateOnly.FromDateTime(ApplicationTime.ToLocal(value, timeZoneId));

    public static bool IsAllowed(int retentionMonths, int gracePeriodDays) =>
        AllowedRetentionMonths.Contains(retentionMonths)
        && AllowedGracePeriodDays.Contains(gracePeriodDays);
}
