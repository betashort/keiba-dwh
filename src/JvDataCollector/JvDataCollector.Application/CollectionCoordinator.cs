namespace JvDataCollector;

public sealed class AttentionBoard : IAttention
{
    private readonly object _gate = new();
    private readonly HashSet<AttentionReason> _reasons = [];

    public bool HasAttention
    {
        get
        {
            lock (_gate)
            {
                return _reasons.Count > 0;
            }
        }
    }

    public string? Message { get; private set; }

    public void Raise(AttentionReason reason, string message)
    {
        lock (_gate)
        {
            _reasons.Add(reason);
            Message = message;
        }
    }

    public void Clear(AttentionReason reason)
    {
        lock (_gate)
        {
            _reasons.Remove(reason);
            if (_reasons.Count == 0)
            {
                Message = null;
            }
        }
    }
}

public sealed record StatusLine(
    string Dataspec,
    string Name,
    int Option,
    string Watermark,
    string LastResult,
    int? Count,
    string Next);

public enum ProgressPhase
{
    Download,
    Read,
}

public readonly record struct ProgressReport(
    string Dataspec,
    ProgressPhase Phase,
    int Current,
    int Total,
    string Unit,
    bool Stored);

public sealed record ProgressSnapshot(
    string? Dataspec,
    ProgressPhase Phase,
    int Current,
    int Total,
    string Unit,
    bool Stored,
    int RunId)
{
    public static ProgressSnapshot Idle { get; } = new(null, ProgressPhase.Download, 0, 0, "", false, 0);

    public string Caption()
    {
        if (string.IsNullOrEmpty(Dataspec))
        {
            return "待機";
        }

        var name = DataspecCatalog.NameOf(Dataspec);
        var phase = Phase == ProgressPhase.Download ? "ダウンロード" : "読み込み";
        if (Total <= 0)
        {
            return $"取得中   {name}   {phase}";
        }

        var unit = string.IsNullOrEmpty(Unit) ? "" : " " + Unit;
        var percent = (int)Math.Round(100d * Math.Clamp(Current, 0, Total) / Total);
        return $"取得中   {name}   {phase}   {Current} / {Total}{unit}   {percent}%";
    }
}

public sealed class CollectionCoordinator
{
    private readonly IConfigStore _configs;
    private readonly ISecretStore _secrets;
    private readonly IRawLog _raw;
    private readonly ILedger _ledger;
    private readonly IClock _clock;
    private readonly IAppLog _log;
    private readonly IAttention _attention;
    private readonly IDiskSpace _disk;
    private readonly SessionRunner _session;
    private readonly JobQueue _queue = new();
    private CollectorConfig _config;
    private HashSet<string> _rejected = [];
    private DateTimeOffset _lastOdds = DateTimeOffset.MinValue;
    private int _running;
    private int _runId;
    private int _activeRun;
    private int _collectionPaused;
    private bool _scheduleBlocked;
    private volatile ProgressSnapshot _progress = ProgressSnapshot.Idle;

    public CollectionCoordinator(
        IConfigStore configs,
        ISecretStore secrets,
        IRawLog raw,
        ILedger ledger,
        IJvGateway jv,
        IClock clock,
        IAppLog log,
        IAttention attention,
        IDiskSpace disk,
        IDelay delay,
        Action? pump = null)
    {
        _configs = configs;
        _secrets = secrets;
        _raw = raw;
        _ledger = ledger;
        _clock = clock;
        _log = log;
        _attention = attention;
        _disk = disk;
        _config = configs.Load();
        _session = new SessionRunner(jv, raw, ledger, clock, log, delay, pump, ReportProgress);
        RefreshRejected();
    }

    public JobQueue Queue => _queue;

    public bool ScheduleBlocked => _scheduleBlocked;

    public ProgressSnapshot Progress => _progress;

    public bool IsBusy(DateTimeOffset utcNow) => Volatile.Read(ref _running) == 1 || _queue.HasDue(utcNow);

    public void Reload()
    {
        _config = _configs.Load().Copy();
        RefreshRejected();
    }

    public void Repair() => _raw.Repair(RecordKeys.Classify);

    public void PauseCollection() => Interlocked.Exchange(ref _collectionPaused, 1);

    public void ResumeCollection() => Interlocked.Exchange(ref _collectionPaused, 0);

