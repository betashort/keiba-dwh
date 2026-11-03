using System.Globalization;

namespace JvDataCollector;

public static class ScheduleKind
{
    public const string ThursdayUpcoming = "thursday-upcoming";
    public const string MeetingSide = "meeting-side";
    public const string MeetingOpen = "meeting-open";
    public const string Watch = "watch";
    public const string OddsMeeting = "odds-meeting";
    public const string OddsFriday = "odds-friday";
}

public sealed class StoredScheduleRow
{
    public string Id { get; set; } = "";
    public string Dataspec { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Day { get; set; } = "毎日";
    public string Time { get; set; } = "13:30";
    public bool WeeklyReread { get; set; }

    public StoredScheduleRow Copy() => new()
    {
        Id = Id,
        Dataspec = Dataspec,
        Enabled = Enabled,
        Day = Day,
        Time = Time,
        WeeklyReread = WeeklyReread,
    };
}

public sealed class RealtimeDataRow
{
    public string Dataspec { get; set; } = "";
    public bool Enabled { get; set; } = true;

    public RealtimeDataRow Copy() => new()
    {
        Dataspec = Dataspec,
        Enabled = Enabled,
    };
}

public sealed class RealtimeScheduleRow
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public string Title { get; set; } = "";
    public string Day { get; set; } = "";
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public int IntervalMinutes { get; set; }
    public List<string> Dataspecs { get; set; } = [];

    public RealtimeScheduleRow Copy() => new()
    {
        Id = Id,
        Kind = Kind,
        Enabled = Enabled,
        Title = Title,
        Day = Day,
        Start = Start,
        End = End,
        IntervalMinutes = IntervalMinutes,
        Dataspecs = [.. Dataspecs],
    };
}

public static class ScheduleText
{
    public static readonly string[] StoredDays = ["毎日", "月曜", "火曜", "水曜", "木曜", "金曜", "土曜", "日曜"];

    public static readonly string[] RealtimeDays = ["開催日", "金・土・日", "月曜", "火曜", "水曜", "木曜", "金曜", "土曜", "日曜"];

    public static bool TryDay(string? text, out DayOfWeek? day)
    {
        switch (text?.Trim())
        {
            case "毎日":
                day = null;
                return true;
            case "月曜":
                day = DayOfWeek.Monday;
                return true;
            case "火曜":
                day = DayOfWeek.Tuesday;
                return true;
            case "水曜":
                day = DayOfWeek.Wednesday;
                return true;
            case "木曜":
                day = DayOfWeek.Thursday;
                return true;
            case "金曜":
                day = DayOfWeek.Friday;
                return true;
            case "土曜":
                day = DayOfWeek.Saturday;
                return true;
            case "日曜":
                day = DayOfWeek.Sunday;
                return true;
            default:
                day = null;
                return false;
        }
    }

    public static bool TryWeekday(string? text, out DayOfWeek day)
    {
        if (TryDay(text, out var value) && value is not null)
        {
            day = value.Value;
            return true;
        }

        day = default;
        return false;
    }

    public static string Day(DayOfWeek? day) => day is null ? "毎日" : Jst.Weekday(day.Value);

