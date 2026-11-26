namespace ControllerBridge;

internal sealed class GameCubeTriggerDialog : Form
{
    internal AnalogHalfBinding? Result { get; private set; }
    private readonly AxisSourceList source = new();
    private readonly List<InputField> inputs;
    private readonly AnalogPairEditor.HalfEditor editor;
    private readonly Dictionary<ushort, int> baselines = [];
    private readonly Label summary = Ui.Label("", 11, true), message = Ui.Label("", 10);
    private readonly Button detectButton, measure, released, restore, apply;
    private readonly bool canLearn;
    private bool detect, measuring, refreshQueued;
    private InputField? Input => inputs.FirstOrDefault(f => AxisSelection.Matches(f, Result));
    internal GameCubeTriggerDialog(int trigger, AnalogHalfBinding? original, List<InputField> fields, bool canLearn)
    {
        this.canLearn = canLearn; Result = original;
        string title = trigger == 0 ? "GC L" : "GC R";
        inputs = fields.Where(f => f.Learnable && !f.Binary && !f.Hat && !f.Motion).ToList();
        if (original is not null && !inputs.Any(f => AxisSelection.Matches(f, original)))
            inputs.Add(new() { Index = ushort.MaxValue, Interface = original.Interface, ReportId = original.ReportId,
                BitOffset = original.BitOffset, Bits = original.Bits, Flags = original.Flags,
                Minimum = Math.Min(original.Neutral, original.Maximum), Maximum = Math.Max(original.Neutral, original.Maximum) });
        string InputName(InputField f) => f.Index == ushort.MaxValue ? L.T("保存済みの入力", "Saved input") : f.Label;
        using IDisposable layout = Ui.Form(this, title + L.T(" の設定", " settings"), new(1060, 740), new(900, 660));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 8 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int row = 0; row < 8; row++) root.RowStyles.Add(new(row == 3 ? SizeType.Percent : SizeType.AutoSize, row == 3 ? 100 : 0));
        root.Controls.Add(Ui.Label(title + L.T(" の入力割り当て", " input assignment"), 17, true), 0, 0);
        Label help = Ui.Label(L.T("入力を選択。同じボタンをもう一度押すと解除します。未押下から最大入力までの範囲を設定してください。",
            "Choose an input. Press the same button again to clear. Set the range from released to full input."), 10);
        help.Dock = DockStyle.Fill; root.Controls.Add(help, 0, 1);
        FlowLayoutPanel tools = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        detectButton = Ui.Button(L.T("操作して入力軸を検出", "Move to detect an input axis"), (_, _) => Detect());
        measure = Ui.Button(L.T("可動範囲を計測", "Measure range"), (_, _) =>
        {
            if (Input is not { Valid: true } input || Result is null) return;
            detect = false; measuring = true; Result.Neutral = Result.Maximum = input.Value; RefreshSelection();
            message.Text = L.T("入力を最大まで操作し、手を離して「未押下を記録」を押してください。", "Move to full input, release it, then choose Record released.");
        });
        released = Ui.Button(L.T("未押下を記録", "Record released"), (_, _) =>
        {
            if (Input is not { Valid: true } input || Result is null) return;
            Result.Neutral = input.Value; measuring = false; RefreshSelection();
            message.Text = L.T("未押下を記録しました。適用後、メイン画面でデバイスに保存してください。", "Released recorded. Apply, then save to the device from the main window.");
        });
        restore = Ui.Button(L.T("元の範囲に戻す", "Restore input range"), (_, _) =>
        {
            if (Input is not { } input || Result is null) return;
            Result.Neutral = input.Minimum; Result.Maximum = input.Maximum; measuring = false; RefreshSelection();
        });
        tools.Controls.Add(detectButton); tools.Controls.Add(measure); tools.Controls.Add(released); tools.Controls.Add(restore); root.Controls.Add(tools, 0, 2);
        Panel area = new() { Dock = DockStyle.Fill, MinimumSize = new(0, 150), Margin = new(0, 0, 0, 6) };
        source.SetInputs(inputs, InputName, unipolar: true); area.Controls.Add(source); area.SizeChanged += (_, _) => source.FitToArea(); root.Controls.Add(area, 0, 3);
        summary.Dock = DockStyle.Fill; root.Controls.Add(summary, 0, 4);
        editor = new(title, inputs, canLearn, InputName, rangeCapture: true); root.Controls.Add(editor, 0, 5);
        editor.Changed += () => { measuring = false; RefreshSelection(); };
        message.Dock = DockStyle.Fill; message.ForeColor = Ui.Muted;
        message.Text = L.T("GC出力で使用できます。ゲーム側がGameCubeアナログL/Rに対応している必要があります。\n", "Available in GC output. The game must support GameCube analog L/R.\n") + L.ProfileSaveNotice;
        root.Controls.Add(message, 0, 6);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        apply = Ui.Button(L.T("適用", "Apply"), (_, _) =>
        {
            try { Result?.Write(new byte[16], 0); DialogResult = DialogResult.OK; Close(); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { Ui.Error(this, ex); }
        }, true);
        Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(apply); footer.Controls.Add(cancel); root.Controls.Add(footer, 0, 7);
        source.Choose += (field, _) => { detect = measuring = false; Result = AxisSelection.Matches(field, Result) ? null : Binding(field); RefreshSelection(); };
        Controls.Add(root); CancelButton = cancel; Ui.ThemeInputs(this); RefreshSelection();
    }
    private static AnalogHalfBinding Binding(InputField f) => new() { Interface = f.Interface, ReportId = f.ReportId,
        BitOffset = f.BitOffset, Bits = f.Bits, Flags = (byte)(f.Flags & InputDirections.SourceFlags),
        Neutral = f.MinimumNeutral ? f.Minimum : f.Valid ? f.Value : AxisSourceList.Midpoint(f),
        Maximum = f.Valid && f.Value == f.Maximum ? f.Minimum : f.Maximum, Deadzone = (ushort)(f.Pressure ? 0 : 5) };
    private void Detect()
    {
        detect = !detect; measuring = false; baselines.Clear();
        foreach (InputField f in inputs.Where(f => f.Valid)) baselines[f.Index] = f.Value;
        RefreshSelection(); message.Text = L.T("割り当てたい入力だけを動かしてください。", "Move only the input you want to assign.");
    }
    private void RefreshSelection()
    {
        source.ShowSingleSelection(Result); editor.Bind(Result); RefreshPreview();
        detectButton.Enabled = canLearn; Ui.ButtonColors(detectButton, detect);
        detectButton.Text = detect ? L.T("入力を検出中 · 押すと中止", "Detecting · press to cancel") : L.T("操作して入力軸を検出", "Move to detect an input axis");
        apply.Enabled = !detect && !measuring;
    }
    private void RefreshPreview()
    {
        InputField? input = Input;
        measure.Enabled = released.Enabled = canLearn && input is { Valid: true }; restore.Enabled = input is not null;
        summary.Text = Result is null ? L.T("未割り当て", "Unassigned") : input is { Valid: true } ?
            L.T($"出力: {Result.Strength(input.Value) / 655.35:0.0}%", $"Output: {Result.Strength(input.Value) / 655.35:0.0}%") : L.T("出力: —（入力待ち）", "Output: — (waiting for input)");
    }
    internal void ConnectionLost() { DialogResult = DialogResult.Cancel; Close(); }
    internal void InputEventsLost()
    {
        if (detect || measuring) message.Text = L.T("入力履歴が欠けたため計測を中断しました。もう一度操作してください。", "Measurement stopped because input events were missed. Start again.");
        detect = measuring = false; RefreshSelection();
    }
    internal void OnInput(InputField field, int value)
    {
        if (IsDisposed) return;
        field.Value = value; field.Valid = true;
        if (detect && inputs.Contains(field))
        {
            if (!baselines.TryGetValue(field.Index, out int start)) baselines[field.Index] = value;
            else if (Math.Abs((long)value - start) >= Math.Max(1, ((long)field.Maximum - field.Minimum) / 5))
            {
                Result = Binding(field); Result.Neutral = start; Result.Maximum = value;
                detect = false; measuring = true; RefreshSelection();
                message.Text = L.T("入力を検出しました。最大まで操作し、手を離して未押下を記録してください。", "Input detected. Move to full input, release it, then record released.");
            }
        }
        if (measuring && ReferenceEquals(Input, field) && Result is not null &&
            Math.Abs((long)value - Result.Neutral) > Math.Abs((long)Result.Maximum - Result.Neutral)) Result.Maximum = value;
        if (refreshQueued || !IsHandleCreated) return;
        refreshQueued = true;
        BeginInvoke(() => { refreshQueued = false; if (IsDisposed) return; foreach (InputField f in inputs) source.RefreshValue(f); editor.Bind(Result); RefreshPreview(); });
    }
}
