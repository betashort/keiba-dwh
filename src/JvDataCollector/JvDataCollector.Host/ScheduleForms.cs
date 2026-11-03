namespace JvDataCollector;

public sealed class StoredScheduleForm : Form
{
    private readonly DataGridView _grid = ScheduleGrid.NewGrid();
    private readonly ComboBox _spec = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public StoredScheduleForm(CollectorConfig config)
    {
        AutoScaleMode = AutoScaleMode.None;
        Text = "蓄積データ";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = DialogPx.Z(780, 640);
        var note = new Label
        {
            Text = "取得する行だけ実行します。曜日と時刻は変えられます。右の既定は初期値なので、変えたあとも残ります。",
            Location = DialogPx.P(16, 12),
            Size = DialogPx.Z(748, 36),
        };
        var specLabel = new Label { Text = "種類", Location = DialogPx.P(16, 56), AutoSize = true };
        _spec.Location = DialogPx.P(64, 52);
        _spec.Width = DialogPx.S(220);
        _spec.DisplayMember = nameof(DataspecInfo.Name);
        _spec.ValueMember = nameof(DataspecInfo.Code);
        _spec.DataSource = DataspecCatalog.Stored.ToArray();
        var add = new Button { Text = "時刻を追加", Location = DialogPx.P(296, 50), Size = DialogPx.Z(120, 28) };
        add.Click += (_, _) => AddRow();
        _grid.Location = DialogPx.P(16, 88);
        _grid.Size = DialogPx.Z(748, 480);
        ScheduleGrid.AddColumn(_grid, "on", "取得", 70);
        ScheduleGrid.AddText(_grid, "name", "種類", 160, readOnly: true);
        ScheduleGrid.AddDays(_grid, ScheduleText.StoredDays, 90);
        ScheduleGrid.AddText(_grid, "time", "時刻", 80, readOnly: false);
        ScheduleGrid.AddColumn(_grid, "weekly", "読み直し", 80);
        ScheduleGrid.AddText(_grid, "preset", "既定", 220, readOnly: true, muted: true);
        foreach (var row in SchedulePlan.EditStored(config))
        {
            AddStored(row);
        }

        var remove = new Button { Text = "追加した行を削除", Location = DialogPx.P(16, 584), Size = DialogPx.Z(160, 28) };
        remove.Click += (_, _) => RemoveCustom();
        var save = new Button { Text = "保存", Location = DialogPx.P(644, 584), Size = DialogPx.Z(120, 28) };
        save.Click += (_, _) => Save();
        Controls.AddRange([note, specLabel, _spec, add, _grid, remove, save]);
    }

    public IReadOnlyList<StoredScheduleRow>? Rows { get; private set; }

    private void AddRow()
    {
        var code = _spec.SelectedValue as string;
        if (string.IsNullOrEmpty(code))
        {
            return;
        }

        AddStored(new StoredScheduleRow
        {
            Id = "c|" + Guid.NewGuid().ToString("N"),
            Dataspec = code,
            Enabled = true,
            Day = "毎日",
            Time = "12:00",
        });
    }

    private void AddStored(StoredScheduleRow row)
    {
        var index = _grid.Rows.Add(row.Enabled, DataspecCatalog.NameOf(row.Dataspec), row.Day, row.Time, row.WeeklyReread, ScheduleDefaults.StoredCaption(row.Id));
        _grid.Rows[index].Tag = new StoredMark(row.Id, row.Dataspec);
    }