    public bool CollectionPaused => Volatile.Read(ref _collectionPaused) == 1;

    public void CatchUp(DateTimeOffset utcNow)
    {
        if (CollectionPaused || !Ready())
        {
            return;
        }

        Reload();
        if (_scheduleBlocked)
        {
            return;
        }

        var works = SchedulePlanner.Due(utcNow, SchedulePlan.From(_config), _rejected, _ledger.MeetingDays());
        foreach (var work in works)
        {
            if (_ledger.WasFired(work.FireKey))
            {
                continue;
            }

            var mark = _ledger.GetWatermark(work.Job.Dataspec, work.Job.Kind == JobKind.Stored ? 1 : 0);
            if (mark.SuspendedOnDate == Jst.Day(utcNow))
            {
                _ledger.MarkFired(work.FireKey, utcNow);
                continue;
            }

            _queue.TryAdd(work.Job);
            _ledger.MarkFired(work.FireKey, utcNow);
        }
    }

    public void MaybeEnqueueOdds(DateTimeOffset utcNow)
    {
        if (CollectionPaused || !Ready() || _scheduleBlocked)
        {
            return;
        }

        var jst = Jst.Now(utcNow);
        var days = _ledger.MeetingDays();
        var plan = SchedulePlan.From(_config);
        var rule = plan.OpenOdds(jst, days);
        var interval = rule is null || rule.IntervalMinutes <= 0 ? 3 : rule.IntervalMinutes;
        if (rule is null || utcNow - _lastOdds < TimeSpan.FromMinutes(interval))
        {
            return;
        }

        var specs = plan.Specs(rule).ToArray();
        var jobs = RealtimePlanner.Odds(jst, days, _ledger.RaceKeysForDays(RealtimePlanner.TargetDays(jst, days, rule)), rule, specs);
        if (_queue.AddOdds(jobs))
        {
            _lastOdds = utcNow;
        }
    }

    public bool WatchDesired(DateTimeOffset utcNow)
    {
        if (CollectionPaused || !Ready())
        {
            return false;
        }

        return SchedulePlan.From(_config).WatchOpen(Jst.Now(utcNow), _ledger.MeetingDays());
    }

    public bool RequestUserFetch()
    {
        if (CollectionPaused)
        {
            return false;
        }

        Reload();
        return _queue.RequestRound(JobGroups.User, BuildUserJobs());
    }

    public bool RequestSetup()
    {
        if (CollectionPaused)
        {
            return false;
        }

        Reload();
        return _queue.RequestRound(JobGroups.Setup, BuildSetupJobs());
    }

    public void RequestShutdown() => _queue.TryAdd(new CollectionJob
    {
        Kind = JobKind.Shutdown,
        Priority = JobPriority.Shutdown,
    });

    public void OnRealtime(RealtimeEventKind kind, string key)
    {
        if (CollectionPaused || !Ready())
        {
            return;
        }

        var enabled = SchedulePlan.From(_config);
        foreach (var job in RealtimePlanner.ForEvent(kind, key))
        {
            if (enabled.IsRealtimeEnabled(job.Dataspec))
            {
                _queue.TryAdd(job);
            }
        }
    }

