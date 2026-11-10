namespace ControllerBridge;

internal sealed record LearnedInput(InputField Field, DigitalBinding Binding);

internal sealed class ButtonLearner
{
    private readonly Dictionary<ushort, int> baseline;
    private readonly HashSet<ushort> armed = [];
    private readonly bool dpad;
    public ButtonLearner(IEnumerable<InputField> fields, bool dpad)
    {
        this.dpad = dpad;
        baseline = fields.Where(f => f.Valid && f.Learnable).ToDictionary(f => f.Index, f => f.Value);
        foreach (InputField f in fields.Where(f => f.Valid && f.Learnable))
            if (f.Binary ? f.Value == f.Minimum : f.Hat ? f.Value < f.Minimum || f.Value > f.Maximum : true) armed.Add(f.Index);
    }
    public LearnedInput? Observe(InputField f, int value)
    {
        if (!f.Learnable || !baseline.TryGetValue(f.Index, out int start)) return null;
        if (f.Binary)
        {
            if (value == f.Minimum) { armed.Add(f.Index); return null; }
            if (value == f.Maximum && armed.Contains(f.Index)) return new(f, f.Digital(1, value, value));
            return null;
        }
        if (f.Hat)
        {
            if (value < f.Minimum || value > f.Maximum) { armed.Add(f.Index); return null; }
            if (!armed.Contains(f.Index)) return null;
            int position = value - f.Minimum;
            if (!dpad) return new(f, f.Digital(1, value, value));
            bool eight = f.Maximum - f.Minimum == 7;
            if (eight && (position & 1) != 0) return null; // Ask for a cardinal press, preserve diagonals during play.
            return new(f, f.Digital((byte)(2 + (eight ? position / 2 : position)), f.Minimum, f.Maximum));
        }
        long span = (long)f.Maximum - f.Minimum, delta = (long)value - start;
        if (value < f.Minimum || value > f.Maximum || Math.Abs(delta) < Math.Max(1, span * 35 / 100)) return null;
        int threshold = (int)(start + delta / 2);
        if (delta > 0) threshold = Math.Max(start + 1, threshold);
        else threshold = Math.Min(start - 1, threshold);
        return new(f, delta > 0 ? f.Digital(1, threshold, f.Maximum) : f.Digital(1, f.Minimum, threshold));
    }
}