    public static bool TryTime(string? text, out TimeOnly time) =>
        TimeOnly.TryParseExact(
            text?.Trim(),
            ["H:mm", "HH:mm"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out time);

    public static string Time(TimeOnly time) => time.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static List<string> SplitSpecs(string? text) =>
        (text ?? "").Split([' ', ',', '、', '　'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static string JoinSpecs(IEnumerable<string> specs) => string.Join(' ', specs);

    public static string? ValidateStored(StoredScheduleRow row)
    {
        var name = DataspecCatalog.NameOf(row.Dataspec);
        if (!DataspecCatalog.Stored.Any(item => item.Code == row.Dataspec))
        {
            return $"{row.Dataspec} は蓄積データの種類ではありません。";
        }

        if (!TryDay(row.Day, out _))
        {
            return $"{name} の曜日が不正です。";
        }

        if (!TryTime(row.Time, out _))
        {
            return $"{name} の時刻は HH:mm で入れてください。";
        }

        return null;
    }

    public static string? ValidateRealtimeData(RealtimeDataRow row)
    {
        if (!DataspecCatalog.Realtime.Any(item => item.Code == row.Dataspec))
        {
            return $"{row.Dataspec} は速報の種類ではありません。";
        }

        return null;
    }

    public static string? ValidateRealtimeRule(RealtimeScheduleRow row)
    {
        if (!TryTime(row.Start, out var start))
        {
            return $"{row.Title} の開始時刻は HH:mm で入れてください。";
        }

        var timed = row.Kind is ScheduleKind.Watch or ScheduleKind.OddsMeeting or ScheduleKind.OddsFriday;
        if (timed)
        {
            if (!TryTime(row.End, out var end))
            {
                return $"{row.Title} の終了時刻は HH:mm で入れてください。";
            }

            if (end <= start)
            {
                return $"{row.Title} の終了は開始より後にしてください。";
            }
        }

        if (row.Kind is ScheduleKind.OddsMeeting or ScheduleKind.OddsFriday
            && (row.IntervalMinutes < 1 || row.IntervalMinutes > 180))
        {
            return $"{row.Title} の間隔は 1 から 180 分にしてください。";
        }

        if (row.Kind == ScheduleKind.ThursdayUpcoming && !TryWeekday(row.Day, out _))
        {
            return $"{row.Title} の曜日を選んでください。";
        }

        if (row.Kind == ScheduleKind.Watch)
        {
            return null;
        }

        if (row.Dataspecs.Count == 0)
        {
            return $"{row.Title} の対象を 1 つ以上入れてください。";
        }

        foreach (var spec in row.Dataspecs)
        {
            if (!DataspecCatalog.Realtime.Any(item => item.Code == spec))
            {
                return $"{spec} は速報の種類ではありません。";
            }
        }

        return null;
    }
}

public static class ScheduleDefaults
{
    private static readonly Dictionary<string, string> StoredCaptions = BuildStoredCaptions();
    private static readonly Dictionary<string, string> RealtimeCaptions = BuildRealtimeCaptions();

    public static List<StoredScheduleRow> StoredRows()
    {
        var rows = new List<StoredScheduleRow>();
        Add(rows, ["SLOP", "WOOD"], null, 6, 30, false);
        Add(rows, DataspecCatalog.DefaultCodes, DayOfWeek.Monday, 6, 30, true);
        Add(rows, DataspecCatalog.DefaultCodes, null, 13, 30, false);
        Add(rows, ["DIFF", "DIFN", "BLOD", "BLDN", "SNAP", "SNPN", "YSCH", "MING", "RACE"], DayOfWeek.Monday, 15, 0, false);
        Add(rows, ["RACE", "DIFF", "DIFN", "BLOD", "BLDN", "SNAP", "SNPN"], DayOfWeek.Thursday, 21, 0, false);
        Add(rows, ["TOKU"], DayOfWeek.Sunday, 21, 0, false);
        Add(rows, ["TOKU"], DayOfWeek.Monday, 21, 0, false);
        Add(rows, ["HOYU"], DayOfWeek.Tuesday, 12, 0, false);
        Add(rows, DataspecCatalog.DefaultCodes, null, 23, 0, false);
        return rows;
    }

    public static List<RealtimeDataRow> RealtimeData() =>
        DataspecCatalog.Realtime.Select(item => new RealtimeDataRow { Dataspec = item.Code, Enabled = true }).ToList();

    public static List<RealtimeScheduleRow> RealtimeRows()
    {
        var odds = DataspecCatalog.OddsSweep.ToList();
        return
        [
            Rule(ScheduleKind.ThursdayUpcoming, "速報レース（これから7日）", "木曜", "21:00", "", 0, ["0B15"]),
            Rule(ScheduleKind.MeetingSide, "開催側の速報", "金・土・日", "13:30", "", 0, ["0B15", "0B13", "0B17"]),
            Rule(ScheduleKind.MeetingOpen, "開催日の開始前", "開催日", "08:00", "", 0, ["0B14", "0B15", "0B13", "0B17"]),
            Rule(ScheduleKind.Watch, "変更の監視", "開催日", "08:00", "17:30", 0, []),
            Rule(ScheduleKind.OddsMeeting, "オッズの周回", "開催日", "09:00", "17:00", 3, odds),
            Rule(ScheduleKind.OddsFriday, "金曜のオッズ", "金曜", "19:00", "21:00", 3, odds),
        ];
    }

    public static bool IsStoredDefault(string id) => StoredCaptions.ContainsKey(id);

    public static bool IsRealtimeDefault(string id) => RealtimeCaptions.ContainsKey(id);

    public static string StoredCaption(string id) =>
        StoredCaptions.TryGetValue(id, out var text) ? text : "（追加）";

    public static string RealtimeCaption(string id) =>
        RealtimeCaptions.TryGetValue(id, out var text) ? text : "（追加）";

    public const string RealtimeDataCaption = "取得する";

    private static void Add(List<StoredScheduleRow> rows, IEnumerable<string> specs, DayOfWeek? day, int hour, int minute, bool weekly)
    {
        var dayText = ScheduleText.Day(day);
        var timeText = ScheduleText.Time(new TimeOnly(hour, minute));
        foreach (var spec in specs)
        {
            var id = $"s|{spec}|{(day is null ? "daily" : day.Value.ToString())}|{timeText}|{(weekly ? "W" : "D")}";
            rows.Add(new StoredScheduleRow
            {
                Id = id,
                Dataspec = spec,
                Enabled = true,
                Day = dayText,
                Time = timeText,
                WeeklyReread = weekly,
            });
        }
    }

    private static RealtimeScheduleRow Rule(string kind, string title, string day, string start, string end, int interval, IReadOnlyList<string> specs) =>
        new()
        {
            Id = kind,
            Kind = kind,
            Enabled = true,
            Title = title,
            Day = day,
            Start = start,
            End = end,
            IntervalMinutes = interval,
            Dataspecs = [.. specs],
        };

    private static Dictionary<string, string> BuildStoredCaptions()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in StoredRows())
        {
            var when = row.WeeklyReread ? $"{row.Day} {row.Time} 読み直し" : $"{row.Day} {row.Time}";
            map[row.Id] = $"取得する / {when}";
        }

        return map;
    }

    private static Dictionary<string, string> BuildRealtimeCaptions()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var row in RealtimeRows())
        {
            var when = string.IsNullOrEmpty(row.End) ? $"{row.Day} {row.Start}" : $"{row.Day} {row.Start}–{row.End}";
            var interval = row.IntervalMinutes > 0 ? $" / {row.IntervalMinutes}分" : "";
            var specs = row.Dataspecs.Count == 0 ? "" : " / " + ScheduleText.JoinSpecs(row.Dataspecs);
            map[row.Id] = $"有効 / {when}{interval}{specs}";
        }

