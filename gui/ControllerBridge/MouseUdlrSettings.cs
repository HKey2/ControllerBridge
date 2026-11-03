namespace ControllerBridge;

internal sealed record MouseUdlrSettings(uint Threshold, uint HoldMs, uint WaitMs, bool Persisted, uint ButtonMap = 0x1012110F, uint PaddleButtonMap = 0x1012110F, uint Rolling = 10, uint Trackball = 1, uint RollingStepDeg = 30)
{
    internal const uint DefaultButtonMap = 0x1012110F; // Explicit Switch Up/Down/Left/Right targets.
    internal bool VirtualRolling => ((Rolling >> 16) & 255) == 1;
    internal uint RollingGain10 => Rolling & 65535;
    internal bool RollingInvert => (Rolling & 0x01000000) != 0;
    internal bool RollingLeft => (Rolling & 0x02000000) != 0;
    internal static void ValidateRolling(uint value)
    {
        if ((value & 65535) is < 1 or > 100 || ((value >> 16) & 255) > 1 || (value >> 24) > 3)
            throw new InvalidDataException(L.T("Virtual Rollingの設定が不正です。", "Invalid Virtual Rolling settings."));
    }
    internal bool TrackballHold => ((Trackball >> 8) & 255) == 1;
    internal uint TrackballHoldThreshold => Trackball & 255;
    internal static void ValidateTrackball(uint value)
    {
        if ((value & 255) is < 1 or > 127 || (value >> 8) > 1)
            throw new InvalidDataException(L.T("Holdの移動しきい値は1～127ptで指定してください。", "Set the Hold movement threshold to 1–127 pt."));
    }
    internal static void ValidateRollingStep(uint value)
    {
        if (value is not (5 or 10 or 15 or 20 or 30 or 40 or 45 or 60 or 90))
            throw new InvalidDataException(L.T("回転刻みは5・10・15・20・30・40・45・60・90°から選択してください。",
                "Select a rotation step of 5, 10, 15, 20, 30, 40, 45, 60 or 90 degrees."));
    }
    internal uint ModeOptions
    {
        get
        {
            ValidateRolling(Rolling); ValidateTrackball(Trackball); ValidateRollingStep(RollingStepDeg);
            return RollingGain10 | ((Rolling >> 16) & 1) << 8 | (Rolling >> 24) << 9 |
                (Trackball >> 8) << 11 | TrackballHoldThreshold << 16 | RollingStepDeg << 24;
        }
    }
    internal MouseUdlrSettings WithModeOptions(uint options)
    {
        if ((options & 0x0000F000u) != 0)
            throw new InvalidDataException(L.T("マウス UDLRのモード設定が不正です。", "Invalid Mouse UDLR mode settings."));
        uint rolling = (options & 255) | ((options >> 8) & 1) << 16 | ((options >> 9) & 3) << 24;
        uint trackball = ((options >> 16) & 255) | ((options >> 11) & 1) << 8;
        uint step = options >> 24;
        ValidateRolling(rolling); ValidateTrackball(trackball); ValidateRollingStep(step);
        return this with { Rolling = rolling, Trackball = trackball, RollingStepDeg = step };
    }
    internal static MouseUdlrSettings Defaults => new(64, 30, 20, true);
    internal static void ValidateEdit(uint threshold, uint holdMs, uint waitMs)
    {
        if (threshold is < 1 or > 256 || holdMs is < 1 or > 512 || waitMs is < 1 or > 512)
            throw new InvalidDataException(L.T("移動しきい値は1～256pt、ホールド時間とウェイト時間は1～512msで指定してください。",
                "Set movement threshold to 1–256 pt and hold/wait times to 1–512 ms."));
    }
    internal static void ValidateTargets(uint buttonMap)
    {
        for (int direction = 0; direction < 4; direction++)
            if (Target(buttonMap, direction) > 18)
                throw new InvalidDataException(L.T("マウス UDLRの割り当てが不正です。", "Invalid Mouse UDLR assignment."));
    }
    internal static byte Target(uint buttonMap, int direction) => (byte)(buttonMap >> (8 * direction));
    internal static uint Assign(uint buttonMap, int direction, byte target)
    {
        if (direction is < 0 or > 3 || target > 18) throw new ArgumentOutOfRangeException();
        int shift = 8 * direction;
        return (buttonMap & ~(255u << shift)) | ((uint)target << shift);
    }
    internal static MouseUdlrSettings Read(byte[] data)
    {
        if (data.Length != 56 || !data.AsSpan(0, 4).SequenceEqual("UDS7"u8) || Wire.U32(data, 4) != 7)
            throw new InvalidDataException(L.T("マウス UDLR設定を読み込めません。本体のファームウェアを更新してください。",
                "Cannot read Mouse UDLR settings. Update the device firmware."));
        uint threshold = Wire.U32(data, 8), hold = Wire.U32(data, 12), wait = Wire.U32(data, 16), flags = Wire.U32(data, 20);
        uint buttonMap = Wire.U32(data, 24), paddleButtonMap = Wire.U32(data, 28); ValidateTargets(buttonMap); ValidateTargets(paddleButtonMap);
        // Preserve valid older durations until an explicit edit is saved.
        if (threshold is < 1 or > 65535 || hold is < 1 or > 1000 || wait is < 1 or > 1000 || flags > 1)
            throw new InvalidDataException(L.T("本体から受信したマウス UDLR設定が不正です。", "The device returned invalid Mouse UDLR settings."));
        return new MouseUdlrSettings(threshold, hold, wait, flags == 1, buttonMap, paddleButtonMap).WithModeOptions(Wire.U32(data, 32));
    }
}

