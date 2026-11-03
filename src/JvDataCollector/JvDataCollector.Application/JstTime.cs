namespace JvDataCollector;

public static class Jst
{
    public const string Ancient = "19860101000000";

    private static readonly TimeZoneInfo Zone = Resolve();

    public static DateTimeOffset Now(DateTimeOffset utc) => TimeZoneInfo.ConvertTime(utc, Zone);

    public static string Stamp(DateTimeOffset utc) =>
        Now(utc).ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);

    public static string Day(DateTimeOffset utc) =>
        Now(utc).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);

    public static string AddDays(string stamp, int days)
    {
        var parsed = DateTime.ParseExact(
            stamp,
            "yyyyMMddHHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.None);
        return parsed.AddDays(days).ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);
    }

    public static string Display(string? stamp)
    {
        if (string.IsNullOrEmpty(stamp) || stamp.Length < 12)
        {
            return "";
        }

        return $"{stamp.Substring(4, 2)}-{stamp.Substring(6, 2)} {stamp.Substring(8, 2)}:{stamp.Substring(10, 2)}";
    }

    public static string FormatNext(DateTimeOffset utcNow, DateTimeOffset nextUtc)
    {
        var now = Now(utcNow);
        var next = Now(nextUtc);
        var today = DateOnly.FromDateTime(now.DateTime);
        var day = DateOnly.FromDateTime(next.DateTime);
        var hm = next.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (day == today)
        {
            return $"今日 {hm}";
        }

        if (day == today.AddDays(1))
        {
            return $"明日 {hm}";
        }

        return $"{Weekday(day.DayOfWeek)} {hm}";
    }

    public static string Weekday(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "月曜",
        DayOfWeek.Tuesday => "火曜",
        DayOfWeek.Wednesday => "水曜",
        DayOfWeek.Thursday => "木曜",
        DayOfWeek.Friday => "金曜",
        DayOfWeek.Saturday => "土曜",
        _ => "日曜",
    };

    private static TimeZoneInfo Resolve()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        }
    }
}
