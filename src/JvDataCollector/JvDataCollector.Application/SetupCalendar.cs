using System.Globalization;

namespace JvDataCollector;

public static class SetupCalendar
{
    public static bool SplitsByYear(CollectionJob job) =>
        job.Kind == JobKind.Stored && job.Option == 4 && job.Dataspec == "RACE";

    public static IReadOnlyList<string> Windows(string fromTime, int currentYear)
    {
        if (string.IsNullOrEmpty(fromTime))
        {
            return [fromTime];
        }

        var hyphen = fromTime.IndexOf('-');
        if (hyphen >= 0 && hyphen != 14)
        {
            return [fromTime];
        }

        var start = hyphen == 14 ? fromTime[..14] : fromTime;
        if (start.Length < 4 || !int.TryParse(start[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var startYear))
        {
            return [fromTime];
        }

        if (currentYear < startYear)
        {
            return [fromTime];
        }

        var windows = new List<string>();
        var year = startYear;
        if (hyphen == 14)
        {
            windows.Add(fromTime);
            var end = fromTime[(hyphen + 1)..];
            if (end.Length < 4 || !int.TryParse(end[..4], NumberStyles.None, CultureInfo.InvariantCulture, out year))
            {
                return windows;
            }
        }

        for (; year < currentYear; year++)
        {
            var begin = year == startYear && hyphen != 14 ? start : YearStart(year);
            windows.Add(begin + "-" + YearStart(year + 1));
        }

        if (year == currentYear)
        {
            windows.Add(year == startYear && hyphen != 14 ? start : YearStart(currentYear));
        }

        return windows;
    }

    public static string YearStart(int year) =>
        year.ToString("D4", CultureInfo.InvariantCulture) + "0101000000";
}