internal sealed class MouseUdlrSettingsClient(Func<byte, byte[]?, Task<byte[]>> command)
{
    internal async Task<MouseUdlrSettings> ReadAsync() => MouseUdlrSettings.Read(await command(52, null));
    internal async Task<MouseUdlrSettings> SaveAsync(MouseUdlrSettings original, MouseUdlrSettings edited)
    {
        MouseUdlrSettings.ValidateEdit(edited.Threshold, edited.HoldMs, edited.WaitMs);
        MouseUdlrSettings.ValidateTargets(original.ButtonMap); MouseUdlrSettings.ValidateTargets(edited.ButtonMap);
        MouseUdlrSettings.ValidateTargets(original.PaddleButtonMap); MouseUdlrSettings.ValidateTargets(edited.PaddleButtonMap);
        MouseUdlrSettings.ValidateRolling(original.Rolling); MouseUdlrSettings.ValidateRolling(edited.Rolling);
        MouseUdlrSettings.ValidateTrackball(original.Trackball); MouseUdlrSettings.ValidateTrackball(edited.Trackball);
        MouseUdlrSettings.ValidateRollingStep(original.RollingStepDeg); MouseUdlrSettings.ValidateRollingStep(edited.RollingStepDeg);
        byte[] args = new byte[56]; "UDS7"u8.CopyTo(args); Wire.W32(args, 4, 7);
        Wire.W32(args, 8, original.Threshold); Wire.W32(args, 12, original.HoldMs); Wire.W32(args, 16, original.WaitMs);
        Wire.W32(args, 20, edited.Threshold); Wire.W32(args, 24, edited.HoldMs); Wire.W32(args, 28, edited.WaitMs);
        Wire.W32(args, 32, original.ButtonMap); Wire.W32(args, 36, edited.ButtonMap);
        Wire.W32(args, 40, original.PaddleButtonMap); Wire.W32(args, 44, edited.PaddleButtonMap);
        Wire.W32(args, 48, original.ModeOptions); Wire.W32(args, 52, edited.ModeOptions);
        MouseUdlrSettings reply = MouseUdlrSettings.Read(await command(53, args));
        Verify(reply, edited);
        MouseUdlrSettings readback = await ReadAsync(); Verify(readback, edited); return readback;
    }
    private static void Verify(MouseUdlrSettings actual, MouseUdlrSettings expected)
    {
        if (!actual.Persisted || actual.Threshold != expected.Threshold || actual.HoldMs != expected.HoldMs ||
            actual.WaitMs != expected.WaitMs || actual.ButtonMap != expected.ButtonMap || actual.PaddleButtonMap != expected.PaddleButtonMap || actual.Rolling != expected.Rolling || actual.Trackball != expected.Trackball || actual.RollingStepDeg != expected.RollingStepDeg)
            throw new IOException(L.T("本体への保存結果を確認できませんでした。値を読み直して確認してください。",
                "Could not verify the saved settings. Read the values from the device again."));
    }
}

