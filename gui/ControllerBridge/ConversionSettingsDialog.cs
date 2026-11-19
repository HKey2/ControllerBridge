namespace ControllerBridge;

internal sealed class ConversionSettingsDialog : Form
{
    internal ConversionSettings Result { get; private set; }
    private readonly ComboBox trackballMode = Choice(), paddleMode = Choice(), rollingStep = Choice(), rollingStick = Choice();
    private readonly ComboBox[] targets = Enumerable.Range(0, 6).Select(_ => Choice()).ToArray();
    private readonly CheckBox rollingInvert = new DarkCheckBox { Text = L.T("回転方向を反転", "Reverse rotation"), AutoSize = true };
    private readonly CheckBox inertia = new DarkCheckBox { Text = L.T("疑似慣性を有効にする", "Enable pseudo-inertia"), AutoSize = true };
    private readonly ComboBox analogStick = Choice();
    private readonly NumericUpDown analogSpeed = Number(1, 50), analogMinimum = Number(1, 100), analogReturn = Number(0, 2550);
    private readonly NumericUpDown outputMinimum = Number(0, 99), outputMaximum = Number(1, 100);
    private readonly AnalogCurvePicker outputCurve = new();
    private readonly ComboBox paddleAnalogAxis = Choice();
    private readonly NumericUpDown paddleAnalogSpeed = Number(1, 50), paddleSmoothing = Number(1, 100), paddleReturn = Number(0, 2550);
    private readonly NumericUpDown paddleOutputMinimum = Number(0, 99), paddleOutputMaximum = Number(1, 100);
    private readonly AnalogCurvePicker paddleOutputCurve = new();
    private static ComboBox Choice() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210, Anchor = AnchorStyles.Left };
    private static NumericUpDown Number(int minimum, int maximum) => new() { Minimum = minimum, Maximum = maximum, Increment = 1, Width = 140, Anchor = AnchorStyles.Left, ThousandsSeparator = true };
    internal ConversionSettingsDialog(DeviceProfile profile)
    {
        Result = profile.Conversion.Clone(); Result.Validate();
        using IDisposable layout = Ui.Form(this, L.T("方向変換の割り当て", "Conversion mapping"), new(660, 820), new(620, 650));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize));
        Label title = Ui.Label(profile.Name, 12, true); title.MaximumSize = new(520, 0); root.Controls.Add(title, 0, 0);
        DarkTabControl tabs = new() { Dock = DockStyle.Fill, Padding = new(20, 10) }; root.Controls.Add(tabs, 0, 1);
        TableLayoutPanel Page(string name)
        {
            TabPage page = new(name) { BackColor = Ui.Surface, Padding = new(14), AutoScroll = true }; tabs.TabPages.Add(page);
            TableLayoutPanel grid = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, Margin = Padding.Empty };
            grid.ColumnStyles.Add(new(SizeType.Percent, 100)); grid.ColumnStyles.Add(new(SizeType.AutoSize)); page.Controls.Add(grid); return grid;
        }
        static void Row(TableLayoutPanel grid, string label, Control control)
        {
            int row = grid.RowCount++; grid.RowStyles.Add(new(SizeType.Absolute, 40));
            Label text = Ui.Label(label); text.Anchor = AnchorStyles.Left; control.Margin = new(4, 4, 4, 4);
            control.AccessibleName = label; grid.Controls.Add(text, 0, row); grid.Controls.Add(control, 1, row);
        }
        TableLayoutPanel ball = Page("Trackball"), paddle = Page("Paddle");
        trackballMode.Items.AddRange(["Virtual Cursor(Repeat)", "Virtual Cursor(Hold)", "Virtual Cursor(Analog)"]); trackballMode.SelectedIndex = Result.TrackballMode;
        paddleMode.Items.AddRange(["Virtual Cursor(Repeat)", "Virtual Rolling", "Virtual Cursor(Analog)"]); paddleMode.SelectedIndex = Result.PaddleMode;
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
        SunkenTableLayoutPanel analog = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Padding = new(12), Margin = new(0, 12, 0, 4), BackColor = Ui.Background, AccessibleName = "Virtual Cursor(Analog)" };
        analog.ColumnStyles.Add(new(SizeType.Percent, 100)); analog.ColumnStyles.Add(new(SizeType.AutoSize));
        void Note(TableLayoutPanel grid, string text, bool heading = false)
        {
            Label note = Ui.Label(text, heading ? 11 : 9, heading); note.MaximumSize = new(540, 0);
            note.ForeColor = heading ? Ui.Ink : Ui.Muted; note.Margin = new(4, 3, 4, 6);
            grid.RowStyles.Add(new(SizeType.AutoSize)); grid.Controls.Add(note, 0, grid.RowCount++); grid.SetColumnSpan(note, 2);
        }
        Note(analog, "Virtual Cursor(Analog)", true);
        analogStick.Items.AddRange([L.T("左スティック", "Left stick"), L.T("右スティック", "Right stick")]); analogStick.SelectedIndex = Result.AnalogRight ? 1 : 0;
        analogSpeed.Minimum = 0.01m; analogSpeed.DecimalPlaces = 2; analogSpeed.Increment = 0.05m;
        int originalSmoothing = ConversionSettings.SmoothingPercent(Result.VirtualPeriodMs);
        analogSpeed.Value = Result.VirtualGain; analogMinimum.Value = originalSmoothing; analogReturn.Increment = 10; analogReturn.Value = Result.VirtualStopMs;
        outputMinimum.Value = Result.AnalogOutputMinimum; outputMaximum.Value = Result.AnalogOutputMaximum; outputCurve.CurveCode = Result.AnalogOutputCurve;
        Row(analog, L.T("出力先", "Output stick"), analogStick);
        Row(analog, L.T("アナログ最小値 (%)", "Analog minimum (%)"), outputMinimum);
        Row(analog, L.T("アナログ最大値 (%)", "Analog maximum (%)"), outputMaximum);
        Row(analog, L.T("カーブ", "Curve"), outputCurve);
        Row(analog, L.T("スピード", "Speed"), analogSpeed);
        Row(analog, L.T("スムージング (%)", "Smoothing (%)"), analogMinimum);
        Row(analog, L.T("慣性 (ms)", "Inertia (ms)"), analogReturn);
        Note(analog, L.T("S / Auto(S)に対応。Proは未対応", "Supports S / Auto(S). Pro is not supported."));
        ball.RowStyles.Add(new(SizeType.AutoSize)); ball.Controls.Add(analog, 0, ball.RowCount++); ball.SetColumnSpan(analog, 2);
        SunkenTableLayoutPanel paddleAnalog = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 2, Padding = new(12), Margin = new(0, 12, 0, 4), BackColor = Ui.Background, AccessibleName = "Paddle Virtual Cursor(Analog)" };
        paddleAnalog.ColumnStyles.Add(new(SizeType.Percent, 100)); paddleAnalog.ColumnStyles.Add(new(SizeType.AutoSize));
        Note(paddleAnalog, "Virtual Cursor(Analog)", true);
        paddleAnalogAxis.Items.AddRange([L.T("左スティック(X)", "Left stick (X)"), L.T("左スティック(Y)", "Left stick (Y)"),
            L.T("右スティック(X)", "Right stick (X)"), L.T("右スティック(Y)", "Right stick (Y)")]);
        paddleAnalogAxis.SelectedIndex = Result.PaddleAnalogAxis;
        paddleAnalogSpeed.Minimum = 0.01m; paddleAnalogSpeed.DecimalPlaces = 2; paddleAnalogSpeed.Increment = 0.05m;
        int originalPaddleSmoothing = ConversionSettings.SmoothingPercent(Result.PaddleAnalogSmoothingMs);
        paddleAnalogSpeed.Value = Result.PaddleAnalogSpeed100 / 100m; paddleSmoothing.Value = originalPaddleSmoothing;
        paddleReturn.Increment = 10; paddleReturn.Value = Result.PaddleAnalogReturnMs;
        paddleOutputMinimum.Value = Result.PaddleAnalogOutputMinimum; paddleOutputMaximum.Value = Result.PaddleAnalogOutputMaximum;
        paddleOutputCurve.CurveCode = Result.PaddleAnalogOutputCurve;
        Row(paddleAnalog, L.T("出力先", "Output axis"), paddleAnalogAxis);
        Row(paddleAnalog, L.T("アナログ最小値 (%)", "Analog minimum (%)"), paddleOutputMinimum);
        Row(paddleAnalog, L.T("アナログ最大値 (%)", "Analog maximum (%)"), paddleOutputMaximum);
        Row(paddleAnalog, L.T("カーブ", "Curve"), paddleOutputCurve);
        Row(paddleAnalog, L.T("スピード", "Speed"), paddleAnalogSpeed);
        Row(paddleAnalog, L.T("スムージング (%)", "Smoothing (%)"), paddleSmoothing);
        Row(paddleAnalog, L.T("慣性 (ms)", "Inertia (ms)"), paddleReturn);
        Note(paddleAnalog, L.T("S / Auto(S)に対応。Proは未対応", "Supports S / Auto(S). Pro is not supported."));
        paddle.RowStyles.Add(new(SizeType.AutoSize)); paddle.Controls.Add(paddleAnalog, 0, paddle.RowCount++); paddle.SetColumnSpan(paddleAnalog, 2);
        void UpdateMode()
        {
            inertia.Enabled = trackballMode.SelectedIndex == 1;
            bool angle = trackballMode.SelectedIndex == 2; analog.Enabled = angle;
            for (int i = 0; i < 4; i++) targets[i].Enabled = !angle;
            bool rolling = paddleMode.SelectedIndex == 1;
            targets[4].Enabled = targets[5].Enabled = paddleMode.SelectedIndex == 0;
            paddleAnalog.Enabled = paddleMode.SelectedIndex == 2;
            rollingStep.Enabled = rollingStick.Enabled = rollingInvert.Enabled = rolling;
        }
        paddleMode.SelectedIndexChanged += (_, _) => UpdateMode(); trackballMode.SelectedIndexChanged += (_, _) => UpdateMode(); UpdateMode();
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        static bool NumberChanged(NumericUpDown editor, decimal original) => decimal.TryParse(editor.Text,
            System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.CurrentCulture, out decimal value) &&
            decimal.Round(Math.Clamp(value, editor.Minimum, editor.Maximum), editor.DecimalPlaces) != original;
        bool CurveChanged() => outputCurve.CurveCode != AnalogCurvePicker.NormalizeCode(Result.AnalogOutputCurve);
        bool PaddleCurveChanged() => paddleOutputCurve.CurveCode != AnalogCurvePicker.NormalizeCode(Result.PaddleAnalogOutputCurve);
        bool HasChanges() => trackballMode.SelectedIndex != Result.TrackballMode || paddleMode.SelectedIndex != Result.PaddleMode ||
            inertia.Checked != Result.InertiaEnabled || rollingInvert.Checked != Result.RollingInvert ||
            (rollingStick.SelectedIndex == 1) != Result.RollingLeft || (int)rollingStep.SelectedItem! != Result.RollingStepDeg ||
            (analogStick.SelectedIndex == 1) != Result.AnalogRight ||
            NumberChanged(analogSpeed, Result.VirtualGain) || NumberChanged(analogMinimum, originalSmoothing) || NumberChanged(analogReturn, Result.VirtualStopMs) ||
            NumberChanged(outputMinimum, Result.AnalogOutputMinimum) || NumberChanged(outputMaximum, Result.AnalogOutputMaximum) || CurveChanged() ||
            paddleAnalogAxis.SelectedIndex != Result.PaddleAnalogAxis || NumberChanged(paddleAnalogSpeed, Result.PaddleAnalogSpeed100 / 100m) ||
            NumberChanged(paddleSmoothing, originalPaddleSmoothing) || NumberChanged(paddleReturn, Result.PaddleAnalogReturnMs) ||
            NumberChanged(paddleOutputMinimum, Result.PaddleAnalogOutputMinimum) || NumberChanged(paddleOutputMaximum, Result.PaddleAnalogOutputMaximum) || PaddleCurveChanged() ||
            targets.Where((control, i) => control.SelectedIndex + 1 != (i < 4 ? Result.TrackballTargets[i] : Result.PaddleTargets[i - 4])).Any();
        Button ok = Ui.Button(L.T("編集に反映", "Apply to draft"), (_, _) => {
            if (!HasChanges()) return;
            ConversionSettings next = new() { TrackballTargets = targets.Take(4).Select(c => (byte)(c.SelectedIndex + 1)).ToArray(),
                PaddleTargets = targets.Skip(4).Select(c => (byte)(c.SelectedIndex + 1)).ToArray(), TrackballMode = (byte)trackballMode.SelectedIndex, InertiaEnabled = inertia.Checked,
                PaddleMode = (byte)paddleMode.SelectedIndex, RollingStepDeg = (byte)(int)rollingStep.SelectedItem!, RollingLeft = rollingStick.SelectedIndex == 1, RollingInvert = rollingInvert.Checked,
                AnalogFullSpeed = Result.AnalogFullSpeed, AnalogRight = analogStick.SelectedIndex == 1, AnalogCurve = Result.AnalogCurve,
                AnalogInvertX = Result.AnalogInvertX, AnalogInvertY = Result.AnalogInvertY,
                AnalogMinimumSpeed = Result.AnalogMinimumSpeed, AnalogReturnMs = Result.AnalogReturnMs,
                AnalogOutputMinimum = (byte)outputMinimum.Value, AnalogOutputMaximum = (byte)outputMaximum.Value,
                AnalogOutputCurve = CurveChanged() ? outputCurve.CurveCode : Result.AnalogOutputCurve,
                PaddleAnalogAxis = (byte)paddleAnalogAxis.SelectedIndex, PaddleAnalogSpeed100 = (ushort)decimal.Round(paddleAnalogSpeed.Value * 100),
                PaddleAnalogSmoothingMs = NumberChanged(paddleSmoothing, originalPaddleSmoothing) ?
                    (byte)ConversionSettings.SmoothingPeriodMs((int)paddleSmoothing.Value) : Result.PaddleAnalogSmoothingMs,
                PaddleAnalogReturnMs = NumberChanged(paddleReturn, Result.PaddleAnalogReturnMs) ?
                    (ushort)(decimal.Round(paddleReturn.Value / 10, 0, MidpointRounding.AwayFromZero) * 10) : Result.PaddleAnalogReturnMs,
                PaddleAnalogOutputMinimum = (byte)paddleOutputMinimum.Value, PaddleAnalogOutputMaximum = (byte)paddleOutputMaximum.Value,
                PaddleAnalogOutputCurve = PaddleCurveChanged() ? paddleOutputCurve.CurveCode : Result.PaddleAnalogOutputCurve };
            if (NumberChanged(analogSpeed, Result.VirtualGain) || NumberChanged(analogMinimum, originalSmoothing) || NumberChanged(analogReturn, Result.VirtualStopMs))
                next.SetVirtualOptions(analogSpeed.Value, NumberChanged(analogMinimum, originalSmoothing) ?
                    ConversionSettings.SmoothingPeriodMs((int)analogMinimum.Value) : Result.VirtualPeriodMs,
                    (int)(decimal.Round(analogReturn.Value / 10, 0, MidpointRounding.AwayFromZero) * 10));
            try { next.Validate(); }
            catch (InvalidDataException ex) { Ui.Error(this, ex); return; }
            Result = next; DialogResult = DialogResult.OK; Close();
        }, true);
        void UpdateApplyButton() => ok.Enabled = HasChanges();
        foreach (ComboBox control in targets.Concat([trackballMode, paddleMode, rollingStep, rollingStick, analogStick, paddleAnalogAxis]))
            control.SelectedIndexChanged += (_, _) => UpdateApplyButton();
        foreach (CheckBox control in new[] { inertia, rollingInvert }) control.CheckedChanged += (_, _) => UpdateApplyButton();
        outputCurve.SelectedIndexChanged += (_, _) => UpdateApplyButton();
        paddleOutputCurve.SelectedIndexChanged += (_, _) => UpdateApplyButton();
        foreach (NumericUpDown control in new[] { analogSpeed, analogMinimum, analogReturn, outputMinimum, outputMaximum, paddleAnalogSpeed, paddleSmoothing, paddleReturn, paddleOutputMinimum, paddleOutputMaximum })
        {
            control.ValueChanged += (_, _) => UpdateApplyButton(); control.TextChanged += (_, _) => UpdateApplyButton();
        }
        UpdateApplyButton();
        Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(ok); footer.Controls.Add(cancel); AcceptButton = ok; CancelButton = cancel; root.Controls.Add(footer, 0, 2);
        Controls.Add(root); Ui.ThemeInputs(this);
    }
}

