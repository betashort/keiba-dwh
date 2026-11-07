namespace DwhLoader;

public sealed record SettingsInput(string DataRoot, string Host, string Port, string Database, string Username, string Password);

public sealed class SettingsForm : Form
{
    private readonly TextBox _dataRoot = new();
    private readonly TextBox _host = new();
    private readonly TextBox _port = new();
    private readonly TextBox _database = new();
    private readonly TextBox _username = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly Label _unknown = new();
    private readonly Label _error = new() { ForeColor = Color.DarkRed };
    private readonly Label _note = new() { Text = "空なら保存済みを残す。値は出さない" };
    private readonly Label _logon = new() { Text = "ログオン時には起動しません。" };
    private readonly Label _cycle = new();
    private readonly Button _cancel;
    private readonly Button _saveButton;
    private readonly int _margin = 16;
    private readonly int _clientWidth;
    private readonly int _textWidth;
    private readonly int _boxLeft;
    private readonly int _boxWidth;
    private readonly int _tailTop;
    private readonly Func<SettingsInput, string> _save;

    public SettingsForm(LoaderSettings settings, bool hasPassword, bool initial, Func<SettingsInput, string> save)
    {
        _save = save;
        Text = initial ? "初期設定" : "設定";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        var gap = 12;
        var names = new[] { "保存先", "ホスト", "ポート", "データベース", "ユーザ名", "パスワード" };
        var boxes = new[] { _dataRoot, _host, _port, _database, _username, _password };
        var labelWidth = names.Max(name => FormMetrics.Label(name, Font).Width);
        var boxWidth = new[] { "空なら保存済みを残す。値は出さない", "保存済み。変更するときだけ入力" }.Max(text => FormMetrics.Label(text, Font).Width) + 24;
        var line = FormMetrics.Label("保存先", Font).Height;
        var messages = new[]
        {
            "保存先、ホスト、データベース、ユーザ名を入れてください",
            "ポートは 1 から 65535 の整数です",
            "パスワードがありません",
            "ログオン時には起動しません。",
            "保存した接続は、次の周回から使います。",
        };
        _clientWidth = Math.Max(_margin + labelWidth + gap + boxWidth + _margin, messages.Max(text => FormMetrics.Label(text, Font).Width) + (_margin * 2));
        _boxWidth = _clientWidth - _margin - labelWidth - gap - _margin;
        _boxLeft = _margin + labelWidth + gap;
        var boxHeight = Math.Max(boxes[0].PreferredHeight, line);
        var y = _margin;
        for (var i = 0; i < names.Length; i++)
        {
            var caption = new Label
            {
                AutoSize = false,
                Text = names[i],
                Location = new Point(_margin, y + Math.Max(0, (boxHeight - line) / 2)),
                Size = new Size(labelWidth, line),
            };
            boxes[i].Location = new Point(_boxLeft, y);
            boxes[i].Size = new Size(_boxWidth, boxHeight);
            Controls.Add(caption);
            Controls.Add(boxes[i]);
            y += boxHeight + 10;
        }

        _dataRoot.Text = settings.DataRoot ?? "";
        _host.Text = settings.Host ?? "";
        _port.Text = settings.Port.ToString();
        _database.Text = settings.Database ?? "";
        _username.Text = settings.Username ?? "";
        _password.Text = "";
        _password.PlaceholderText = hasPassword
            ? "保存済み。変更するときだけ入力"
            : "空なら保存済みを残す。値は出さない";
        if (settings.UnknownKeys.Count > 0)
        {
            _unknown.Text = "知らないキー  " + string.Join("  ", settings.UnknownKeys);
        }
        else if (settings.Invalid)
        {
            _unknown.Text = "設定の値を読めません";
        }

        _cycle.Text = initial ? "" : "保存した接続は、次の周回から使います。";
        _textWidth = _clientWidth - (_margin * 2);
        _tailTop = y;
        _cancel = new Button
        {
            Text = initial ? "閉じる" : "キャンセル",
            DialogResult = DialogResult.Cancel,
            Size = FormMetrics.Button(initial ? "閉じる" : "キャンセル", Font),
        };
        _saveButton = new Button
        {
            Text = initial ? "保存して開始" : "保存",
            Size = FormMetrics.Button(initial ? "保存して開始" : "保存", Font),
        };
        _saveButton.Click += (_, _) =>
        {
            if (TrySave() is null)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
        };
        Controls.Add(_note);
        Controls.Add(_unknown);
        Controls.Add(_error);
        Controls.Add(_logon);
        Controls.Add(_cycle);
        Controls.Add(_cancel);
        Controls.Add(_saveButton);
        ArrangeTail();
        AcceptButton = _saveButton;
        CancelButton = _cancel;
    }