// Reused by the main window and the registration wizard. All edits stay in RAM
// until the owning window explicitly calls SaveAsync.
internal sealed partial class MappingEditor : UserControl
{
    private readonly bool mouseMode, shiftPage;
    private readonly Button shift, inherit;
    private ShiftDialog? shiftDialog;
    private readonly TableLayoutPanel buttonGrid;
    private readonly Label outputHeading;
    private readonly Label mouseHelp;
    private readonly Button[] buttons = new Button[18];
    private readonly Button[] axes = new Button[4];
    private readonly Label instruction;
    private readonly Label detail;
    private readonly Button clear;
    private readonly Button rapid;
    private readonly ToolTip tips = new() { AutoPopDelay = 15000 };
    private ButtonLearner? learner;
    private int selected = -1;
    private bool canLearn;
    private bool supportsRapid;
    private DeviceProfile profile = new();
    private List<InputField> fields = [];
    private AxisDialog? axisDialog;
    private AnalogStickWizard? stickWizard;
    private readonly Button axisWizard;
    private int learningRequest;
    public event EventHandler? Modified;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Func<Task<bool>>? PrepareLearningAsync { get; set; }
    public MappingEditor(bool mouseMode = false, bool shiftPage = false)
    {
        this.mouseMode = mouseMode; this.shiftPage = shiftPage;
        SuspendLayout();
        Dock = DockStyle.Fill; BackColor = Ui.Surface; Padding = new(8);
        AutoScaleDimensions = new(96, 96); AutoScaleMode = AutoScaleMode.Dpi; AutoScroll = true;
        // Keep complete controls at their preferred height, even when the host
        // window is small or an instruction wraps. The editor scrolls as needed.
        TableLayoutPanel root = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize));
        TableLayoutPanel heading = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1 };
        heading.ColumnStyles.Add(new(SizeType.Percent, 100)); heading.ColumnStyles.Add(new(SizeType.AutoSize));
        outputHeading = Ui.Label(mouseMode ? L.T("1  マウスの出力ボタンを選択", "1  Select a mouse output button") : L.T("1. コンソール側の対応ボタンを選択", "1. Select the corresponding console button"), 12, true);
        heading.Controls.Add(outputHeading, 0, 0);
        rapid = Ui.Button(L.T("連射設定…", "Rapid fire…"), (_, _) => EditRapidFire());
        shift = Ui.Button(L.T("シフト設定…", "Button shift…"), (_, _) => EditShift()); shift.Visible = !shiftPage;
        FlowLayoutPanel actions = new() { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false };
        actions.Controls.Add(rapid); actions.Controls.Add(shift); heading.Controls.Add(actions, 1, 0); root.Controls.Add(heading, 0, 0);
        rapid.Visible = !mouseMode;
        // Empty cells preserve the Switch controller's spatial groups. These
        // values are existing output IDs; their wire/profile order never changes.
        int[,] layout = {
            {  6,  4, -1, -1, -1, -1, -1, -1, -1,  5,  7 }, // ZL L       R ZR
            { 10, -1, 14, -1,  8, -1,  9, -1, -1,  3, -1 }, // L stick, up, -/+, X
            { -1, 17, -1, 15, -1, -1, -1, -1,  0, -1,  2 }, // left/right, Y/A
            { -1, -1, 16, -1, 13, -1, 12, -1, 11,  1, -1 }  // down, capture/home, R stick, B
        };
        if (mouseMode) layout = new int[,] { { 1, 2, -1 }, { 8, 9, 12 } };
        TableLayoutPanel grid = new() { Anchor = AnchorStyles.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = layout.GetLength(1), RowCount = layout.GetLength(0), Margin = new(0, 5, 0, 5), GrowStyle = TableLayoutPanelGrowStyle.FixedSize };
        // Equal fixed cell dimensions keep buttons square instead of stretching
        // with the window. WinForms scales both dimensions with the monitor DPI.
        buttonGrid = grid;
        for (int c = 0; c < grid.ColumnCount; c++) grid.ColumnStyles.Add(new(SizeType.Absolute, mouseMode ? 170 : 56));
        for (int r = 0; r < grid.RowCount; r++) grid.RowStyles.Add(new(SizeType.Absolute, mouseMode ? 72 : 56));
        for (int row = 0; row < grid.RowCount; row++)
        for (int column = 0; column < grid.ColumnCount; column++)
        {
            int i = layout[row, column]; if (i < 0) continue;
            Button b = Ui.Button(""); b.AutoSize = false; b.Dock = DockStyle.Fill; b.MinimumSize = Size.Empty;
            b.Padding = new(2); b.Font = new Font(i == 12 ? "Segoe UI Symbol" : L.FontFamily, i == 12 ? 16 : i is 10 or 11 or 13 ? 9 : 11, FontStyle.Bold);
            b.Click += (_, _) => SelectButton(i);
            buttons[i] = b; grid.Controls.Add(b, column, row);
        }
        root.Controls.Add(grid, 0, 1);
        TableLayoutPanel guide = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 2, Padding = new(6), BackColor = Ui.Selected };
        guide.ColumnStyles.Add(new(SizeType.Percent, 100)); guide.ColumnStyles.Add(new(SizeType.AutoSize));
        guide.RowStyles.Add(new(SizeType.AutoSize)); guide.RowStyles.Add(new(SizeType.AutoSize));
        instruction = Ui.Label(L.T("2. 変換対象コントローラーのボタンを押す", "2. Press a button on the input controller"), 11, true); instruction.Dock = DockStyle.Fill;
        detail = Ui.Label(L.T("ボタンを選ぶと、ここに操作手順を表示します。", "Select an output button to see the instructions here."), 9); detail.Dock = DockStyle.Fill; detail.ForeColor = Ui.Muted;
        clear = Ui.Button(shiftPage ? L.T("出力なし", "No output") : L.T("割り当て解除", "Clear mapping"), (_, _) => { if (selected < 0) return; learningRequest++; SetBinding(selected, new()); learner = null; UpdateButtons(); detail.Visible = true; instruction.Text = TargetName(selected) + L.T("：出力なし", ": no output"); detail.Text = L.T("割り当てを解除しました。デバイスに保存すると反映されます。", "Mapping cleared. Save to the device to apply it."); Modified?.Invoke(this, EventArgs.Empty); });
        guide.Controls.Add(instruction, 0, 0); guide.Controls.Add(detail, 0, 1); inherit = Ui.Button(L.T("表から継承", "Inherit from main"), (_, _) => {
            if (selected < 0) return; learningRequest++; profile.Shift.Overrides &= ~(1u << selected); profile.Shift.Buttons[selected] = new(); learner = null;
            UpdateButtons(); detail.Visible = true; instruction.Text = TargetName(selected) + L.T("：表から継承", ": inherit from main"); detail.Text = L.T("表ページの割り当てを引き継ぎます。", "This output inherits its main-page mapping."); Modified?.Invoke(this, EventArgs.Empty);
        }); inherit.Visible = shiftPage;
        FlowLayoutPanel mappingActions = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        mappingActions.Controls.Add(clear); mappingActions.Controls.Add(inherit);
        guide.Controls.Add(mappingActions, 1, 0); guide.SetRowSpan(mappingActions, 2); root.Controls.Add(guide, 0, 2);
        RaisedTableLayoutPanel axisGroup = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 2, Padding = new(6, 4, 6, 4), Margin = new(0, 6, 0, 4) };
        axisGroup.ColumnStyles.Add(new(SizeType.Percent, 100));
        axisGroup.RowStyles.Add(new(SizeType.AutoSize)); axisGroup.RowStyles.Add(new(SizeType.AutoSize));
        Label axisHeading = Ui.Label(L.T("アナログスティック", "Analog sticks"), 11, true);
        axisHeading.Anchor = AnchorStyles.Left;
        axisGroup.Controls.Add(axisHeading, 0, 0);
        axisGroup.Visible = !mouseMode && !shiftPage; root.Controls.Add(axisGroup, 0, 3);
        TableLayoutPanel axisRow = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 5, RowCount = 1 };
        axisRow.ColumnStyles.Add(new(SizeType.AutoSize));
        axisWizard = Ui.Button(L.T("設定ウィザード", "Setup wizard"), (_, _) => EditSticks());
        axisWizard.Dock = DockStyle.Fill; axisRow.Controls.Add(axisWizard, 0, 0);
        for (int i = 0; i < 4; i++)
        {
            axisRow.ColumnStyles.Add(new(SizeType.Percent, 25)); int axis = i;
            Button b = Ui.Button(""); b.Dock = DockStyle.Fill; b.Click += (_, _) => EditAxis(axis); axes[i] = b; axisRow.Controls.Add(b, i + 1, 0);
        }
        axisGroup.Controls.Add(axisRow, 0, 1);
        axisRow.Layout += (_, _) =>
        {
            foreach (Button button in axisRow.Controls)
            {
                int width = button.ClientSize.Width - button.Padding.Horizontal;
                if (width <= 0) continue;
                Size text = TextRenderer.MeasureText(button.Text, button.Font, new(width, int.MaxValue),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                int height = Math.Max(button.LogicalToDeviceUnits(40), text.Height + button.Padding.Vertical + button.LogicalToDeviceUnits(2));
                if (button.MinimumSize.Height != height) button.MinimumSize = new(button.MinimumSize.Width, height);
            }
        };
        Label axisHelp = Ui.Label(L.T("出力軸: Xは左→右、Yは上→下。2つのボタンからスティック軸も作れます。", "Output axes: X is left → right; Y is up → down. Two buttons can also control an axis."), 9);
        mouseHelp = axisHelp;
        axisHelp.Dock = DockStyle.Fill; axisHelp.ForeColor = Ui.Muted;
        if (mouseMode) {
            axisHelp.Text = L.T("左右ボタンはゲームパッドのB・Aと割り当てを共有します。\n「プロファイルをデバイスに保存」で反映されます。", "Mouse buttons share the gamepad B / A mappings.\nChoose “Save profile to device” to apply your changes.");
            root.Controls.Add(axisHelp, 0, 4);
        } else {
            foreach (Button axis in axes) tips.SetToolTip(axis, axisHelp.Text);
            axisHelp.Dispose();
        }
        Controls.Add(root); UpdateButtons();
        if (mouseMode) SetNavigationMode(false);
        ResumeLayout(true);
    }
    public void SetNavigationMode(bool enabled)
    {
        if (!mouseMode) return;
        outputHeading.Text = enabled ? L.T("1  マウス・操作ボタンの出力を選択", "1  Select a mouse or navigation output button") : L.T("1  マウスの出力ボタンを選択", "1  Select a mouse output button");
        foreach (int index in new[] { 8, 9, 12 }) buttons[index].Visible = enabled;
        // Use the already-scaled first row, including during initial construction.
        buttonGrid.RowStyles[1].Height = enabled ? buttonGrid.RowStyles[0].Height : 0;
    }
    public void Bind(DeviceProfile value, List<InputField> source, bool allowLearn, bool allowRapid = false)
    {
        using IDisposable layout = Ui.BatchLayout(this);
        detail.Visible = true;
        AbortLearning(); profile = value; fields = source; canLearn = allowLearn; supportsRapid = allowRapid; selected = -1;
        instruction.Text = allowLearn ? L.T("2. 変換対象コントローラーのボタンを押す", "2. Press a button on the input controller") : L.T("2. 対象機器をUSB-Aへ接続して割り当てたい操作を行う", "Connect this profile’s controller to USB-A to learn inputs");
        detail.Text = allowLearn ? L.T("入力を離した状態で出力ボタンを選んでください。", "Release all inputs, then select an output button.") : L.T("保存済みの割り当ては確認・解除できます。", "You can review and clear the saved mappings.");
        UpdateButtons();
    }
    public void AbortLearning(string? reason = null)
    {
        learningRequest++;
        learner = null;
        if (axisDialog is not null) axisDialog.ConnectionLost();
        stickWizard?.ConnectionLost();
        shiftDialog?.ConnectionLost();
        if (reason is not null) { instruction.Text = reason; detail.Text = L.T("入力を離してから、出力ボタンを選び直してください。", "Release all inputs, then select the output button again."); }
    }
    public async void SelectButton(int index)
    {
        if (index == 13 && OutputPresentation.XInput) return;
        int request = ++learningRequest; selected = index; learner = null; UpdateButtons();
        if (!profile.Enabled) { instruction.Text = L.T("先に「コントローラーを追加」を選んでください。", "Choose “Add controller” first."); return; }
        if (!canLearn) { instruction.Text = L.T("このプロファイルのコントローラーを接続してください。", "Connect the controller for this profile."); detail.Text = Binding(index).ToString(); return; }
        instruction.Text = L.T("現在の入力状態を確認しています…", "Checking the current input state…");
        if (PrepareLearningAsync is not null && !await PrepareLearningAsync()) return;
        if (request != learningRequest || IsDisposed) return;
        learner = new(fields, index >= 14);
        instruction.Text = L.T($"{TargetName(index)} に割り当てる操作を行ってください", $"Perform the action to assign to {TargetName(index)}.");
        detail.Text = ""; detail.Visible = false;
        ScrollControlIntoView(instruction.Parent!);
    }
    internal void InputEventsLost(string reason)
    {
        learningRequest++; learner = null;
        axisDialog?.InputEventsLost(); stickWizard?.InputEventsLost(); shiftDialog?.InputEventsLost(reason);
        instruction.Text = reason;
        detail.Text = L.T("入力を離してから、もう一度割り当ててください。", "Release the input and start the assignment again.");
    }
    public void OnInput(InputField field, int value)
    {
        axisDialog?.OnInput(field, value); shiftDialog?.OnInput(field, value);
        if (learner?.Observe(field, value) is not LearnedInput learned || selected < 0) return;
        detail.Visible = true;
        learned.Binding.RapidHz = Binding(selected).RapidHz;
        SetBinding(selected, learned.Binding); learner = null;
        instruction.Text = L.T($"{TargetName(selected)} ← {learned.Field.Label} を割り当てました", $"Assigned {TargetName(selected)} ← {learned.Field.Label}");
        detail.Text = learned.Binding + L.T("　／　デバイスに保存すると反映されます。", " / Save to the device to apply it.");
        UpdateButtons(); Modified?.Invoke(this, EventArgs.Empty);
    }
    public void ObserveAxisWizard(InputField field, int value, uint time) => stickWizard?.OnInput(field, value, time);
    public void RefreshAxisWizard() => stickWizard?.OnInputRefresh();
    private async void EditSticks()
    {
        if (!profile.Enabled || !canLearn) return;
        int request = ++learningRequest; learner = null;
        if (PrepareLearningAsync is not null && !await PrepareLearningAsync()) return;
        if (request != learningRequest || IsDisposed) return;
        using AnalogStickWizard dialog = new(profile, fields); stickWizard = dialog;
        try
        {
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
            DeviceProfile before = profile.Clone(); profile.Axes = dialog.Result;
            if (!before.SameContent(profile)) { UpdateButtons(); Modified?.Invoke(this, EventArgs.Empty); }
        }
        finally { stickWizard = null; }
    }
    private async void EditAxis(int axis)
    {
        if (!profile.Enabled) return;
        int request = ++learningRequest; learner = null;
        if (canLearn && PrepareLearningAsync is not null && !await PrepareLearningAsync()) return;
        if (request != learningRequest || IsDisposed) return;
        using AxisDialog dialog = new(axis, profile.Axes[axis], fields, canLearn, PrepareLearningAsync); axisDialog = dialog;
        try
        {
            if (dialog.ShowDialog(FindForm()) == DialogResult.OK) { profile.Axes[axis] = dialog.Result; UpdateButtons(); Modified?.Invoke(this, EventArgs.Empty); }
        }
        finally { axisDialog = null; }
    }
    private void EditRapidFire()
    {
        AbortLearning();
        DeviceProfile effective = profile.Clone();
        effective.Buttons = Enumerable.Range(0, 18).Select(i => ShiftSettings.Copy(Binding(i))).ToArray();
        using RapidFireDialog dialog = new(effective);
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        bool changed = false;
        for (int i = 0; i < 18; i++)
        {
            if (Binding(i).RapidHz == dialog.Rates[i]) continue;
            DigitalBinding binding = ShiftSettings.Copy(Binding(i)); binding.RapidHz = dialog.Rates[i];
            SetBinding(i, binding); changed = true;
        }
        if (!changed) return;
        UpdateButtons(); Modified?.Invoke(this, EventArgs.Empty);
        instruction.Text = L.T("連射設定を変更しました", "Rapid fire settings changed"); detail.Text = L.T("デバイスに保存すると、設定したボタンを押している間だけ連射します。", "Save to the device to enable rapid fire while holding the configured buttons.");
    }
    private DigitalBinding Binding(int i) => shiftPage && (profile.Shift.Overrides & (1u << i)) != 0 ? profile.Shift.Buttons[i] : profile.Buttons[i];
    private void SetBinding(int i, DigitalBinding value)
    {
        if (shiftPage) { profile.Shift.Overrides |= 1u << i; profile.Shift.Buttons[i] = value; }
        else profile.Buttons[i] = value;
    }
    private void EditShift()
    {
        if (!profile.Enabled || shiftPage) return;
        AbortLearning();
        using ShiftDialog dialog = new(profile, fields, canLearn, supportsRapid, PrepareLearningAsync); shiftDialog = dialog;
        try {
            if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
            profile.Shift = dialog.Result; UpdateButtons(); Modified?.Invoke(this, EventArgs.Empty);
        } finally { shiftDialog = null; }
    }
    private string TargetName(int index) => mouseMode ? index switch
        { 1 => L.T("左ボタン", "Left button"), 2 => L.T("右ボタン", "Right button"), 8 => OutputPresentation.XInput ? "Back" : "SELECT / −", 9 => OutputPresentation.XInput ? "Start" : "START / ＋", 12 => DeviceProfile.Targets[12], _ => DeviceProfile.Targets[index] } : DeviceProfile.Targets[index];
    private void UpdateButtons()
    {
        if (mouseMode) mouseHelp.Text = L.T($"左右ボタンはゲームパッドの{DeviceProfile.Targets[1]}・{DeviceProfile.Targets[2]}と割り当てを共有します。\n「プロファイルをデバイスに保存」で反映されます。",
            $"Mouse buttons share the gamepad {DeviceProfile.Targets[1]} / {DeviceProfile.Targets[2]} mappings.\nChoose “Save profile to device” to apply your changes.");
        for (int i = 0; i < 18; i++)
        {
            if (buttons[i] is null) continue;
            bool set = Binding(i).Mode != 0;
            buttons[i].Enabled = i != 13 || !OutputPresentation.XInput;
            buttons[i].Text = mouseMode ? TargetName(i) + (i is 1 or 2 ? L.T($"\n{DeviceProfile.Targets[i]}と共通", $"\nShared with {DeviceProfile.Targets[i]}") : "") : i switch { 10 => L.T("L\n押込", "L\nstick"), 11 => L.T("R\n押込", "R\nstick"), 12 => "⌂", 13 => OutputPresentation.XInput ? "—" : L.T("撮影", "CAP"), _ => DeviceProfile.Targets[i] };
            bool shiftedOverride = shiftPage && (profile.Shift.Overrides & (1u << i)) != 0;
            bool registeredShift = shiftedOverride && set;
            if (registeredShift) buttons[i].Text += " *";
            buttons[i].AccessibleName = TargetName(i) + L.T(" 出力ボタン", " output button");
            buttons[i].AccessibleDescription = (set ? L.T("割り当て済み: ", "Assigned: ") : "") + Binding(i);
            // A no-output override must still suppress inheritance, but it is not a registered input.
            string shiftStatus = !shiftPage ? "" : !shiftedOverride ? L.T("（表から継承）", " (inherited)") :
                registeredShift ? L.T("（裏ボタン登録あり）", " (registered shifted mapping)") : L.T("（裏ボタン登録なし）", " (no registered shifted mapping)");
            tips.SetToolTip(buttons[i], TargetName(i) + " ← " + Binding(i) + shiftStatus);
            Ui.ButtonColors(buttons[i], i == selected, set);
            buttons[i].Invalidate();
        }
        clear.Enabled = selected >= 0 && profile.Enabled;
        inherit.Enabled = shiftPage && selected >= 0 && profile.Enabled;
        shift.Enabled = profile.Enabled;
        axisWizard.Enabled = profile.Enabled && canLearn && fields.Any(StickCalibration.IsAnalog);
        tips.SetToolTip(axisWizard, canLearn ? L.T("左X、左Y、右X、右Yの順に操作して設定します。", "Configure left X, left Y, right X, then right Y by moving each axis.") :
            L.T("このプロファイルのコントローラーを接続してください。", "Connect the controller for this profile."));
        shift.Text = profile.Shift.Keys.Count == 0 ? L.T("シフト設定…", "Button shift…") : L.T($"シフト設定…（{profile.Shift.Keys.Count}キー）", $"Button shift… ({profile.Shift.Keys.Count} {(profile.Shift.Keys.Count == 1 ? "key" : "keys")})");
        int rapidCount = Enumerable.Range(0, 18).Count(i => Binding(i).RapidHz != 0);
        rapid.Text = supportsRapid ? (rapidCount == 0 ? L.T("連射設定…", "Rapid fire…") : L.T($"連射設定…（{rapidCount}）", $"Rapid fire… ({rapidCount})")) : L.T("連射設定（デバイスの更新が必要）", "Rapid fire (update required)");
        rapid.Enabled = profile.Enabled && supportsRapid;
        tips.SetToolTip(rapid, supportsRapid ? L.T("ボタン別にOFF / 毎秒1～60回を設定します。新規設定は20回です。", "Set each button to OFF or 1–60 presses per second. New settings start at 20.") : L.T("ファームウェア0.6.0-dev以降で使用できます。", "Available with firmware 0.6.0-dev or later."));
        string[] shortNames = [L.T("左 X", "Left X"), L.T("左 Y", "Left Y"), L.T("右 X", "Right X"), L.T("右 Y", "Right Y")];
        for (int i = 0; i < 4; i++) axes[i].Text = shortNames[i] + " · " + (profile.Axes[i].Mode switch { 1 => L.T("アナログ", "Analog"), 2 => L.T("2ボタン", "2 buttons"), _ => L.T("未使用", "Unused") });
    }
}
