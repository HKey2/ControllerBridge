namespace ControllerBridge;

internal sealed class AnalogStickWizard : Form
{
    private readonly StickCalibration session;
    private readonly Button[] steps = new Button[4];
    private readonly StickRangeView[] views = [new(), new()];
    private readonly Label instruction = Ui.Label("", 11, true);
    private readonly Button next, back, skip;
    private readonly CheckBox invert = new() { Text = L.T("向きを反転", "Invert direction"), AutoSize = true, Margin = new(8, 12, 12, 4) };
    private bool updating, refreshQueued;
    private GyroWizard? gyroWizard;
    private readonly List<InputField> fields;
    private readonly ToolTip gyroTips = new() { AutoPopDelay = 15000 };
    internal uint RecenterButtons => session.RecenterButtons;
    private long lastRefresh = Environment.TickCount64;
    internal AxisBinding[] Result => session.Result;
    private static string ReadyHint => L.T("軸を示す帯が固定されると確定可能です。", "Confirm once the axis band is fixed.");

    internal AnalogStickWizard(DeviceProfile original, List<InputField> fields)
    {
        this.fields = fields; session = new(original, fields); session.Start();
        using IDisposable layout = Ui.Form(this, L.T("アナログスティックの設定ウィザード", "Analog stick setup wizard"), new(860, 680), new(820, 640));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) root.RowStyles.Add(new(i == 2 ? SizeType.Percent : SizeType.AutoSize, i == 2 ? 100 : 0));
        TableLayoutPanel progress = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, Margin = new(0, 5, 0, 8) };
        for (int i = 0; i < 4; i++)
        {
            int axis = i; progress.ColumnStyles.Add(new(SizeType.Percent, 25));
            Button step = steps[i] = Ui.Button("", (_, _) => { if (session.Navigate(axis)) { RefreshStep(); } });
            step.Dock = DockStyle.Fill; step.MinimumSize = new(0, 54); progress.Controls.Add(step, i, 0);
        }
        root.Controls.Add(progress, 0, 0);
        instruction.Dock = DockStyle.Fill; instruction.Margin = new(4, 3, 4, 8); root.Controls.Add(instruction, 0, 1);
        TableLayoutPanel graphs = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, MinimumSize = new(0, 240) };
        graphs.ColumnStyles.Add(new(SizeType.Percent, 50)); graphs.ColumnStyles.Add(new(SizeType.Percent, 50));
        graphs.RowStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 2; i++)
        {
            int side = i;
            Button gyro = Ui.Button(L.T("ジャイロ", "Gyro"), (_, _) => EditGyro(side));
            gyro.Enabled = fields.Any(f => f.Gyroscope);
            gyro.Anchor = AnchorStyles.Bottom | (i == 0 ? AnchorStyles.Left : AnchorStyles.Right);
            gyroTips.SetToolTip(gyro, gyro.Enabled ? L.T("このスティックの横方向をジャイロで操作します。", "Use gyro steering for this stick’s horizontal axis.") :
                L.T("対応するDS3を接続し、同梱ファームウェアへ更新してください。個体校正値を読み取れた場合に使用できます。", "Connect a supported DS3 and update to the bundled firmware. Factory calibration must be readable."));
            views[i].Controls.Add(gyro);
            void PlaceGyro()
            {
                int margin = views[side].LogicalToDeviceUnits(8);
                gyro.Location = new(side == 0 ? margin : Math.Max(margin, views[side].Width - gyro.Width - margin),
                    Math.Max(margin, views[side].Height - gyro.Height - margin));
            }
            views[i].SizeChanged += (_, _) => PlaceGyro(); gyro.SizeChanged += (_, _) => PlaceGyro();
            graphs.Controls.Add(views[i], i, 0);
        }
        root.Controls.Add(graphs, 0, 2);
        FlowLayoutPanel options = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        options.Controls.Add(invert);
        Label saveNotice = Ui.Label(L.ProfileSaveNotice, 9); saveNotice.ForeColor = Ui.Muted; saveNotice.Margin = new(12, 14, 4, 4); options.Controls.Add(saveNotice);
        root.Controls.Add(options, 0, 3);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = true };
        next = Ui.Button("", (_, _) => Next(), true);
        skip = Ui.Button(L.T("この軸は変更しない", "Keep this axis unchanged"), (_, _) => { session.Skip(); RefreshStep(); });
        back = Ui.Button(L.T("戻る", "Back"), (_, _) => { session.Back(); RefreshStep(); });
        Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(next); footer.Controls.Add(skip); footer.Controls.Add(back); footer.Controls.Add(cancel);
        root.Controls.Add(footer, 0, 4); Controls.Add(root); CancelButton = cancel; AcceptButton = next;
        invert.CheckedChanged += (_, _) => { if (!updating) { session.SetInvert(invert.Checked); RefreshValues(); } };
        Ui.ThemeInputs(this);
        void ReserveLayout()
        {
            using IDisposable batch = Ui.BatchLayout(this);
            // Measure after scaling, then repeat on monitor changes: measuring
            // in the constructor would scale these pixel dimensions twice.
            string currentInstruction = instruction.Text, currentNext = next.Text;
            instruction.MinimumSize = Size.Empty;
            instruction.Text = L.T("左スティックを左右に動かしてください。", "Move the left stick left and right.") + "\n" + ReadyHint;
            instruction.MinimumSize = new(0, instruction.GetPreferredSize(Size.Empty).Height);
            instruction.Text = currentInstruction;
            options.MinimumSize = new(0, Math.Max(invert.GetPreferredSize(Size.Empty).Height + invert.Margin.Vertical,
                saveNotice.GetPreferredSize(Size.Empty).Height + saveNotice.Margin.Vertical) + options.Padding.Vertical);
            next.MinimumSize = LogicalToDeviceUnits(new Size(90, 40));
            Size confirmSize = Size.Empty;
            foreach (string caption in new[] { L.T("確定して次へ", "Confirm and next"), L.T("確定して確認へ", "Confirm and review"), L.T("設定を反映", "Apply settings") })
            {
                next.Text = caption; Size preferred = next.GetPreferredSize(Size.Empty);
                confirmSize = new(Math.Max(confirmSize.Width, preferred.Width), Math.Max(confirmSize.Height, preferred.Height));
            }
            next.AutoSize = false; next.Size = next.MinimumSize = confirmSize; next.Text = currentNext;
        }
        Load += (_, _) => ReserveLayout();
        DpiChanged += (_, _) => BeginInvoke(() => { if (!IsDisposed) ReserveLayout(); });
        RefreshStep();
    }
    private void Next()
    {
        if (session.Reviewing) { DialogResult = DialogResult.OK; Close(); return; }
        if (session.Confirm()) { RefreshStep(); }
        else RefreshValues();
    }
    internal void OnInput(InputField field, int value, uint time)
    {
        if (IsDisposed) return;
        if (gyroWizard is not null) { gyroWizard.OnInput(field, value, time); return; }
        bool confirm = session.Observe(field, value, time);
        if (confirm && !session.Reviewing) Next();
        else { QueueRefresh(); }
    }
    internal void OnInputRefresh()
    {
        if (IsDisposed) return;
        if (gyroWizard is not null) { gyroWizard.RefreshView(); return; }
        long now = Environment.TickCount64, elapsed = now - lastRefresh; lastRefresh = now;
        if (session.AdvanceHeld(elapsed > 250 ? 251 : (int)Math.Max(0, elapsed))) QueueRefresh();
    }
    private void QueueRefresh()
    {
        if (!IsHandleCreated) { RefreshValues(); return; }
        if (refreshQueued) return;
        refreshQueued = true;
        BeginInvoke(() => { refreshQueued = false; if (!IsDisposed) RefreshValues(); });
    }
    internal void ConnectionLost() { gyroWizard?.ConnectionLost(); DialogResult = DialogResult.Cancel; Close(); }
    internal void InputEventsLost()
    {
        gyroWizard?.InputEventsLost(); session.InputEventsLost(); RefreshStep();
        if (session.Started && !session.Reviewing)
            instruction.Text = L.T("入力履歴が欠けたため、この軸をもう一度動かしてください。",
                "Input events were missed. Move this axis again.");
    }
    internal void GyroEventsLost() => gyroWizard?.InputEventsLost();
    private void EditGyro(int side)
    {
        if (!fields.Any(f => f.Gyroscope)) return;
        using GyroWizard dialog = new(side, session.Axis(side * 2), session.RecenterButtons, fields, session.InputInterval); gyroWizard = dialog;
        try
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            session.SetGyro(side * 2, dialog.Result, dialog.RecenterButtons);
            DialogResult = DialogResult.OK; Close();
        }
        finally { gyroWizard = null; }
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) gyroTips.Dispose();
        base.Dispose(disposing);
    }
    private void RefreshStep()
    {
        for (int i = 0; i < 4; i++)
        {
            string status = session.Confirmed(i) ? L.T("確定済み", "Confirmed") : session.Visited(i) ? L.T("変更なし", "Unchanged") : L.T("未確認", "Pending");
            steps[i].Text = (i + 1) + ". " + DeviceProfile.AxisNames[i] + "\n" + status;
            steps[i].Enabled = session.Started && (session.Visited(i) || i == session.Step);
            Ui.ButtonColors(steps[i], session.Started && i == session.Step, session.Confirmed(i));
        }
        back.Enabled = session.Started && session.Step > 0;
        skip.Visible = invert.Visible = session.Started && !session.Reviewing;
        next.Text = session.Reviewing ? L.T("設定を反映", "Apply settings") :
            session.Step == 3 ? L.T("確定して確認へ", "Confirm and review") : L.T("確定して次へ", "Confirm and next");
        if (session.Reviewing)
            instruction.Text = L.T("4軸の設定を確認してください。", "Review all four axes.");
        else
        {
            string motion = session.Step switch
            {
                0 => L.T("左スティックを左右に動かしてください。", "Move the left stick left and right."),
                1 => L.T("左スティックを上下に動かしてください。", "Move the left stick up and down."),
                2 => L.T("右スティックを左右に動かしてください。", "Move the right stick left and right."),
                _ => L.T("右スティックを上下に動かしてください。", "Move the right stick up and down.")
            };
            instruction.Text = motion + "\n" + ReadyHint;
        }
        RefreshValues();
    }
    private void RefreshValues()
    {
        updating = true;
        try { invert.Checked = session.Invert; invert.Enabled = session.Started && !session.Reviewing; }
        finally { updating = false; }
        for (int i = 0; i < 2; i++)
            views[i].Display(i == 0 ? L.T("左スティック", "Left stick") : L.T("右スティック", "Right stick"), session.View(i));
        next.Enabled = session.Reviewing || session.CanConfirm;
    }
}
