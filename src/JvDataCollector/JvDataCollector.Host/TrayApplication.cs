namespace JvDataCollector;

public sealed class TrayApplication : ApplicationContext
{
    private readonly CollectionCoordinator _coordinator;
    private readonly StaWorker _worker;
    private readonly LandingStore _store;
    private readonly IConfigStore _configs;
    private readonly ISecretStore _secrets;
    private readonly IAppLog _log;
    private readonly IAttention _attention;
    private readonly NotifyIcon _icon;
    private readonly Icon _idle;
    private readonly Icon _busy;
    private readonly Icon _alert;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly EventWaitHandle _showRequest;
    private ImportProgressForm? _progressForm;
    private StatusForm? _statusForm;
    private HomeForm? _homeForm;
    private int _dismissedRun;
    private bool _closingProgress;

    public TrayApplication(
        CollectionCoordinator coordinator,
        StaWorker worker,
        LandingStore store,
        IConfigStore configs,
        ISecretStore secrets,
        IAppLog log,
        IAttention attention,
        EventWaitHandle showRequest)
    {
        _coordinator = coordinator;
        _worker = worker;
        _store = store;
        _configs = configs;
        _secrets = secrets;
        _log = log;
        _attention = attention;
        _showRequest = showRequest;
        _idle = IconFactory.Circle(Color.DimGray);
        _busy = IconFactory.Circle(Color.DodgerBlue);
        _alert = IconFactory.Circle(Color.OrangeRed);
        var menu = new ContextMenuStrip();
        menu.Items.Add("ホーム", null, (_, _) => ShowHome());
        menu.Items.Add("今すぐ取得", null, (_, _) => FetchNow());
        menu.Items.Add("セットアップ", null, (_, _) => Setup());
        menu.Items.Add("蓄積データ", null, (_, _) => ShowStored());
        menu.Items.Add("開催日の速報", null, (_, _) => ShowRealtime());
        menu.Items.Add("状態", null, (_, _) => ShowStatus());
        menu.Items.Add("JV-Link 設定", null, (_, _) => OpenJvSettings());
        menu.Items.Add("ログの場所", null, (_, _) => OpenLogs());
        menu.Items.Add("終了", null, (_, _) => Exit());
        _icon = new NotifyIcon
        {
            Icon = _idle,
            Visible = true,
            Text = "競馬データ収集",
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => ShowHome();
        _timer.Tick += (_, _) => UpdateIcon();
        _timer.Start();
        UpdateIcon();
    }

    private void FetchNow()
    {
        if (!EnsureReady())
        {
            return;
        }

        var queued = _coordinator.RequestUserFetch();
        _log.Info(queued ? "user fetch queued" : "user fetch waiting");
    }

    private void Setup()
    {
        if (!EnsureReady())
        {
            return;
        }

        var answer = Tell(
            "全期間の取り込みを開始します。容量が大きく、JV-Link のダイアログが出ます。",
            "セットアップ",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);
        if (answer != DialogResult.OK)
        {
            return;
        }

        var queued = _coordinator.RequestSetup();
        _log.Info(queued ? "setup queued" : "setup waiting");
    }

    private void ShowStored()
    {
        var config = _configs.Load();
        using var form = new StoredScheduleForm(config);
        if (OpenDialog(form) != DialogResult.OK || form.Rows is null)
        {
            return;
        }

        var saved = _configs.Load().Copy();
        saved.StoredSchedule = [.. form.Rows];
        saved.Dataspecs = form.Rows.Where(row => row.Enabled).Select(row => row.Dataspec).Distinct(StringComparer.Ordinal).ToList();
        _configs.Save(saved);
        _coordinator.Reload();
        _log.Info("stored schedule saved");
    }

    private void ShowRealtime()
    {
        var config = _configs.Load();
        using var form = new RealtimeScheduleForm(config);
        if (OpenDialog(form) != DialogResult.OK || form.Data is null || form.Rules is null)
        {
            return;
        }

        var saved = _configs.Load().Copy();
        saved.RealtimeData = [.. form.Data];
        saved.RealtimeSchedule = [.. form.Rules];
        _configs.Save(saved);
        _coordinator.Reload();
        _log.Info("realtime schedule saved");
    }

    public void ShowHomeOnStart()
    {
        var once = new System.Windows.Forms.Timer { Interval = 1 };
        once.Tick += (_, _) =>
        {
            once.Stop();
            once.Dispose();
            ShowHome();
        };
        once.Start();
    }

    private void ShowHome()
    {
        if (_homeForm is { IsDisposed: false })
        {
            if (_homeForm.WindowState == FormWindowState.Minimized)
            {
                _homeForm.WindowState = FormWindowState.Normal;
            }

            _homeForm.Activate();
            return;
        }

        var form = new HomeForm(
            () => _coordinator.Status(DateTimeOffset.UtcNow),
            () => _coordinator.Progress,
            () => _attention.HasAttention ? _attention.Message : null,
            () => _coordinator.CollectionPaused,
            new HomeCommands(FetchNow, Setup, ShowStored, ShowRealtime, ShowStatus, OpenJvSettings, OpenLogs));
        _homeForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_homeForm, form))
            {
                _homeForm = null;
            }
        };
        form.Show();
        form.Activate();
    }

    private void ShowStatus()
    {
        if (_statusForm is { IsDisposed: false })
        {
            if (_statusForm.WindowState == FormWindowState.Minimized)
            {
                _statusForm.WindowState = FormWindowState.Normal;
            }

            _statusForm.Activate();
            return;
        }

        var form = new StatusForm(
            () => _coordinator.Status(DateTimeOffset.UtcNow),
            () => _coordinator.Progress,
            () => _attention.HasAttention ? _attention.Message : null);
        _statusForm = form;
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_statusForm, form))
            {
                _statusForm = null;
            }
        };
        form.Show();
        form.Activate();
    }

    private void OpenJvSettings()
    {
        var code = _worker.SetUiProperties();
        if (code == -100)
        {
            Tell("JV-Link の設定画面を開けません。", "JV-Link 設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OpenLogs()
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);
        System.Diagnostics.Process.Start("explorer.exe", AppPaths.LogDirectory);
    }

    private bool EnsureReady()
    {
        var config = _configs.Load();
        if (!JvRegistration.IsInstalled)
        {
            using var guide = new GuideForm();
            OpenDialog(guide);
            return false;
        }

        if (!config.SettingsComplete || !_secrets.Exists)
        {
            ShowSetup();
            return _configs.Load().SettingsComplete && _secrets.Exists;
        }

        return true;
    }

    public void ShowSetup()
    {
        var config = _configs.Load();
        using var form = new SetupForm(config, _secrets.Exists, JvRegistration.IsInstalled, SaveSetup);
        OpenDialog(form);
    }

    private DialogResult OpenDialog(Form form) =>
        _homeForm is { IsDisposed: false } owner ? form.ShowDialog(owner) : form.ShowDialog();

    private DialogResult Tell(string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon) =>
        _homeForm is { IsDisposed: false } owner
            ? MessageBox.Show(owner, text, title, buttons, icon)
            : MessageBox.Show(text, title, buttons, icon);

    private string? SaveSetup(SetupInput input)
    {
        if (!JvRegistration.IsInstalled)
        {
            return "32bit の JV-Link が入っていません。";
        }

        if (!Directory.Exists(input.DataRoot))
        {
            return "保存先のフォルダがありません。";
        }

        if (!input.LaunchAtLogon)
        {
            return "ログオン時起動をオンにしてください。";
        }

        var key = input.ServiceKey;
        if (string.IsNullOrWhiteSpace(key))
        {
            key = _secrets.Load() ?? "";
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return "利用キーを入力してください。";
        }

        var code = _worker.ApplyConnection(Path.Combine(input.DataRoot, "jvlink"), key);
        if (code != 0)
        {
            return code switch
            {
                -101 => "利用キーは登録済みです。変更には JV-Link の再インストールが必要です。",
                -201 => "JV-Link を初期化できません。",
                _ => $"JV-Link の設定に失敗しました（{code}）。",
            };
        }

        _worker.Invoke(() => _store.Reopen(input.DataRoot));
        var saved = _configs.Load().Copy();
        saved.DataRoot = input.DataRoot;
        if (saved.Dataspecs.Count == 0)
        {
            saved.Dataspecs = [.. DataspecCatalog.DefaultCodes];
        }

        saved.LaunchAtLogon = true;
        saved.SettingsComplete = true;
        if (string.IsNullOrEmpty(saved.DifferentialEpoch))
        {
            saved.DifferentialEpoch = Jst.Stamp(DateTimeOffset.UtcNow);
        }

        _configs.Save(saved);
        _secrets.Save(key);
        _log.NoteSecret(key);
        LogonRegistration.Set(true);
        _coordinator.Reload();
        _attention.Clear(AttentionReason.ServiceKey);
        _attention.Clear(AttentionReason.SavePath);
        return null;
    }

    private void UpdateIcon()
    {
        if (_showRequest.WaitOne(0))
        {
            ShowHome();
        }

        var progress = _coordinator.Progress;
        ShowImport(progress);
        if (_attention.HasAttention || !JvRegistration.IsInstalled)
        {
            _icon.Icon = _alert;
            _icon.Text = "要対応";
            return;
        }

        if (_coordinator.CollectionPaused)
        {
            _icon.Icon = _busy;
            _icon.Text = "起動準備";
            return;
        }

        if (_coordinator.IsBusy(DateTimeOffset.UtcNow))
        {
            _icon.Icon = _busy;
            _icon.Text = Tip(string.IsNullOrEmpty(progress.Dataspec) ? "取得中" : progress.Caption(DateTimeOffset.UtcNow));
            return;
        }

        _icon.Icon = _idle;
        _icon.Text = "待機";
    }

    private void ShowImport(ProgressSnapshot progress)
    {
        var visible = progress.Stored && !string.IsNullOrEmpty(progress.Dataspec);
        if (!visible)
        {
            CloseProgress();
            return;
        }

        if (_dismissedRun == progress.RunId)
        {
            return;
        }

        if (_progressForm is null || _progressForm.IsDisposed)
        {
            _progressForm = new ImportProgressForm();
            _progressForm.FormClosed += (_, _) =>
            {
                if (!_closingProgress && _coordinator.Progress.RunId != 0)
                {
                    _dismissedRun = _coordinator.Progress.RunId;
                }

                _progressForm = null;
            };
            _progressForm.Show();
        }

        _progressForm.Apply(progress);
    }

    private void CloseProgress()
    {
        if (_progressForm is not { IsDisposed: false })
        {
            _progressForm = null;
            return;
        }

        _closingProgress = true;
        try
        {
            _progressForm.Close();
        }
        finally
        {
            _closingProgress = false;
            _progressForm = null;
        }
    }

    private static string Tip(string text) => text.Length <= 63 ? text : text[..63];

    private void Exit()
    {
        CloseProgress();
        if (_statusForm is { IsDisposed: false })
        {
            _statusForm.Close();
        }

        if (_homeForm is { IsDisposed: false })
        {
            _homeForm.Close();
        }

        _coordinator.RequestShutdown();
        _worker.Dispose();
        _icon.Visible = false;
        _timer.Dispose();
        _icon.Dispose();
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        base.ExitThreadCore();
    }
}
