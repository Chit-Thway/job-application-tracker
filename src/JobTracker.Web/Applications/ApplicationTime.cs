namespace JobTracker.Web.Applications;

public static class ApplicationTime
{
    public static bool TryConvertToUtc(
        DateTime localDateTime,
        string timeZoneId,
        out DateTimeOffset utcValue)
    {
        utcValue = default;
        if (!TryFindTimeZone(timeZoneId, out var timeZone))
        {
            return false;
        }

        var unspecifiedLocalTime = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(unspecifiedLocalTime))
        {
            return false;
        }

        var utcDateTime = TimeZoneInfo.ConvertTimeToUtc(unspecifiedLocalTime, timeZone);
        utcValue = new DateTimeOffset(utcDateTime, TimeSpan.Zero);
        return true;
    }

    public static DateTime ToLocal(DateTimeOffset value, string timeZoneId)
    {
        var timeZone = TryFindTimeZone(timeZoneId, out var foundTimeZone)
            ? foundTimeZone
            : TimeZoneInfo.Utc;
        return DateTime.SpecifyKind(
            TimeZoneInfo.ConvertTime(value, timeZone).DateTime,
            DateTimeKind.Unspecified);
    }

    public static string Display(DateTimeOffset value, string timeZoneId) =>
        $"{ToLocal(value, timeZoneId):d MMM yyyy, h:mm tt} ({timeZoneId})";

    public static bool IsSupported(string timeZoneId) => TryFindTimeZone(timeZoneId, out _);

    private static bool TryFindTimeZone(string timeZoneId, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
            return true;
        }
        catch (TimeZoneNotFoundException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
