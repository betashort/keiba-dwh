namespace JvDataCollector;

static class DialogPx
{
    private static readonly float Factor = Read();

    public static int S(int value) => (int)Math.Round(value * Factor);

    public static Point P(int x, int y) => new(S(x), S(y));

    public static Size Z(int width, int height) => new(S(width), S(height));

    private static float Read()
    {
        using var graphics = Graphics.FromHwnd(IntPtr.Zero);
        return graphics.DpiX / 96f;
    }
}

public sealed class GuideForm : Form
{
    public GuideForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        Text = "JV-Link の導入";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = DialogPx.Z(520, 220);
        var message = new Label
        {
            AutoSize = false,
            Location = DialogPx.P(16, 16),
            Size = DialogPx.Z(488, 140),
            Text = "32bit の JV-Link が見つかりません。公式の JRA-VAN Data Lab. SDK 5.0.0（32bit）をインストールし、利用規約に同意してから、このアプリを起動し直してください。入手先: https://jra-van.jp/dlb/sdv/sdk.html このアプリは JV-Link を同梱しません。",
        };
        var close = new Button { Text = "閉じる", DialogResult = DialogResult.OK, Location = DialogPx.P(408, 172), Size = DialogPx.Z(96, 28) };
        Controls.Add(message);
        Controls.Add(close);
        AcceptButton = close;
    }
}

public sealed class SetupForm : Form
{
    private readonly TextBox _key = new() { UseSystemPasswordChar = true };
    private readonly CheckBox _mask = new() { Text = "マスク", Checked = true, AutoSize = true };
    private readonly TextBox _path = new();
    private readonly Label _free = new() { AutoSize = true };
    private readonly CheckBox _logon = new() { Text = "ログオン時起動", Checked = true, AutoSize = true };
    private readonly Label _jv = new() { AutoSize = true };
    private readonly Func<SetupInput, string?> _save;

