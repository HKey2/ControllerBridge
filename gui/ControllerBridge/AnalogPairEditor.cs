namespace ControllerBridge;

// Each half has its own raw released/pressed range. Summation uses equal
// positive and negative scales, matching the firmware before output encoding.
internal sealed class AnalogPairEditor : Panel
{
    private readonly HalfEditor negative, positive;
    private readonly Label preview = Ui.Label("", 11, true);
    internal AnalogPairEditor(AxisBinding binding, List<InputField> fields, bool canLearn)
    {
        Dock = DockStyle.Fill; AutoScroll = true;
        TableLayoutPanel root = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) root.RowStyles.Add(new(SizeType.AutoSize));
        negative = new(L.T("負方向（左 / 上）", "Negative (left / up)"), binding.NegativeAnalog, fields, canLearn);
        positive = new(L.T("正方向（右 / 下）", "Positive (right / down)"), binding.PositiveAnalog, fields, canLearn);
        negative.Changed += UpdatePreview; positive.Changed += UpdatePreview;
        root.Controls.Add(negative, 0, 0); root.Controls.Add(positive, 0, 1);
        preview.Dock = DockStyle.Fill; preview.Padding = new(4, 8, 4, 8); root.Controls.Add(preview, 0, 2);
        Controls.Add(root); UpdatePreview();
    }
    internal void Apply(AxisBinding binding)
    {
        AnalogHalfBinding n = negative.Read(), p = positive.Read();
        byte[] wire = new byte[32]; n.Write(wire, 0); p.Write(wire, 16);
        binding.NegativeAnalog = n; binding.PositiveAnalog = p;
    }
    internal void OnInput(InputField field)
    {
        negative.OnInput(field); positive.OnInput(field); UpdatePreview();
    }
    private void UpdatePreview()
    {
        int? n = negative.Strength, p = positive.Strength;
        preview.Text = n is null || p is null ? L.T("合成値: —（両方の入力を設定し、操作データを受信してください）", "Combined: — (configure both inputs and wait for input data)") :
            L.T($"合成値: {(p.Value - n.Value) / 655.35:+0.0;-0.0;0.0}%", $"Combined: {(p.Value - n.Value) / 655.35:+0.0;-0.0;0.0}%");
    }

    private sealed class HalfEditor : TableLayoutPanel
    {
        private readonly ComboBox source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, DropDownWidth = 600 };
        private readonly NumericUpDown neutral = Number(), maximum = Number();
        private readonly NumericUpDown deadzone = new() { Minimum = 0, Maximum = 50, Width = 75 };
        private readonly Label live = Ui.Label("", 10);
        private readonly AnalogHalfBinding original;
        internal event Action? Changed;
        private static NumericUpDown Number() => new() { Minimum = int.MinValue, Maximum = int.MaxValue, Width = 145, ThousandsSeparator = true };
        internal HalfEditor(string title, AnalogHalfBinding original, List<InputField> fields, bool canLearn)
        {
            this.original = original;
            Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; ColumnCount = 1; RowCount = 4; Padding = new(4, 6, 4, 8);
            ColumnStyles.Add(new(SizeType.Percent, 100));
            for (int i = 0; i < 4; i++) RowStyles.Add(new(SizeType.AutoSize));
            Controls.Add(Ui.Label(title, 11, true), 0, 0);
            source.Items.Add(L.T("入力元を選択", "Select an input source"));
            foreach (InputField field in fields.Where(f => f.Learnable && !f.Binary && !f.Hat)) source.Items.Add(field);
            source.SelectedIndex = 0; Controls.Add(source, 0, 1);
            FlowLayoutPanel numbers = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
            foreach ((string text, NumericUpDown box) in new[] { (L.T("未押下の値", "Released value"), neutral), (L.T("最大押下の値", "Fully pressed value"), maximum), (L.T("デッドゾーン %", "Dead zone %"), deadzone) })
            {
                FlowLayoutPanel column = new() { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Padding = new(0, 2, 12, 2) };
                column.Controls.Add(Ui.Label(text, 9)); column.Controls.Add(box); numbers.Controls.Add(column);
            }
            Controls.Add(numbers, 0, 2);
            FlowLayoutPanel tools = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
            live.MinimumSize = new(170, 36); live.TextAlign = ContentAlignment.MiddleLeft; tools.Controls.Add(live);
            Button released = Ui.Button(L.T("現在値を未押下に", "Use current as released"), (_, _) => { if (source.SelectedItem is InputField { Valid: true } f) neutral.Value = f.Value; });
            Button pressed = Ui.Button(L.T("現在値を最大押下に", "Use current as fully pressed"), (_, _) => { if (source.SelectedItem is InputField { Valid: true } f) maximum.Value = f.Value; });
            released.Enabled = pressed.Enabled = canLearn; tools.Controls.Add(released); tools.Controls.Add(pressed); Controls.Add(tools, 0, 3);
            source.SelectedIndexChanged += (_, _) =>
            {
                if (source.SelectedItem is InputField f)
                {
                    neutral.Value = f.Pressure ? f.Minimum : f.Valid ? Math.Clamp(f.Value, f.Minimum, f.Maximum) : AxisSourceList.Midpoint(f);
                    maximum.Value = neutral.Value == f.Maximum ? f.Minimum : f.Maximum; deadzone.Value = f.Pressure ? 0 : 5;
                }
                RefreshLive(); Changed?.Invoke();
            };
            foreach (NumericUpDown box in new[] { neutral, maximum, deadzone }) box.ValueChanged += (_, _) => Changed?.Invoke();
            if (original.Bits != 0)
            {
                for (int i = 1; i < source.Items.Count; i++)
                    if (source.Items[i] is InputField f && f.Interface == original.Interface && f.ReportId == original.ReportId && f.BitOffset == original.BitOffset && f.Bits == original.Bits && f.Flags == original.Flags) { source.SelectedIndex = i; break; }
                if (source.SelectedIndex == 0) { source.Items.Add(L.T("保存済み: ", "Saved: ") + original); source.SelectedIndex = source.Items.Count - 1; }
                neutral.Value = original.Neutral; maximum.Value = original.Maximum; deadzone.Value = original.Deadzone;
            }
            RefreshLive();
        }
        internal AnalogHalfBinding Read()
        {
            InputField? f = source.SelectedItem as InputField;
            bool saved = source.SelectedIndex > 0 && f is null;
            return new() { Interface = f?.Interface ?? (saved ? original.Interface : (byte)0), ReportId = f?.ReportId ?? (saved ? original.ReportId : (byte)0),
                BitOffset = f?.BitOffset ?? (saved ? original.BitOffset : (ushort)0), Bits = f?.Bits ?? (saved ? original.Bits : (byte)0),
                Flags = f?.Flags ?? (saved ? original.Flags : (byte)0), Neutral = (int)neutral.Value, Maximum = (int)maximum.Value, Deadzone = (ushort)deadzone.Value };
        }
        internal int? Strength => source.SelectedItem is InputField { Valid: true } f && neutral.Value != maximum.Value ? Read().Strength(f.Value) : null;
        internal void OnInput(InputField field) { if (source.SelectedItem == field) RefreshLive(); }
        private void RefreshLive() => live.Text = source.SelectedItem is InputField { Valid: true } f ? L.T($"入力値: {f.Value:N0}", $"Input value: {f.Value:N0}") : L.T("入力値: —", "Input value: —");
    }
}