    private void ArrangeTail()
    {
        var y = _tailTop;
        y = Stack(_note, _boxLeft, _boxWidth, y);
        y = Stack(_unknown, _margin, _textWidth, y);
        y = Stack(_error, _margin, _textWidth, y);
        y = Stack(_logon, _margin, _textWidth, y);
        y = Stack(_cycle, _margin, _textWidth, y);
        y += 8;
        _saveButton.Location = new Point(_clientWidth - _margin - _saveButton.Width, y);
        _cancel.Location = new Point(_saveButton.Left - 8 - _cancel.Width, y);
        ClientSize = new Size(_clientWidth, _saveButton.Bottom + _margin);
    }

    private int Stack(Label label, int x, int width, int y, int emptyHeight = 0)
    {
        var height = string.IsNullOrEmpty(label.Text) ? emptyHeight : Math.Max(emptyHeight, FormMetrics.Wrapped(label.Text, Font, width).Height);
        label.AutoSize = false;
        label.Location = new Point(x, y);
        label.Size = new Size(width, height);
        return height == 0 ? y : y + height + 8;
    }

    public string PasswordText => _password.Text;

    public string UnknownText => _unknown.Text;

    public string VisibleText()
    {
        var text = new System.Text.StringBuilder();
        foreach (Control control in Controls)
        {
            text.AppendLine(control.Text);
        }

        return text.ToString();
    }

    public string TrySave()
    {
        var error = _save(new SettingsInput(
            _dataRoot.Text,
            _host.Text,
            _port.Text,
            _database.Text,
            _username.Text,
            _password.Text));
        _error.Text = error ?? "";
        ArrangeTail();
        return error;
    }
}