    public void Execute(CollectionJob job, CancellationToken cancel)
    {
        if (job.Kind == JobKind.Shutdown)
        {
            return;
        }

        Volatile.Write(ref _running, 1);
        _activeRun = Interlocked.Increment(ref _runId);
        try
        {
            Reload();
            var root = _config.DataRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                _attention.Raise(AttentionReason.SavePath, "保存先がありません。");
                return;
            }

            Directory.CreateDirectory(root);
            var free = _disk.AvailableBytes(root);
            if (free < JvCodes.MinimumFreeBytes)
            {
                _attention.Raise(AttentionReason.DiskSpace, "保存先の空き容量が 5 GB 未満です。新しい取得は止めています。");
                _log.Info($"disk low dataspec={job.Dataspec} free={free}");
                return;
            }

            _attention.Clear(AttentionReason.DiskSpace);
            var option = job.Kind == JobKind.Stored ? job.Option : 0;
            var mark = _ledger.GetWatermark(job.Dataspec, option);
            if (mark.SuspendedOnDate == Jst.Day(_clock.UtcNow) && job.Group == JobGroups.None)
            {
                _log.Info($"suspended dataspec={job.Dataspec}");
                return;
            }

            if (job.Option == 1 && mark.Option1Rejected)
            {
                return;
            }

            var from = ResolveFrom(job, mark);
            if (from is null)
            {
                _log.Info($"skip dataspec={job.Dataspec}");
                return;
            }

            var secret = _secrets.Load();
            if (string.IsNullOrEmpty(secret))
            {
                _attention.Raise(AttentionReason.ServiceKey, "利用キーがありません。");
                _scheduleBlocked = true;
                return;
            }

            var started = _clock.UtcNow;
            var outcome = _session.Run(job, secret, Path.Combine(root, "jvlink"), from, cancel);
            _ledger.RecordRun(new RunDraft(
                job.Dataspec,
                option,
                job.Key,
                started,
                _clock.UtcNow,
                outcome.RecordCount,
                outcome.NewCount,
                outcome.DuplicateCount,
                outcome.ReadCount,
                outcome.DownloadCount,
                outcome.LastFileTimestamp,
                outcome.Code,
                outcome.Kind.ToString()));
            Apply(job, option, mark, outcome);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
            _progress = ProgressSnapshot.Idle;
        }
    }

    public void OnJobFinished(CollectionJob job) =>
        _queue.Complete(job, group => group switch
        {
            JobGroups.User => BuildUserJobs(),
            JobGroups.Setup => BuildSetupJobs(),
            _ => [],
        });

    public IReadOnlyList<StatusLine> Status(DateTimeOffset utcNow)
    {
        Reload();
        var plan = SchedulePlan.From(_config);
        var days = _ledger.MeetingDays();
        var lines = new List<StatusLine>();
        foreach (var spec in _config.Dataspecs)
        {
            lines.Add(Line(spec, 1, utcNow, plan, days));
        }

        foreach (var spec in new[] { "0B11", "0B12", "0B13", "0B14", "0B15", "0B16", "0B17" })
        {
            if (plan.IsRealtimeEnabled(spec))
            {
                lines.Add(Line(spec, 0, utcNow, plan, days));
            }
        }

        return lines;
    }

    private StatusLine Line(string spec, int option, DateTimeOffset utcNow, SchedulePlan plan, IReadOnlyCollection<string> meetingDays)
    {
        var mark = _ledger.GetWatermark(spec, option);
        var next = SchedulePlanner.NextUtc(utcNow, spec, plan, meetingDays);
        return new StatusLine(
            spec,
            DataspecCatalog.NameOf(spec),
            option,
            Jst.Display(mark.Confirmed),
            mark.LastResult ?? "",
            mark.LastNewCount,
            next is null ? "" : Jst.FormatNext(utcNow, next.Value));
    }

    private void Apply(CollectionJob job, int option, WatermarkState mark, SessionOutcome outcome)
    {
        var now = _clock.UtcNow;
        if (outcome.Kind is SessionOutcomeKind.Success or SessionOutcomeKind.NoData)
        {
            _ledger.NoteSuccess(job.Dataspec, option, outcome.Kind == SessionOutcomeKind.NoData ? "該当なし" : "成功", outcome.NewCount, now);
            _scheduleBlocked = false;
            _attention.Clear(AttentionReason.Agreement);
            _attention.Clear(AttentionReason.ServiceKey);
            _attention.Clear(AttentionReason.SavePath);
            _attention.Clear(AttentionReason.JvRejected);
            RefreshRejected();
            return;
        }

        if (outcome.Kind == SessionOutcomeKind.Cancelled)
        {
            _ledger.NoteSuccess(job.Dataspec, option, "中止", outcome.NewCount, now);
            return;
        }

        var decision = RetryPolicy.For(outcome.Code, mark.ConsecutiveFailures, job.Dataspec, option);
        switch (decision.Action)
        {
            case RetryAction.RejectOption1:
                _ledger.RejectOption1(job.Dataspec);
                _ledger.NoteFailure(job.Dataspec, option, outcome.Code, false, Jst.Day(now), now);
                RefreshRejected();
                _log.Info($"option1 rejected dataspec={job.Dataspec}");
                break;
            case RetryAction.Attention:
                _ledger.NoteFailure(job.Dataspec, option, outcome.Code, true, Jst.Day(now), now);
                _attention.Raise(decision.Reason ?? AttentionReason.JvRejected, AttentionText(outcome.Code));
                if (JvCodes.BlocksSchedule(outcome.Code))
                {
                    _scheduleBlocked = true;
                }

                break;
            case RetryAction.SuspendToday:
                _ledger.NoteFailure(job.Dataspec, option, outcome.Code, true, Jst.Day(now), now);
                _log.Info($"suspended for today dataspec={job.Dataspec} code={outcome.Code}");
                break;
            case RetryAction.Backoff:
                _ledger.NoteFailure(job.Dataspec, option, outcome.Code, false, Jst.Day(now), now);
                _queue.TryAdd(new CollectionJob
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
                    NotBeforeUtc = now.Add(decision.Delay),
                });
                break;
        }
    }

    private string? ResolveFrom(CollectionJob job, WatermarkState mark)
    {
        if (job.Kind != JobKind.Stored)
        {
            return "";
        }

        if (job.Option != 4 && !string.IsNullOrEmpty(mark.ResumeTimestamp))
        {
            return mark.ResumeTimestamp;
        }

        if (job.WeeklyReread)
        {
            return string.IsNullOrEmpty(mark.Confirmed) ? null : Jst.AddDays(mark.Confirmed, -7);
        }

        if (job.Option == 4)
        {
            return string.IsNullOrEmpty(mark.OpenFromTime) ? Jst.Ancient : mark.OpenFromTime;
        }

        if (job.FullHistory)
        {
            return Jst.Ancient;
        }

        if (!string.IsNullOrEmpty(mark.Confirmed))
        {
            return mark.Confirmed;
        }

        return _config.DifferentialEpoch ?? Jst.Stamp(_clock.UtcNow);
    }

    private bool Ready() =>
        _config.SettingsComplete
        && !string.IsNullOrWhiteSpace(_config.DataRoot)
        && _secrets.Exists;

    private void RefreshRejected()
    {
        var rejected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var spec in new[] { "COMM", "MING" })
        {
            if (_ledger.GetWatermark(spec, 1).Option1Rejected)
            {
                rejected.Add(spec);
            }
        }

        _rejected = rejected;
    }

    private List<CollectionJob> BuildUserJobs()
    {
        var jobs = new List<CollectionJob>();
        foreach (var spec in Ordered(_config.Dataspecs))
        {
            if (_rejected.Contains(spec))
            {
                continue;
            }

            jobs.Add(new CollectionJob
            {
                Kind = JobKind.Stored,
                Priority = JobPriority.User,
                Group = JobGroups.User,
                Dataspec = spec,
                Option = 1,
            });
        }

        return jobs;
    }

    private List<CollectionJob> BuildSetupJobs()
    {
        var specs = Ordered(_config.Dataspecs.Concat(["COMM", "MING"]).Distinct(StringComparer.Ordinal));
        var jobs = new List<CollectionJob>();
        foreach (var spec in specs)
        {
            var toku = spec == "TOKU";
            jobs.Add(new CollectionJob
            {
                Kind = JobKind.Stored,
                Priority = JobPriority.User,
                Group = JobGroups.Setup,
                Dataspec = spec,
                Option = toku ? 1 : 4,
                FullHistory = toku,
            });
        }

        return jobs;
    }

    private static IEnumerable<string> Ordered(IEnumerable<string> codes)
    {
        var rank = DataspecCatalog.Stored.Select((item, index) => (item.Code, index)).ToDictionary(item => item.Code, item => item.index);
        return codes.OrderBy(code => rank.TryGetValue(code, out var index) ? index : int.MaxValue);
    }

    private void ReportProgress(ProgressReport report) =>
        _progress = new ProgressSnapshot(
            report.Dataspec,
            report.Phase,
            report.Current,
            report.Total,
            report.Unit,
            report.Stored,
            _activeRun);

    private static string AttentionText(int code) => code switch
    {
        -305 => "JV-Link の利用規約への同意が必要です。",
        -301 or -302 or -303 => "利用キーが拒否されました。",
        -211 => "保存先に書けません。フォルダがあるか確認してください。",
        -201 => "JV-Link の初期化に失敗しました。",
        _ => $"JV-Link がエラーを返しました（{code}）。",
    };
}
