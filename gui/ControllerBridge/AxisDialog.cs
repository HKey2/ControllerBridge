namespace ControllerBridge;

internal sealed class AxisDialog : Form
{
    public AxisBinding Result { get; private set; }
    private readonly List<InputField> fields, inputs;
    private readonly Dictionary<InputField, string> savedLabels = [];
    private readonly AxisSelection selection;
    private readonly AxisSourceList source = new();
    private readonly NumericUpDown minimum = Number(), center = Number(), maximum = Number();
    private readonly NumericUpDown deadzone = new() { Minimum = 0, Maximum = 50, Value = 5, Width = 90 };
    private readonly CheckBox invert = new() { Text = L.T("向きを反転", "Invert direction"), AutoSize = true, Margin = new(10, 24, 4, 4) };
    private readonly Label live = Ui.Label("", 10), summary = Ui.Label("", 11, true), message = Ui.Label("", 9);
    private readonly Label gyroNotice = Ui.Label(L.T("校正とRecenterは、スティック設定画面の「ジャイロ」から変更できます。", "Open Gyro in the stick setup window to calibrate or change Recenter."), 10);
    private readonly TableLayoutPanel analogPanel;
    private readonly AnalogPairEditor analogPair;
    private readonly Button detectButton, apply, calibrate, setCenter, descriptor;
    private readonly Button[] digital = new Button[2];
    private readonly ToolTip digitalTips = new() { AutoPopDelay = 15000 };
    private readonly HashSet<InputField> dirtyInputs = [];
    private readonly Dictionary<ushort, int> baselines = [];
    private readonly bool canLearn;
    private readonly int axis;
    private readonly Func<Task<bool>>? prepareLearning;
    private ButtonLearner? pairLearner;
    private int learningSide = -1, learningRequest;
    private bool detect, measuring, loading, preparing, refreshQueued;
    private static NumericUpDown Number() => new() { Minimum = int.MinValue, Maximum = int.MaxValue, Width = 145, ThousandsSeparator = true };

    public AxisDialog(int axis, AxisBinding original, List<InputField> fields, bool canLearn, Func<Task<bool>>? prepareLearning = null)
    {
        this.axis = axis; this.fields = fields; this.canLearn = canLearn; this.prepareLearning = prepareLearning;
        if (canLearn) InputDirections.RequireCurrentInput(fields);
        selection = new(original, axis); Result = selection.Build();
        inputs = fields.Where(f => f.Learnable && !f.Binary && !f.Hat).ToList();
        AddSavedInputs();
        using IDisposable layout = Ui.Form(this, DeviceProfile.AxisNames[axis] + L.T(" の設定", " settings"), new(1060, 820), new(900, 680));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 9 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int row = 0; row < 9; row++) root.RowStyles.Add(new(row == 3 ? SizeType.Percent : SizeType.AutoSize, row == 3 ? 100 : 0));
        root.Controls.Add(Ui.Label(DeviceProfile.AxisNames[axis] + L.T(" の入力割り当て", " input assignment"), 17, true), 0, 0);
        Label help = Ui.Label(L.F("左側で全体、右上で「{0}」、右下で「{1}」を選択。同じボタンをもう一度押すと解除します。", "Choose the whole axis on the left, {0} at upper right, or {1} at lower right. Press again to clear.", AxisDirections.Label(axis, 1), AxisDirections.Label(axis, 0)), 10);
        help.Dock = DockStyle.Fill; root.Controls.Add(help, 0, 1);