        return map;
    }
}

public sealed class SchedulePlan
{
    private readonly HashSet<string> _realtimeEnabled;

    private SchedulePlan(
        IReadOnlyList<StoredScheduleRow> stored,
        IReadOnlyList<RealtimeDataRow> realtimeData,
        IReadOnlyList<RealtimeScheduleRow> realtime)
    {
        Stored = stored;
        RealtimeData = realtimeData;
        Realtime = realtime;
        _realtimeEnabled = new HashSet<string>(
            realtimeData.Where(row => row.Enabled).Select(row => row.Dataspec),
            StringComparer.Ordinal);
    }

    public IReadOnlyList<StoredScheduleRow> Stored { get; }

    public IReadOnlyList<RealtimeDataRow> RealtimeData { get; }

    public IReadOnlyList<RealtimeScheduleRow> Realtime { get; }

    public bool IsRealtimeEnabled(string dataspec) => _realtimeEnabled.Contains(dataspec);

    public static SchedulePlan From(CollectorConfig config)
    {
        var selected = new HashSet<string>(config.Dataspecs, StringComparer.Ordinal);
        List<StoredScheduleRow> stored;
        if (config.StoredSchedule is null)
        {
            stored = ScheduleDefaults.StoredRows();
            foreach (var row in stored)
            {
                row.Enabled = selected.Contains(row.Dataspec);
            }
        }
        else
        {
            stored = config.StoredSchedule.Select(row => row.Copy()).ToList();
            foreach (var row in stored)
            {
                row.Enabled = row.Enabled && selected.Contains(row.Dataspec);
            }
        }

        var data = config.RealtimeData is null
            ? ScheduleDefaults.RealtimeData()
            : config.RealtimeData.Select(row => row.Copy()).ToList();
        var rules = config.RealtimeSchedule is null
            ? ScheduleDefaults.RealtimeRows()
            : config.RealtimeSchedule.Select(row => row.Copy()).ToList();
        return new SchedulePlan(stored, data, rules);
    }

