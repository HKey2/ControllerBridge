namespace ControllerBridge;

internal sealed class ConversionSettingsDialog : Form
{
    internal ConversionSettings Result { get; private set; }
    private readonly ComboBox trackballMode = Choice(), paddleMode = Choice(), rollingStep = Choice(), rollingStick = Choice();
    private readonly ComboBox[] targets = Enumerable.Range(0, 6).Select(_ => Choice()).ToArray();
    private readonly CheckBox rollingInvert = new DarkCheckBox { Text = L.T("回転方向を反転", "Reverse rotation"), AutoSize = true };
    private readonly CheckBox inertia = new DarkCheckBox { Text = L.T("疑似慣性を有効にする", "Enable pseudo-inertia"), AutoSize = true };
    private static ComboBox Choice() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Anchor = AnchorStyles.Left };
    internal ConversionSettingsDialog(DeviceProfile profile)
    {
        Result = profile.Conversion.Clone(); Result.Validate();
        using IDisposable layout = Ui.Form(this, L.T("方向変換の割り当て", "Conversion mapping"), new(580, 610));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 4 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        Label title = Ui.Label(profile.Name, 12, true); title.MaximumSize = new(520, 0); root.Controls.Add(title, 0, 0);
        DarkTabControl tabs = new() { Dock = DockStyle.Fill, Padding = new(20, 10) }; root.Controls.Add(tabs, 0, 1);
        TableLayoutPanel Page(string name)
        {
            TabPage page = new(name) { BackColor = Ui.Surface, Padding = new(14) }; tabs.TabPages.Add(page);
            TableLayoutPanel grid = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
            grid.ColumnStyles.Add(new(SizeType.Percent, 100)); grid.ColumnStyles.Add(new(SizeType.AutoSize)); page.Controls.Add(grid); return grid;
        }
        static void Row(TableLayoutPanel grid, string label, Control control)
        {
            int row = grid.RowCount++; grid.RowStyles.Add(new(SizeType.Absolute, 48));
            Label text = Ui.Label(label); text.Anchor = AnchorStyles.Left; control.Margin = new(4, 8, 4, 8);
            control.AccessibleName = label; grid.Controls.Add(text, 0, row); grid.Controls.Add(control, 1, row);
        }
        TableLayoutPanel ball = Page("Trackball"), paddle = Page("Paddle");
        trackballMode.Items.AddRange(["Virtual Cursor", "Hold"]); trackballMode.SelectedIndex = Result.TrackballMode;
        paddleMode.Items.AddRange([L.T("ボタンクリック", "Button clicks"), "Virtual Rolling"]); paddleMode.SelectedIndex = Result.PaddleMode;
        Row(ball, L.T("変換モード", "Conversion mode"), trackballMode);
        inertia.Checked = Result.InertiaEnabled; Row(ball, L.T("疑似慣性", "Pseudo-inertia"), inertia);
        Row(paddle, L.T("変換モード", "Conversion mode"), paddleMode);
        string[] labels = ["↑", "↓", "←", "→", L.T("負方向", "Negative direction"), L.T("正方向", "Positive direction")];
        for (int i = 0; i < targets.Length; i++)
        {
            targets[i].Items.AddRange(DeviceProfile.Targets.Cast<object>().ToArray());
            targets[i].SelectedIndex = (i < 4 ? Result.TrackballTargets[i] : Result.PaddleTargets[i - 4]) - 1;
            Row(i < 4 ? ball : paddle, labels[i], targets[i]);
        }
        rollingStep.Items.AddRange(ConversionSettings.Steps.Cast<object>().ToArray()); rollingStep.SelectedItem = (int)Result.RollingStepDeg;
        rollingStick.Items.AddRange([L.T("右スティック", "Right stick"), L.T("左スティック", "Left stick")]); rollingStick.SelectedIndex = Result.RollingLeft ? 1 : 0;
        rollingInvert.Checked = Result.RollingInvert;
        Row(paddle, L.T("回転刻み (°)", "Rotation step (°)"), rollingStep); Row(paddle, L.T("出力先", "Output stick"), rollingStick);
        Row(paddle, L.T("回転方向", "Rotation direction"), rollingInvert);
        void UpdateMode()
        {
            inertia.Enabled = trackballMode.SelectedIndex == 1;
            bool rolling = paddleMode.SelectedIndex == 1;
            targets[4].Enabled = targets[5].Enabled = !rolling;
            rollingStep.Enabled = rollingStick.Enabled = rollingInvert.Enabled = rolling;
        }
        paddleMode.SelectedIndexChanged += (_, _) => UpdateMode(); trackballMode.SelectedIndexChanged += (_, _) => UpdateMode(); UpdateMode();
        Label notice = Ui.Label(L.T("しきい値・時間・感度は「共通設定」で調整します。\nUSBマウス単独では、設定を保持したまま方向変換を停止します。",
            "Adjust thresholds, timings and sensitivity in Shared settings.\nUSB mouse-only output retains these preferences without running conversion."), 9);
        notice.MaximumSize = new(520, 0); notice.ForeColor = Ui.Muted; root.Controls.Add(notice, 0, 2);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        bool HasChanges() => trackballMode.SelectedIndex != Result.TrackballMode || paddleMode.SelectedIndex != Result.PaddleMode ||
            inertia.Checked != Result.InertiaEnabled || rollingInvert.Checked != Result.RollingInvert ||
            (rollingStick.SelectedIndex == 1) != Result.RollingLeft || (int)rollingStep.SelectedItem! != Result.RollingStepDeg ||
            targets.Where((control, i) => control.SelectedIndex + 1 != (i < 4 ? Result.TrackballTargets[i] : Result.PaddleTargets[i - 4])).Any();
        Button ok = Ui.Button(L.T("編集に反映", "Apply to draft"), (_, _) => {
            if (!HasChanges()) return;
            Result = new() { TrackballTargets = targets.Take(4).Select(c => (byte)(c.SelectedIndex + 1)).ToArray(),
                PaddleTargets = targets.Skip(4).Select(c => (byte)(c.SelectedIndex + 1)).ToArray(), TrackballMode = (byte)trackballMode.SelectedIndex, InertiaEnabled = inertia.Checked,
                PaddleMode = (byte)paddleMode.SelectedIndex, RollingStepDeg = (byte)(int)rollingStep.SelectedItem!, RollingLeft = rollingStick.SelectedIndex == 1, RollingInvert = rollingInvert.Checked };
            Result.Validate(); DialogResult = DialogResult.OK; Close();
        }, true);
        void UpdateApplyButton() => ok.Enabled = HasChanges();
        foreach (ComboBox control in targets.Concat([trackballMode, paddleMode, rollingStep, rollingStick]))
            control.SelectedIndexChanged += (_, _) => UpdateApplyButton();
        inertia.CheckedChanged += (_, _) => UpdateApplyButton();
        rollingInvert.CheckedChanged += (_, _) => UpdateApplyButton();
        UpdateApplyButton();
        Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(ok); footer.Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel; root.Controls.Add(footer, 0, 3);
        Controls.Add(root); Ui.ThemeInputs(this);
    }
}

