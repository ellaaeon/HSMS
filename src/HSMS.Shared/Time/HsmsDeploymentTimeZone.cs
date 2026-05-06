namespace HSMS.Shared.Time;

/// <summary>
/// HSMS is deployed in the United Arab Emirates (Abu Dhabi). All cycle timestamps are stored in UTC;
/// this type converts between UTC and the deployment display/filter zone (UTC+04:00, no DST).
/// </summary>
public static class HsmsDeploymentTimeZone
{
    /// <summary>Windows / .NET on Windows.</summary>
    public const string WindowsTimeZoneId = "Arabian Standard Time";

    /// <summary>IANA id used on Linux and macOS.</summary>
    public const string IanaTimeZoneId = "Asia/Dubai";

    private static readonly Lazy<TimeZoneInfo> LazyZone = new(Resolve);

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(WindowsTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(IanaTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
        }
        catch (InvalidTimeZoneException)
        {
        }

        return TimeZoneInfo.Utc;
    }

    public static TimeZoneInfo Zone => LazyZone.Value;

    /// <summary>
    /// Normalizes a value read from the database: stored as UTC wall clock, often surfaced as <see cref="DateTimeKind.Unspecified"/>.
    /// </summary>
    public static DateTime AsUtcKind(DateTime stored) =>
        stored.Kind switch
        {
            DateTimeKind.Utc => stored,
            DateTimeKind.Local => stored.ToUniversalTime(),
            _ => DateTime.SpecifyKind(stored, DateTimeKind.Utc)
        };

    public static DateTime UtcToDeployment(DateTime utcOrUnspecifiedFromDb) =>
        TimeZoneInfo.ConvertTimeFromUtc(AsUtcKind(utcOrUnspecifiedFromDb), Zone);

    /// <summary>
    /// Interprets <paramref name="calendarDateAndTime"/> as a wall-clock instant in the deployment zone and returns UTC.
    /// </summary>
    public static DateTime DeploymentWallToUtc(DateTime calendarDateAndTime) =>
        TimeZoneInfo.ConvertTimeToUtc(
            DateTime.SpecifyKind(calendarDateAndTime, DateTimeKind.Unspecified),
            Zone);

    public static string FormatInDeploymentZone(DateTime utcOrUnspecifiedFromDb, string format, IFormatProvider provider) =>
        UtcToDeployment(utcOrUnspecifiedFromDb).ToString(format, provider);

    /// <summary>Start of the selected calendar day in the deployment zone, as UTC (inclusive).</summary>
    public static DateTime? DeploymentCalendarDayStartUtc(DateTime? dateFromPicker)
    {
        if (dateFromPicker is null) return null;
        var start = DateTime.SpecifyKind(dateFromPicker.Value.Date, DateTimeKind.Unspecified);
        return DeploymentWallToUtc(start);
    }

    /// <summary>End of the selected calendar day in the deployment zone, as UTC (inclusive).</summary>
    public static DateTime? DeploymentCalendarDayEndUtc(DateTime? dateFromPicker)
    {
        if (dateFromPicker is null) return null;
        var end = DateTime.SpecifyKind(dateFromPicker.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Unspecified);
        return DeploymentWallToUtc(end);
    }

    /// <summary>Current date/time in the deployment zone (from UTC now).</summary>
    public static DateTime NowInDeploymentZone() => UtcToDeployment(DateTime.UtcNow);
}