    private void RemoveCustom()
    {
        if (_grid.CurrentRow?.Tag is not StoredMark mark || !mark.Id.StartsWith("c|", StringComparison.Ordinal))
        {
            MessageBox.Show(this, "標準の行は、取得のチェックを外してください。", "蓄積データ", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _grid.Rows.Remove(_grid.CurrentRow);
    }

    private void Save()
    {
        ScheduleGrid.Commit(_grid);
        var rows = new List<StoredScheduleRow>();
        foreach (DataGridViewRow gridRow in _grid.Rows)
        {
            if (gridRow.Tag is not StoredMark mark)
            {
                continue;
            }

            var row = new StoredScheduleRow
            {
                Id = mark.Id,
                Dataspec = mark.Dataspec,
                Enabled = ScheduleGrid.IsChecked(gridRow.Cells["on"]),
                Day = ScheduleGrid.CellText(gridRow, "day"),
                Time = ScheduleGrid.CellText(gridRow, "time"),
                WeeklyReread = ScheduleGrid.IsChecked(gridRow.Cells["weekly"]),
            };
            var error = ScheduleText.ValidateStored(row);
            if (error is not null)
            {
                MessageBox.Show(this, error, "蓄積データ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (ScheduleText.TryTime(row.Time, out var time))
            {
                row.Time = ScheduleText.Time(time);
            }

            rows.Add(row);
        }

        if (!rows.Any(row => row.Enabled))
        {
            MessageBox.Show(this, "取得対象を 1 つ以上選んでください。", "蓄積データ", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Rows = rows;
        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record StoredMark(string Id, string Dataspec);
}

public sealed class RealtimeScheduleForm : Form
{
    private readonly DataGridView _data = ScheduleGrid.NewGrid();
    private readonly DataGridView _rules = ScheduleGrid.NewGrid();

    public RealtimeScheduleForm(CollectorConfig config)
    {
        AutoScaleMode = AutoScaleMode.None;
        Text = "開催日の速報";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = DialogPx.Z(860, 640);
        var note = new Label
        {
            Text = "上で取得する種類、下で時刻を決めます。右の既定は初期値なので、変えたあとも残ります。監視を外すと、馬体重や変更の通知は受けません。",
            Location = DialogPx.P(16, 12),
            Size = DialogPx.Z(828, 36),
        };
        var dataLabel = new Label { Text = "取得するデータ", Location = DialogPx.P(16, 52), AutoSize = true };
        _data.Location = DialogPx.P(16, 76);
        _data.Size = DialogPx.Z(828, 220);
        ScheduleGrid.AddColumn(_data, "on", "取得", 70);
        ScheduleGrid.AddText(_data, "name", "種類", 220, readOnly: true);
        ScheduleGrid.AddText(_data, "preset", "既定", 120, readOnly: true, muted: true);
        foreach (var row in SchedulePlan.EditRealtimeData(config))
        {
            var index = _data.Rows.Add(row.Enabled, $"{DataspecCatalog.NameOf(row.Dataspec)}  {row.Dataspec}", ScheduleDefaults.RealtimeDataCaption);
            _data.Rows[index].Tag = row.Dataspec;
        }

        var ruleLabel = new Label { Text = "スケジュール", Location = DialogPx.P(16, 308), AutoSize = true };
        _rules.Location = DialogPx.P(16, 332);
        _rules.Size = DialogPx.Z(828, 240);
        _rules.CellBeginEdit += BeginRuleEdit;
        ScheduleGrid.AddColumn(_rules, "on", "有効", 60);
        ScheduleGrid.AddText(_rules, "title", "内容", 160, readOnly: true);
        ScheduleGrid.AddDays(_rules, ScheduleText.RealtimeDays, 100);
        ScheduleGrid.AddText(_rules, "start", "開始", 70, readOnly: false);
        ScheduleGrid.AddText(_rules, "end", "終了", 70, readOnly: false);
        ScheduleGrid.AddText(_rules, "interval", "間隔", 60, readOnly: false);
        ScheduleGrid.AddText(_rules, "specs", "対象", 180, readOnly: false);
        ScheduleGrid.AddText(_rules, "preset", "既定", 220, readOnly: true, muted: true);
        foreach (var row in SchedulePlan.EditRealtime(config))
        {
            var specs = row.Kind == ScheduleKind.Watch ? "イベントに従う" : ScheduleText.JoinSpecs(row.Dataspecs);
            var interval = row.IntervalMinutes > 0 ? row.IntervalMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
            var index = _rules.Rows.Add(row.Enabled, row.Title, row.Day, row.Start, row.End, interval, specs, ScheduleDefaults.RealtimeCaption(row.Id));
            _rules.Rows[index].Tag = new RuleMark(row.Id, row.Kind, row.Title);
            LockFixedCells(_rules.Rows[index], row.Kind);
        }

        var save = new Button { Text = "保存", Location = DialogPx.P(724, 588), Size = DialogPx.Z(120, 28) };
        save.Click += (_, _) => Save();
        Controls.AddRange([note, dataLabel, _data, ruleLabel, _rules, save]);
    }

    public IReadOnlyList<RealtimeDataRow>? Data { get; private set; }

    public IReadOnlyList<RealtimeScheduleRow>? Rules { get; private set; }

    private void BeginRuleEdit(object? sender, DataGridViewCellCancelEventArgs e)
    {
        if (e.RowIndex < 0 || _rules.Rows[e.RowIndex].Tag is not RuleMark mark)
        {
            return;
        }

        var name = _rules.Columns[e.ColumnIndex].Name;
        var locked = name is "title" or "preset"
            || (name == "day" && mark.Kind != ScheduleKind.ThursdayUpcoming)
            || (name == "end" && mark.Kind is not (ScheduleKind.Watch or ScheduleKind.OddsMeeting or ScheduleKind.OddsFriday))
            || (name == "interval" && mark.Kind is not (ScheduleKind.OddsMeeting or ScheduleKind.OddsFriday))
            || (name == "specs" && mark.Kind == ScheduleKind.Watch);
        e.Cancel = locked;
    }

    private void Save()
    {
        ScheduleGrid.Commit(_data);
        ScheduleGrid.Commit(_rules);
        var data = new List<RealtimeDataRow>();
        foreach (DataGridViewRow gridRow in _data.Rows)
        {
            if (gridRow.Tag is not string code)
            {
                continue;
            }

            var row = new RealtimeDataRow { Dataspec = code, Enabled = ScheduleGrid.IsChecked(gridRow.Cells["on"]) };
            var error = ScheduleText.ValidateRealtimeData(row);
            if (error is not null)
            {
                MessageBox.Show(this, error, "開催日の速報", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            data.Add(row);
        }

        var rules = new List<RealtimeScheduleRow>();
        foreach (DataGridViewRow gridRow in _rules.Rows)
        {
            if (gridRow.Tag is not RuleMark mark)
            {
                continue;
            }

            var intervalText = ScheduleGrid.CellText(gridRow, "interval");
            var interval = 0;
            if (intervalText.Length > 0 && !int.TryParse(intervalText, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out interval))
            {
                MessageBox.Show(this, $"{mark.Title} の間隔は分の整数で入れてください。", "開催日の速報", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var row = new RealtimeScheduleRow
            {
                Id = mark.Id,
                Kind = mark.Kind,
                Enabled = ScheduleGrid.IsChecked(gridRow.Cells["on"]),
                Title = mark.Title,
                Day = ScheduleGrid.CellText(gridRow, "day"),
                Start = ScheduleGrid.CellText(gridRow, "start"),
                End = ScheduleGrid.CellText(gridRow, "end"),
                IntervalMinutes = interval,
                Dataspecs = mark.Kind == ScheduleKind.Watch ? [] : ScheduleText.SplitSpecs(ScheduleGrid.CellText(gridRow, "specs")),
            };
            if (ScheduleText.TryTime(row.Start, out var start))
            {
                row.Start = ScheduleText.Time(start);
            }

            if (ScheduleText.TryTime(row.End, out var end))
            {
                row.End = ScheduleText.Time(end);
            }

            var error = ScheduleText.ValidateRealtimeRule(row);
            if (error is not null)
            {
                MessageBox.Show(this, error, "開催日の速報", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            rules.Add(row);
        }

        Data = data;
        Rules = rules;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void LockFixedCells(DataGridViewRow row, string kind)
    {
        if (kind != ScheduleKind.ThursdayUpcoming)
        {
            Lock(row.Cells["day"]);
        }

        if (kind is not (ScheduleKind.Watch or ScheduleKind.OddsMeeting or ScheduleKind.OddsFriday))
        {
            Lock(row.Cells["end"]);
        }

        if (kind is not (ScheduleKind.OddsMeeting or ScheduleKind.OddsFriday))
        {
            Lock(row.Cells["interval"]);
        }

        if (kind == ScheduleKind.Watch)
        {
            Lock(row.Cells["specs"]);
        }
    }

    private static void Lock(DataGridViewCell cell)
    {
        cell.ReadOnly = true;
        cell.Style.BackColor = SystemColors.Control;
        cell.Style.ForeColor = SystemColors.GrayText;
    }

    private sealed record RuleMark(string Id, string Kind, string Title);
}

file static class ScheduleGrid
{
    public static DataGridView NewGrid()
    {
        var grid = new DataGridView
        {
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = SystemColors.Window,
            BorderStyle = BorderStyle.FixedSingle,
            EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
        };
        grid.DataError += (_, eventArgs) => eventArgs.ThrowException = false;
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty)
            {
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        };
        return grid;
    }

    public static void AddColumn(DataGridView grid, string name, string header, int weight)
    {
        grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = weight,
        });
    }

    public static void AddText(DataGridView grid, string name, string header, int weight, bool readOnly, bool muted = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            FillWeight = weight,
            ReadOnly = readOnly,
        };
        if (muted)
        {
            column.DefaultCellStyle.ForeColor = SystemColors.GrayText;
        }

        grid.Columns.Add(column);
    }

    public static void AddDays(DataGridView grid, string[] days, int weight)
    {
        var column = new DataGridViewComboBoxColumn
        {
            Name = "day",
            HeaderText = "曜日",
            FlatStyle = FlatStyle.Flat,
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            FillWeight = weight,
        };
        column.Items.AddRange(days);
        grid.Columns.Add(column);
    }

    public static void Commit(DataGridView grid)
    {
        grid.EndEdit();
        if (grid.Rows.Count > 0)
        {
            grid.CurrentCell = null;
        }
    }

    public static bool IsChecked(DataGridViewCell cell) => cell.Value is true or CheckState.Checked;

    public static string CellText(DataGridViewRow row, string name) => row.Cells[name].Value?.ToString()?.Trim() ?? "";
}
