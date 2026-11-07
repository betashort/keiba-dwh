namespace DwhLoader;

public sealed class LoaderSession : IDisposable
{
    private readonly RollingFileLog _log;
    private readonly Dictionary<long, DateTimeOffset> _retry = [];
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly List<FileLine> _lines = [];
    private LoaderSettings _settings;
    private string _password;
    private PostgresWarehouse _warehouse;
    private string _ob14 = "前回の写しのまま";
    private int _cycleWrote;

    public LoaderSession(RollingFileLog log, LoaderSettings settings, string password, Func<DateTimeOffset> clock = null)
    {
        _log = log;
        _settings = settings;
        _password = password ?? "";
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _log.NotePassword(_password);
    }

    public StatusSnapshot Snapshot { get; private set; } = StatusText.Idle();

    public event Action<StatusSnapshot> Changed;

    public LoaderSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _settings;
            }
        }
    }

    public void Use(LoaderSettings settings, string password)
    {
        lock (_gate)
        {
            _settings = settings;
            if (!string.IsNullOrEmpty(password))
            {
                _password = password;
            }
        }

        if (!string.IsNullOrEmpty(password))
        {
            _log.NotePassword(password);
        }
    }

    public void Loop(CancellationToken cancel)
    {
        FollowLoop.Run(
            () =>
            {
                try
                {
                    return RunOnce(cancel);
                }
                catch (Exception ex)
                {
                    _log.Info(ex.GetType().Name);
                    Publish(StatusText.PostgresDown());
                    return new CycleResult { WarehouseDown = true, Ob14Kept = true };
                }
            },
            _ => { },
            (delay, token) => token.WaitHandle.WaitOne(delay),
            cancel);
    }

    public CycleResult RunOnce() => RunOnce(CancellationToken.None);

    public CycleResult RunOnce(CancellationToken cancel)
    {
        if (cancel.IsCancellationRequested)
        {
            return new CycleResult { Stopped = true };
        }

        LoaderSettings settings;
        string password;
        lock (_gate)
        {
            settings = _settings;
            password = _password;
            _cycleWrote = 0;
        }

        PostgresWarehouse warehouse = null;
        try
        {
            warehouse = new PostgresWarehouse(settings.Host, settings.Port, settings.Database, settings.Username, password);
            lock (_gate)
            {
                _warehouse = warehouse;
            }

            if (cancel.IsCancellationRequested)
            {
                return new CycleResult { Stopped = true };
            }

            if (!warehouse.SchemaReady())
            {
                var missing = new CycleResult { SchemaMissing = true };
                Publish(StatusText.SchemaMissing());
                return missing;
            }

            var ledger = new LedgerReader(settings.DataRoot);
            var result = LoadCycle.Execute(
                ledger,
                warehouse,
                _retry,
                _clock(),
                cancel,
                _log,
                new Progress(this));
            Publish(Describe(result));
            return result;
        }
        catch (LedgerUnavailableException)
        {
            Publish(StatusText.LedgerDown());
            return new CycleResult { LedgerDown = true, Ob14Kept = true };
        }
        catch (WarehouseUnavailableException)
        {
            Publish(StatusText.PostgresDown());
            return new CycleResult { WarehouseDown = true, Ob14Kept = true };
        }
        catch (SqlFailureException ex) when (ex.Disconnect)
        {
            Publish(StatusText.PostgresDown());
            return new CycleResult { WarehouseDown = true, Ob14Kept = true };
        }
        catch (SqlFailureException ex) when (ex.SqlState is "42P01" or "3F000")
        {
            Publish(StatusText.SchemaMissing());
            return new CycleResult { SchemaMissing = true };
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_warehouse, warehouse))
                {
                    _warehouse = null;
                }
            }

            warehouse?.Dispose();
        }
    }

    public void Stop()
    {
        PostgresWarehouse warehouse;
        lock (_gate)
        {
            warehouse = _warehouse;
        }

        warehouse?.RollbackFile();
    }

    public void Dispose() => Stop();

    private StatusSnapshot Describe(CycleResult result)
    {
        if (result.Stopped)
        {
            return Snapshot;
        }

        if (result.SchemaMissing)
        {
            return StatusText.SchemaMissing();
        }

        List<FileLine> lines;
        string ob14;
        lock (_gate)
        {
            foreach (var outcome in result.Files)
            {
                Upsert(Line(outcome));
            }

            if (result.LedgerDown || result.WarehouseDown || result.Ob14Kept)
            {
                _ob14 = "前回の写しのまま";
            }
            else
            {
                _ob14 = StatusText.Ob14(result.Ob14Members, false);
            }

            lines = [.. _lines];
            ob14 = _ob14;
        }

        if (result.LedgerDown)
        {
            return StatusText.LedgerDown();
        }

        if (result.WarehouseDown)
        {
            return StatusText.PostgresDown();
        }

        return result.WroteAny
            ? StatusText.Following(result.Wrote, ob14, lines)
            : StatusText.Waiting(ob14, lines);
    }

    private void NoteWriting(CompleteFile file)
    {
        StatusSnapshot snapshot;
        lock (_gate)
        {
            Upsert(new FileLine(file.Id, file.Dataspec, "書いています", null, "", null));
            snapshot = StatusText.Writing(file, _cycleWrote, false, _ob14, [.. _lines]);
        }

        Publish(snapshot);
    }

    private void NoteFinished(FileOutcome outcome)
    {
        lock (_gate)
        {
            if (outcome.Result == "書けた")
            {
                _cycleWrote++;
            }

            Upsert(Line(outcome));
        }
    }

    private void Upsert(FileLine line)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            if (_lines[i].Id == line.Id)
            {
                _lines[i] = line;
                return;
            }
        }

        _lines.Add(line);
    }

    private static FileLine Line(FileOutcome outcome) =>
        new(outcome.Id, outcome.Dataspec, outcome.Result, outcome.Count, outcome.Reason, outcome.RetryUntil);

    private void Publish(StatusSnapshot snapshot)
    {
        Snapshot = snapshot;
        Changed?.Invoke(snapshot);
    }

    private sealed class Progress : ICycleProgress
    {
        private readonly LoaderSession _session;

        public Progress(LoaderSession session) => _session = session;

        public void Writing(CompleteFile file) => _session.NoteWriting(file);

        public void Finished(FileOutcome outcome) => _session.NoteFinished(outcome);
    }
}
