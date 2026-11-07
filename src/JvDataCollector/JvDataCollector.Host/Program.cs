namespace JvDataCollector;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var background = args.Any(arg => arg == "--background");
        using var mutex = AcquireMutex();
        if (mutex is null)
        {
            return;
        }

        using var showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KeibaDwh.Collector.Show");
        ApplicationConfiguration.Initialize();
        LogonRegistration.EnsureBackgroundArgument();
        var log = new RollingFileLog();
        var attention = new AttentionBoard();
        Application.ThreadException += (_, eventArgs) => log.Error(eventArgs.Exception.GetType().Name);
        var configs = new JsonConfigStore();
        var secrets = new DpapiSecretStore();
        if (secrets.Exists)
        {
            log.NoteSecret(secrets.Load());
        }

        var config = configs.Load();
        var root = string.IsNullOrWhiteSpace(config.DataRoot)
            ? Path.Combine(AppPaths.Root, "pending")
            : config.DataRoot;
        var store = new LandingStore(root, log);
        using var worker = new StaWorker(log, attention);
        worker.Start();
        var coordinator = new CollectionCoordinator(
            configs,
            secrets,
            store,
            store,
            worker.Gateway,
            new SystemClock(),
            log,
            attention,
            new DriveDiskSpace(),
            new ThreadDelay(),
            () => Application.DoEvents());
        worker.Attach(coordinator);
        coordinator.PauseCollection();
        if (!JvRegistration.IsInstalled)
        {
            attention.Raise(AttentionReason.JvLinkMissing, "32bit の JV-Link がありません。");
        }
        else if (!secrets.Exists || !config.SettingsComplete)
        {
            attention.Raise(AttentionReason.ServiceKey, "初期設定が終わるまで定期取得は始まりません。");
        }

        using var schedulerStop = new CancellationTokenSource();
        var scheduler = new Thread(() =>
        {
            while (!schedulerStop.IsCancellationRequested)
            {
                try
                {
                    var now = DateTimeOffset.UtcNow;
                    coordinator.CatchUp(now);
                    coordinator.MaybeEnqueueOdds(now);
                }
                catch (Exception ex)
                {
                    log.Error(ex.GetType().Name);
                }

                if (schedulerStop.Token.WaitHandle.WaitOne(15_000))
                {
                    break;
                }
            }
        })
        {
            IsBackground = true,
            Name = "Scheduler",
        };
        scheduler.Start();
        var tray = new TrayApplication(coordinator, worker, store, configs, secrets, log, attention, showRequest);
        if (!JvRegistration.IsInstalled)
        {
            using var guide = new GuideForm();
            guide.ShowDialog();
        }
        else if (!config.SettingsComplete || !secrets.Exists)
        {
            tray.ShowSetup();
        }

        if (!background && JvRegistration.IsInstalled && configs.Load().SettingsComplete && secrets.Exists)
        {
            tray.ShowHomeOnStart();
        }

        Thread? repairThread = null;
        if (!string.IsNullOrWhiteSpace(configs.Load().DataRoot))
        {
            repairThread = new Thread(() =>
            {
                try
                {
                    log.Info("repair start");
                    store.Repair(RecordKeys.Classify);
                    log.Info("repair done");
                }
                catch (Exception ex)
                {
                    log.Error(ex.GetType().Name);
                }
                finally
                {
                    coordinator.ResumeCollection();
                }
            })
            {
                IsBackground = true,
                Name = "Repair",
            };
            repairThread.Start();
        }
        else
        {
            coordinator.ResumeCollection();
        }

        Application.Run(tray);
        schedulerStop.Cancel();
        scheduler.Join(TimeSpan.FromSeconds(20));
        coordinator.RequestShutdown();
        worker.Dispose();
        repairThread?.Join();
        store.Dispose();
    }

    private static void SignalShowHome()
    {
        try
        {
            using var show = EventWaitHandle.OpenExisting(@"Local\KeibaDwh.Collector.Show");
            show.Set();
        }
        catch (WaitHandleCannotBeOpenedException)
        {
        }
    }

    private static Mutex? AcquireMutex()
    {
        try
        {
            var mutex = new Mutex(true, @"Local\KeibaDwh.Collector", out var created);
            if (created)
            {
                return mutex;
            }

            mutex.Dispose();
            SignalShowHome();
            return null;
        }
        catch (AbandonedMutexException)
        {
            return new Mutex(true, @"Local\KeibaDwh.Collector", out _);
        }
    }
}
