namespace JvDataCollector;

public static class MeetingCalendar
{
    public static bool IsMeetingDay(DateOnly day, IReadOnlyCollection<string> meetingDays)
    {
        if (meetingDays.Count == 0)
        {
            return day.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday;
        }

        return meetingDays.Contains(day.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
    }

    public static bool IsMeetingSide(DateOnly day, IReadOnlyCollection<string> meetingDays) =>
        day.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday
        && IsMeetingDay(day, meetingDays);

    public static IReadOnlyList<string> UpcomingMeetingDays(DateOnly today, IReadOnlyCollection<string> meetingDays)
    {
        if (meetingDays.Count == 0)
        {
            return UpcomingWeekend(today)
                .Select(day => day.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
        }

        var limit = today.AddDays(7);
        return meetingDays
            .Where(day => day.Length == 8 && day.CompareTo(today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture)) >= 0)
            .Where(day =>
            {
                var parsed = DateOnly.ParseExact(day, "yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
                return parsed < limit;
            })
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<DateOnly> UpcomingWeekend(DateOnly today)
    {
        var list = new List<DateOnly>();
        for (var day = today; day < today.AddDays(7); day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                list.Add(day);
            }

            if (day.DayOfWeek == DayOfWeek.Sunday && list.Count > 0)
            {
                break;
            }
        }

        return list;
    }
}

public static class OddsWindow
{
    public static bool IsOpen(DateTimeOffset jst, IReadOnlyCollection<string> meetingDays)
    {
        var day = DateOnly.FromDateTime(jst.DateTime);
        var time = TimeOnly.FromTimeSpan(jst.TimeOfDay);
        if (MeetingCalendar.IsMeetingDay(day, meetingDays) && time >= new TimeOnly(9, 0) && time < new TimeOnly(17, 0))
        {
            return true;
        }

        return jst.DayOfWeek == DayOfWeek.Friday
            && time >= new TimeOnly(19, 0)
            && time < new TimeOnly(21, 0)
            && WeekendSale(day, meetingDays);
    }

    public static bool WatchDesired(DateTimeOffset jst, IReadOnlyCollection<string> meetingDays)
    {
        var day = DateOnly.FromDateTime(jst.DateTime);
        var time = TimeOnly.FromTimeSpan(jst.TimeOfDay);
        return MeetingCalendar.IsMeetingDay(day, meetingDays)
            && time >= new TimeOnly(8, 0)
            && time < new TimeOnly(17, 30);
    }

    private static bool WeekendSale(DateOnly friday, IReadOnlyCollection<string> meetingDays)
    {
        if (meetingDays.Count == 0)
        {
            return true;
        }

        var saturday = friday.AddDays(1).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        var sunday = friday.AddDays(2).ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
        return meetingDays.Contains(saturday) || meetingDays.Contains(sunday);
    }
}

public static class RealtimePlanner
{
    public static IReadOnlyList<CollectionJob> ForEvent(RealtimeEventKind kind, string key)
    {
        var value = key ?? "";
        var day = value.Length >= 8 && RecordKeys.Digits(System.Text.Encoding.ASCII.GetBytes(value[..8]))
            ? value[..8]
            : value;
        return kind switch
        {
            RealtimeEventKind.Weight =>
            [
                Event("0B11", value, day),
                Event("0B13", value, day),
                Event("0B17", value, day),
            ],
            RealtimeEventKind.Weather =>
            [
                Event("0B16", value, day),
                Event("0B14", day, day),
                Event("0B15", day, day),
                Event("0B13", day, day),
                Event("0B17", day, day),
            ],
            RealtimeEventKind.JockeyChange or RealtimeEventKind.CourseChange or RealtimeEventKind.Scratch or RealtimeEventKind.PostTime =>
            [
                Event("0B16", value, day),
                Event("0B14", day, day),
                Event("0B15", day, day),
            ],
            _ =>
            [
                Event("0B12", value, day),
                Event("0B15", value, day),
            ],
        };
    }

    public static IReadOnlyList<CollectionJob> Odds(DateTimeOffset jst, IReadOnlyCollection<string> meetingDays, IReadOnlyList<string> raceKeys)
    {
        var targets = TargetDays(jst, meetingDays);
        var keys = new List<string>();
        foreach (var race in raceKeys)
        {
            if (race.Length >= 8 && targets.Contains(race[..8]))
            {
                keys.Add(race);
            }
        }

        if (keys.Count == 0)
        {
            keys.AddRange(targets);
        }

        var jobs = new List<CollectionJob>();
        foreach (var key in keys)
        {
            var day = key.Length >= 8 ? key[..8] : key;
            foreach (var spec in DataspecCatalog.OddsSweep)
            {
                jobs.Add(new CollectionJob
                {
                    Kind = JobKind.Odds,
                    Priority = JobPriority.OddsSweep,
                    Group = JobGroups.Odds,
                    Dataspec = spec,
                    Key = key,
                    MeetingDay = day,
                });
            }
        }

        return jobs;
    }

    public static IReadOnlyList<string> TargetDays(DateTimeOffset jst, IReadOnlyCollection<string> meetingDays)
    {
        var day = DateOnly.FromDateTime(jst.DateTime);
        var list = new List<string>();
        var time = TimeOnly.FromTimeSpan(jst.TimeOfDay);
        if (MeetingCalendar.IsMeetingDay(day, meetingDays) && time >= new TimeOnly(9, 0) && time < new TimeOnly(17, 0))
        {
            list.Add(day.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
        }

        if (jst.DayOfWeek == DayOfWeek.Friday && time >= new TimeOnly(19, 0) && time < new TimeOnly(21, 0))
        {
            foreach (var upcoming in MeetingCalendar.UpcomingWeekend(day).SkipWhile(item => item == day))
            {
                if (MeetingCalendar.IsMeetingDay(upcoming, meetingDays))
                {
                    list.Add(upcoming.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
                }
            }
        }

        return list.Distinct(StringComparer.Ordinal).ToArray();
    }

    private static CollectionJob Event(string spec, string key, string day) => new()
    {
        Kind = JobKind.Realtime,
        Priority = JobPriority.RealtimeEvent,
        Dataspec = spec,
        Key = key,
        MeetingDay = day,
    };
}
