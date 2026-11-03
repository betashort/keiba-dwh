using System.Runtime.InteropServices;

namespace JvDataCollector;

public sealed class MissingJv : IJvGateway
{
    public int Init(string softwareId) => -201;

    public int SetServiceKey(string serviceKey) => -201;

    public int SetSavePath(string path) => -201;

    public int SetSaveFlag(int flag) => -201;

    public int SetUiProperties() => -100;

    public OpenResult Open(string dataspec, string fromTime, int option) => new(-201, 0, 0, null);

    public OpenResult RtOpen(string dataspec, string key) => new(-201, 0, 0, null);

    public int Status() => -201;

    public ReadResult Read() => new(-201, null, null);

    public void Skip()
    {
    }

    public void CancelDownload()
    {
    }

    public int Close() => 0;

    public int DeleteFile(string fileName) => -201;

    public string? CurrentFileTimeStamp() => null;

    public int TotalReadFileSizeKb() => 0;

    public int CurrentReadFileSizeKb() => 0;

    public void StartWatch(Action<RealtimeEventKind, string> onEvent)
    {
    }

    public void StopWatch()
    {
    }
}

public sealed class StaWorker : IDisposable
{
    private readonly IAppLog _log;
    private readonly IAttention _attention;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Queue<Action> _actions = new();
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _started = new(false);
    private readonly ManualResetEventSlim _attached = new(false);
    private readonly Thread _thread;
    private CollectionCoordinator? _coordinator;
    private JvGateway? _jv;
    private Form? _pump;
    private bool _watchArmed;
    private int _disposed;

    public StaWorker(IAppLog log, IAttention attention)
    {
        _log = log;
        _attention = attention;
        _thread = new Thread(Loop)
        {
            Name = "JV-Link STA",
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.STA);
    }

    public IJvGateway Gateway => _jv is null ? new MissingJv() : _jv;

    public void Start()
    {
        _thread.Start();
        _started.Wait();
    }

    public void Attach(CollectionCoordinator coordinator)
    {
        _coordinator = coordinator;
        _attached.Set();
    }

    public void Invoke(Action action)
    {
        var done = new ManualResetEventSlim(false);
        Exception? error = null;
        lock (_gate)
        {
            _actions.Enqueue(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    error = ex;
                }
                finally
                {
                    done.Set();
                }
            });
        }

        done.Wait();
        if (error is not null)
        {
            throw error;
        }
    }

    public int SetUiProperties()
    {
        if (_jv is null)
        {
            return -100;
        }

        var code = 0;
        Invoke(() => code = _jv.SetUiProperties());
        return code;
    }

    public int ApplyConnection(string savePath, string serviceKey)
    {
        if (_jv is null)
        {
            return -201;
        }

        var code = 0;
        Invoke(() =>
        {
            Directory.CreateDirectory(savePath);
            code = _jv.Init(SoftwareId.Value);
            if (code != 0)
            {
                return;
            }

            code = _jv.SetServiceKey(serviceKey);
            if (code != 0 && code != -101)
            {
                return;
            }

            code = _jv.SetSavePath(savePath);
            if (code != 0)
            {
                return;
            }

            code = _jv.SetSaveFlag(1);
        });
        return code;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _cancel.Cancel();
        _attached.Set();
        if (_thread.IsAlive)
        {
            _thread.Join(TimeSpan.FromSeconds(20));
        }

        _cancel.Dispose();
        _started.Dispose();
        _attached.Dispose();
    }

    private void Loop()
    {
        _pump = new Form
        {
            ShowInTaskbar = false,
            FormBorderStyle = FormBorderStyle.FixedToolWindow,
            Opacity = 0,
            Size = new Size(0, 0),
        };
        _pump.CreateControl();
        if (JvRegistration.IsInstalled)
        {
            try
            {
                _jv = new JvGateway(_log);
            }
            catch (Exception ex)
            {
                _log.Error(FormatFailure(ex));
                _attention.Raise(AttentionReason.JvLinkMissing, "JV-Link を初期化できません。");
            }
        }

        _started.Set();
        _attached.Wait();
        while (true)
        {
            Drain();
            if (_coordinator is null)
            {
                break;
            }

            if (_cancel.IsCancellationRequested && _coordinator.Queue.HasShutdown)
            {
                break;
            }

            if (_jv is null)
            {
                Application.DoEvents();
                Thread.Sleep(50);
                if (_cancel.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            ApplyWatch();
            var job = _coordinator.Queue.TakeDue(DateTimeOffset.UtcNow);
            if (job is null)
            {
                Application.DoEvents();
                Thread.Sleep(40);
                if (_cancel.IsCancellationRequested)
                {
                    break;
                }

                continue;
            }

            if (job.Kind == JobKind.Shutdown)
            {
                break;
            }

            _coordinator.Execute(job, _cancel.Token);
            _coordinator.OnJobFinished(job);
            Application.DoEvents();
        }

        try
        {
            _jv?.StopWatch();
        }
        catch (Exception ex)
        {
            _log.Error(FormatFailure(ex));
        }

        _pump.Dispose();
    }

    private void Drain()
    {
        while (true)
        {
            Action? action;
            lock (_gate)
            {
                if (_actions.Count == 0)
                {
                    return;
                }

                action = _actions.Dequeue();
            }

            action();
        }
    }

    private static string FormatFailure(Exception ex) =>
        ex is COMException com ? $"COMException 0x{com.HResult:X8}" : ex.GetType().Name;

    private void ApplyWatch()
    {
        if (_coordinator is null || _jv is null)
        {
            return;
        }

        var desired = _coordinator.WatchDesired(DateTimeOffset.UtcNow);
        if (desired && !_watchArmed)
        {
            _jv.StartWatch((kind, key) => _coordinator.OnRealtime(kind, key));
            _watchArmed = true;
        }
        else if (!desired && _watchArmed)
        {
            _jv.StopWatch();
            _watchArmed = false;
        }
    }
}
