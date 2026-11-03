namespace JvDataCollector;

public sealed class GuideForm : Form
{
    public GuideForm()
    {
        Text = "JV-Link の導入";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(520, 220);
        var message = new Label
        {
            AutoSize = false,
            Location = new Point(16, 16),
            Size = new Size(488, 140),
            Text = JvRegistration.Is32BitInstalled && !JvRegistration.Is64BitRegistered
                ? "32bit 版の JV-Link は入っています。このアプリは 64bit 版が必要です。SDK 5.0.0 の 64bit を導入し、利用規約に同意してから、起動し直してください。利用キーはビット数ごとに別登録です。入手先: https://jra-van.jp/dlb/sdv/sdk.html このアプリは JV-Link を同梱しません。"
                : "64bit の JV-Link が見つかりません。JRA-VAN Data Lab. SDK 5.0.0 の 64bit 版をインストールし、利用規約に同意してから、このアプリを起動し直してください。入手先: https://jra-van.jp/dlb/sdv/sdk.html このアプリは JV-Link を同梱しません。",
        };
        var close = new Button { Text = "閉じる", DialogResult = DialogResult.OK, Location = new Point(408, 172), Size = new Size(96, 28) };
        Controls.Add(message);
        Controls.Add(close);
        AcceptButton = close;
    }
}

public sealed class SetupForm : Form
{
    private readonly TextBox _key = new() { UseSystemPasswordChar = true };
    private readonly TextBox _path = new();
    private readonly Label _free = new() { AutoSize = true };
    private readonly CheckedListBox _specs = new();
    private readonly CheckBox _logon = new() { Text = "ログオン時起動", Checked = true };
    private readonly Label _jv = new() { AutoSize = true };
    private readonly Func<SetupInput, string?> _save;

    public SetupForm(CollectorConfig config, bool hasKey, bool jvReady, Func<SetupInput, string?> save)
    {
        _save = save;
        Text = "初期設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 420);
        _jv.Text = jvReady ? "JV-Link    導入済み" : "JV-Link    未導入";
        _jv.Location = new Point(16, 16);
        var keyLabel = new Label { Text = "利用キー", Location = new Point(16, 48), AutoSize = true };
        _key.Location = new Point(120, 44);
        _key.Width = 220;
        if (hasKey)
        {
            _key.PlaceholderText = "保存済み。変更するときだけ入力";
        }

        var pathLabel = new Label { Text = "保存先", Location = new Point(16, 84), AutoSize = true };
        _path.Location = new Point(120, 80);
        _path.Width = 220;
        _path.Text = config.DataRoot ?? "";
        var browse = new Button { Text = "参照", Location = new Point(348, 78), Size = new Size(72, 26) };
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
        _free.Location = new Point(120, 108);
        var target = new Label { Text = "取得対象", Location = new Point(16, 140), AutoSize = true };
        _specs.Location = new Point(120, 140);
        _specs.Size = new Size(300, 180);
        var selected = new HashSet<string>(config.Dataspecs, StringComparer.Ordinal);
        foreach (var spec in DataspecCatalog.Stored)
        {
            _specs.Items.Add($"{spec.Code}  {spec.Name}", selected.Count == 0 || selected.Contains(spec.Code));
        }

        _logon.Location = new Point(120, 332);
        _logon.Checked = !config.SettingsComplete || config.LaunchAtLogon;
        var note = new Label
        {
            Text = "全期間の取り込みは、メニューのセットアップから開始します。",
            Location = new Point(16, 360),
            AutoSize = true,
        };
        var saveButton = new Button { Text = "保存して完了", Location = new Point(320, 380), Size = new Size(120, 28) };
        saveButton.Click += (_, _) => Save();
        Controls.AddRange([_jv, keyLabel, _key, pathLabel, _path, browse, _free, target, _specs, _logon, note, saveButton]);
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
        var specs = new List<string>();
        foreach (var item in _specs.CheckedItems)
        {
            var text = item?.ToString() ?? "";
            var code = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (!string.IsNullOrEmpty(code))
            {
                specs.Add(code);
            }
        }

        var error = _save(new SetupInput(_path.Text.Trim(), _key.Text, specs, _logon.Checked));
        if (error is null)
        {
            DialogResult = DialogResult.OK;
            Close();
            return;
        }

        MessageBox.Show(this, error, "初期設定", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}

public sealed record SetupInput(string DataRoot, string ServiceKey, IReadOnlyList<string> Dataspecs, bool LaunchAtLogon);

public sealed class StatusForm : Form
{
    private readonly Func<IReadOnlyList<StatusLine>> _rows;
    private readonly Func<ProgressSnapshot> _progress;
    private readonly Func<string?> _attention;
    private readonly Label _progressLabel = new() { AutoSize = false, Location = new Point(16, 12), Size = new Size(560, 24) };
    private readonly ListView _list = new() { Location = new Point(16, 44), Size = new Size(560, 280), View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };

    public StatusForm(Func<IReadOnlyList<StatusLine>> rows, Func<ProgressSnapshot> progress, Func<string?> attention)
    {
        _rows = rows;
        _progress = progress;
        _attention = attention;
        Text = "状態";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(592, 380);
        foreach (var column in new[] { "種類", "option", "どこまで", "最終結果", "件数", "次回" })
        {
            _list.Columns.Add(column, column == "種類" ? 150 : 80);
        }

        var close = new Button { Text = "閉じる", Location = new Point(480, 340), Size = new Size(96, 28), DialogResult = DialogResult.OK };
        Controls.Add(_progressLabel);
        Controls.Add(_list);
        Controls.Add(close);
        _timer.Tick += (_, _) => RefreshRows();
        _timer.Start();
        FormClosed += (_, _) => _timer.Dispose();
        RefreshRows();
    }

    private void RefreshRows()
    {
        var progress = _progress();
        var attention = _attention();
        if (!string.IsNullOrEmpty(attention))
        {
            _progressLabel.Text = attention;
        }
        else if (!string.IsNullOrEmpty(progress.Dataspec))
        {
            _progressLabel.Text = $"取得中   {DataspecCatalog.NameOf(progress.Dataspec)}   {progress.Status} / {progress.DownloadCount}";
        }
        else
        {
            _progressLabel.Text = "待機";
        }

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