internal sealed partial class MouseUdlrSettingsDialog : Form
{
    private readonly NumericUpDown threshold = new() { Minimum = 1, Maximum = 256, Value = 64, Width = 90, Anchor = AnchorStyles.Left,
        AccessibleName = L.T("移動しきい値", "Movement threshold") };
    private readonly NumericUpDown hold = new() { Minimum = 1, Maximum = 512, Value = MouseUdlrSettings.Defaults.HoldMs, Width = 90, Anchor = AnchorStyles.Left,
        AccessibleName = L.T("ホールド時間", "Hold time") };
    private readonly NumericUpDown wait = new() { Minimum = 1, Maximum = 512, Value = MouseUdlrSettings.Defaults.WaitMs, Width = 90, Anchor = AnchorStyles.Left,
        AccessibleName = L.T("ウェイト時間", "Wait time") };
    private static readonly int[] DirectionOutputs = [14, 16, 17, 15]; // U/D/L/R.
    private sealed record TargetChoice(byte Target, string Name)
    {
        public override string ToString() => Name;
    }
    // Switch outputs only. Keep the encoded output IDs independent of list order.
    private static readonly TargetChoice[] TargetChoices = new byte[] { 15, 17, 18, 16, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14 }
        .Select(target => new TargetChoice(target, DeviceProfile.Targets[target - 1])).ToArray();
    private readonly ComboBox[] directionChoices = new ComboBox[8];
    private readonly Button[] resetTargets = new Button[2];
    private readonly uint[] buttonMaps = new uint[2];
    private readonly DarkTabControl sourceTabs = new() { Dock = DockStyle.Fill, Padding = new(18, 6), Margin = new(0, 5, 0, 5) };
    private readonly ComboBox trackballMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Anchor = AnchorStyles.Left,
        AccessibleName = L.T("トラックボールモード", "Trackball mode") };
    private readonly NumericUpDown trackballHoldThreshold = new() { Minimum = 1, Maximum = 127, Value = 1, Width = 90, Anchor = AnchorStyles.Left,
        AccessibleName = L.T("Holdの移動しきい値", "Hold movement threshold") };
    private TableLayoutPanel clickValues = null!, trackballHoldRow = null!;
    private readonly ComboBox paddleMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Anchor = AnchorStyles.Left,
        AccessibleName = L.T("パドルモード", "Paddle mode") };
    private readonly NumericUpDown rollingGain = new() { Minimum = 0.1m, Maximum = 10.0m, DecimalPlaces = 1, Increment = 0.1m,
        Value = 1.0m, Width = 90, Anchor = AnchorStyles.Left, AccessibleName = L.T("回転感度", "Rotation sensitivity") };
    private static readonly int[] RollingSteps = [5, 10, 15, 20, 30, 40, 45, 60, 90];
    private readonly ComboBox rollingStep = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90, Anchor = AnchorStyles.Right,
        AccessibleName = L.T("回転刻み", "Rotation step") };
    private readonly ComboBox rollingStick = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, Anchor = AnchorStyles.Left,
        AccessibleName = L.T("出力先アナログスティック", "Output analog stick") };
    private readonly CheckBox rollingInvert = new ModeCheckBox() { Text = L.T("回転方向を反転", "Reverse rotation"), AutoSize = true, Anchor = AnchorStyles.Left };
    private TableLayoutPanel paddleDirections = null!, rollingPanel = null!;
    private readonly Label status = ModeText("", 9);
    private readonly Button save, cancel;
    private readonly MouseUdlrSettings original;
    private readonly Func<MouseUdlrSettings, MouseUdlrSettings, Task<MouseUdlrSettings>> persist;
    private bool saving;
    internal bool Saved { get; private set; }

    internal MouseUdlrSettingsDialog(MouseUdlrSettings current, Func<MouseUdlrSettings, MouseUdlrSettings, Task<MouseUdlrSettings>> persist)
    {
        MouseUdlrSettings.ValidateTargets(current.ButtonMap); MouseUdlrSettings.ValidateTargets(current.PaddleButtonMap);
        MouseUdlrSettings.ValidateRolling(current.Rolling); MouseUdlrSettings.ValidateTrackball(current.Trackball); MouseUdlrSettings.ValidateRollingStep(current.RollingStepDeg);
        original = current; buttonMaps[0] = current.ButtonMap; buttonMaps[1] = current.PaddleButtonMap; this.persist = persist;
        using IDisposable layout = Ui.Form(this, L.T("マウス UDLR設定", "Mouse UDLR settings"), new(560, 840));
        MaximizeBox = false; MinimizeBox = false;
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 6 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) root.RowStyles.Add(new(i == 4 ? SizeType.Percent : SizeType.AutoSize, i == 4 ? 100 : 0));
        root.Controls.Add(ModeText(L.T("マウス UDLR設定", "Mouse UDLR settings"), 18, true), 0, 0);
        root.Controls.Add(ModeText(L.T("ポインタ移動をゲームパッド操作に変換します。\nすべてのプロファイルで共通です。",
            "Convert pointer movement to gamepad input.\nShared by all profiles.")), 0, 1);
        clickValues = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 3, Padding = new(0, 14, 0, 10) };
        TableLayoutPanel values = clickValues;
        values.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 2; i++) values.ColumnStyles.Add(new(SizeType.AutoSize));
        for (int row = 0; row < 3; row++) values.RowStyles.Add(new(SizeType.AutoSize));
        void AddRow(int row, string label, NumericUpDown input, string unit)
        {
            Label name = ModeText(label, 11); name.Anchor = AnchorStyles.Left;
            Label units = ModeText(unit); units.Anchor = AnchorStyles.Left; units.Margin = new(0, 3, 0, 3);
            input.Margin = new(6, 7, 0, 7); name.Margin = new(4, 7, 4, 7);
            values.Controls.Add(name, 0, row); values.Controls.Add(input, 1, row); values.Controls.Add(units, 2, row);
        }
        AddRow(0, L.T("移動しきい値", "Movement threshold"), threshold, "pt");
        AddRow(1, L.T("ホールド時間", "Hold time"), hold, "ms");
        AddRow(2, L.T("ウェイト時間", "Wait time"), wait, "ms");
        root.Controls.Add(values, 0, 2);
        // Reserve both mode panes once. Changing mode only changes Enabled state.
        root.RowStyles[3] = new(SizeType.Absolute, 470);
        for (int source = 0; source < 2; source++)
        {
            int sourceIndex = source;
            TabPage page = new(source == 0 ? L.T("トラックボール", "Trackball") : L.T("パドル", "Paddle")) { Padding = new(2), BackColor = Ui.Surface, ForeColor = Ui.Ink };
            sourceTabs.TabPages.Add(page);
            TableLayoutPanel directions = new() { Anchor = AnchorStyles.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 3, RowCount = 3, Margin = new(0, 5, 0, 5), GrowStyle = TableLayoutPanelGrowStyle.FixedSize };
            for (int i = 0; i < 3; i++) { directions.ColumnStyles.Add(new(SizeType.Absolute, 156)); directions.RowStyles.Add(new(SizeType.Absolute, 48)); }
            int[,] cells = { { 1, 0 }, { 1, 2 }, { 0, 1 }, { 2, 1 } };
            for (int i = 0; i < 4; i++)
            {
                int direction = i;
                ComboBox choice = new() { DropDownStyle = ComboBoxStyle.DropDownList, Anchor = AnchorStyles.Left | AnchorStyles.Right,
                    Width = 148, Margin = new(4), MaxDropDownItems = 18,
                    AccessibleName = DeviceProfile.Targets[DirectionOutputs[i]] + L.T(" 送信先", " output target") };
                choice.Items.AddRange(TargetChoices);
                choice.SelectedIndexChanged += (_, _) =>
                {
                    if (!saving && choice.SelectedItem is TargetChoice selected)
                        buttonMaps[sourceIndex] = MouseUdlrSettings.Assign(buttonMaps[sourceIndex], direction, selected.Target);
                };
                directionChoices[source * 4 + i] = choice; directions.Controls.Add(choice, cells[i, 0], cells[i, 1]);
                byte target = MouseUdlrSettings.Target(buttonMaps[source], i);
                // Older saved zero means the original direction. Display that actual
                // Switch output while retaining the raw original for the save guard.
                AssignTarget(i, target == 0 ? MouseUdlrSettings.Target(MouseUdlrSettings.DefaultButtonMap, i) : target, source);
            }
            resetTargets[source] = Ui.Button(L.T("デフォルトに戻す", "Reset to defaults"), (_, _) => ResetTargets(sourceIndex));
            resetTargets[source].Anchor = AnchorStyles.None; directions.Controls.Add(resetTargets[source], 1, 1);
            TableLayoutPanel host = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = source == 0 ? 2 : 3 };
            host.ColumnStyles.Add(new(SizeType.Percent, 100)); host.RowStyles.Add(new(SizeType.AutoSize));
            host.RowStyles.Add(new(source == 0 ? SizeType.Percent : SizeType.AutoSize, source == 0 ? 100 : 0));
            if (source == 1) host.RowStyles.Add(new(SizeType.Percent, 100));
            host.Controls.Add(directions, 0, 1); page.Controls.Add(host);
            if (source == 0)
            {
                TableLayoutPanel modeHost = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2 };
                modeHost.ColumnStyles.Add(new(SizeType.Percent, 100)); modeHost.RowStyles.Add(new(SizeType.AutoSize)); modeHost.RowStyles.Add(new(SizeType.AutoSize));
                FlowLayoutPanel modeRow = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new(8, 4, 8, 4) };
                Label modeLabel = ModeText(L.T("モード", "Mode")); modeLabel.Margin = new(4, 7, 12, 3);
                trackballMode.Items.AddRange(["Virtual Rolling", "Hold"]);
                modeRow.Controls.Add(modeLabel); modeRow.Controls.Add(trackballMode); modeHost.Controls.Add(modeRow, 0, 0);
                trackballHoldRow = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Padding = new(12, 0, 12, 6) };
                trackballHoldRow.ColumnStyles.Add(new(SizeType.Percent, 100)); trackballHoldRow.ColumnStyles.Add(new(SizeType.AutoSize)); trackballHoldRow.ColumnStyles.Add(new(SizeType.AutoSize));
                trackballHoldRow.RowStyles.Add(new(SizeType.AutoSize));
                Label thresholdLabel = ModeText(L.T("移動しきい値", "Movement threshold")), units = ModeText("pt");
                thresholdLabel.Anchor = units.Anchor = AnchorStyles.Left; units.Margin = new(0, 3, 0, 3); trackballHoldThreshold.Margin = new(6, 5, 0, 5);
                trackballHoldRow.Controls.Add(thresholdLabel, 0, 0); trackballHoldRow.Controls.Add(trackballHoldThreshold, 1, 0); trackballHoldRow.Controls.Add(units, 2, 0);
                modeHost.Controls.Add(trackballHoldRow, 0, 1); host.Controls.Add(modeHost, 0, 0);
                trackballHoldThreshold.Value = current.TrackballHoldThreshold;
                trackballMode.SelectedIndex = current.TrackballHold ? 1 : 0;
            }
            if (source == 1)
            {
                paddleDirections = directions;
                FlowLayoutPanel modeRow = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Padding = new(8, 4, 8, 4) };
                Label modeLabel = ModeText(L.T("モード", "Mode")); modeLabel.Margin = new(4, 7, 12, 3);
                paddleMode.Items.AddRange([L.T("ボタンクリック", "Button clicks"), "Virtual Rolling"]);
                modeRow.Controls.Add(modeLabel); modeRow.Controls.Add(paddleMode); host.Controls.Add(modeRow, 0, 0);
                rollingPanel = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 5, Padding = new(12, 8, 12, 0) };
                rollingPanel.ColumnStyles.Add(new(SizeType.Percent, 100)); rollingPanel.ColumnStyles.Add(new(SizeType.AutoSize)); rollingPanel.ColumnStyles.Add(new(SizeType.AutoSize));
                for (int row = 0; row < 5; row++) rollingPanel.RowStyles.Add(new(SizeType.AutoSize));
                Label gainLabel = ModeText(L.T("回転感度", "Rotation sensitivity")), gainUnits = ModeText("°/pt");
                gainLabel.Anchor = gainUnits.Anchor = AnchorStyles.Left; gainUnits.Margin = new(0, 3, 0, 3); rollingGain.Margin = new(6, 6, 0, 6);
                rollingPanel.Controls.Add(gainLabel, 0, 0); rollingPanel.Controls.Add(rollingGain, 1, 0); rollingPanel.Controls.Add(gainUnits, 2, 0);
                Label stepLabel = ModeText(L.T("回転刻み", "Rotation step")), stepUnits = ModeText("°");
                stepLabel.Anchor = stepUnits.Anchor = AnchorStyles.Left; stepUnits.Margin = new(0, 3, 0, 3); rollingStep.Margin = new(6, 6, 0, 6);
                rollingStep.Items.AddRange(RollingSteps.Cast<object>().ToArray()); rollingStep.SelectedItem = (int)current.RollingStepDeg;
                rollingPanel.Controls.Add(stepLabel, 0, 1); rollingPanel.Controls.Add(rollingStep, 1, 1); rollingPanel.Controls.Add(stepUnits, 2, 1);
                Label stickLabel = ModeText(L.T("出力先アナログスティック", "Output analog stick")); stickLabel.Anchor = AnchorStyles.Left;
                rollingStick.Items.AddRange([L.T("右スティック", "Right stick"), L.T("左スティック", "Left stick")]);
                rollingStick.Margin = new(6, 6, 0, 6); rollingPanel.Controls.Add(stickLabel, 0, 2); rollingPanel.Controls.Add(rollingStick, 1, 2); rollingPanel.SetColumnSpan(rollingStick, 2);
                rollingInvert.Margin = new(4, 8, 4, 8); rollingPanel.Controls.Add(rollingInvert, 0, 3); rollingPanel.SetColumnSpan(rollingInvert, 3);
                Label hint = ModeText(L.T("パドルの仮想角度をアナログ軸の傾きに変換します。", "Converts the paddle's virtual angle into analog-axis tilt."), 9);
                hint.ForeColor = Ui.Muted; hint.MaximumSize = new(460, 0); rollingPanel.Controls.Add(hint, 0, 4); rollingPanel.SetColumnSpan(hint, 3);
                host.Controls.Add(rollingPanel, 0, 2);
                rollingGain.Value = current.RollingGain10 / 10m; rollingStick.SelectedIndex = current.RollingLeft ? 1 : 0; rollingInvert.Checked = current.RollingInvert;
                paddleMode.SelectedIndex = current.VirtualRolling ? 1 : 0;
            }
        }
        root.Controls.Add(sourceTabs, 0, 3);
        status.Dock = DockStyle.Fill; status.MaximumSize = new(510, 0); status.ForeColor = Ui.Muted; root.Controls.Add(status, 0, 4);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new(0, 8, 0, 0) };
        save = Ui.Button(L.T("本体に保存", "Save to device"), async (_, _) => await SaveSettings(), true);
        cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(save); footer.Controls.Add(cancel); AcceptButton = save; CancelButton = cancel; root.Controls.Add(footer, 0, 5);
        threshold.Value = Math.Clamp(current.Threshold, 1u, 256u); hold.Value = Math.Clamp(current.HoldMs, 1u, 512u); wait.Value = Math.Clamp(current.WaitMs, 1u, 512u);
        if (threshold.Value != current.Threshold || hold.Value != current.HoldMs || wait.Value != current.WaitMs)
            status.Text = L.T("旧設定が範囲外のため、保存すると表示中の値へ更新します。", "The old setting is outside this range. Saving applies the values shown.");
        else if (!current.Persisted)
            status.Text = L.T("本体に保存されていません。「本体に保存」で確定してください。", "These settings are not stored yet. Choose Save to device.");
        FormClosing += (_, e) => { if (saving) e.Cancel = true; };
        Controls.Add(root); Ui.ThemeInputs(this);
        // Wire state updates after all controls and the initial tab exist. The
        // same path handles first open, navigation, mode changes and save retry.
        sourceTabs.SelectedIndex = 0;
        sourceTabs.SelectedIndexChanged += (_, _) => UpdateModeControls();
        trackballMode.SelectedIndexChanged += (_, _) => UpdateModeControls();
        paddleMode.SelectedIndexChanged += (_, _) => UpdateModeControls();
        UpdateModeControls();
    }
    private static readonly Color DisabledText = Color.FromArgb(0xad, 0xad, 0xad);
    // Partial paint buffers have a translated origin and a restricted clip.
    // Keep both when TextRenderer switches from Graphics to its native HDC.
    private const TextFormatFlags DisabledGraphicsFlags = TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
    private static Label ModeText(string text, float size = 10, bool bold = false) => new ModeLabel
    {
        Text = text, AutoSize = true, ForeColor = Ui.Ink, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
        Font = new Font(L.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new(4, 3, 4, 3)
    };
    // Native disabled label/check-box text can be black in WinForms dark mode.
    // Retain disabled input/accessibility and use readable grey for this dialog.
    private sealed class ModeLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            if (Enabled) { base.OnPaint(e); return; }
            Rectangle text = Rectangle.FromLTRB(Padding.Left, Padding.Top, Width - Padding.Right, Height - Padding.Bottom);
            TextRenderer.DrawText(e.Graphics, Text, Font, text, DisabledText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | DisabledGraphicsFlags);
        }
    }
    private sealed class ModeCheckBox : DarkCheckBox { }
    private void UpdateModeControls()
    {
        bool interactive = !saving;
        bool trackballClicks = trackballMode.SelectedIndex == 0;
        bool paddleClicks = paddleMode.SelectedIndex == 0;
        // Parent Enabled greys labels/units as well as editors and retains all
        // values and geometry. Unselected tabs retain their own mode state.
        clickValues.Enabled = interactive && (sourceTabs.SelectedIndex == 0 ? trackballClicks : paddleClicks);
        trackballHoldRow.Enabled = interactive && !trackballClicks;
        paddleDirections.Enabled = interactive && paddleClicks;
        rollingPanel.Enabled = interactive && !paddleClicks;
        sourceTabs.Enabled = interactive;
        save.Enabled = cancel.Enabled = interactive;
    }
    private uint TrackballValue => (uint)trackballHoldThreshold.Value | (uint)(trackballMode.SelectedIndex << 8);
    private uint RollingStepValue => (uint)(int)rollingStep.SelectedItem!;
    private uint RollingValue => (uint)(rollingGain.Value * 10m) | (uint)(paddleMode.SelectedIndex << 16) |
        (rollingInvert.Checked ? 0x01000000u : 0) | (rollingStick.SelectedIndex == 1 ? 0x02000000u : 0);
    private void AssignTarget(int direction, byte target, int source = 0)
    {
        if (saving || direction is < 0 or > 3 || source is < 0 or > 1) return;
        int index = Array.FindIndex(TargetChoices, choice => choice.Target == target);
        if (index < 0) throw new ArgumentOutOfRangeException(nameof(target));
        directionChoices[source * 4 + direction].SelectedIndex = index;
    }
    private void ResetTargets(int source)
    {
        for (int i = 0; i < 4; i++) AssignTarget(i, MouseUdlrSettings.Target(MouseUdlrSettings.DefaultButtonMap, i), source);
    }
    private async Task SaveSettings()
    {
        if (saving) return;
        bool close = false;
        try
        {
            MouseUdlrSettings next = new((uint)threshold.Value, (uint)hold.Value, (uint)wait.Value, true, buttonMaps[0], buttonMaps[1], RollingValue, TrackballValue, RollingStepValue);
            MouseUdlrSettings.ValidateEdit(next.Threshold, next.HoldMs, next.WaitMs); foreach (uint map in buttonMaps) MouseUdlrSettings.ValidateTargets(map);
            saving = true; UpdateModeControls();
            status.Text = L.T("本体に保存し、読み戻して確認しています…", "Saving to the device and verifying…");
            MouseUdlrSettings actual = await persist(original, next);
            if (actual != next) throw new IOException(L.T("保存結果が一致しません。", "Saved values do not match."));
            Saved = true; close = true;
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally
        {
            saving = false; UpdateModeControls();
        }
        if (close) { DialogResult = DialogResult.OK; Close(); }
    }
}

internal sealed partial class MainForm
{
    private async Task ConfigureMouseUdlr()
    {
        if (working || bridge is null || caps?.SupportsRollingStep != true) return;
        working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
        try
        {
            MouseUdlrSettingsClient client = new(bridge.RawAsync);
            MouseUdlrSettings current = await client.ReadAsync();
            using MouseUdlrSettingsDialog dialog = new(current, client.SaveAsync);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Saved)
                SetText(saved, L.T("マウス UDLR設定を本体に保存しました。", "Saved Mouse UDLR settings to the device.") +
                    (dirty.Any(x => x) ? L.T(" プロファイルの未保存の編集は残っています。", " Profile edits are still unsaved.") : ""));
        }
        catch (Exception ex) { Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
}
