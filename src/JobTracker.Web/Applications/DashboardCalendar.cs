using JobTracker.Web.Data;

namespace JobTracker.Web.Applications;

public sealed record DashboardMonth(
    DateOnly StartsOn,
    DateOnly EndsOn,
    string Label);

public sealed record DashboardCalendarWindow(
    string TimeZoneId,
    DateOnly Today,
    DateOnly StartsOn,
    DateOnly EndsOn,
    DateTimeOffset StartsAtUtc,
    DateTimeOffset EndsAtUtcExclusive,
    IReadOnlyList<DashboardMonth> Months)
{
    public bool Contains(DateOnly date) => date >= StartsOn && date <= EndsOn;

    public bool Contains(DateTimeOffset instant) =>
        instant >= StartsAtUtc && instant < EndsAtUtcExclusive;
}

public enum GhostingAttentionKind
{
    None,
    FollowUp,
    ConfirmGhosted,
}

public sealed record GhostingAssessment(
    GhostingAttentionKind Kind,
    int DaysSinceApplied);

public static class DashboardCalendar
{
    public static DashboardCalendarWindow Create(
        DateTimeOffset utcNow,
        string requestedTimeZoneId)
    {
        var timeZoneId = ApplicationTime.IsSupported(requestedTimeZoneId)
            ? requestedTimeZoneId
            : TimeZoneInfo.Utc.Id;
        var localNow = ApplicationTime.ToLocal(utcNow, timeZoneId);
        var today = DateOnly.FromDateTime(localNow);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        var startsOn = currentMonth.AddMonths(-2);
        var endsAtExclusive = currentMonth.AddMonths(1);
        var endsOn = endsAtExclusive.AddDays(-1);

        if (!ApplicationTime.TryConvertToUtc(
                startsOn.ToDateTime(TimeOnly.MinValue),
                timeZoneId,
                out var startsAtUtc)
            || !ApplicationTime.TryConvertToUtc(
                endsAtExclusive.ToDateTime(TimeOnly.MinValue),
                timeZoneId,
                out var endsAtUtcExclusive))
        {
            timeZoneId = TimeZoneInfo.Utc.Id;
            startsAtUtc = new DateTimeOffset(
                startsOn.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            endsAtUtcExclusive = new DateTimeOffset(
                endsAtExclusive.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
        }

        var months = Enumerable.Range(0, 3)
            .Select(offset => startsOn.AddMonths(offset))
            .Select(start => new DashboardMonth(
                start,
                start.AddMonths(1).AddDays(-1),
                start.ToString("MMM yyyy")))
            .ToList();

        return new DashboardCalendarWindow(
            timeZoneId,
            today,
            startsOn,
            endsOn,
            startsAtUtc,
            endsAtUtcExclusive,
            months);
    }

    public static GhostingAssessment AssessGhosting(
        DateOnly appliedOn,
        DateOnly today,
        ApplicationOutcome outcome,
        bool hasMeaningfulResponse)
    {
        var daysSinceApplied = Math.Max(0, today.DayNumber - appliedOn.DayNumber);
        if (outcome != ApplicationOutcome.Active || hasMeaningfulResponse)
        {
            return new GhostingAssessment(GhostingAttentionKind.None, daysSinceApplied);
        }

        var kind = daysSinceApplied >= ApplicationRules.GhostingConfirmationAfterDays
            ? GhostingAttentionKind.ConfirmGhosted
            : daysSinceApplied >= ApplicationRules.FollowUpSuggestionAfterDays
                ? GhostingAttentionKind.FollowUp
                : GhostingAttentionKind.None;
        return new GhostingAssessment(kind, daysSinceApplied);
    }
}