    public static List<StoredScheduleRow> EditStored(CollectorConfig config)
    {
        var effective = From(config).Stored;
        if (config.StoredSchedule is null)
        {
            return [.. effective];
        }

        var map = effective.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var rows = new List<StoredScheduleRow>();
        foreach (var template in ScheduleDefaults.StoredRows())
        {
            if (map.TryGetValue(template.Id, out var saved))
            {
                rows.Add(saved);
                continue;
            }

            var missing = template.Copy();
            missing.Enabled = false;
            rows.Add(missing);
        }

        foreach (var extra in effective)
        {
            if (!ScheduleDefaults.IsStoredDefault(extra.Id))
            {
                rows.Add(extra);
            }
        }

        return rows;
    }

    public static List<RealtimeDataRow> EditRealtimeData(CollectorConfig config)
    {
        var map = From(config).RealtimeData.ToDictionary(row => row.Dataspec, StringComparer.Ordinal);
        var rows = new List<RealtimeDataRow>();
        foreach (var item in DataspecCatalog.Realtime)
        {
            if (map.TryGetValue(item.Code, out var saved))
            {
                rows.Add(saved.Copy());
            }
            else
            {
                rows.Add(new RealtimeDataRow { Dataspec = item.Code, Enabled = config.RealtimeData is null });
            }
        }

        return rows;
    }

    public static List<RealtimeScheduleRow> EditRealtime(CollectorConfig config)
    {
        var effective = From(config).Realtime;
        if (config.RealtimeSchedule is null)
        {
            return effective.Select(row => row.Copy()).ToList();
        }

        var map = effective.ToDictionary(row => row.Id, StringComparer.Ordinal);
        var rows = new List<RealtimeScheduleRow>();
        foreach (var template in ScheduleDefaults.RealtimeRows())
        {
            if (map.TryGetValue(template.Id, out var saved))
            {
                rows.Add(saved.Copy());
                continue;
            }

            var missing = template.Copy();
            missing.Enabled = false;
            rows.Add(missing);
        }

        foreach (var extra in effective)
        {
            if (!ScheduleDefaults.IsRealtimeDefault(extra.Id))
            {
                rows.Add(extra.Copy());
            }
        }

        return rows;
    }

    public RealtimeScheduleRow? OpenOdds(DateTimeOffset jst, IReadOnlyCollection<string> meetingDays)
    {
        foreach (var rule in Realtime)
        {
            if (rule.Enabled && rule.Kind is ScheduleKind.OddsMeeting or ScheduleKind.OddsFriday && OddsWindow.Contains(jst, meetingDays, rule))
            {
                return rule;
            }
        }

        return null;
    }

    public bool WatchOpen(DateTimeOffset jst, IReadOnlyCollection<string> meetingDays)
    {
        foreach (var rule in Realtime)
        {
            if (rule.Enabled && rule.Kind == ScheduleKind.Watch && OddsWindow.Contains(jst, meetingDays, rule))
            {
                return true;
            }
        }

        return false;
    }

    public IEnumerable<string> Specs(RealtimeScheduleRow rule) =>
        rule.Dataspecs.Where(IsRealtimeEnabled).Distinct(StringComparer.Ordinal);
}