    public SetupForm(CollectorConfig config, bool hasKey, bool jvReady, Func<SetupInput, string?> save)
    {
        _save = save;
        AutoScaleMode = AutoScaleMode.None;
        Text = "初期設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = DialogPx.Z(460, 280);
        _jv.Text = jvReady ? "JV-Link    導入済み" : "JV-Link    未導入";
        _jv.Location = DialogPx.P(16, 16);
        var keyLabel = new Label { Text = "利用キー", Location = DialogPx.P(16, 48), AutoSize = true };
        _key.Location = DialogPx.P(120, 44);
        _key.Width = DialogPx.S(180);
        _mask.Location = DialogPx.P(308, 46);
        _mask.CheckedChanged += (_, _) => _key.UseSystemPasswordChar = _mask.Checked;
        if (hasKey)
        {
            _key.PlaceholderText = "保存済み。変更するときだけ入力";
        }

        var pathLabel = new Label { Text = "保存先", Location = DialogPx.P(16, 84), AutoSize = true };
        _path.Location = DialogPx.P(120, 80);
        _path.Width = DialogPx.S(220);
        _path.Text = config.DataRoot ?? "";
        var browse = new Button { Text = "参照", Location = DialogPx.P(348, 78), Size = DialogPx.Z(72, 26) };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog();
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                _path.Text = dialog.SelectedPath;
                ShowFree();
            }
        };
        _path.Leave += (_, _) => ShowFree();
        _free.Location = DialogPx.P(120, 108);
        _logon.Location = DialogPx.P(120, 140);
        _logon.Checked = !config.SettingsComplete || config.LaunchAtLogon;
        var note = new Label
        {
            Text = "取得する種類と時刻は、「蓄積データ」と「開催日の速報」で設定します。最初は標準のスケジュールです。全期間の取り込みは、メニューのセットアップから開始します。",
            Location = DialogPx.P(16, 176),
            Size = DialogPx.Z(428, 52),
        };
        var saveButton = new Button { Text = "保存して完了", Location = DialogPx.P(320, 240), Size = DialogPx.Z(120, 28) };
        saveButton.Click += (_, _) => Save();
        Controls.AddRange([_jv, keyLabel, _key, _mask, pathLabel, _path, browse, _free, _logon, note, saveButton]);
        ShowFree();
    }

    private void ShowFree()
    {
        if (!Directory.Exists(_path.Text))
        {
            _free.Text = "フォルダがありません";
            return;
        }

        try
        {
            var free = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(_path.Text))!).AvailableFreeSpace;
            _free.Text = $"空き容量の目安  {free / 1024d / 1024d / 1024d:0.0} GB";
        }
        catch (Exception)
        {
            _free.Text = "空き容量を取得できません";
        }
    }

    private void Save()
    {
        var error = _save(new SetupInput(_path.Text.Trim(), _key.Text, _logon.Checked));
        if (error is null)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        MessageBox.Show(this, error, "初期設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

public sealed record SetupInput(string DataRoot, string ServiceKey, bool LaunchAtLogon);

public sealed class StatusForm : Form
{
    private readonly Func<IReadOnlyList<StatusLine>> _rows;
    private readonly Func<ProgressSnapshot> _progress;
    private readonly Func<string?> _attention;
    private readonly Label _progressLabel = new() { AutoSize = false };
    private readonly ProgressBar _progressBar = new();
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    public StatusForm(Func<IReadOnlyList<StatusLine>> rows, Func<ProgressSnapshot> progress, Func<string?> attention)
    {
        _rows = rows;
        _progress = progress;
        _attention = attention;
        AutoScaleMode = AutoScaleMode.None;
        Text = "状態";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = DialogPx.Z(592, 408);
        _progressLabel.Location = DialogPx.P(16, 12);
        _progressLabel.Size = DialogPx.Z(560, 36);
        _progressBar.Location = DialogPx.P(16, 52);
        _progressBar.Size = DialogPx.Z(560, 18);
        _list.Location = DialogPx.P(16, 80);
        _list.Size = DialogPx.Z(560, 272);
        foreach (var column in new[] { "種類", "option", "どこまで", "最終結果", "件数", "次回" })
        {
            _list.Columns.Add(column, DialogPx.S(column == "種類" ? 150 : 80));
        }

        var close = new Button { Text = "閉じる", Location = DialogPx.P(480, 364), Size = DialogPx.Z(96, 28), DialogResult = DialogResult.OK };
        close.Click += (_, _) => Close();
        Controls.Add(_progressLabel);
        Controls.Add(_progressBar);
        Controls.Add(_list);
        Controls.Add(close);
        _timer.Tick += (_, _) => RefreshRows();
        _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
        RefreshRows();
    }

    private void RefreshRows()
    {
        ProgressView.Apply(_progressLabel, _progressBar, _progress(), _attention());

        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var row in _rows())
        {
            var item = new ListViewItem(row.Name);
            item.SubItems.Add(row.Option == 0 ? "速報" : row.Option.ToString());
            item.SubItems.Add(row.Watermark);
            item.SubItems.Add(row.LastResult);
            item.SubItems.Add(row.Count?.ToString() ?? "");
            item.SubItems.Add(row.Next);
            _list.Items.Add(item);
        }

        _list.EndUpdate();
    }
}

static class ProgressView
{
    public static void Apply(Label label, ProgressBar bar, ProgressSnapshot progress, string? attention)
    {
        if (!string.IsNullOrEmpty(attention))
        {
            label.Text = attention;
            SetBar(bar, 0, 0, active: false);
            return;
        }

        label.Text = progress.Caption(DateTimeOffset.UtcNow);
        SetBar(bar, progress.Current, progress.Total, active: !string.IsNullOrEmpty(progress.Dataspec));
    }

    private static void SetBar(ProgressBar bar, int current, int total, bool active)
    {
        if (active && total <= 0)
        {
            bar.Style = ProgressBarStyle.Marquee;
            bar.MarqueeAnimationSpeed = 30;
            return;
        }

        var max = active ? total : 1;
        var value = active ? Math.Clamp(current, 0, max) : 0;
        bar.Style = ProgressBarStyle.Continuous;
        bar.MarqueeAnimationSpeed = 0;
        if (bar.Value > max)
        {
            bar.Value = 0;
        }

        bar.Maximum = max;
        bar.Value = value;
    }
}

public sealed class ImportProgressForm : Form
{
    private readonly Label _label = new() { AutoSize = false };
    private readonly ProgressBar _bar = new();

    public ImportProgressForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        Text = "取り込み";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.Manual;
        MaximizeBox = false;
        MinimizeBox = true;
        ShowInTaskbar = true;
        ClientSize = DialogPx.Z(440, 132);
        _label.Location = DialogPx.P(16, 16);
        _label.Size = DialogPx.Z(408, 40);
        _bar.Location = DialogPx.P(16, 60);
        _bar.Size = DialogPx.Z(408, 18);
        var note = new Label
        {
            AutoSize = false,
            Text = "閉じても取り込みは続きます。",
            Location = DialogPx.P(16, 88),
            Size = DialogPx.Z(240, 24),
        };
        var close = new Button { Text = "閉じる", Location = DialogPx.P(328, 88), Size = DialogPx.Z(96, 28) };
        close.Click += (_, _) => Close();
        Controls.Add(_label);
        Controls.Add(_bar);
        Controls.Add(note);
        Controls.Add(close);
        CancelButton = close;
    }

    public void Apply(ProgressSnapshot progress) => ProgressView.Apply(_label, _bar, progress, null);

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var area = Screen.FromControl(this).WorkingArea;
        Location = new Point(area.Right - Width - DialogPx.S(16), area.Bottom - Height - DialogPx.S(16));
        Activate();
    }
}
