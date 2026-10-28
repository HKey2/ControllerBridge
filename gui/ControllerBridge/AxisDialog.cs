namespace ControllerBridge;

internal sealed partial class AxisDialog : Form
{
    public AxisBinding Result { get; private set; }
    private readonly List<InputField> fields;
    private readonly ComboBox mode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly AxisSourceList source = new();
    private readonly NumericUpDown minimum = Number(), center = Number(), maximum = Number();
    private readonly NumericUpDown deadzone = new() { Minimum = 0, Maximum = 50, Value = 5, Width = 75 };
    private readonly CheckBox invert = new() { Text = L.T("向きを反転", "Invert direction"), AutoSize = true, Padding = new(4) };
    private readonly Label live = Ui.Label(L.T("入力値: —", "Input value: —"), 12, true), message = Ui.Label("", 10);
    private readonly Label negative = Ui.Label(L.T("未割り当て", "Unassigned"), 9), positive = Ui.Label(L.T("未割り当て", "Unassigned"), 9);
    private readonly Panel analogPanel = new() { Dock = DockStyle.Fill, AutoScroll = true }, pairPanel = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Dictionary<ushort, int> baselines;
    private bool detect, measuring;
    private ButtonLearner? pairLearner;
    private int pairIndex;
    private readonly bool canLearn;
    private readonly Func<Task<bool>>? prepareLearning;
    private readonly TableLayoutPanel analogLayout;
    private static NumericUpDown Number() => new() { Minimum = int.MinValue, Maximum = int.MaxValue, Width = 145, ThousandsSeparator = true };
    public AxisDialog(int axis, AxisBinding original, List<InputField> fields, bool canLearn, Func<Task<bool>>? prepareLearning = null)
    {
        this.fields = fields; this.canLearn = canLearn;
        this.prepareLearning = prepareLearning;
        Result = new DeviceProfile { Flags = 0, Axes = [original, new(), new(), new()] }.Clone().Axes[0];
        baselines = fields.Where(f => f.Valid).ToDictionary(f => f.Index, f => f.Value);
        Ui.Form(this, DeviceProfile.AxisNames[axis] + L.T(" の設定", " settings"), new(860, 570)); MaximizeBox = false;
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.Controls.Add(Ui.Label(DeviceProfile.AxisNames[axis], 17, true), 0, 0);
        mode.Items.AddRange([L.T("未使用（中心に固定）", "Unused (centered)"), L.T("アナログ入力", "Analog input"), L.T("2つのボタンで負方向 / 正方向", "Two buttons for negative / positive")]);
        mode.SelectedIndex = Result.Mode; root.Controls.Add(mode, 0, 1);
        Panel content = new() { Dock = DockStyle.Fill }; content.Controls.Add(analogPanel); content.Controls.Add(pairPanel); root.Controls.Add(content, 0, 2);
        TableLayoutPanel a = analogLayout = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 6 };
        a.RowStyles.Add(new(SizeType.AutoSize)); a.RowStyles.Add(new(SizeType.Absolute, 56));
        for (int row = 2; row < 6; row++) a.RowStyles.Add(new(SizeType.AutoSize));
        foreach (InputField f in fields.Where(f => f.Learnable && !f.Binary && !f.Hat)) source.Items.Add(f);
        Label axisHint = Ui.Label(source.Items.Count == 0 ? L.T("利用できるアナログ入力軸がありません。", "No analog input axes are available.") :
            L.T("入力軸を選択  ·  バーの中央 = 記述子の中心 / 右端 = 生の入力値", "Select an input axis  ·  Meter center = descriptor midpoint / right = raw value"), 9);
        a.Controls.Add(axisHint, 0, 0); a.Controls.Add(source, 0, 1);
        FlowLayoutPanel tools = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
        Button detectButton = Ui.Button(L.T("操作して入力軸を検出", "Move to detect an input axis"), (_, _) =>
        {
            pairLearner = null; detect = true; measuring = false; baselines.Clear();
            foreach (InputField f in fields.Where(f => f.Valid)) baselines[f.Index] = f.Value;
            message.Text = L.T("設定する1軸だけを大きく動かしてください。", "Move only the axis you want to configure through a large range.");
        }); detectButton.Enabled = canLearn; tools.Controls.Add(detectButton);
        Button calibrate = Ui.Button(L.T("可動範囲を計測", "Measure range"), (_, _) =>
        {
            if (source.SelectedItem is not InputField f || !f.Valid) return;
            minimum.Value = maximum.Value = center.Value = f.Value; measuring = true; detect = false;
            message.Text = L.T("両端まで往復した後、手を離して「中心を記録」を押してください。", "Move to both ends, release the control, then choose “Record center”.");
        }); calibrate.Enabled = canLearn; tools.Controls.Add(calibrate); a.Controls.Add(tools, 0, 2);
        FlowLayoutPanel read = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false }; read.Controls.Add(live);
        Button setCenter = Ui.Button(L.T("中心を記録", "Record center"), (_, _) => { if (source.SelectedItem is InputField f && f.Valid) { center.Value = f.Value; measuring = false; message.Text = L.T("中心を記録しました。最小・中心・最大を確認して適用します。", "Center recorded. Check the minimum, center, and maximum before applying."); } });
        setCenter.Enabled = canLearn; read.Controls.Add(setCenter);
        read.Controls.Add(Ui.Button(L.T("記述子の範囲を使う", "Use descriptor range"), (_, _) => { if (source.SelectedItem is InputField f) { minimum.Value = f.Minimum; maximum.Value = f.Maximum; center.Value = f.Valid ? Math.Clamp(f.Value, f.Minimum, f.Maximum) : (int)(((long)f.Minimum + f.Maximum) / 2); } }));
        a.Controls.Add(read, 0, 3);
        FlowLayoutPanel numbers = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
        foreach ((string title, NumericUpDown box) in new[] { (L.T("最小（左 / 上）", "Minimum (left / up)"), minimum), (L.T("中心", "Center"), center), (L.T("最大（右 / 下）", "Maximum (right / down)"), maximum) })
        { FlowLayoutPanel column = new() { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Padding = new(0, 3, 12, 3) }; column.Controls.Add(Ui.Label(title, 9)); column.Controls.Add(box); numbers.Controls.Add(column); }
        a.Controls.Add(numbers, 0, 4);
        FlowLayoutPanel options = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink }; options.Controls.Add(Ui.Label(L.T("デッドゾーン %", "Dead zone %"))); options.Controls.Add(deadzone); options.Controls.Add(invert); a.Controls.Add(options, 0, 5); analogPanel.Controls.Add(a);
        TableLayoutPanel p = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, RowCount = 4, ColumnCount = 1 };
        for (int row = 0; row < 4; row++) p.RowStyles.Add(new(SizeType.AutoSize));
        Button neg = Ui.Button(L.T("負方向（左 / 上）を学習", "Learn negative (left / up)"), (_, _) => LearnPair(0)); neg.Enabled = canLearn;
        Button pos = Ui.Button(L.T("正方向（右 / 下）を学習", "Learn positive (right / down)"), (_, _) => LearnPair(1)); pos.Enabled = canLearn;
        p.Controls.Add(neg, 0, 0); p.Controls.Add(negative, 0, 1); p.Controls.Add(pos, 0, 2); p.Controls.Add(positive, 0, 3); pairPanel.Controls.Add(p);
        message.Dock = DockStyle.Fill; message.Padding = new(0, 8, 0, 8); message.ForeColor = Ui.Muted; root.Controls.Add(message, 0, 3);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft };
        Button apply = Ui.Button(L.T("適用", "Apply"), (_, _) => Apply(), true); Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(apply); footer.Controls.Add(cancel); CancelButton = cancel; root.Controls.Add(footer, 0, 4); Controls.Add(root);
        source.SelectedIndexChanged += (_, _) =>
        {
            if (source.SelectedItem is not InputField f) return;
            minimum.Value = f.Minimum; maximum.Value = f.Maximum; center.Value = f.Valid ? f.Value : (int)(((long)f.Minimum + f.Maximum) / 2);
            live.Text = f.Valid ? L.T($"入力値: {f.Value}", $"Input value: {f.Value}") : L.T("入力値: —", "Input value: —"); measuring = false;
        };
        mode.SelectedIndexChanged += (_, _) => { pairLearner = null; detect = measuring = false; ShowMode(); };
        if (Result.Mode == 1)
        {
            AnalogBinding old = Result.Analog;
            for (int i = 0; i < source.Items.Count; i++) if (source.Items[i] is InputField f && f.Interface == old.Interface && f.ReportId == old.ReportId && f.BitOffset == old.BitOffset) source.SelectedIndex = i;
            minimum.Value = old.Minimum; center.Value = old.Center; maximum.Value = old.Maximum; deadzone.Value = old.Deadzone; invert.Checked = (old.Flags & 4) != 0;
        }
        else if (source.Items.Count > 0) source.SelectedIndex = 0;
        negative.Text = Result.Negative.ToString(); positive.Text = Result.Positive.ToString(); Ui.ThemeInputs(this); ShowMode();
    }
    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e); FitAxisList();
    }
    private void FitAxisList()
    {
        if (!IsHandleCreated || mode.SelectedIndex != 1) return;
        int U(int value) => LogicalToDeviceUnits(value);
        Rectangle work = Screen.FromControl(this).WorkingArea;
        int rows = Math.Max(1, Math.Min(Math.Max(1, source.Items.Count), (work.Height - U(560)) / U(54)));
        int columns = Math.Max(1, (source.Items.Count + rows - 1) / rows);
        rows = Math.Max(1, (source.Items.Count + columns - 1) / columns);
        int width = Math.Min(work.Width - U(32), U(860) * columns);
        MinimumSize = new(Math.Min(width, U(860)) + Width - ClientSize.Width, U(570));
        ClientSize = new(width, Math.Min(work.Height - (Height - ClientSize.Height) - U(24), U(490) + rows * U(54)));
        PerformLayout();
        analogLayout.RowStyles[1].Height = source.Fit(rows, analogLayout.ClientSize.Width - source.Margin.Horizontal) + source.Margin.Vertical;
        PerformLayout();
        // Prevent partially visible rows after the user resizes the dialog smaller.
        MinimumSize = Size;
        Location = new(Math.Clamp(Left, work.Left, Math.Max(work.Left, work.Right - Width)),
            Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - Height)));
    }
    private void ShowMode()
    {
        analogPanel.Visible = mode.SelectedIndex == 1; pairPanel.Visible = mode.SelectedIndex == 2;
        message.Text = mode.SelectedIndex switch { 0 => L.T("この軸は中心に固定されます。", "This axis remains centered."), 1 => L.T("軸を選択 → 両端まで往復 → 手を離して中心を記録。直接数値を調整することもできます。", "Select an axis → move to both ends → release and record the center. You can also edit the values directly."), _ => L.T("入力を離した状態で学習ボタンを選んでください。同時押しは中心になります。", "Release all inputs before learning. Pressing both buttons centers the axis.") };
        if (!canLearn && mode.SelectedIndex != 0) message.Text = L.T("入力コントローラー未接続。保存済みの数値を編集できます。", "No input controller is connected. You can edit the saved values.");
        FitAxisList();
    }
    private async void LearnPair(int index)
    {
        pairLearner = null;
        if (prepareLearning is not null && !await prepareLearning()) return;
        if (IsDisposed) return;
        pairIndex = index; pairLearner = new(fields, false);
        message.Text = (index == 0 ? L.T("負方向", "Negative") : L.T("正方向", "Positive")) + L.T("にするボタンを押してください。", ": press the button you want to assign.");
    }
    public void OnInput(InputField field, int value)
    {
        if (pairLearner?.Observe(field, value) is LearnedInput learned)
        {
            if (pairIndex == 0) { Result.Negative = learned.Binding; negative.Text = learned.Field.Label + " / " + learned.Binding; }
            else { Result.Positive = learned.Binding; positive.Text = learned.Field.Label + " / " + learned.Binding; }
            pairLearner = null; message.Text = L.T("割り当てました。もう一方も指定して適用してください。", "Assigned. Configure the other direction, then apply.");
        }
        if (mode.SelectedIndex != 1 || !field.Learnable || field.Binary || field.Hat) return;
        source.RefreshValue(field);
        if (detect && !baselines.ContainsKey(field.Index)) baselines[field.Index] = value;
        if (detect && baselines.TryGetValue(field.Index, out int start) && Math.Abs((long)value - start) >= Math.Max(1, ((long)field.Maximum - field.Minimum) / 5))
        {
            source.SelectedItem = field; minimum.Value = Math.Min(start, value); maximum.Value = Math.Max(start, value); center.Value = start;
            detect = false; measuring = true; message.Text = L.T("軸を検出しました。両端へ往復した後、手を離して中心を記録してください。", "Axis detected. Move to both ends, then release and record the center.");
        }
        if (source.SelectedItem != field) return;
        live.Text = L.T($"入力値: {value}", $"Input value: {value}");
        if (measuring && value >= field.Minimum && value <= field.Maximum) { minimum.Value = Math.Min(minimum.Value, value); maximum.Value = Math.Max(maximum.Value, value); }
    }
    private void Apply()
    {
        try
        {
            Result.Mode = (byte)mode.SelectedIndex;
            if (Result.Mode == 1)
            {
                if (source.SelectedItem is InputField f) Result.Analog = f.Analog((int)minimum.Value, (int)center.Value, (int)maximum.Value, (ushort)deadzone.Value, invert.Checked);
                else if (Result.Analog.Bits == 0) throw new InvalidDataException(L.T("アナログ入力軸を選択してください。", "Select an analog input axis."));
                else { Result.Analog.Minimum = (int)minimum.Value; Result.Analog.Center = (int)center.Value; Result.Analog.Maximum = (int)maximum.Value; Result.Analog.Deadzone = (ushort)deadzone.Value; Result.Analog.Flags = (byte)((Result.Analog.Flags & 3) | (invert.Checked ? 4 : 0)); }
            }
            new DeviceProfile { Axes = [Result, new(), new(), new()] }.Encode();
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { Ui.Error(this, ex); }
    }
    public void ConnectionLost() { DialogResult = DialogResult.Cancel; Close(); }
}
