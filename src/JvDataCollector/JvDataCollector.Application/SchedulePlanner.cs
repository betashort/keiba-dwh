namespace JvDataCollector;

public sealed record PlannedWork(string FireKey, CollectionJob Job);

public static class SchedulePlanner
{
    public static IReadOnlyList<PlannedWork> Due(
        DateTimeOffset utcNow,
        IReadOnlyList<string> enabled,
        IReadOnlySet<string> option1Rejected,
        IReadOnlyCollection<string> meetingDays)
    {
        var jst = Jst.Now(utcNow);
        var today = DateOnly.FromDateTime(jst.DateTime);
        var time = TimeOnly.FromTimeSpan(jst.TimeOfDay);
        var works = new List<PlannedWork>();
        foreach (var slot in Slots)
        {
            if (time < slot.Time)
            {
                continue;
            }

            if (slot.Day is not null && jst.DayOfWeek != slot.Day)
            {
                continue;
            }

            var specs = slot.All
                ? enabled.Where(spec => !option1Rejected.Contains(spec)).ToArray()
                : slot.Specs.Where(spec => enabled.Contains(spec) && !option1Rejected.Contains(spec)).ToArray();
            foreach (var spec in specs)
            {
                works.Add(Stored(today, slot.Time, spec, slot.Weekly));
            }
        }

        var weekly = works.Where(item => item.Job.WeeklyReread).Select(item => item.Job.Dataspec).ToHashSet();
        works.RemoveAll(item => !item.Job.WeeklyReread && item.Job.Kind == JobKind.Stored && weekly.Contains(item.Job.Dataspec));

        if (time >= new TimeOnly(8, 0) && MeetingCalendar.IsMeetingDay(today, meetingDays))
        {
            var day = today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            works.Add(Realtime(today, new TimeOnly(8, 0), "0B14", day));
            works.Add(Realtime(today, new TimeOnly(8, 0), "0B15", day));
            works.Add(Realtime(today, new TimeOnly(8, 0), "0B13", day));
            works.Add(Realtime(today, new TimeOnly(8, 0), "0B17", day));
        }

        if (time >= new TimeOnly(13, 30) && MeetingCalendar.IsMeetingSide(today, meetingDays))
        {
            var day = today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture);
            works.Add(Realtime(today, new TimeOnly(13, 30), "0B15", day));
            works.Add(Realtime(today, new TimeOnly(13, 30), "0B13", day));
            works.Add(Realtime(today, new TimeOnly(13, 30), "0B17", day));
        }

        if (jst.DayOfWeek == DayOfWeek.Thursday && time >= new TimeOnly(21, 0))
        {
            foreach (var day in MeetingCalendar.UpcomingMeetingDays(today, meetingDays))
            {
                works.Add(Realtime(today, new TimeOnly(21, 0), "0B15", day));
            }
        }

        return works;
    }

    public static DateTimeOffset? NextUtc(DateTimeOffset utcNow, string dataspec, IReadOnlyList<string> enabled)
    {
        if (!enabled.Contains(dataspec) && dataspec is not ("0B13" or "0B14" or "0B15" or "0B17"))
        {
            return null;
        }

        var jst = Jst.Now(utcNow);
        var start = DateOnly.FromDateTime(jst.DateTime);
        for (var offset = 0; offset < 8; offset++)
        {
            var day = start.AddDays(offset);
            foreach (var slot in CandidateTimes(dataspec, day.DayOfWeek).Order())
            {
                var moment = new DateTime(day, slot, DateTimeKind.Unspecified);
                var zoned = new DateTimeOffset(moment, jst.Offset);
                if (zoned > jst)
                {
                    return zoned.ToUniversalTime();
                }
            }
        }

        return null;
    }

    private static IEnumerable<TimeOnly> CandidateTimes(string dataspec, DayOfWeek day)
    {
        foreach (var slot in Slots)
        {
            if (slot.Day is not null && slot.Day != day)
            {
                continue;
            }

            var stored = DataspecCatalog.Stored.Any(item => item.Code == dataspec);
            if (slot.All)
            {
                if (stored)
                {
                    yield return slot.Time;
                }
            }
            else if (slot.Specs.Contains(dataspec))
            {
                yield return slot.Time;
            }
        }

        if (dataspec is "0B13" or "0B15" or "0B17")
        {
            yield return new TimeOnly(13, 30);
        }

        if (dataspec is "0B13" or "0B14" or "0B15" or "0B17")
        {
            yield return new TimeOnly(8, 0);
        }

        if (dataspec == "0B15" && day == DayOfWeek.Thursday)
        {
            yield return new TimeOnly(21, 0);
        }
    }

    private static PlannedWork Stored(DateOnly day, TimeOnly time, string spec, bool weekly)
    {
        var key = $"{day:yyyyMMdd}-{time:HHmm}-{spec}-{(weekly ? "W" : "D")}";
        return new PlannedWork(key, new CollectionJob
        {
            Kind = JobKind.Stored,
            Priority = JobPriority.Scheduled,
            Dataspec = spec,
            Option = 1,
            WeeklyReread = weekly,
        });
    }

    private static PlannedWork Realtime(DateOnly day, TimeOnly time, string spec, string key)
    {
        var fire = $"{day:yyyyMMdd}-{time:HHmm}-{spec}-{key}";
        return new PlannedWork(fire, new CollectionJob
        {
            Kind = JobKind.Realtime,
            Priority = JobPriority.RealtimeScheduled,
            Dataspec = spec,
            Key = key,
            MeetingDay = key.Length >= 8 ? key[..8] : key,
        });
    }

    private readonly record struct Slot(TimeOnly Time, DayOfWeek? Day, string[] Specs, bool All, bool Weekly);

    private static readonly Slot[] Slots =
    [
        new(new TimeOnly(6, 30), null, ["SLOP", "WOOD"], false, false),
        new(new TimeOnly(6, 30), DayOfWeek.Monday, [], true, true),
        new(new TimeOnly(13, 30), null, [], true, false),
        new(new TimeOnly(15, 0), DayOfWeek.Monday, ["DIFF", "DIFN", "BLOD", "BLDN", "SNAP", "SNPN", "YSCH", "MING", "RACE"], false, false),
        new(new TimeOnly(21, 0), DayOfWeek.Thursday, ["RACE", "DIFF", "DIFN", "BLOD", "BLDN", "SNAP", "SNPN"], false, false),
        new(new TimeOnly(21, 0), DayOfWeek.Sunday, ["TOKU"], false, false),
        new(new TimeOnly(21, 0), DayOfWeek.Monday, ["TOKU"], false, false),
        new(new TimeOnly(12, 0), DayOfWeek.Tuesday, ["HOYU"], false, false),
        new(new TimeOnly(23, 0), null, [], true, false),
    ];
}