internal sealed class SunkenTableLayoutPanel : BufferedTableLayoutPanel
{
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Rectangle r = ClientRectangle; r.Width--; r.Height--;
        if (r.Width < 2 || r.Height < 2) return;
        using Pen shadow = new(Color.FromArgb(8, 8, 8)), light = new(Ui.Stroke);
        e.Graphics.DrawLine(shadow, r.Left, r.Bottom, r.Left, r.Top); e.Graphics.DrawLine(shadow, r.Left, r.Top, r.Right, r.Top);
        e.Graphics.DrawLine(light, r.Left, r.Bottom, r.Right, r.Bottom); e.Graphics.DrawLine(light, r.Right, r.Bottom, r.Right, r.Top);
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
        SetText(saved, L.T("方向変換の編集を保持しています。設定をデバイスに保存すると反映されます。", "Conversion edits are retained. Save the profile to device to apply them."));
    }
    private static string LegacyImportNotice(bool fromDevice) =>
        L.T("旧形式を変換しました。4個目のシフトキーがある場合は削除し、先頭3個を保持します。\n", "Converted the older format. A fourth shift key is removed; the first three are retained.\n") +
        (fromDevice ? L.T("旧ファイルに含まれない方向変換設定は、このデバイスに残る移行用設定で補完しました。保存前に割り当てと回転刻みを確認してください。",
            "Conversion settings missing from the old file use this device's retained migration defaults. Check mappings and rotation steps before saving.") :
            L.T("旧ファイルに含まれない方向変換設定は標準値で補完しました。保存前に割り当てと回転刻みを確認してください。",
            "Conversion settings missing from the old file use standard defaults. Check mappings and rotation steps before saving."));
}
