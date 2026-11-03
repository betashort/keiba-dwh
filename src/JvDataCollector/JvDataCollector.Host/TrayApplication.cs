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

    public TrayApplication(
        CollectionCoordinator coordinator,
        StaWorker worker,
        LandingStore store,
        IConfigStore configs,
        ISecretStore secrets,
        IAppLog log,
        IAttention attention)
    {
        _coordinator = coordinator;
        _worker = worker;
        _store = store;
        _configs = configs;
        _secrets = secrets;
        _log = log;
        _attention = attention;
        _idle = IconFactory.Circle(Color.DimGray);
        _busy = IconFactory.Circle(Color.DodgerBlue);
        _alert = IconFactory.Circle(Color.OrangeRed);
        var menu = new ContextMenuStrip();
        menu.Items.Add("今すぐ取得", null, (_, _) => FetchNow());
        menu.Items.Add("セットアップ", null, (_, _) => Setup());
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

        var answer = MessageBox.Show(
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

    private void ShowStatus()
    {
        using var form = new StatusForm(
            () => _coordinator.Status(DateTimeOffset.UtcNow),
            () => _coordinator.Progress,
            () => _attention.HasAttention ? _attention.Message : null);
        form.ShowDialog();
    }

    private void OpenJvSettings()
    {
        var code = _worker.SetUiProperties();
        if (code == -100)
        {
            MessageBox.Show("JV-Link の設定画面を開けません。", "JV-Link 設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        if (!JvRegistration.Is64BitRegistered)
        {
            using var guide = new GuideForm();
            guide.ShowDialog();
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
        using var form = new SetupForm(config, _secrets.Exists, JvRegistration.Is64BitRegistered, SaveSetup);
        form.ShowDialog();
    }

    private string? SaveSetup(SetupInput input)
    {
        if (!JvRegistration.Is64BitRegistered)
        {
            return "64bit の JV-Link が入っていません。";
        }

        if (!Directory.Exists(input.DataRoot))
        {
            return "保存先のフォルダがありません。";
        }

        if (input.Dataspecs.Count == 0)
        {
            return "取得対象を 1 つ以上選んでください。";
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
            return code == -101
                ? "利用キーは登録済みです。変更には JV-Link の再インストールが必要です。"
                : $"JV-Link の設定に失敗しました（{code}）。";
        }

        _worker.Invoke(() => _store.Reopen(input.DataRoot));
        var saved = _configs.Load().Copy();
        saved.DataRoot = input.DataRoot;
        saved.Dataspecs = [.. input.Dataspecs];
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
        if (_attention.HasAttention || !JvRegistration.Is64BitRegistered)
        {
            _icon.Icon = _alert;
            _icon.Text = "要対応";
            return;
        }

        if (_coordinator.IsBusy(DateTimeOffset.UtcNow))
        {
            _icon.Icon = _busy;
            _icon.Text = "取得中";
            return;
        }

        _icon.Icon = _idle;
        _icon.Text = "待機";
    }

    private void Exit()
    {
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