        FlowLayoutPanel tools = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new(0, 2, 0, 2) };
        detectButton = Ui.Button(L.T("操作して入力軸を検出", "Move to detect an input axis"), (_, _) => Detect()); detectButton.Enabled = canLearn;
        tools.Controls.Add(detectButton);
        for (int i = 0; i < 2; i++)
        {
            int side = i; digital[i] = Ui.Button("", (_, _) => LearnPair(side));
            digital[i].AutoSize = false; digital[i].MinimumSize = Size.Empty; digital[i].Size = new(100, 28);
            digital[i].Font = new Font(L.FontFamily, 9); digital[i].Padding = new(4, 1, 4, 1); digital[i].Margin = new(4, 15, 4, 4);
        }
        root.Controls.Add(tools, 0, 2);

        Panel inputArea = new() { Dock = DockStyle.Fill, AutoScroll = false, MinimumSize = new(0, 150), Margin = new(0, 0, 0, 6) };
        source.SetInputs(inputs, InputName, axis); inputArea.Controls.Add(source); root.Controls.Add(inputArea, 0, 3);
        inputArea.SizeChanged += (_, _) => source.FitToArea();
        summary.Dock = DockStyle.Fill; root.Controls.Add(summary, 0, 4);

        TableLayoutPanel editors = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3 };
        editors.ColumnStyles.Add(new(SizeType.Percent, 100)); for (int i = 0; i < 3; i++) editors.RowStyles.Add(new(SizeType.AutoSize));
        analogPanel = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2 };
        analogPanel.ColumnStyles.Add(new(SizeType.Percent, 100)); analogPanel.RowStyles.Add(new(SizeType.AutoSize)); analogPanel.RowStyles.Add(new(SizeType.AutoSize));
        FlowLayoutPanel numbers = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = false };
        foreach ((string title, NumericUpDown number) in new[] { (L.T("最小入力値", "Minimum input"), minimum), (L.T("中心", "Center"), center), (L.T("最大入力値", "Maximum input"), maximum), (L.T("デッドゾーン %", "Dead zone %"), deadzone) })
        {
            FlowLayoutPanel column = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new(2, 2, 12, 2) };
            column.Controls.Add(Ui.Label(title, 9)); column.Controls.Add(number); numbers.Controls.Add(column);
            number.ValueChanged += (_, _) => StoreAnalog();
        }
        numbers.Controls.Add(invert); invert.CheckedChanged += (_, _) => StoreAnalog(); analogPanel.Controls.Add(numbers, 0, 0);
        FlowLayoutPanel read = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = false };
        live.AutoSize = false; live.Size = new(195, 40); read.Controls.Add(live);
        calibrate = Ui.Button(L.T("可動範囲を計測", "Measure range"), (_, _) =>
        {
            if (WholeInput is not { Valid: true } f) return;
            CancelLearning(); minimum.Value = center.Value = maximum.Value = f.Value; measuring = true;
            message.Text = L.T("両端まで往復し、手を離して「中心を記録」を押してください。", "Move to both ends, release the control, then choose Record center.");
        });
        setCenter = Ui.Button(L.T("中心を記録", "Record center"), (_, _) =>
        {
            if (WholeInput is not { Valid: true } f) return;
            center.Value = Math.Clamp(f.Value, f.Minimum, f.Maximum); measuring = false;
            message.Text = L.T("中心を記録しました。", "Center recorded.");
        });
        descriptor = Ui.Button(L.T("元の範囲に戻す", "Restore input range"), (_, _) =>
        {
            if (WholeInput is not { } f) return;
            minimum.Value = f.Minimum; maximum.Value = f.Maximum;
            center.Value = f.Pressure ? f.Minimum : AxisSourceList.Midpoint(f); measuring = false;
        });
        read.Controls.Add(calibrate); read.Controls.Add(setCenter); read.Controls.Add(descriptor); analogPanel.Controls.Add(read, 0, 1);
        analogPair = new(inputs, canLearn, InputName, axis); gyroNotice.Dock = DockStyle.Fill;
        editors.Controls.Add(analogPanel, 0, 0); editors.Controls.Add(analogPair, 0, 1); editors.Controls.Add(gyroNotice, 0, 2); root.Controls.Add(editors, 0, 5);
        message.Dock = DockStyle.Fill; message.ForeColor = Ui.Muted; message.Margin = new(4, 6, 4, 6); root.Controls.Add(message, 0, 6);
        Label notice = Ui.Label(L.ProfileSaveNotice, 9); notice.ForeColor = Ui.Muted; notice.Dock = DockStyle.Fill; root.Controls.Add(notice, 0, 7);
        FlowLayoutPanel footer = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        apply = Ui.Button(L.T("適用", "Apply"), (_, _) => Apply(), true);
        Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(apply); footer.Controls.Add(cancel); footer.Controls.Add(digital[AxisDirections.ProfileSide(axis, 1)]); footer.Controls.Add(digital[AxisDirections.ProfileSide(axis, 0)]);
        root.Controls.Add(footer, 0, 8); CancelButton = cancel; Controls.Add(root);
        source.Choose += (field, part) =>
        {
            CancelLearning();
            if (part < 0) selection.ToggleWhole(field); else selection.ToggleHalf(part, field);
            ShowSelection();
        };
        Ui.ThemeInputs(this); ShowSelection();
    }
    private string InputName(InputField f) => savedLabels.TryGetValue(f, out string? label) ? label : f.Label;
    private void AddSavedInputs()
    {
        void Add(byte iface, byte id, ushort offset, byte bits, byte flags, int min, int max, bool gyro)
        {
            InputField? existing = fields.FirstOrDefault(f => AxisSelection.Matches(f, iface, id, offset, bits, flags));
            if (existing is not null) { if (!inputs.Contains(existing)) inputs.Add(existing); return; }
            if (inputs.Any(f => AxisSelection.Matches(f, iface, id, offset, bits, flags))) return;
            InputField saved = new() { Index = (ushort)(65535 - savedLabels.Count), Interface = iface, ReportId = id, BitOffset = offset,
                Bits = bits, Flags = (byte)(flags & InputDirections.SourceFlags), Minimum = min, Maximum = max, DualShock3 = gyro, UsagePage = gyro ? (ushort)0xff00 : (ushort)0, Usage = gyro ? (ushort)4 : (ushort)0 };
            savedLabels[saved] = gyro ? L.T("ジャイロ（設定済み）", "Gyro (configured)") : L.T("保存済みの入力", "Saved input"); inputs.Add(saved);
        }
        if (selection.Whole is { } a) Add(a.Interface, a.ReportId, a.BitOffset, a.Bits, a.Flags, a.Minimum, a.Maximum, a.Gyro);
        foreach (AnalogHalfBinding h in selection.Halves.OfType<AnalogHalfBinding>()) Add(h.Interface, h.ReportId, h.BitOffset, h.Bits, h.Flags, Math.Min(h.Neutral, h.Maximum), Math.Max(h.Neutral, h.Maximum), false);
    }
    private InputField? WholeInput => inputs.FirstOrDefault(f => AxisSelection.Matches(f, selection.Whole));
    private string ButtonName(DigitalBinding? b) => b is null ? L.T("未割り当て", "Unassigned") : fields.FirstOrDefault(f => AxisSelection.Matches(f, b))?.Label ?? L.T("保存済み", "Saved");
    private string HalfName(int side) => selection.Halves[side] is not { } a ? L.T("未割り当て", "Unassigned") :
        inputs.FirstOrDefault(f => AxisSelection.Matches(f, a)) is { } f ? InputName(f) : L.T("保存済み", "Saved");
    private void StoreAnalog()
    {
        if (loading || selection.Whole is not { Gyro: false } a) return;
        a.Minimum = (int)minimum.Value; a.Center = (int)center.Value; a.Maximum = (int)maximum.Value; a.Deadzone = (ushort)deadzone.Value;
        a.Flags = (byte)((a.Flags & InputDirections.SourceFlags) | (AxisDirections.ProfileInvert(axis, invert.Checked) ? 4 : 0));
    }
    private void ShowSelection()
    {
        using IDisposable layout = Ui.BatchLayout(this);
        source.ShowSelection(selection);
        analogPanel.Visible = selection.Whole is { Gyro: false }; analogPair.Visible = selection.HasHalves; gyroNotice.Visible = selection.Whole is { Gyro: true };
        if (selection.Whole is { Gyro: false } a)
        {
            loading = true; minimum.Value = a.Minimum; center.Value = a.Center; maximum.Value = a.Maximum; deadzone.Value = a.Deadzone; invert.Checked = AxisDirections.ProfileInvert(axis, (a.Flags & 4) != 0); loading = false;
        }
        analogPair.Bind(selection); RefreshLive(); RefreshButtons();
        summary.Text = selection.Whole is not null ? L.T("全体: ", "Whole axis: ") + (WholeInput is { } f ? InputName(f) : L.T("保存済み", "Saved")) :
            selection.HasHalves ? AxisDirections.Label(axis, 0) + ": " + HalfName(AxisDirections.ProfileSide(axis, 0)) + "    /    " + AxisDirections.Label(axis, 1) + ": " + HalfName(AxisDirections.ProfileSide(axis, 1)) :
            selection.HasButtons ? L.T("デジタル  ", "Digital  ") + AxisDirections.Label(axis, 0) + ": " + ButtonName(selection.Buttons[AxisDirections.ProfileSide(axis, 0)]) + "    /    " + AxisDirections.Label(axis, 1) + ": " + ButtonName(selection.Buttons[AxisDirections.ProfileSide(axis, 1)]) :
            L.T("未使用 — 入力は選択されていません。出力は中心に固定されます。", "Unused — no input selected. Output stays centered.");
        message.Text = !canLearn ? L.T("入力コントローラー未接続。保存済みの設定を編集・解除できます。", "Input controller disconnected. Saved assignments can be edited or cleared.") :
            selection.HasHalves ? L.T("片側だけでも使用できます。正負の押し込み量が等しいと出力は中心になります。", "Either side can be used alone. Equal positive and negative input returns the output to center.") :
            selection.HasButtons ? L.T("同時押しは中心になります。割り当て済みの学習ボタンを押すと解除します。", "Pressing both buttons centers the axis. Press an assigned learning button to clear it.") :
            L.T("何も選択しない状態で適用すると、この軸は未使用になります。", "Apply with nothing selected to leave this axis unused.");
    }
    private void RefreshButtons()
    {
        for (int i = 0; i < 2; i++)
        {
            int displaySide = AxisDirections.ProfileSide(axis, i);
            string direction = L.T("デジタル ", "Digital ") + AxisDirections.Label(axis, displaySide);
            bool learning = learningSide == i && (preparing || pairLearner is not null);
            digital[i].Text = learning ? AxisDirections.Label(axis, displaySide, compact: true) + " " + L.T("学習中", "Learning") : direction;
            string detail = learning ? L.T("入力待ち。もう一度押すと中止します。", "Waiting for input. Press again to cancel.") :
                selection.Buttons[i] is null ? L.T("押してから、割り当てるボタンを操作してください。", "Press, then operate the button to assign.") :
                ButtonName(selection.Buttons[i]) + L.T(" — 押すと解除します。", " — press to clear.");
            digitalTips.SetToolTip(digital[i], detail); digital[i].AccessibleName = direction; digital[i].AccessibleDescription = detail;
            digital[i].Enabled = canLearn || selection.Buttons[i] is not null;
            Ui.ButtonColors(digital[i], learning, selection.Buttons[i] is not null); digital[i].Invalidate();
        }
        Ui.ButtonColors(detectButton, detect); detectButton.Text = detect ? L.T("入力を検出中 · 押すと中止", "Detecting · press to cancel") : L.T("操作して入力軸を検出", "Move to detect an input axis");
        apply.Enabled = !preparing && pairLearner is null && !detect;
    }
    private void RefreshLive()
    {
        InputField? f = WholeInput;
        live.Text = f is { Valid: true } ? L.T($"入力値: {f.Value:N0}", $"Input value: {f.Value:N0}") : L.T("入力値: —", "Input value: —");
        calibrate.Enabled = setCenter.Enabled = canLearn && f is { Valid: true }; descriptor.Enabled = f is not null;
    }
    private void CancelLearning()
    {
        learningRequest++; preparing = detect = measuring = false; pairLearner = null; learningSide = -1;
        if (apply is not null) RefreshButtons();
    }
    private void Detect()
    {
        bool wasDetecting = detect; CancelLearning();
        if (!canLearn || wasDetecting) { ShowSelection(); return; }
        detect = true; baselines.Clear(); foreach (InputField f in inputs.Where(f => f.Valid)) baselines[f.Index] = f.Value;
        message.Text = L.T("割り当てたい入力だけを大きく動かしてください。検出した入力を全体へ割り当てます。", "Move only the input you want. It will be assigned to the whole axis."); RefreshButtons();
    }
    private async void LearnPair(int side)
    {
        bool wasLearning = learningSide == side && (preparing || pairLearner is not null); CancelLearning();
        if (wasLearning) { ShowSelection(); return; }
        if (selection.Buttons[side] is not null) { selection.SetButton(side, null); ShowSelection(); return; }
        if (!canLearn) return;
        int request = ++learningRequest; learningSide = side; preparing = true; RefreshButtons();
        try
        {
            bool ready = prepareLearning is null || await prepareLearning();
            if (IsDisposed || request != learningRequest) return;
            preparing = false;
            if (!ready) { CancelLearning(); ShowSelection(); return; }
            pairLearner = new(fields, false); RefreshButtons();
            message.Text = L.F("「{0}」にするボタンを押してください。", "Press the button for {0}.", AxisDirections.Label(axis, AxisDirections.ProfileSide(axis, side)));
        }
        catch (Exception ex) { if (!IsDisposed && request == learningRequest) { CancelLearning(); ShowSelection(); Ui.Error(this, ex); } }
    }
    public void OnInput(InputField field, int value)
    {
        if (IsDisposed) return;
        field.Value = value; field.Valid = true; dirtyInputs.Add(field); QueueInputView();
        if (pairLearner?.Observe(field, value) is LearnedInput learned)
        {
            selection.SetButton(learningSide, learned.Binding); CancelLearning(); ShowSelection();
            message.Text = L.T("割り当てました。同じ学習ボタンを押すと解除できます。", "Assigned. Press the same learning button to clear."); return;
        }
        if (!inputs.Contains(field) || !field.Learnable || field.Gyroscope) return;
        if (detect && !baselines.ContainsKey(field.Index)) baselines[field.Index] = value;
        if (detect && baselines.TryGetValue(field.Index, out int start) && Math.Abs((long)value - start) >= Math.Max(1, ((long)field.Maximum - field.Minimum) / 5))
        {
            selection.SelectWhole(field); CancelLearning(); ShowSelection();
            minimum.Value = Math.Min(start, value); maximum.Value = Math.Max(start, value); center.Value = start; measuring = true;
            message.Text = L.T("入力を検出しました。両端へ動かし、手を離して中心を記録してください。", "Input detected. Move to both ends, release it, then record the center.");
        }
        if (!ReferenceEquals(WholeInput, field)) return;
        if (measuring && value >= field.Minimum && value <= field.Maximum) { minimum.Value = Math.Min(minimum.Value, value); maximum.Value = Math.Max(maximum.Value, value); }
    }
    // Learning and calibration consume every event above. Live labels and
    // meters render the latest state once after that received batch completes.
    private void QueueInputView()
    {
        if (refreshQueued || !IsHandleCreated) return;
        refreshQueued = true;
        BeginInvoke(() =>
        {
            refreshQueued = false;
            if (IsDisposed) return;
            foreach (InputField input in dirtyInputs) source.RefreshValue(input);
            dirtyInputs.Clear();
            if (analogPair.Visible) analogPair.RefreshValues();
            if (analogPanel.Visible) RefreshLive();
        });
    }
    private void Apply()
    {
        if (!apply.Enabled) return;
        try
        {
            AxisBinding result = selection.Build();
            new DeviceProfile { Vid = 0x054c, Pid = 0x0268, Axes = [result, new(), new(), new()] }.Encode();
            Result = result; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { Ui.Error(this, ex); }
    }
    public void ConnectionLost() { CancelLearning(); DialogResult = DialogResult.Cancel; Close(); }
    internal void InputEventsLost()
    {
        bool interrupted = detect || measuring || pairLearner is not null || preparing;
        CancelLearning();
        if (interrupted) message.Text = L.T("入力履歴が欠けたため計測を中断しました。もう一度操作してください。", "Measurement stopped because input events were missed. Start again.");
    }
    protected override void OnFormClosed(FormClosedEventArgs e) { learningRequest++; base.OnFormClosed(e); }
    protected override void Dispose(bool disposing) { if (disposing) digitalTips.Dispose(); base.Dispose(disposing); }
}
