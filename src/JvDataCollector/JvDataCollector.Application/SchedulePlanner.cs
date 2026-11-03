namespace JvDataCollector;

public sealed record PlannedWork(string FireKey, CollectionJob Job);

public static class SchedulePlanner
{
    public static IReadOnlyList<PlannedWork> Due(
        DateTimeOffset utcNow,
        SchedulePlan plan,
        IReadOnlySet<string> option1Rejected,
        IReadOnlyCollection<string> meetingDays)
    {
        var jst = Jst.Now(utcNow);
        var today = DateOnly.FromDateTime(jst.DateTime);
        var time = TimeOnly.FromTimeSpan(jst.TimeOfDay);
        var works = new List<PlannedWork>();
        foreach (var row in plan.Stored)
        {
            if (!row.Enabled || option1Rejected.Contains(row.Dataspec))
            {
                continue;
            }

            if (!ScheduleText.TryDay(row.Day, out var day) || !ScheduleText.TryTime(row.Time, out var slot))
            {
                continue;
            }

            if (time < slot)
            {
                continue;
            }

            if (day is not null && jst.DayOfWeek != day)
            {
                continue;
            }

            works.Add(Stored(today, slot, row.Dataspec, row.WeeklyReread));
        }

        var weekly = works.Where(item => item.Job.WeeklyReread).Select(item => item.Job.Dataspec).ToHashSet(StringComparer.Ordinal);
        works.RemoveAll(item => !item.Job.WeeklyReread && item.Job.Kind == JobKind.Stored && weekly.Contains(item.Job.Dataspec));

        foreach (var rule in plan.Realtime)
        {
            if (!rule.Enabled || !ScheduleText.TryTime(rule.Start, out var start) || time < start)
            {
                continue;
            }

            switch (rule.Kind)
            {
                case ScheduleKind.ThursdayUpcoming:
                    if (!ScheduleText.TryWeekday(rule.Day, out var weekday) || jst.DayOfWeek != weekday)
                    {
                        break;
                    }

                    foreach (var day in MeetingCalendar.UpcomingMeetingDays(today, meetingDays))
                    {
                        AddRealtime(works, plan, rule, today, start, day);
                    }

                    break;
                case ScheduleKind.MeetingSide:
                    if (!MeetingCalendar.IsMeetingSide(today, meetingDays))
                    {
                        break;
                    }

                    AddRealtime(works, plan, rule, today, start, today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
                    break;
                case ScheduleKind.MeetingOpen:
                    if (!MeetingCalendar.IsMeetingDay(today, meetingDays))
                    {
                        break;
                    }

                    AddRealtime(works, plan, rule, today, start, today.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture));
                    break;
            }
        }

        return works;
    }

    public static DateTimeOffset? NextUtc(
        DateTimeOffset utcNow,
        string dataspec,
        SchedulePlan plan,
        IReadOnlyCollection<string> meetingDays)
    {
        var jst = Jst.Now(utcNow);
        var start = DateOnly.FromDateTime(jst.DateTime);
        for (var offset = 0; offset < 8; offset++)
        {
            var day = start.AddDays(offset);
            foreach (var slot in Times(plan, dataspec, day, meetingDays).Order())
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

    private static IEnumerable<TimeOnly> Times(SchedulePlan plan, string dataspec, DateOnly day, IReadOnlyCollection<string> meetingDays)
    {
        foreach (var row in plan.Stored)
        {
            if (!row.Enabled || !string.Equals(row.Dataspec, dataspec, StringComparison.Ordinal))
            {
                continue;
            }

            if (!ScheduleText.TryDay(row.Day, out var rowDay) || !ScheduleText.TryTime(row.Time, out var time))
            {
                continue;
            }

            if (rowDay is null || rowDay == day.DayOfWeek)
            {
                yield return time;
            }
        }

        if (!plan.IsRealtimeEnabled(dataspec))
        {
            yield break;
        }

        foreach (var rule in plan.Realtime)
        {
            if (!rule.Enabled || !rule.Dataspecs.Contains(dataspec, StringComparer.Ordinal) || !ScheduleText.TryTime(rule.Start, out var start))
            {
                continue;
            }

            switch (rule.Kind)
            {
                case ScheduleKind.ThursdayUpcoming:
                    if (ScheduleText.TryWeekday(rule.Day, out var weekday) && day.DayOfWeek == weekday)
                    {
                        yield return start;
                    }

                    break;
                case ScheduleKind.MeetingSide:
                    if (MeetingCalendar.IsMeetingSide(day, meetingDays))
                    {
                        yield return start;
                    }

                    break;
                case ScheduleKind.MeetingOpen:
                    if (MeetingCalendar.IsMeetingDay(day, meetingDays))
                    {
                        yield return start;
                    }

                    break;
            }
        }
    }

    private static void AddRealtime(List<PlannedWork> works, SchedulePlan plan, RealtimeScheduleRow rule, DateOnly today, TimeOnly start, string key)
    {
        foreach (var spec in plan.Specs(rule))
        {
            works.Add(Realtime(today, start, spec, key));
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
}