internal sealed partial class MainForm
{
    private void ConfigureConversion()
    {
        if (working || !profiles[selected].Enabled) return;
        AbortLearning(); using ConversionSettingsDialog dialog = new(profiles[selected]);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        DeviceProfile before = profiles[selected].Clone(); profiles[selected].Conversion = dialog.Result;
        if (!profiles[selected].SameContent(before)) MarkDirty();
        SetText(saved, L.T("方向変換の編集を保持しています。プロファイルをデバイスに保存すると反映されます。", "Conversion edits are retained. Save the profile to device to apply them."));
    }
    private static string LegacyImportNotice(bool fromDevice) =>
        L.T("旧形式を変換しました。4個目のシフトキーがある場合は削除し、先頭3個を保持します。\n", "Converted the older format. A fourth shift key is removed; the first three are retained.\n") +
        (fromDevice ? L.T("旧ファイルに含まれない方向変換設定は、このデバイスに残る移行用設定で補完しました。保存前に割り当てと回転刻みを確認してください。",
            "Conversion settings missing from the old file use this device's retained migration defaults. Check mappings and rotation steps before saving.") :
            L.T("旧ファイルに含まれない方向変換設定は標準値で補完しました。保存前に割り当てと回転刻みを確認してください。",
            "Conversion settings missing from the old file use standard defaults. Check mappings and rotation steps before saving."));
}
