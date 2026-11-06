namespace ControllerBridge;

internal sealed partial class MouseUdlrSettingsDialog : Form
{
    private readonly MouseUdlrSettings original;
    private readonly Func<MouseUdlrSettings, MouseUdlrSettings, Task<MouseUdlrSettings>> persist;
    private readonly NumericUpDown threshold = Number(1, 256), hold = Number(1, 512), wait = Number(1, 512),
        holdThreshold = Number(1, 127), inertiaThreshold = Number(1, 65535), inertiaDecay = Number(0, .99m),
        inertiaInterval = Number(1, 1000), rollingGain = Number(.1m, 10m);
    private readonly Label status = Ui.Label("", 9);
    private readonly Button save, cancel;
    private readonly TableLayoutPanel values;
    private bool saving;
    private readonly NumericUpDown[] editors;
    private readonly decimal[] initialValues;
    internal bool Saved { get; private set; }
    private static NumericUpDown Number(decimal min, decimal max) => new() { Minimum = min, Maximum = max, Width = 115, Anchor = AnchorStyles.Left };
    internal MouseUdlrSettingsDialog(MouseUdlrSettings current, Func<MouseUdlrSettings, MouseUdlrSettings, Task<MouseUdlrSettings>> persist)
    {
        original = current; this.persist = persist;
        using IDisposable layout = Ui.Form(this, L.T("共通設定", "Shared settings"), new(640, 700));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 4 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        Label hint = Ui.Label(L.T("全プロファイル共通です。変換先・モード・回転刻みは、各プロファイルの「方向変換の割り当て」で設定します。",
            "Shared by all profiles. Set targets, modes and rotation steps in each profile's Conversion mapping."), 9);
        hint.MaximumSize = new(560, 0); hint.ForeColor = Ui.Muted; root.Controls.Add(hint, 0, 0);
        values = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, Padding = new(0, 10, 0, 0) };
        values.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) values.RowStyles.Add(new(SizeType.AutoSize));
        TableLayoutPanel Group(string title, int row)
        {
            RaisedTableLayoutPanel group = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3, RowCount = 0, Padding = new(12, 8, 12, 8), Margin = new(0, 0, 0, 10), AccessibleName = title };
            group.ColumnStyles.Add(new(SizeType.Percent, 100)); group.ColumnStyles.Add(new(SizeType.AutoSize)); group.ColumnStyles.Add(new(SizeType.Absolute, 80));
            Label heading = Ui.Label(title, 11, true); heading.Margin = new(4, 0, 4, 6);
            group.RowStyles.Add(new(SizeType.AutoSize)); group.Controls.Add(heading, 0, group.RowCount++); group.SetColumnSpan(heading, 3);
            values.Controls.Add(group, 0, row); return group;
        }
        static void Row(TableLayoutPanel group, string label, NumericUpDown control, string unit)
        {
            int row = group.RowCount++; group.RowStyles.Add(new(SizeType.Absolute, 38));
            Label text = Ui.Label(label); text.Anchor = AnchorStyles.Left; control.AccessibleName = label;
            group.Controls.Add(text, 0, row); group.Controls.Add(control, 1, row);
            if (unit.Length != 0) { Label suffix = Ui.Label(unit); suffix.Anchor = AnchorStyles.Left; group.Controls.Add(suffix, 2, row); }
        }
        static void Note(TableLayoutPanel group, string text, bool heading = false)
        {
            Label note = Ui.Label(text, heading ? 10 : 9, heading); note.ForeColor = Ui.Muted; note.MaximumSize = new(540, 0);
            note.Margin = heading ? new(4, 8, 4, 2) : new(4, 0, 4, 4);
            group.RowStyles.Add(new(SizeType.AutoSize)); group.Controls.Add(note, 0, group.RowCount++); group.SetColumnSpan(note, 3);
        }
        TableLayoutPanel clicks = Group(L.T("クリック変換", "Click conversion"), 0);
        Note(clicks, L.T("Trackball: Virtual Rolling ／ Paddle: ボタンクリック", "Trackball: Virtual Rolling / Paddle: Button clicks"));
        Row(clicks, L.T("クリックの移動しきい値", "Click movement threshold"), threshold, "pt");
        Row(clicks, L.T("クリックのホールド時間", "Click hold time"), hold, "ms");
        Row(clicks, L.T("クリックのウェイト時間", "Click wait time"), wait, "ms");
        TableLayoutPanel trackball = Group("Trackball — Hold", 1);
        Row(trackball, L.T("Holdの移動しきい値", "Hold movement threshold"), holdThreshold, "pt");
        Note(trackball, L.T("疑似慣性", "Pseudo-inertia"), heading: true);
        Row(trackball, L.T("疑似慣性の発生値", "Inertia threshold"), inertiaThreshold, "pt/20ms");
        Row(trackball, L.T("疑似慣性の減衰率", "Inertia decay factor"), inertiaDecay, "");
        Row(trackball, L.T("疑似慣性の減衰間隔", "Inertia decay interval"), inertiaInterval, "ms");
        Note(trackball, L.T("減衰率0.90は、減衰間隔ごとに慣性量を90%にします。発生値以下まで減衰すると終了します。\n有効／無効は各プロファイルのHold設定で切り替えます。",
            "A factor of 0.90 retains 90% of inertia each interval. Inertia ends at or below the threshold.\nEnable it in each profile's Hold settings."));
        TableLayoutPanel paddle = Group("Paddle — Virtual Rolling", 2);
        Row(paddle, L.T("回転感度", "Rotation sensitivity"), rollingGain, "°/pt");
        threshold.Value = Math.Clamp(current.Threshold, 1u, 256u); hold.Value = Math.Clamp(current.HoldMs, 1u, 512u); wait.Value = Math.Clamp(current.WaitMs, 1u, 512u);
        holdThreshold.Value = current.TrackballHoldThreshold; inertiaThreshold.Value = current.InertiaThreshold; inertiaInterval.Value = current.InertiaIntervalMs;
        inertiaDecay.DecimalPlaces = 2; inertiaDecay.Increment = .01m; inertiaDecay.Value = current.InertiaDecayPercent / 100m;
        rollingGain.DecimalPlaces = 1; rollingGain.Increment = .1m; rollingGain.Value = current.RollingGain10 / 10m;
        status.MaximumSize = new(560, 0); status.ForeColor = Ui.Muted; status.Visible = false;
        if (threshold.Value != current.Threshold || hold.Value != current.HoldMs || wait.Value != current.WaitMs)
        {
            status.Visible = true;
            status.Text = L.T("旧設定が編集範囲を超えているため、表示値を編集範囲へ収めています。本体への変更は保存時に行います。", "Older values exceed the editing range. Displayed values are clamped; the device changes only when you save.");
        }
        root.Controls.Add(values, 0, 1); root.Controls.Add(status, 0, 2);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        save = Ui.Button(L.T("本体に保存", "Save to device"), async (_, _) => await SaveSettings(), true);
        cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(save); footer.Controls.Add(cancel); root.Controls.Add(footer, 0, 3); AcceptButton = save; CancelButton = cancel;
        editors = [threshold, hold, wait, holdThreshold, inertiaThreshold, inertiaDecay, inertiaInterval, rollingGain];
        initialValues = editors.Select(control => control.Value).ToArray();
        foreach (NumericUpDown control in editors)
        {
            control.ValueChanged += (_, _) => UpdateSaveButton();
            control.TextChanged += (_, _) => UpdateSaveButton();
        }
        UpdateSaveButton();
        FormClosing += (_, e) => { if (saving) e.Cancel = true; }; Controls.Add(root); Ui.ThemeInputs(this);
    }
    private bool HasChanges()
    {
        bool changed = false;
        for (int i = 0; i < editors.Length; i++)
        {
            NumericUpDown control = editors[i];
            // Inspect pending keyboard input without committing or rewriting it.
            if (!decimal.TryParse(control.Text, System.Globalization.NumberStyles.Number,
                System.Globalization.CultureInfo.CurrentCulture, out decimal value)) return false;
            value = Math.Clamp(value, control.Minimum, control.Maximum);
            decimal scale = control.DecimalPlaces == 2 ? 100m : control.DecimalPlaces == 1 ? 10m : 1m;
            changed |= decimal.Truncate(value * scale) / scale != initialValues[i];
        }
        return changed;
    }
    private void UpdateSaveButton() => save.Enabled = !saving && HasChanges();
    private async Task SaveSettings()
    {
        if (saving || !HasChanges()) return;
        bool close = false;
        try
        {
            MouseUdlrSettings next = original with { Threshold = (uint)threshold.Value, HoldMs = (uint)hold.Value, WaitMs = (uint)wait.Value,
                Trackball = (original.Trackball & ~255u) | (uint)holdThreshold.Value, InertiaThreshold = (uint)inertiaThreshold.Value, InertiaDecayPercent = (uint)(inertiaDecay.Value * 100m), InertiaIntervalMs = (uint)inertiaInterval.Value,
                Rolling = (original.Rolling & ~65535u) | (uint)(rollingGain.Value * 10m), Persisted = true };
            saving = true; values.Enabled = save.Enabled = cancel.Enabled = false;
            MouseUdlrSettings actual = await persist(original, next);
            if (actual != next) throw new IOException(L.T("保存結果が一致しません。", "Saved values do not match."));
            Saved = close = true;
        }
        catch (Exception ex) { status.Text = ex.Message; status.Visible = true; }
        finally { saving = false; values.Enabled = cancel.Enabled = true; UpdateSaveButton(); }
        if (close) { DialogResult = DialogResult.OK; Close(); }
    }
}

internal sealed partial class MainForm
{
    private async Task ConfigureMouseUdlr()
    {
        if (working || bridge is null || caps?.SupportsProfileConversion != true) return;
        working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
        try
        {
            MouseUdlrSettingsClient client = new(bridge.RawAsync);
            using MouseUdlrSettingsDialog dialog = new(await client.ReadAsync(), client.SaveAsync);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Saved)
                SetText(saved, L.T("共通設定を保存しました。プロファイルの編集中の内容は保持しています。", "Saved shared settings. Profile drafts are retained."));
        }
        catch (Exception ex) { Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
}
