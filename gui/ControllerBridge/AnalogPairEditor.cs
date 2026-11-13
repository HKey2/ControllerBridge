namespace ControllerBridge;

internal sealed class AnalogPairEditor : TableLayoutPanel
{
    private readonly HalfEditor[] halves;
    private readonly int axis;
    private readonly Label preview = Ui.Label("", 11, true);
    internal event Action? Changed;
    internal AnalogPairEditor(List<InputField> fields, bool canLearn, Func<InputField, string> label, int axis = 0)
    {
        this.axis = axis;
        Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; ColumnCount = 2; RowCount = 2;
        ColumnStyles.Add(new(SizeType.Percent, 50)); ColumnStyles.Add(new(SizeType.Percent, 50));
        RowStyles.Add(new(SizeType.AutoSize)); RowStyles.Add(new(SizeType.AutoSize));
        halves = [new(AxisDirections.Label(axis, 0), fields, canLearn, label),
            new(AxisDirections.Label(axis, 1), fields, canLearn, label)];
        for (int i = 0; i < 2; i++) { halves[i].Changed += () => { UpdatePreview(); Changed?.Invoke(); }; Controls.Add(halves[i], i, 0); }
        preview.AutoSize = false; preview.Size = new(350, 26);
        Controls.Add(preview, 0, 1); SetColumnSpan(preview, 2);
    }
    internal void Bind(AxisSelection selection)
    {
        halves[0].Bind(selection.Halves[AxisDirections.ProfileSide(axis, 0)]); halves[1].Bind(selection.Halves[AxisDirections.ProfileSide(axis, 1)]); UpdatePreview();
    }
    internal void RefreshValues() { foreach (HalfEditor half in halves) half.RefreshLive(); UpdatePreview(); }
    private void UpdatePreview()
    {
        int? negative = halves[0].Strength, positive = halves[1].Strength;
        if (negative is null || positive is null) { preview.Text = L.T("出力: —（入力待ち）", "Output: — (waiting for input)"); return; }
        int difference = positive.Value - negative.Value;
        preview.Text = difference == 0 ? L.T("出力: 中心 0.0%", "Output: Center 0.0%") :
            L.F("出力: {0} {1:+0.0;-0.0;0.0}%", "Output: {0} {1:+0.0;-0.0;0.0}%",
                AxisDirections.Name(axis, difference > 0 ? 1 : 0), difference / 655.35);
    }
    private sealed class HalfEditor : TableLayoutPanel
    {
        private readonly string title;
        private readonly List<InputField> fields;
        private readonly Func<InputField, string> label;
        private readonly bool canLearn;
        private readonly Label heading = Ui.Label("", 10, true), live = Ui.Label("", 9);
        private readonly NumericUpDown neutral = Number(), maximum = Number();
        private readonly NumericUpDown deadzone = new() { Minimum = 0, Maximum = 50, Width = 90 };
        private readonly Button released, pressed;
        private AnalogHalfBinding? binding;
        private InputField? input;
        private bool loading;
        internal event Action? Changed;
        private static NumericUpDown Number() => new() { Minimum = int.MinValue, Maximum = int.MaxValue, Width = 125, ThousandsSeparator = true };
        internal HalfEditor(string title, List<InputField> fields, bool canLearn, Func<InputField, string> label)
        {
            this.title = title; this.fields = fields; this.canLearn = canLearn; this.label = label;
            Dock = DockStyle.Top; AutoSize = true; AutoSizeMode = AutoSizeMode.GrowAndShrink; ColumnCount = 1; RowCount = 3; Padding = new(2, 4, 2, 4);
            ColumnStyles.Add(new(SizeType.Percent, 100)); for (int i = 0; i < 3; i++) RowStyles.Add(new(SizeType.AutoSize));
            heading.Dock = DockStyle.Fill; Controls.Add(heading, 0, 0);
            FlowLayoutPanel numbers = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = false };
            foreach ((string text, NumericUpDown number) in new[] { (L.T("未押下 / 中立", "Released / neutral"), neutral), (L.T("最大入力", "Full input"), maximum), (L.T("デッドゾーン %", "Dead zone %"), deadzone) })
            {
                FlowLayoutPanel column = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = new(2) };
                column.Controls.Add(Ui.Label(text, 9)); column.Controls.Add(number); numbers.Controls.Add(column);
                number.ValueChanged += (_, _) =>
                {
                    if (loading || binding is null) return;
                    binding.Neutral = (int)neutral.Value; binding.Maximum = (int)maximum.Value; binding.Deadzone = (ushort)deadzone.Value;
                    Changed?.Invoke();
                };
            }
            Controls.Add(numbers, 0, 1);
            FlowLayoutPanel tools = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill, WrapContents = false };
            released = Ui.Button(L.T("未押下を記録", "Record released"), (_, _) => { if (input is { Valid: true }) neutral.Value = input.Value; });
            pressed = Ui.Button(L.T("最大入力を記録", "Record full input"), (_, _) => { if (input is { Valid: true }) maximum.Value = input.Value; });
            live.AutoSize = false; live.Size = new(115, 40); live.AutoEllipsis = true;
            tools.Controls.Add(released); tools.Controls.Add(pressed); tools.Controls.Add(live); Controls.Add(tools, 0, 2);
        }
        internal void Bind(AnalogHalfBinding? value)
        {
            binding = value; input = fields.FirstOrDefault(f => AxisSelection.Matches(f, value)); loading = true;
            neutral.Value = value?.Neutral ?? 0; maximum.Value = value?.Maximum ?? 0; deadzone.Value = value?.Deadzone ?? 0;
            loading = false;
            heading.Text = title + ": " + (value is null ? L.T("未割り当て", "Unassigned") : input is null ? L.T("保存済みの入力", "Saved input") : label(input));
            neutral.Enabled = maximum.Enabled = deadzone.Enabled = value is not null;
            RefreshLive();
        }
        internal int? Strength => binding is null ? 0 : input is { Valid: true } ? binding.Strength(input.Value) : null;
        internal void RefreshLive()
        {
            live.Text = binding is null ? "" : input is { Valid: true } ? L.T($"入力 {input.Value:N0}", $"Input {input.Value:N0}") : L.T("入力待ち", "Waiting");
            released.Enabled = pressed.Enabled = canLearn && binding is not null && input is { Valid: true };
        }
    }
}
