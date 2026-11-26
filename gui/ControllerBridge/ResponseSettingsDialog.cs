namespace ControllerBridge;

internal sealed partial class MouseUdlrSettingsDialog : Form
{
    private readonly MouseUdlrSettings original;
    private readonly Func<MouseUdlrSettings, MouseUdlrSettings, Task<MouseUdlrSettings>> persist;
    private readonly NumericUpDown threshold = Number(1, 256), hold = Number(1, 512), wait = Number(1, 512),
        holdThreshold = Number(1, 127), inertiaThreshold = Number(1, 65535), inertiaDecay = Number(0, .99m),
        inertiaInterval = Number(1, 1000), rollingGain = Number(.1m, 10m);
    private readonly Label status = Ui.Label("", 9);
    private readonly Button save, cancel, saveFile, restoreFile;
    private readonly TableLayoutPanel values;
    private bool saving;
    private readonly ComboBox displayType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private uint SelectedDisplayType => displayType.SelectedItem is DisplayTypes.Choice choice ? choice.Id : original.DisplayType;
    private readonly DarkRadioButton hostS = new() { Text = "S", AutoSize = true },
        hostPro = new() { Text = "Pro", AutoSize = true },
        hostGc = new() { Text = "GC", AutoSize = true },
        hostAuto = new() { Text = "Auto", AutoSize = true },
        hostX = new() { Text = "X", AutoSize = true };
    internal MouseUdlrSettings Result { get; private set; }
    private readonly NumericUpDown[] editors;
    private readonly decimal[] initialValues;
    internal bool Saved { get; private set; }
    private static NumericUpDown Number(decimal min, decimal max) => new() { Minimum = min, Maximum = max, Width = 115, Anchor = AnchorStyles.Left };
    internal MouseUdlrSettingsDialog(MouseUdlrSettings current, Func<MouseUdlrSettings, MouseUdlrSettings, Task<MouseUdlrSettings>> persist, bool supportsRumble = false)
    {
        original = Result = current; this.persist = persist;
        using IDisposable layout = Ui.Form(this, L.T("共通設定", "Shared settings"), new(680, 960));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        saveFile = Ui.Button(L.T("保存", "Save"), (_, _) => SaveFile());
        restoreFile = Ui.Button(L.T("復帰", "Restore"), (_, _) => RestoreFile());
        saveFile.AccessibleDescription = L.T("画面の共通設定をファイルに保存", "Save the displayed shared settings to a file");
        restoreFile.AccessibleDescription = L.T("ファイルから共通設定を編集画面に復帰", "Restore shared settings from a file into the editor");
        values = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 5, Margin = Padding.Empty, Padding = Padding.Empty };
        values.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) values.RowStyles.Add(new(SizeType.AutoSize));
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
        Note(clicks, L.T("Trackball / Paddle: Virtual Cursor(Repeat)", "Trackball / Paddle: Virtual Cursor(Repeat)"));
        Row(clicks, L.T("クリックの移動しきい値", "Click movement threshold"), threshold, "pt");
        Row(clicks, L.T("クリックのホールド時間", "Click hold time"), hold, "ms");
        Row(clicks, L.T("クリックのウェイト時間", "Click wait time"), wait, "ms");
        TableLayoutPanel trackball = Group("Trackball — Virtual Cursor(Hold)", 1);
        Row(trackball, L.T("Virtual Cursor(Hold)の移動しきい値", "Virtual Cursor(Hold) threshold"), holdThreshold, "pt");
        Note(trackball, L.T("疑似慣性", "Pseudo-inertia"), heading: true);
        Row(trackball, L.T("疑似慣性の発生値", "Inertia threshold"), inertiaThreshold, "pt/20ms");
        Row(trackball, L.T("疑似慣性の減衰率", "Inertia decay factor"), inertiaDecay, "");
        Row(trackball, L.T("疑似慣性の減衰間隔", "Inertia decay interval"), inertiaInterval, "ms");
        Note(trackball, L.T("減衰率0.90は、減衰間隔ごとに慣性量を90%にします。発生値以下まで減衰すると終了します。\n有効／無効は各プロファイルのVirtual Cursor(Hold)設定で切り替えます。",
            "A factor of 0.90 retains 90% of inertia each interval. Inertia ends at or below the threshold.\nEnable it in each profile's Virtual Cursor(Hold) settings."));
        TableLayoutPanel paddle = Group("Paddle — Virtual Rolling", 2);
        Row(paddle, L.T("回転感度", "Rotation sensitivity"), rollingGain, "°/pt");
        TableLayoutPanel host = Group(L.T("出力モード", "Host mode"), 3);
        FlowLayoutPanel modes = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new(4, 2, 4, 6) };
        foreach (RadioButton radio in new[] { hostS, hostAuto, hostX, hostPro, hostGc }) { radio.Margin = new(4, 4, 24, 4); modes.Controls.Add(radio); }
        hostPro.Enabled = supportsRumble; hostPro.AccessibleName = L.T("Pro：Switch Pro互換・振動対応", "Pro: Switch Pro compatible with rumble");
        hostPro.Checked = current.HostMode == 3; hostGc.Checked = current.HostMode == 4;
        hostGc.AccessibleName = L.T("GC：GameCube接続タップ互換・アナログL/R", "GC: GameCube adapter with analog L/R");
        hostS.AccessibleName = L.T("S：Switch互換", "S: Switch compatible");
        hostAuto.AccessibleName = L.T("Auto：Xを優先し、不成立ならSへ一度だけ切替", "Auto: try X first, then S once if needed");
        hostX.AccessibleName = L.T("X : Xbox360互換", "X: Xbox 360 XInput");
        hostS.Checked = current.HostMode == 0; hostX.Checked = current.HostMode == 1; hostAuto.Checked = current.HostMode == 2;
        host.RowStyles.Add(new(SizeType.AutoSize)); host.Controls.Add(modes, 0, host.RowCount++); host.SetColumnSpan(modes, 3);
        Note(host, L.T("S：Switch互換出力　X : Xbox360互換\nAuto：Xを優先し、5秒で成立しなければSへ一度だけ切替。\nPro：初代Pro・Switch 2 Pro・DS3への振動転送に対応。DS3はXでも振動を利用できます。\n各プロファイルで振動を有効にしてください。マウスを含むプロファイルはSで動作します。\nGC：GameCube接続タップ互換。独立したアナログL/Rを設定できます。\nモードの変更を保存するとUSBが再接続されます。",
            "S: Switch compatible output   X: Xbox 360 XInput output\nAuto: try X for 5 seconds, then switch to S once if needed.\nPro: rumble to original Pro, Switch 2 Pro and DS3; DS3 also supports rumble with X.\nEnable rumble per profile. Profiles with mouse output use S.\nGC: GameCube adapter with independent analog L/R assignments.\nSaving a mode change reconnects USB."));
        // Keep this entry hidden until PS4 console connection is implemented.
        // Button ps4Files = Ui.Button(L.T("PS4認証ファイル（準備）…", "PS4 credential files (preparation)…"), (_, _) =>
        // {
        //     using Ps4CredentialsDialog dialog = new(); dialog.ShowDialog(this);
        // });
        // ps4Files.Anchor = AnchorStyles.Left;
        // host.RowStyles.Add(new(SizeType.AutoSize)); host.Controls.Add(ps4Files, 0, host.RowCount++); host.SetColumnSpan(ps4Files, 3);
        TableLayoutPanel display = Group(L.T("ディスプレイ", "Display"), 4);
        displayType.AccessibleName = L.T("表示タイプ", "Display type");
        displayType.Items.AddRange(DisplayTypes.Choices);
        displayType.SelectedItem = displayType.Items.Cast<DisplayTypes.Choice>().Single(item => item.Id == current.DisplayType);
        display.RowStyles.Add(new(SizeType.AutoSize)); display.Controls.Add(displayType, 0, display.RowCount++); display.SetColumnSpan(displayType, 3);
        threshold.Value = Math.Clamp(current.Threshold, 1u, 256u); hold.Value = Math.Clamp(current.HoldMs, 1u, 512u); wait.Value = Math.Clamp(current.WaitMs, 1u, 512u);
        holdThreshold.Value = current.TrackballHoldThreshold; inertiaThreshold.Value = current.InertiaThreshold; inertiaInterval.Value = current.InertiaIntervalMs;
        inertiaDecay.DecimalPlaces = 2; inertiaDecay.Increment = .01m; inertiaDecay.Value = current.InertiaDecayPercent / 100m;
        rollingGain.DecimalPlaces = 1; rollingGain.Increment = .1m; rollingGain.Value = current.RollingGain10 / 10m;
        status.MaximumSize = new(560, 0); status.ForeColor = Ui.Muted; status.Visible = false;
        if (threshold.Value != current.Threshold || hold.Value != current.HoldMs || wait.Value != current.WaitMs)
        {
            status.Visible = true;
            status.Text = L.T("旧設定が編集範囲を超えているため、表示値を編集範囲へ収めています。デバイスへの変更は保存時に行います。", "Older values exceed the editing range. Displayed values are clamped; the device changes only when you save.");
        }
        root.Controls.Add(values, 0, 0); root.Controls.Add(status, 0, 1);
        TableLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 5, RowCount = 1, Margin = Padding.Empty };
        footer.ColumnStyles.Add(new(SizeType.AutoSize)); footer.ColumnStyles.Add(new(SizeType.AutoSize));
        footer.ColumnStyles.Add(new(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new(SizeType.AutoSize)); footer.ColumnStyles.Add(new(SizeType.AutoSize));
        footer.RowStyles.Add(new(SizeType.AutoSize));
        save = Ui.Button(L.T("デバイスに保存", "Save to device"), async (_, _) => await SaveSettings(), true);
        cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        foreach (Button button in new[] { saveFile, restoreFile, cancel, save }) button.Dock = DockStyle.Fill;
        footer.Controls.Add(saveFile, 0, 0); footer.Controls.Add(restoreFile, 1, 0);
        footer.Controls.Add(cancel, 3, 0); footer.Controls.Add(save, 4, 0);
        root.Controls.Add(footer, 0, 2); AcceptButton = save; CancelButton = cancel;
        editors = [threshold, hold, wait, holdThreshold, inertiaThreshold, inertiaDecay, inertiaInterval, rollingGain];
        initialValues = editors.Select(control => control.Value).ToArray();
        foreach (NumericUpDown control in editors)
        {
            control.ValueChanged += (_, _) => UpdateSaveButton();
            control.TextChanged += (_, _) => UpdateSaveButton();
        }
        displayType.SelectedIndexChanged += (_, _) => UpdateSaveButton();
        hostGc.CheckedChanged += (_, _) => UpdateSaveButton();
        hostPro.CheckedChanged += (_, _) => UpdateSaveButton();
        hostS.CheckedChanged += (_, _) => UpdateSaveButton(); hostX.CheckedChanged += (_, _) => UpdateSaveButton(); hostAuto.CheckedChanged += (_, _) => UpdateSaveButton();
        UpdateSaveButton();
        FormClosing += (_, e) => { if (saving) e.Cancel = true; }; Controls.Add(root); Ui.ThemeInputs(this);
    }
    private MouseUdlrSettings ReadEditors()
    {
        ValidateChildren();
        return original with { Threshold = (uint)threshold.Value, HoldMs = (uint)hold.Value, WaitMs = (uint)wait.Value,
            Trackball = (original.Trackball & ~255u) | (uint)holdThreshold.Value, InertiaThreshold = (uint)inertiaThreshold.Value,
            InertiaDecayPercent = (uint)(inertiaDecay.Value * 100m), InertiaIntervalMs = (uint)inertiaInterval.Value,
            Rolling = (original.Rolling & ~65535u) | (uint)(rollingGain.Value * 10m), DisplayType = SelectedDisplayType,
            HostMode = hostGc.Checked ? 4u : hostPro.Checked ? 3u : hostAuto.Checked ? 2u : hostX.Checked ? 1u : 0u, Persisted = true };
    }
    private void SaveFile()
    {
        if (saving) return;
        using SaveFileDialog dialog = new() { Filter = SharedSettingsFile.Filter, DefaultExt = "json", AddExtension = true,
            OverwritePrompt = true, Title = L.T("共通設定を保存", "Save shared settings"),
            FileName = $"ControllerBridge-shared-{DateTime.Now:yyyyMMdd-HHmmss}.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SharedSettingsFile.Save(dialog.FileName, ReadEditors());
            status.Text = L.T("画面の共通設定をファイルに保存しました。", "Saved the displayed shared settings to a file.");
            status.Visible = true;
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    private void RestoreFile()
    {
        if (saving) return;
        using OpenFileDialog dialog = new() { Filter = SharedSettingsFile.Filter, Title = L.T("共通設定を復帰", "Restore shared settings") };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            // Validate the complete file before changing any editor. Hidden legacy
            // conversion fields come from this device, never from a settings file.
            MouseUdlrSettings loaded = SharedSettingsFile.Load(dialog.FileName, original, hostPro.Enabled);
            threshold.Value = loaded.Threshold; hold.Value = loaded.HoldMs; wait.Value = loaded.WaitMs;
            holdThreshold.Value = loaded.TrackballHoldThreshold; inertiaThreshold.Value = loaded.InertiaThreshold;
            inertiaDecay.Value = loaded.InertiaDecayPercent / 100m; inertiaInterval.Value = loaded.InertiaIntervalMs;
            rollingGain.Value = loaded.RollingGain10 / 10m;
            hostS.Checked = loaded.HostMode == 0; hostX.Checked = loaded.HostMode == 1;
            hostAuto.Checked = loaded.HostMode == 2; hostPro.Checked = loaded.HostMode == 3; hostGc.Checked = loaded.HostMode == 4;
            displayType.SelectedItem = displayType.Items.Cast<DisplayTypes.Choice>().Single(item => item.Id == loaded.DisplayType);
            UpdateSaveButton();
            status.Text = L.T("共通設定を画面に復帰しました。「デバイスに保存」で反映します。", "Restored shared settings into the editor. Choose “Save to device” to apply them.");
            status.Visible = true;
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    private bool HasChanges()
    {
        bool changed = (hostGc.Checked ? 4u : hostPro.Checked ? 3u : hostAuto.Checked ? 2u : hostX.Checked ? 1u : 0u) != original.HostMode || SelectedDisplayType != original.DisplayType;
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
            MouseUdlrSettings next = ReadEditors();
            saving = true; values.Enabled = save.Enabled = cancel.Enabled = saveFile.Enabled = restoreFile.Enabled = false;
            MouseUdlrSettings actual = await persist(original, next);
            if (actual != next) throw new IOException(L.T("保存結果が一致しません。", "Saved values do not match."));
            Result = actual; Saved = close = true;
        }
        catch (Exception ex) { status.Text = ex.Message; status.Visible = true; }
        finally { saving = false; values.Enabled = cancel.Enabled = saveFile.Enabled = restoreFile.Enabled = true; UpdateSaveButton(); }
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
            using MouseUdlrSettingsDialog dialog = new(await client.ReadAsync(), client.SaveAsync, caps.SupportsRumble);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Saved)
            {
                // Save/readback is complete before asking the device to detach.
                await bridge.ApplyHostOutputAsync();
                await RefreshDevice(); BindSelected();
                SetText(saved, L.T("共通設定を保存しました。プロファイルの編集中の内容は保持しています。", "Saved shared settings. Profile drafts are retained."));
            }
        }
        catch (Exception ex) { Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
}
