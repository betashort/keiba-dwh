namespace JvDataCollector;

public enum JobPriority
{
    Shutdown = 0,
    RealtimeEvent = 1,
    RealtimeScheduled = 2,
    OddsSweep = 3,
    User = 4,
    Scheduled = 5,
}

public enum JobKind
{
    Shutdown,
    Stored,
    Realtime,
    Odds,
}

public static class JobGroups
{
    public const int None = 0;
    public const int User = 1;
    public const int Setup = 2;
    public const int Odds = 3;
}

public sealed class CollectionJob
{
    public required JobKind Kind { get; init; }
    public required JobPriority Priority { get; init; }
    public string Dataspec { get; init; } = "";
    public int Option { get; init; }
    public string Key { get; init; } = "";
    public string? MeetingDay { get; init; }
    public bool WeeklyReread { get; init; }
    public bool FullHistory { get; init; }
    public int Group { get; init; }
    public DateTimeOffset NotBeforeUtc { get; init; }
    public long EnqueuedOrder { get; init; }

    public string Identity => $"{Kind}|{Dataspec}|{Option}|{Key}|{WeeklyReread}|{FullHistory}";
}

public sealed class JobQueue
{
    private readonly object _gate = new();
    private readonly List<CollectionJob> _items = [];
    private long _sequence;
    private bool _userActive;
    private bool _userPending;
    private bool _setupActive;
    private bool _setupPending;
    private bool _oddsActive;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public bool HasDue(DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            return _items.Any(item => item.NotBeforeUtc <= utcNow);
        }
    }

    public bool HasShutdown
    {
        get
        {
            lock (_gate)
            {
                return _items.Any(item => item.Kind == JobKind.Shutdown);
            }
        }
    }

    public bool RequestRound(int group, IReadOnlyList<CollectionJob> jobs)
    {
        lock (_gate)
        {
            if (group == JobGroups.User)
            {
                if (_userActive || _items.Any(item => item.Group == group))
                {
                    _userPending = true;
                    return false;
                }

                if (jobs.Count == 0)
                {
                    return false;
                }

                _userActive = true;
            }
            else if (group == JobGroups.Setup)
            {
                if (_setupActive || _items.Any(item => item.Group == group))
                {
                    _setupPending = true;
                    return false;
                }

                if (jobs.Count == 0)
                {
                    return false;
                }

                _setupActive = true;
            }

            AddRange(jobs);
            return true;
        }
    }

    public bool TryAdd(CollectionJob job)
    {
        lock (_gate)
        {
            if (job.Kind == JobKind.Shutdown)
            {
                if (_items.Any(item => item.Kind == JobKind.Shutdown))
                {
                    return false;
                }

                _items.Add(Stamp(job));
                return true;
            }

            if (job.Kind == JobKind.Stored)
            {
                var existing = _items.FirstOrDefault(item => item.Kind == JobKind.Stored && item.Dataspec == job.Dataspec);
                if (existing is not null)
                {
                    if (job.WeeklyReread && !existing.WeeklyReread)
                    {
                        _items.Remove(existing);
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            else if (_items.Any(item => item.Identity == job.Identity))
            {
                return false;
            }

            _items.Add(Stamp(job));
            return true;
        }
    }

    public bool AddOdds(IReadOnlyList<CollectionJob> jobs)
    {
        lock (_gate)
        {
            if (_oddsActive || _items.Any(item => item.Group == JobGroups.Odds) || jobs.Count == 0)
            {
                return false;
            }

            _oddsActive = true;
            AddRange(jobs);
            return true;
        }
    }

    public CollectionJob? TakeDue(DateTimeOffset utcNow)
    {
        lock (_gate)
        {
            var index = -1;
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].NotBeforeUtc > utcNow)
                {
                    continue;
                }

                if (index < 0 || Before(_items[i], _items[index]))
                {
                    index = i;
                }
            }

            if (index < 0)
            {
                return null;
            }

            var job = _items[index];
            _items.RemoveAt(index);
            return job;
        }
    }

    public void Complete(CollectionJob job, Func<int, IReadOnlyList<CollectionJob>>? followUp)
    {
        lock (_gate)
        {
            if (job.Group == JobGroups.User && !_items.Any(item => item.Group == JobGroups.User))
            {
                _userActive = false;
                if (_userPending)
                {
                    _userPending = false;
                    var jobs = followUp?.Invoke(JobGroups.User) ?? [];
                    if (jobs.Count > 0)
                    {
                        _userActive = true;
                        AddRange(jobs);
                    }
                }
            }
            else if (job.Group == JobGroups.Setup && !_items.Any(item => item.Group == JobGroups.Setup))
            {
                _setupActive = false;
                if (_setupPending)
                {
                    _setupPending = false;
                    var jobs = followUp?.Invoke(JobGroups.Setup) ?? [];
                    if (jobs.Count > 0)
                    {
                        _setupActive = true;
                        AddRange(jobs);
                    }
                }
            }
            else if (job.Group == JobGroups.Odds && !_items.Any(item => item.Group == JobGroups.Odds))
            {
                _oddsActive = false;
            }
        }
    }

    private void AddRange(IReadOnlyList<CollectionJob> jobs)
    {
        foreach (var job in jobs)
        {
            if (job.Kind == JobKind.Stored)
            {
                var existing = _items.FirstOrDefault(item => item.Kind == JobKind.Stored && item.Dataspec == job.Dataspec);
                if (existing is not null)
                {
                    continue;
                }
            }
            else if (_items.Any(item => item.Identity == job.Identity))
            {
                continue;
            }

            _items.Add(Stamp(job));
        }
    }

    private CollectionJob Stamp(CollectionJob job) => new()
    {
        Kind = job.Kind,
        Priority = job.Priority,
        Dataspec = job.Dataspec,
        Option = job.Option,
        Key = job.Key,
        MeetingDay = job.MeetingDay,
        WeeklyReread = job.WeeklyReread,
        FullHistory = job.FullHistory,
        Group = job.Group,
        NotBeforeUtc = job.NotBeforeUtc,
        EnqueuedOrder = ++_sequence,
    };

    private static bool Before(CollectionJob left, CollectionJob right)
    {
        var priority = left.Priority.CompareTo(right.Priority);
        return priority != 0 ? priority < 0 : left.EnqueuedOrder < right.EnqueuedOrder;
    }
}