public sealed class StatusForm : Form
{
    private readonly LoaderSession _session;
    private readonly Func<SettingsInput, string> _save;
    private readonly Func<LoaderSettings> _reload;
    private readonly Func<bool> _hasPassword;
    private readonly Font _headlineFont;
    private readonly Label _headline = new() { AutoSize = false };
    private readonly Label _detail = new() { AutoSize = false };
    private readonly Label _warehouseCaption = new() { Text = "倉庫", AutoSize = false };
    private readonly Label _warehouse = new() { AutoSize = false };
    private readonly Label _ob14Caption = new() { Text = "0B14 の写し", AutoSize = false };
    private readonly Label _ob14 = new() { AutoSize = false };
    private readonly Label _cycleCaption = new() { Text = "この周回", AutoSize = false };
    private readonly Label _cycle = new() { AutoSize = false };
    private readonly Button _settingsButton = new() { Text = "設定" };
    private readonly Button _logsButton = new() { Text = "ログの場所" };
    private readonly Button _stopButton = new() { Text = "停止" };
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, GridLines = true };
    private readonly int _margin = 16;
    private readonly int _captionWidth;
    private readonly int _valueLeft;
    private readonly int _valueWidth;
    private readonly int _line;
    private readonly int _listWidth;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private readonly CancellationTokenSource _cancel = new();
    private Thread _worker;
    private bool _closing;

    public StatusForm(
        LoaderSession session,
        Func<SettingsInput, string> save,
        Func<LoaderSettings> reload,
        Func<bool> hasPassword)
    {
        _session = session;
        _save = save;
        _reload = reload;
        _hasPassword = hasPassword;
        Text = "DWH ローダ";
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = true;
        var gap = 8;
        _headlineFont = new Font(Font.FontFamily, Font.Size + 5f, FontStyle.Bold, Font.Unit);
        _headline.Font = _headlineFont;
        var columns = new (string Name, int Width)[]
        {
            ("id", FormMetrics.Column(Font, "id", "0000000000")),
            ("dataspec", FormMetrics.Column(Font, "dataspec")),
            ("結果", FormMetrics.Column(Font, "結果", "書いています", "再試行待ち")),
            ("件数", FormMetrics.Column(Font, "件数", "00000", "—")),
            ("理由", FormMetrics.Column(Font, "理由", "HashMismatchException ハッシュ不一致   あと 00 秒")),
        };
        var listWidth = columns.Sum(column => column.Width) + SystemInformation.VerticalScrollBarWidth + 4;
        var captions = new[] { _warehouseCaption, _ob14Caption, _cycleCaption };
        _captionWidth = captions.Max(caption => FormMetrics.Label(caption.Text, Font).Width);
        _valueLeft = _margin + _captionWidth + 16;
        var valueWidth = Math.Max(280, listWidth - _captionWidth - 16);
        var contentWidth = Math.Max(listWidth, _valueLeft + valueWidth - _margin);
        _listWidth = contentWidth;
        _valueWidth = _listWidth - _captionWidth - 16;
        _line = FormMetrics.Label("倉庫", Font).Height;
        _headline.SetBounds(_margin, _margin, contentWidth, FormMetrics.Label("PostgreSQL に届きません", _headlineFont).Height);
        _detail.SetBounds(_margin, _headline.Bottom + gap, contentWidth, _line);
        foreach (var column in columns)
        {
            _list.Columns.Add(column.Name, column.Width);
        }

        var spare = _listWidth - columns.Sum(column => column.Width) - SystemInformation.VerticalScrollBarWidth - 4;
        if (spare > 0)
        {
            _list.Columns[^1].Width += spare;
        }

        _settingsButton.Size = FormMetrics.Button("設定", Font);
        _settingsButton.Click += (_, _) => OpenSettings();
        _logsButton.Size = FormMetrics.Button("ログの場所", Font);
        _logsButton.Click += (_, _) => OpenLogs();
        _stopButton.Size = FormMetrics.Button("停止", Font);
        _stopButton.Click += (_, _) => Close();
        _settingsButton.Left = _margin;
        _logsButton.Left = _settingsButton.Right + gap;
        _stopButton.Left = _margin + _listWidth - _stopButton.Width;
        Controls.AddRange([
            _headline, _detail, _warehouseCaption, _warehouse, _ob14Caption, _ob14, _cycleCaption, _cycle, _list,
            _settingsButton, _logsButton, _stopButton,
        ]);
        ClientSize = new Size(_margin + _listWidth + _margin, 320);
        Arrange();
        _session.Changed += OnChanged;
        _timer.Tick += (_, _) => Render(_session.Snapshot);
        FormClosing += (_, _) => Shutdown();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (_worker is not null)
        {
            return;
        }

        Render(_session.Snapshot);
        _timer.Start();
        _worker = new Thread(() => _session.Loop(_cancel.Token))
        {
            IsBackground = true,
            Name = "Load",
        };
        _worker.Start();
    }

    public string VisibleText()
    {
        var text = new System.Text.StringBuilder();
        foreach (Control control in Controls)
        {
            text.AppendLine(control.Text);
            if (control is ListView list)
            {
                foreach (ListViewItem item in list.Items)
                {
                    text.AppendLine(string.Join(" ", item.SubItems.Cast<ListViewItem.ListViewSubItem>().Select(sub => sub.Text)));
                }
            }
        }

        return text.ToString();
    }

    public void Apply(StatusSnapshot snapshot) => Render(snapshot);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _cancel.Dispose();
            _headlineFont?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void OnChanged(StatusSnapshot snapshot)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        BeginInvoke(() =>
        {
            if (!IsDisposed)
            {
                Render(snapshot);
            }
        });
    }

    private void Render(StatusSnapshot snapshot)
    {
        _headline.Text = snapshot.Headline;
        _detail.Text = snapshot.Detail;
        _warehouse.Text = snapshot.Warehouse;
        _ob14.Text = snapshot.ShowOb14 ? snapshot.Ob14 : "";
        _ob14.Visible = snapshot.ShowOb14;
        _ob14Caption.Visible = snapshot.ShowOb14;
        _cycle.Text = snapshot.Cycle;
        var showCycle = !string.IsNullOrEmpty(snapshot.Cycle);
        _cycle.Visible = showCycle;
        _cycleCaption.Visible = showCycle;
        _list.Visible = snapshot.ShowList;
        _list.BeginUpdate();
        _list.Items.Clear();
        if (snapshot.ShowList)
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var line in snapshot.Files)
            {
                var item = new ListViewItem(line.Id.ToString());
                item.SubItems.Add(line.Dataspec);
                item.SubItems.Add(line.Result);
                item.SubItems.Add(line.Count is null ? "—" : line.Count.Value.ToString());
                item.SubItems.Add(line.ReasonText(now));
                _list.Items.Add(item);
            }
        }

        _list.EndUpdate();
        var detail = snapshot.Detail ?? "";
        _detail.Height = detail.Length == 0 ? 0 : FormMetrics.Wrapped(detail, _detail.Font, _detail.Width).Height;
        Arrange();
    }

    private void Arrange()
    {
        var y = _detail.Bottom + 12;
        Place(_warehouseCaption, _warehouse, _margin, _valueLeft, _captionWidth, _valueWidth, _line, ref y);
        if (_ob14.Visible)
        {
            Place(_ob14Caption, _ob14, _margin, _valueLeft, _captionWidth, _valueWidth, _line, ref y);
        }

        if (_cycle.Visible)
        {
            Place(_cycleCaption, _cycle, _margin, _valueLeft, _captionWidth, _valueWidth, _line, ref y);
        }

        y += 4;
        if (_list.Visible)
        {
            _list.SetBounds(_margin, y, _listWidth, 200);
            y = _list.Bottom + 12;
        }

        _settingsButton.Top = y;
        _logsButton.Top = y;
        _stopButton.Top = y;
        ClientSize = new Size(_margin + _listWidth + _margin, _stopButton.Bottom + _margin);
    }

    private static void Place(Label caption, Label value, int left, int valueLeft, int captionWidth, int valueWidth, int line, ref int y)
    {
        caption.SetBounds(left, y, captionWidth, line);
        value.SetBounds(valueLeft, y, valueWidth, line);
        y = caption.Bottom + 8;
    }

    private void OpenSettings()
    {
        using var dialog = new SettingsForm(_reload(), _hasPassword(), false, _save);
        dialog.ShowDialog(this);
    }

    private static void OpenLogs()
    {
        Directory.CreateDirectory(AppPaths.LogDirectory);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", AppPaths.LogDirectory) { UseShellExecute = true });
    }

    private void Shutdown()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _cancel.Cancel();
        _session.Stop();
        _worker?.Join(TimeSpan.FromSeconds(8));
    }
}

static class FormMetrics
{
    public static Size Label(string text, Font font)
    {
        using var sample = new Label { AutoSize = true, Font = font, Text = text };
        return sample.PreferredSize;
    }

    public static Size Wrapped(string text, Font font, int width)
    {
        using var sample = new Label
        {
            AutoSize = true,
            Font = font,
            Text = text,
            MaximumSize = new Size(Math.Max(width, 1), 0),
        };
        return sample.PreferredSize;
    }

    public static Size Button(string text, Font font)
    {
        using var sample = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Font = font,
            Text = text,
        };
        var size = sample.PreferredSize;
        return new Size(Math.Max(size.Width, 80), Math.Max(size.Height, 28));
    }

    public static int Column(Font font, params string[] samples)
    {
        var width = 0;
        foreach (var sample in samples)
        {
            width = Math.Max(width, Label(sample, font).Width);
        }

        return width + 24;
    }
}
