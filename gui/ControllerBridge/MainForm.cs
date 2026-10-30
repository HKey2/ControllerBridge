namespace ControllerBridge;

internal sealed partial class MainForm : Form
{
    private HidBridge? bridge;
    private Capabilities? caps;
    private OutputState? outputState;
    private DeviceProfile[] profiles = Enumerable.Range(0, DeviceProfile.SlotCount).Select(_ => new DeviceProfile()).ToArray();
    private DeviceProfile[] persistedProfiles = Enumerable.Range(0, DeviceProfile.SlotCount).Select(_ => new DeviceProfile()).ToArray();
    private readonly bool[] dirty = new bool[DeviceProfile.SlotCount];
    private readonly List<InputField> fields = [];
    private uint cursor, generation, bankGeneration, observedBankGeneration;
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 25 };
    private bool polling, working, connecting, loading, closing, syncingOutput;
    private readonly Lazy<FirmwarePackage> bundledFirmware = new(FirmwarePackage.Load);
    private readonly FirmwareNoticeTracker firmwareNotices = new();
    private Func<string, Task<ConnectionRead>> readConnection = ConnectionRead.OpenAsync;
    private Func<string?, Task>? firmwareGuide;
    private DateTime reconnectAfter;
    private int selected, displayedActiveSlot = -2;
    private int selectionBindings;
    private RegistrationWizard? wizard;
    private readonly bool preview;
    private readonly bool automaticFirmwareGuidance;
    private readonly ToolTip tips = new();
    private readonly SlotList slots = new();
    private readonly MappingEditor editor = new(), mouseEditor = new(true), combinedEditor = new();
    private readonly DarkTabControl outputTabs = new() { Dock = DockStyle.Fill, Padding = new(23, 12) };
    private readonly BufferedTableLayoutPanel[] tabContents = new BufferedTableLayoutPanel[3];
    private readonly BufferedTableLayoutPanel mouseContent, timing;
    private readonly BufferedTableLayoutPanel body, details;
    private readonly TextBox name = new() { Dock = DockStyle.Fill, MaxLength = 47 };
    private readonly NumericUpDown interval = new() { Minimum = 0, Maximum = 255, Width = 78, Anchor = AnchorStyles.Left };
    private readonly NumericUpDown mouseGain = new() { Minimum = 1, Maximum = 64, Increment = .1m, DecimalPlaces = 1, Width = 78, Anchor = AnchorStyles.Left };
    private readonly NumericUpDown paddleGain = new() { Minimum = 1, Maximum = 10, Increment = .1m, DecimalPlaces = 1, Width = 78, Anchor = AnchorStyles.Left };
    private readonly CheckBox trackballOn = new() { Text = L.T("トラックボール ON", "Trackball ON"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox paddleOn = new() { Text = L.T("パドル ON", "Paddle ON"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox invertX = new() { Text = L.T("X方向を反転", "Invert X"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox invertY = new() { Text = L.T("Y方向を反転", "Invert Y"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox invertPaddle = new() { Text = L.T("回転方向を反転", "Reverse rotation"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly RadioButton mouseButtons = new() { Text = L.T("マウス扱い", "Mouse"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly RadioButton gamepadButtons = new() { Text = L.T("コントローラー扱い", "Controller"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly FlowLayoutPanel buttonBehavior = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
    private readonly Label connection = Ui.Label(L.T("本体のUSB-CをPCへ接続してください。", "Connect the bridge's USB-C port to this PC."), 10);
    private string connectionDetail = "";
    private readonly Label live = Ui.Label(L.T("入力: —", "Input: —"), 9);
    private readonly Label identity = Ui.Label("", 9);
    private readonly Label saved = Ui.Label(L.T("16スロットを本体に保存できます。", "Store up to 16 profiles on the bridge."), 9);
    private readonly Label outputStatus = Ui.Label("", 9);
    private readonly Button connect, reload, saveButton, useProfile, switchMethod, delete, duplicate, ledSettings, backup, restore, firmwareUpdate;
    private MappingEditor ActiveEditor => outputTabs.SelectedIndex == 0 ? editor :
        outputTabs.SelectedIndex == 2 && gamepadButtons.Checked ? combinedEditor : mouseEditor;
    public bool ConnectedSuccessfully { get; private set; }
    public string? StartupError { get; private set; }

    public MainForm(bool preview = false, bool autoConnect = true, bool automaticFirmwareGuidance = true)
    {
        this.preview = preview; this.automaticFirmwareGuidance = automaticFirmwareGuidance;
        Ui.Form(this, "ControllerBridge v" + typeof(MainForm).Assembly.GetName().Version?.ToString(3), new(1290, 1100));
        // The combined controller layout needs room for tuning and all mappings.
        MinimumSize = Size; StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BufferedTableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(16, 12, 16, 12), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize));
        BufferedTableLayoutPanel header = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 4, RowCount = 1, Padding = new(2, 0, 2, 8) };
        header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.Absolute, 180)); header.ColumnStyles.Add(new(SizeType.Absolute, 170)); header.ColumnStyles.Add(new(SizeType.Absolute, 216));
        header.RowStyles.Add(new(SizeType.AutoSize));
        connection.Dock = DockStyle.Fill; connection.ForeColor = Ui.Muted;
        header.Controls.Add(connection, 0, 0);
        firmwareUpdate = Ui.Button(L.T("ファームウェア更新", "Firmware update"), async (_, _) => await UpdateFirmware()); firmwareUpdate.Dock = DockStyle.Fill; header.Controls.Add(firmwareUpdate, 1, 0);
        connect = Ui.Button(L.T("接続 / 再接続", "Connect / Reconnect"), async (_, _) => await Connect()); connect.Dock = DockStyle.Fill; header.Controls.Add(connect, 2, 0);
        switchMethod = Ui.Button(L.T("プロファイル切替方法…", "Profile switching…"), async (_, _) => await ConfigureSwitchMethod()); switchMethod.Dock = DockStyle.Fill; header.Controls.Add(switchMethod, 3, 0);
        root.Controls.Add(header, 0, 0);

        body = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.ColumnStyles.Add(new(SizeType.Absolute, 400)); body.ColumnStyles.Add(new(SizeType.Percent, 100));
        BufferedTableLayoutPanel left = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new(0, 0, 12, 0), Margin = Padding.Empty };
        left.RowStyles.Add(new(SizeType.Percent, 100)); left.RowStyles.Add(new(SizeType.AutoSize));
        RaisedTableLayoutPanel listPane = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        listPane.RowStyles.Add(new(SizeType.AutoSize)); listPane.RowStyles.Add(new(SizeType.Percent, 100)); listPane.RowStyles.Add(new(SizeType.AutoSize));
        listPane.Controls.Add(Ui.Label(L.T("プロファイルリスト", "Profile list"), 12, true), 0, 0); listPane.Controls.Add(slots, 0, 1);
        Label dragHint = Ui.Label(L.T("ドラッグで順番を変更", "Drag profiles to change their order"), 9); dragHint.ForeColor = Ui.Muted; listPane.Controls.Add(dragHint, 0, 2);
        left.Controls.Add(listPane, 0, 0);
        RaisedTableLayoutPanel files = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        for (int i = 0; i < 3; i++) files.RowStyles.Add(new(SizeType.AutoSize));
        files.Controls.Add(Ui.Label(L.T("保存／復帰", "Backup / Restore"), 12, true), 0, 0);
        backup = Ui.Button(L.T("全スロットをバックアップ", "Back up all slots"), async (_, _) => await BackupAll());
        restore = Ui.Button(L.T("全スロットを復帰", "Restore all slots"), async (_, _) => await RestoreAll());
        backup.Dock = restore.Dock = DockStyle.Fill; files.Controls.Add(backup, 0, 1); files.Controls.Add(restore, 0, 2); left.Controls.Add(files, 0, 1);

        RaisedTableLayoutPanel right = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        right.RowStyles.Add(new(SizeType.AutoSize)); right.RowStyles.Add(new(SizeType.Percent, 100));
        BufferedTableLayoutPanel metadata = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new(2, 0, 2, 4) };
        metadata.ColumnStyles.Add(new(SizeType.Percent, 100)); metadata.ColumnStyles.Add(new(SizeType.Absolute, 190));
        metadata.RowStyles.Add(new(SizeType.AutoSize));
        BufferedTableLayoutPanel nameRow = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        for (int i = 0; i < 4; i++) nameRow.RowStyles.Add(new(SizeType.AutoSize));
        nameRow.Controls.Add(Ui.Label(L.T("プロファイル名", "Profile name"), 9), 0, 0); nameRow.Controls.Add(name, 0, 1); name.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        nameRow.Controls.Add(identity, 0, 2); nameRow.Controls.Add(outputStatus, 0, 3);
        metadata.Controls.Add(nameRow, 0, 0);
        BufferedTableLayoutPanel profileActions = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 3, Padding = new(8, 0, 0, 0) };
        for (int i = 0; i < 3; i++) profileActions.RowStyles.Add(new(SizeType.AutoSize));
        delete = Ui.Button(L.T("プロファイル削除", "Delete profile"), async (_, _) => { if (profiles[selected].Enabled) DeleteProfile(); else await AddController(); });
        duplicate = Ui.Button(L.T("プロファイル複製", "Duplicate profile"), (_, _) => DuplicateProfile());
        delete.Dock = duplicate.Dock = DockStyle.Fill; profileActions.Controls.Add(duplicate, 0, 0); profileActions.Controls.Add(delete, 0, 1);
        ledSettings = Ui.Button(L.T("LED RGB設定", "LED RGB settings"), (_, _) => ConfigureLed());
        ledSettings.Dock = DockStyle.Fill; profileActions.Controls.Add(ledSettings, 0, 2);
        metadata.Controls.Add(profileActions, 1, 0); right.Controls.Add(metadata, 0, 0);
        details = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        details.RowStyles.Add(new(SizeType.Percent, 100)); details.RowStyles.Add(new(SizeType.AutoSize));
        identity.Dock = outputStatus.Dock = live.Dock = DockStyle.Fill;
        identity.ForeColor = outputStatus.ForeColor = live.ForeColor = Ui.Muted;
        details.Controls.Add(outputTabs, 0, 0); details.Controls.Add(live, 0, 1); right.Controls.Add(details, 0, 1);
        string[] titles = [L.T("ゲームパッド", "Gamepad"), L.T("USBマウス", "USB mouse"), L.T("マウス＋ゲームパッド", "Mouse + Gamepad")];
        for (int i = 0; i < 3; i++)
        {
            TabPage page = new(titles[i]) { BackColor = Ui.Surface, Padding = new(5) };
            BufferedTableLayoutPanel content = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
            content.RowStyles.Add(new(SizeType.AutoSize)); content.RowStyles.Add(new(SizeType.Percent, 100));
            tabContents[i] = content; page.Controls.Add(content); outputTabs.TabPages.Add(page);
        }
        timing = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 1, Padding = new(10, 4, 8, 4) };
        timing.ColumnStyles.Add(new(SizeType.AutoSize)); timing.ColumnStyles.Add(new(SizeType.AutoSize)); timing.ColumnStyles.Add(new(SizeType.Percent, 100));
        Label intervalLabel = Ui.Label(L.T("入力間隔 (ms)", "Input interval (ms)"), 10); intervalLabel.Anchor = AnchorStyles.Left;
        Label intervalHelp = Ui.Label(L.T("0 = コントローラーの標準値", "0 = controller default"), 9); intervalHelp.Anchor = AnchorStyles.Left; intervalHelp.ForeColor = Ui.Muted;
        timing.Controls.Add(intervalLabel, 0, 0); timing.Controls.Add(interval, 1, 0); timing.Controls.Add(intervalHelp, 2, 0);
        tabContents[0].Controls.Add(editor, 0, 1);
        mouseContent = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        mouseContent.RowStyles.Add(new(SizeType.AutoSize)); mouseContent.RowStyles.Add(new(SizeType.Percent, 100));
        BufferedTableLayoutPanel tuning = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 3, Padding = new(10, 2, 6, 4) };
        for (int i = 0; i < 3; i++) tuning.RowStyles.Add(new(SizeType.AutoSize));
        BufferedTableLayoutPanel ballRow = TuningRow(trackballOn, mouseGain, invertX, invertY);
        BufferedTableLayoutPanel paddleRow = TuningRow(paddleOn, paddleGain, invertPaddle);
        Label buttonBehaviorLabel = Ui.Label(L.T("ボタン動作", "Button behavior"), 10); buttonBehaviorLabel.Anchor = AnchorStyles.Left;
        buttonBehavior.Controls.Add(buttonBehaviorLabel); buttonBehavior.Controls.Add(mouseButtons); buttonBehavior.Controls.Add(gamepadButtons);
        tuning.Controls.Add(ballRow, 0, 0); tuning.Controls.Add(paddleRow, 0, 1); tuning.Controls.Add(buttonBehavior, 0, 2);
        mouseContent.Controls.Add(tuning, 0, 0);
        mouseContent.Controls.Add(mouseEditor, 0, 1);
        body.Controls.Add(left, 0, 0); body.Controls.Add(right, 1, 0); root.Controls.Add(body, 0, 1);
        BufferedTableLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, Padding = new(0, 12, 0, 0) };
        footer.ColumnStyles.Add(new(SizeType.Percent, 100)); for (int i = 0; i < 3; i++) footer.ColumnStyles.Add(new(SizeType.AutoSize));
        saved.Dock = DockStyle.Fill; saved.ForeColor = Ui.Muted; footer.Controls.Add(saved, 0, 0);
        reload = Ui.Button(L.T("本体から再読込", "Reload device"), async (_, _) => await Reload());
        useProfile = Ui.Button(L.T("この設定を使用", "Use this profile"), async (_, _) => await UseProfile());
        saveButton = Ui.Button(L.T("スロットを本体に保存", "Save slot to device"), async (_, _) => await SaveSelected(), true);
        footer.Controls.Add(reload, 1, 0); footer.Controls.Add(useProfile, 2, 0); footer.Controls.Add(saveButton, 3, 0); root.Controls.Add(footer, 0, 2); Controls.Add(root);

        slots.DrawItem += DrawSlot;
        slots.SelectedIndexChanged += (_, _) => { if (loading || slots.SelectedIndex < 0) return; AbortLearning(); selected = slots.SelectedIndex; BindSelected(); };
        slots.CanMove = i => i >= 0 && i < profiles.Length && profiles[i].Enabled && !working && !dirty.Any(x => x) && (preview || bridge is not null && caps?.SupportsBankTransfer == true);
        slots.MoveRequested += async (_, e) => await MoveSlot(e.Source, e.Destination);
        InitializeProfileMenu();
        name.TextChanged += (_, _) => { if (loading || !profiles[selected].Enabled) return; profiles[selected].Name = name.Text; MarkDirty(); };
        interval.ValueChanged += (_, _) => { if (loading) return; profiles[selected].InputInterval = (byte)interval.Value; MarkDirty(); };
        foreach (MappingEditor mapping in new[] { editor, mouseEditor, combinedEditor }) { mapping.Modified += (_, _) => MarkDirty(); mapping.PrepareLearningAsync = PrepareLearning; }
        outputTabs.Selecting += (_, e) =>
        {
            if (syncingOutput || loading) return;
            e.Cancel = working || !profiles[selected].Enabled || !preview && caps?.SupportsProfileOutput != true;
        };
        outputTabs.SelectedIndexChanged += (_, _) =>
        {
            if (syncingOutput || loading || outputTabs.SelectedIndex < 0) return;
            profiles[selected].OutputMode = (byte)outputTabs.SelectedIndex;
            AbortLearning(); MarkDirty(); BindSelected();
        };
        mouseGain.ValueChanged += (_, _) => MouseProfileChanged(); paddleGain.ValueChanged += (_, _) => MouseProfileChanged();
        foreach (CheckBox toggle in new[] { trackballOn, paddleOn, invertX, invertY, invertPaddle }) toggle.CheckedChanged += (_, _) => MouseProfileChanged();
        foreach (RadioButton option in new[] { mouseButtons, gamepadButtons }) option.CheckedChanged += (_, _) =>
        {
            if (syncingOutput || loading || !option.Checked || outputTabs.SelectedIndex != 2) return;
            profiles[selected].ControllerButtons = gamepadButtons.Checked;
            AbortLearning(); MarkDirty(); BindSelected();
        };
        timer.Tick += async (_, _) => await Poll();
        Shown += async (_, _) => { if (preview) LoadPreview(); else if (autoConnect) await Connect(false); };
        Shown += (_, _) => SizeSlotRows(); DpiChanged += (_, _) => SizeSlotRows(); FormClosing += OnClosing;
        Ui.ThemeInputs(this); SyncMousePage(); RefreshSlots(); BindSelected(); UpdateEnabled();
    }
    private static BufferedTableLayoutPanel TuningRow(CheckBox toggle, NumericUpDown gain, params CheckBox[] options)
    {
        BufferedTableLayoutPanel row = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3 + options.Length, RowCount = 1, Margin = Padding.Empty };
        for (int i = 0; i < row.ColumnCount; i++) row.ColumnStyles.Add(new(SizeType.AutoSize));
        Label label = Ui.Label(L.T("感度（倍）", "Gain (×)"), 10); label.Anchor = AnchorStyles.Left;
        row.Controls.Add(toggle, 0, 0); row.Controls.Add(label, 1, 0); row.Controls.Add(gain, 2, 0);
        for (int i = 0; i < options.Length; i++) row.Controls.Add(options[i], i + 3, 0);
        return row;
    }
    private static void SetText(Control control, string value) { if (control.Text != value) control.Text = value; }
    private void SetTip(Control control, string value) { if (tips.GetToolTip(control) != value) tips.SetToolTip(control, value); }
    private void SetConnectionDetail(string value) { connectionDetail = value; SetTip(connection, value); }
    private void SizeSlotRows()
    {
        using Graphics g = slots.CreateGraphics(); using Font small = new(L.FontFamily, 9);
        slots.ItemHeight = Math.Max(LogicalToDeviceUnits(58), TextRenderer.MeasureText(g, "Agあ", Font).Height + TextRenderer.MeasureText(g, "Agあ", small).Height + LogicalToDeviceUnits(16));
    }
    private int ConnectedSlot => caps is { Connected: true } && caps.ActiveSlot < profiles.Length ? caps.ActiveSlot : -1;
    private void DrawSlot(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return; bool current = (e.State & DrawItemState.Selected) != 0;
        Color face = current ? Ui.Selected : slots.DragSource == e.Index ? Ui.Background : Ui.Surface;
        using SolidBrush bg = new(face); e.Graphics.FillRectangle(bg, e.Bounds);
        using Pen separator = new(Color.FromArgb(51, 51, 51)); e.Graphics.DrawLine(separator, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        if (current) { using SolidBrush marker = new(Ui.Focus); e.Graphics.FillRectangle(marker, e.Bounds.Left, e.Bounds.Top, LogicalToDeviceUnits(3), e.Bounds.Height); }
        DeviceProfile p = profiles[e.Index]; bool connected = p.Enabled && ConnectedSlot == e.Index;
        using Font titleFont = new(Font, connected ? Font.Style | FontStyle.Bold : Font.Style);
        using Font small = new(L.FontFamily, 9, connected ? FontStyle.Bold : FontStyle.Regular);
        int pad = LogicalToDeviceUnits(10), top = LogicalToDeviceUnits(7), firstHeight = TextRenderer.MeasureText(e.Graphics, "Agあ", titleFont).Height;
        Rectangle first = new(e.Bounds.X + pad, e.Bounds.Y + top, e.Bounds.Width - 2 * pad, firstHeight);
        Rectangle second = new(e.Bounds.X + pad, first.Bottom + LogicalToDeviceUnits(3), e.Bounds.Width - 2 * pad, e.Bounds.Bottom - first.Bottom - LogicalToDeviceUnits(7));
        string title = $"{e.Index + 1:00}  {ProfileSlots.Name(profiles, e.Index)}" + (dirty[e.Index] ? " *" : "");
        TextRenderer.DrawText(e.Graphics, title, titleFont, first, p.Enabled ? Ui.Ink : Ui.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        string sub = p.Enabled ? $"{p.Vid:X4}:{p.Pid:X4}" + (connected ? L.T("   接続中", "   Connected") : "") : "";
        TextRenderer.DrawText(e.Graphics, sub, small, second, Ui.Muted, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        if (connected)
        {
            int inset = LogicalToDeviceUnits(2);
            Rectangle outline = Rectangle.Inflate(e.Bounds, -inset, -inset); outline.Width--; outline.Height--;
            using Pen border = new(Ui.Focus, LogicalToDeviceUnits(2)); e.Graphics.DrawRectangle(border, outline);
        }
        e.DrawFocusRectangle();
    }
    private void RefreshSlots()
    {
        loading = true; int top = slots.TopIndex; slots.BeginUpdate(); slots.Items.Clear();
        for (int i = 0; i < profiles.Length; i++) slots.Items.Add($"{i + 1:00} {ProfileSlots.Name(profiles, i)}");
        slots.SelectedIndex = selected; if (top >= 0 && top < slots.Items.Count) slots.TopIndex = Math.Min(top, selected);
        slots.EndUpdate(); loading = false;
    }
    private void BindSelected()
    {
        selectionBindings++;
        loading = true; DeviceProfile p = profiles[selected];
        SyncOutputControls();
        name.Text = p.Enabled ? p.Name : L.T("未使用", "Unused"); name.ReadOnly = !p.Enabled;
        details.Visible = identity.Visible = outputStatus.Visible = p.Enabled; duplicate.Visible = ledSettings.Visible = p.Enabled;
        delete.Text = p.Enabled ? L.T("プロファイル削除", "Delete profile") : L.T("コントローラーを追加", "Add controller"); Ui.ButtonColors(delete, !p.Enabled);
        interval.Value = p.InputInterval; trackballOn.Checked = p.Mouse.TrackballEnabled; paddleOn.Checked = p.Mouse.PaddleEnabled;
        mouseGain.Value = p.Mouse.TrackballGain; paddleGain.Value = p.Mouse.PaddleGain;
        invertX.Checked = p.Mouse.InvertX; invertY.Checked = p.Mouse.InvertY; invertPaddle.Checked = p.Mouse.InvertPaddle;
        identity.Text = p.Enabled ? $"VID {p.Vid:X4} / PID {p.Pid:X4}" + ((p.Flags & 2) != 0 ? L.T($"  •  入力形式 {p.DescriptorHash:X8}", $"  •  Input layout {p.DescriptorHash:X8}") : "") : "";
        loading = false;
        bool learn = preview || bridge is not null && caps is { Connected: true } && p.Matches(caps);
        foreach (MappingEditor mapping in new[] { editor, mouseEditor, combinedEditor }) mapping.Bind(p, fields, learn, preview || caps?.SupportsRapidFire == true);
        UpdateProfileOutputStatus();
        UpdateEnabled();
    }
    private void MarkDirty()
    {
        dirty[selected] = true; slots.Invalidate(slots.GetItemRectangle(selected));
        SetText(saved, L.T("* は未保存の編集です。「スロットを本体に保存」で反映します。", "* marks unsaved edits. Choose “Save slot to device” to apply them.")); UpdateEnabled();
    }
    private void UpdateEnabled()
    {
        bool online = bridge is not null || preview, enabled = profiles[selected].Enabled, clean = !dirty.Any(x => x);
        body.Enabled = !working; connect.Enabled = !working; firmwareUpdate.Enabled = !working && !preview;
        reload.Enabled = online && !working; saveButton.Enabled = online && !working && (dirty[selected] || caps is { Persisted: false });
        name.Enabled = enabled && !working; name.BackColor = enabled ? Ui.Input : Ui.Surface;
        interval.Enabled = enabled && !working;
        delete.Enabled = !working && (enabled || online && CanRegisterSlot(selected) && (preview || CanRegisterInput && caps!.SupportsGuardedRegistration && fields.Any(f => f.Learnable && f.Valid)));
        duplicate.Enabled = enabled && !working && ProfileSlots.EmptyBelow(profiles, selected) >= 0;
        ledSettings.Enabled = enabled && !working;
        SetTip(duplicate, ProfileSlots.EmptyBelow(profiles, selected) >= 0 ? L.T("下側の最も近い未使用スロットへ複製します。", "Copy to the nearest unused slot below this profile.") : L.T("このスロットより下に未使用スロットがありません。", "There are no unused slots below this profile."));
        backup.Enabled = restore.Enabled = online && !working && clean && (preview || caps?.SupportsBankTransfer == true);
        string backupTip = caps?.SupportsBankTransfer == true || preview ? L.T("全16スロットと切替設定を保存・復帰します。", "Back up or restore all 16 slots and profile switching settings.") :
            L.T("ファームウェア0.14.0以降が必要です。", "Requires firmware 0.14.0 or later.");
        SetTip(backup, backupTip); SetTip(restore, backupTip);
        switchMethod.Enabled = online && !working && !preview && clean && enabled && caps?.SupportsSwitchKeys == true;
        trackballOn.Enabled = paddleOn.Enabled = enabled && !working;
        mouseGain.Enabled = invertX.Enabled = invertY.Enabled = enabled && !working && trackballOn.Checked;
        paddleGain.Enabled = invertPaddle.Enabled = enabled && !working && paddleOn.Checked;
        mouseButtons.Enabled = gamepadButtons.Enabled = enabled && !working && (preview || caps?.SupportsProfileOutput == true);
        useProfile.Enabled = online && !working && !preview && !dirty[selected] && caps is { SupportsOnBoardVariants: true, Connected: true } && caps.ActiveSlot != selected && profiles[selected].Matches(caps);
    }
    private bool ConfirmDiscard() => !dirty.Any(x => x) || MessageBox.Show(this, L.T("未保存の編集を破棄して、本体から読み直しますか？", "Discard unsaved edits and reload the device?"),
        L.T("未保存の編集", "Unsaved edits"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
    private async Task WaitForPoll() { while (polling && !closing) await Task.Delay(5); }
    private async Task WaitForConnection() { while (connecting && !closing) await Task.Delay(5); }
    private bool ModalEditorOpen => !Enabled || OwnedForms.Any(form => form.Modal);
    private void ApplyBank(DeviceProfile[] data, uint version)
    {
        profiles = data; persistedProfiles = data.Select(p => p.Clone()).ToArray(); Array.Clear(dirty); bankGeneration = observedBankGeneration = version;
    }
    private async Task Connect(bool showErrors = true, bool allowFirmwareNotice = true)
    {
        if (working || closing || connecting || !showErrors && ModalEditorOpen) return;
        connecting = true;
        // Background discovery does not disable/re-enable the whole editor.
        // The USB handle remains local to ConnectionRead until every read succeeds.
        if (showErrors) { working = true; UpdateEnabled(); }
        if (showErrors || bridge is not null) AbortLearning();
        timer.Stop(); await WaitForPoll();
        string? guidance = null;
        try
        {
            bridge?.Dispose(); bridge = null;
            using ConnectionRead read = await readConnection(bundledFirmware.Value.Version);
            if (closing || !showErrors && (working || ModalEditorOpen)) return;
            if (read.Status.Kind != ConnectionKind.Ready)
            {
                ClearDeviceState();
                string detail = read.Status.Guidance(bundledFirmware.Value.Version);
                StartupError = read.Status.Kind == ConnectionKind.NotDetected ? null : detail;
                SetText(connection, read.Status.Summary); SetConnectionDetail(detail);
                if (firmwareNotices.Observe(read.Status, allowFirmwareNotice && automaticFirmwareGuidance)) guidance = StartupError;
                return;
            }
            firmwareNotices.Observe(read.Status, allowFirmwareNotice);
            bridge = read.TakeBridge(); var bank = read.Bank;
            bool retain = dirty.Any(x => x), unchanged = Enumerable.Range(0, profiles.Length).All(i => persistedProfiles[i].SameContent(bank.Profiles[i]));
            if (!retain) ApplyBank(bank.Profiles, bank.Caps.BankGeneration); else if (unchanged) bankGeneration = bank.Caps.BankGeneration;
            observedBankGeneration = bank.Caps.BankGeneration;
            DeviceState device = read.Device!; caps = device.Caps; generation = caps.ConnectionGeneration; cursor = device.Cursor;
            fields.Clear(); fields.AddRange(device.Fields); outputState = read.Output;
            if (!retain && caps.ActiveSlot < profiles.Length) selected = caps.ActiveSlot;
            UpdateConnection();
            RefreshSlots(); BindSelected(); ConnectedSuccessfully = true; StartupError = null;
            SetText(saved, retain ? L.T("再接続しました。未保存の編集を保持しています。", "Reconnected. Unsaved edits have been kept.") : L.T("本体から16スロットを読み込みました。", "Loaded all 16 slots from the device."));
        }
        catch (Exception ex)
        {
            bridge?.Dispose(); bridge = null;
            if (closing || !showErrors && (working || ModalEditorOpen)) return;
            ClearDeviceState(); StartupError = ex.Message;
            SetConnectionDetail(ex.Message); SetText(connection, L.T("本体の接続を確認できません。接続・ファームウェアを確認してください。", "Could not verify the device connection. Check USB and firmware.")); if (showErrors) Ui.Error(this, ex);
        }
        finally
        {
            connecting = false; if (showErrors) working = false;
            reconnectAfter = DateTime.UtcNow.AddSeconds(2);
            if (!closing)
            {
                UpdateEnabled();
                if (guidance is not null && !working)
                {
                    if (firmwareGuide is not null) await firmwareGuide(guidance);
                    else await UpdateFirmware(guidance);
                }
                if (!working && !preview) timer.Start();
                HandleControllerArrival();
            }
        }
    }
    private void ClearDeviceState()
    {
        controllerArrivals.Reset();
        bool wasConnected = caps is not null;
        caps = null; outputState = null; fields.Clear();
        if (displayedActiveSlot >= 0 && displayedActiveSlot < profiles.Length) slots.Invalidate(slots.GetItemRectangle(displayedActiveSlot));
        displayedActiveSlot = -2;
        SetText(live, L.T("入力: —", "Input: —"));
        if (wasConnected) BindSelected();
    }
    private async Task Reload()
    {
        if (working || !ConfirmDiscard()) return;
        if (preview) { Array.Clear(dirty); LoadPreview(); return; }
        if (bridge is null) return;
        working = true; AbortLearning(); UpdateEnabled(); await WaitForPoll();
        try { var bank = await bridge.ReadProfilesAsync(); ApplyBank(bank.Profiles, bank.Caps.BankGeneration); await RefreshDevice(); RefreshSlots(); BindSelected(); SetText(saved, L.T("本体のスロットを再読込しました。", "Reloaded the device's slots.")); }
        catch (Exception ex) { Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
    private async Task RefreshDevice()
    {
        if (bridge is null) return;
        if (bridge.OutputReconnectPending)
        {
            bridge.Dispose(); bridge = null; reconnectAfter = DateTime.UtcNow.AddMilliseconds(750);
            SetText(connection, L.T("出力方式を適用しています。USBの再接続を待っています…", "Applying the output mode. Waiting for USB to reconnect…"));
            return;
        }
        DeviceState device = await bridge.ReadDeviceAsync(); caps = device.Caps; generation = caps.ConnectionGeneration; cursor = device.Cursor;
        fields.Clear(); fields.AddRange(device.Fields); outputState = caps.SupportsMouseOutput ? await bridge.ReadOutputStateAsync() : null;
        SyncOutputControls(); UpdateConnection();
    }
    internal static bool NoInput(Capabilities? value) => value is { Connected: false, Vid: 0, FieldCount: 0 };
    internal static string WaitingState(Capabilities value) => NoInput(value)
        ? L.T("入力コントローラーの接続待ち", "Waiting for an input controller")
        : value.HostStatus is 2 or 4 or 6 or 10 ? L.T("入力コントローラーを初期化できません。接続と対応形式を確認してください。", "The input controller could not be initialized. Check the connection and device type.")
        : L.T("入力コントローラーを初期化しています…", "Initializing the input controller…");
    private void UpdateConnection()
    {
        if (caps is null) return;
        string connectionText = caps.Connected ? L.T($"本体 接続済み  •  入力 {caps.Vid:X4}:{caps.Pid:X4}  •  {caps.ProtocolLabel} {caps.FieldCount}項目", $"Device connected  •  Input {caps.Vid:X4}:{caps.Pid:X4}  •  {caps.ProtocolLabel}, {caps.FieldCount} fields") :
            caps.Vid != 0 ? L.T($"本体 接続済み  •  入力 {caps.Vid:X4}:{caps.Pid:X4} を検出", $"Device connected  •  Input {caps.Vid:X4}:{caps.Pid:X4} detected") :
            L.T("本体 接続済み  •  入力コントローラーをUSB-Aへ接続してください。", "Device connected  •  Connect an input controller to USB-A.");
        string stateText = !caps.Connected ? WaitingState(caps) : caps.InvalidBinding ? L.T("入力形式が保存済みの割り当てと一致しません。再登録してください。", "The input layout does not match the saved mapping. Register the controller again.") :
            caps.ActiveSlot < profiles.Length ? L.T($"スロット {caps.ActiveSlot + 1} を使用中", $"Slot {caps.ActiveSlot + 1} is active") :
            L.T("このコントローラーに一致するプロファイルがありません。未使用スロットから追加してください。", "This controller has no matching profile. Use an empty slot to add it.");
        if (caps.Connected && caps.ProfileCount > 1) stateText += L.T($"  •  [{caps.ProfileNumber}/{caps.ProfileCount}]  ", $"  •  [{caps.ProfileNumber}/{caps.ProfileCount}]  ") +
            (caps.SwitchKeys == 0 ? L.T("BOOT長押しで切替", "Hold BOOT to cycle") : L.T("ショートカットで切替", "Use the shortcut to cycle"));
        if (caps.Connected && caps.Partial && outputState?.Active.Mouse != true) stateText += L.T("  •  一部の入力形式は未対応", "  •  Some inputs are unsupported");
        if (caps.Connected && !fields.Any(f => f.Learnable)) stateText = L.T("割り当て可能なボタン・絶対値入力がありません。", "No assignable buttons or absolute inputs were found.");
        else if (caps.Connected && !fields.Any(f => f.Learnable && f.Valid)) stateText = L.T("操作データの受信待ち", "Waiting for controller input");
        if (!caps.Persisted) stateText += L.T("  •  初期設定を使用中", "  •  Using default settings");
        if (caps.SelectionUnsaved) stateText += L.T("  •  選択スロットが未保存", "  •  Active slot not saved");
        if (caps.BankGeneration != bankGeneration) {
            stateText = L.T("本体の設定が変更されています。「本体から再読込」を押してください。", "The device settings have changed. Choose “Reload device”.");
            SetText(saved, stateText);
        }
        if (InputNeedsSpace) { stateText = NoProfileSpace; connectionText += "\n" + NoProfileSpace; }
        // Compose the complete strings before assigning. Appending status text
        // directly to Labels repainted them on every poll, even with no input.
        SetText(connection, connectionText); SetConnectionDetail(stateText); UpdateProfileOutputStatus();
        if (displayedActiveSlot != ConnectedSlot)
        {
            int previous = displayedActiveSlot; displayedActiveSlot = ConnectedSlot;
            if (previous >= 0 && previous < profiles.Length) slots.Invalidate(slots.GetItemRectangle(previous));
            if (displayedActiveSlot >= 0) slots.Invalidate(slots.GetItemRectangle(displayedActiveSlot));
        }
        UpdateEnabled();
    }
    private void UpdateProfileOutputStatus()
    {
        DeviceProfile p = profiles[selected];
        string text = p.OutputMode == 0 ? L.T("出力: ゲームパッド", "Output: gamepad") : p.OutputMode == 1 ? L.T("出力: USBマウス", "Output: USB mouse") :
            p.ControllerButtons ? L.T("出力: マウス移動＋コントローラー操作", "Output: mouse movement + controller inputs") : L.T("出力: マウス＋−／＋／HOME", "Output: mouse + − / + / HOME");
        if (p.OutputMode != 0)
            text += L.T($"  •  ボール {(p.Mouse.TrackballEnabled ? p.Mouse.TrackballGain.ToString("0.0") + "×" : "OFF")} / パドル {(p.Mouse.PaddleEnabled ? p.Mouse.PaddleGain.ToString("0.0") + "×" : "OFF")}",
                $"  •  Ball {(p.Mouse.TrackballEnabled ? p.Mouse.TrackballGain.ToString("0.0") + "×" : "OFF")} / Paddle {(p.Mouse.PaddleEnabled ? p.Mouse.PaddleGain.ToString("0.0") + "×" : "OFF")}");
        SetText(outputStatus, p.Enabled ? text : "");
    }
    private void ApplyChanges(IEnumerable<InputChange> changes, bool deliver)
    {
        string? latest = null;
        foreach (InputChange change in changes)
        {
            InputField? f = fields.FirstOrDefault(f => f.Index == change.Index);
            // Observe every received change, including baseline draining and a
            // final overrun event, independently of whether learning accepts it.
            wizard?.ObserveInput(f, change);
            if (change.Lost) { AbortLearning(L.T("入力の観測が追いつかなかったため、割り当てを中止しました。", "Assignment stopped because input events were missed.")); wizard?.ConnectionLost(); deliver = false; }
            if (f is null) continue;
            f.Value = change.Value; f.Valid = true;
            latest = L.T($"入力: {f.Label} = {change.Value}", $"Input: {f.Label} = {change.Value}");
            if (deliver) { ActiveEditor.OnInput(f, change.Value); wizard?.OnInput(f, change.Value); }
        }
        // Deliver every event to learning, but paint only the final visible value.
        if (latest is not null) SetText(live, latest);
    }
    private async Task Poll()
    {
        if (polling || working || connecting || closing) return;
        if (bridge is null) { if (!preview && DateTime.UtcNow >= reconnectAfter) await Connect(false); return; }
        polling = true;
        try
        {
            PollResult result = await bridge.PollAsync(cursor, generation); if (closing) return;
            bool emptyRetry = NoInput(caps) && NoInput(result.Caps);
            if (!emptyRetry && (result.GenerationChanged || result.Caps.FieldCount != fields.Count || result.Caps.DescriptorHash != caps?.DescriptorHash))
            {
                AbortLearning(); wizard?.ConnectionLost(); await RefreshDevice();
                if (!dirty.Any(x => x) && caps!.ActiveSlot < profiles.Length) { selected = caps.ActiveSlot; RefreshSlots(); }
                BindSelected();
            }
            else
            {
                int oldActive = caps?.ActiveSlot ?? 255; bool rebind = false;
                caps = result.Caps; cursor = emptyRetry ? caps.EventHead : result.Cursor; generation = caps.ConnectionGeneration;
                if (caps.BankGeneration != observedBankGeneration)
                {
                    var bank = await bridge.ReadProfilesAsync(); observedBankGeneration = bank.Caps.BankGeneration; caps = bank.Caps;
                    bool unchanged = Enumerable.Range(0, profiles.Length).All(i => persistedProfiles[i].SameContent(bank.Profiles[i]));
                    if (!dirty.Any(x => x)) { ApplyBank(bank.Profiles, bank.Caps.BankGeneration); RefreshSlots(); rebind = true; }
                    else if (unchanged) bankGeneration = bank.Caps.BankGeneration;
                }
                bool activeChanged = oldActive != caps.ActiveSlot;
                if (activeChanged)
                {
                    AbortLearning(); wizard?.ConnectionLost();
                    outputState = await bridge.ReadOutputStateAsync();
                    if (!dirty.Any(x => x) && caps.ActiveSlot < profiles.Length) { selected = caps.ActiveSlot; RefreshSlots(); rebind = true; }
                }
                if (rebind) BindSelected();
                if (!emptyRetry) ApplyChanges(result.Changes, !activeChanged);
                UpdateConnection();
            }
        }
        catch (Exception ex)
        {
            AbortLearning(); wizard?.ConnectionLost(); bridge?.Dispose(); bridge = null; reconnectAfter = DateTime.UtcNow.AddSeconds(2);
            ClearDeviceState();
            SetConnectionDetail(ex.Message); SetText(connection, L.T("本体の再接続待ち。編集内容は保持しています。", "Waiting for the device to reconnect. Your edits are kept.")); UpdateEnabled();
        }
        finally { polling = false; HandleControllerArrival(); }
    }
    private async Task<bool> PrepareLearning()
    {
        if (preview) return true;
        if (bridge is null || working || closing) return false;
        working = true; await WaitForPoll();
        try
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                PollResult result = await bridge.PollAsync(cursor, generation);
                if (result.GenerationChanged) { AbortLearning(); wizard?.ConnectionLost(); await RefreshDevice(); BindSelected(); return false; }
                cursor = result.Cursor; caps = result.Caps; ApplyChanges(result.Changes, false);
                if (unchecked((int)(cursor - caps.EventHead)) >= 0) return true;
            }
            SetText(saved, L.T("操作を止めてから、割り当てるボタンを選び直してください。", "Stop moving the controller, then select the button to assign again.")); return false;
        }
        catch (Exception ex) { Ui.Error(this, ex); return false; }
        finally { working = false; UpdateEnabled(); }
    }
    private async Task SaveSelected()
    {
        if (working || bridge is null && !preview) return; int slot = selected;
        try
        {
            DeviceProfile p = profiles[slot].Clone(); CheckDuplicate(slot, p);
            if (p.Enabled && p.ProfileId == Guid.Empty) p.ProfileId = Guid.NewGuid();
            working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
            SetText(saved, L.T("スロットを保存し、読み戻して照合しています…", "Saving and verifying the slot…"));
            if (!preview) bankGeneration = await bridge!.SaveAsync(slot, p, bankGeneration);
            profiles[slot] = p; persistedProfiles[slot] = p.Clone(); dirty[slot] = false; observedBankGeneration = bankGeneration;
            if (!preview) await RefreshDevice(); RefreshSlots(); BindSelected();
            SetText(saved, L.F("スロット {0} を本体に保存しました。", "Saved slot {0} to the device.", slot + 1));
        }
        catch (Exception ex) { SetText(saved, L.T("保存結果を確認できませんでした。編集内容は保持しています。", "The save could not be verified. Your edits are kept.")); Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
    private void CheckDuplicate(int slot, DeviceProfile profile) => ProfileSlots.Validate(persistedProfiles, slot, profile, preview || caps?.SupportsOnBoardVariants == true);
    private async Task ConfigureSwitchMethod()
    {
        if (bridge is null || working || dirty.Any(x => x)) return;
        working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
        try
        {
            uint current = await bridge.ReadSwitchKeysAsync(selected);
            DeviceProfile[] group = persistedProfiles.Where(p => p.SameController(persistedProfiles[selected])).ToArray();
            using SwitchMethodDialog dialog = new(group, current, caps!.SupportsImmediateSwitch);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            bankGeneration = observedBankGeneration = await bridge.SaveSwitchKeysAsync(selected, dialog.SwitchKeys, bankGeneration);
            await RefreshDevice(); SetText(saved, L.T("同じコントローラーの切替方法を保存しました。", "Saved the switching method for this controller's profiles."));
        }
        catch (Exception ex) { Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
    private void AbortLearning(string? reason = null) { editor.AbortLearning(reason); mouseEditor.AbortLearning(reason); combinedEditor.AbortLearning(reason); }
    private void SyncMousePage()
    {
        int index = Math.Max(0, outputTabs.SelectedIndex);
        if (timing.Parent != tabContents[index]) tabContents[index].Controls.Add(timing, 0, 0);
        if (index > 0 && mouseContent.Parent != tabContents[index]) tabContents[index].Controls.Add(mouseContent, 0, 1);
        bool combined = index == 2; buttonBehavior.Visible = combined;
        MappingEditor mapping = combined && gamepadButtons.Checked ? combinedEditor : mouseEditor;
        if (mapping.Parent != mouseContent) { mouseContent.Controls.Remove(mouseEditor); mouseContent.Controls.Remove(combinedEditor); mouseContent.Controls.Add(mapping, 0, 1); }
        mouseEditor.SetNavigationMode(combined);
        string mouseHint = combined && gamepadButtons.Checked ? L.T("マウス移動と、下のボタン・方向・スティックを送信します。マウスクリックは出力しません。", "Sends mouse movement and the buttons, D-pad and sticks below. No mouse clicks.") :
            combined ? L.T("B→左クリック、A→右クリック。コントローラー側は−／＋／HOMEのみ送信します。", "B → left click, A → right click. The controller sends only − / + / HOME.") :
            L.T("パドルは横移動に変換します。両方ONで同時に使えます。", "The paddle controls horizontal movement. Enable both to use them together.");
        SetTip(buttonBehavior, mouseHint); SetTip(mouseButtons, mouseHint); SetTip(gamepadButtons, mouseHint);
    }
    private void MouseProfileChanged()
    {
        if (loading) return;
        profiles[selected].Mouse = new() { TrackballEnabled = trackballOn.Checked, PaddleEnabled = paddleOn.Checked, TrackballGain = mouseGain.Value, PaddleGain = paddleGain.Value,
            InvertX = invertX.Checked, InvertY = invertY.Checked, InvertPaddle = invertPaddle.Checked }; MarkDirty();
    }
    private void SyncOutputControls()
    {
        DeviceProfile profile = profiles[selected]; bool previousSync = syncingOutput; syncingOutput = true;
        try { gamepadButtons.Checked = profile.ControllerButtons; mouseButtons.Checked = !profile.ControllerButtons;
            outputTabs.SelectedIndex = profile.OutputMode; SyncMousePage(); }
        finally { syncingOutput = previousSync; }
    }
    private async Task UseProfile()
    {
        if (bridge is null || working || dirty[selected]) return;
        working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
        try { bankGeneration = observedBankGeneration = await bridge.SelectProfileAsync(selected, bankGeneration); await RefreshDevice(); BindSelected();
            SetText(saved, L.T("選択したプロファイルへ切り替えました。", "Switched to the selected profile.")); }
        catch (Exception ex) { Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
    private async Task AddController()
    {
        if (working || closing || !CanRegisterSlot(selected) || !preview && (!CanRegisterInput || !caps!.SupportsGuardedRegistration || !fields.Any(f => f.Learnable && f.Valid))) return;
        int slot = selected; HidBridge? targetBridge = bridge; InputConnection input = InputConnection.From(caps!);
        uint expectedBank = bankGeneration;
        bool SameTarget() => !closing && caps is not null && input.Matches(caps) && ReferenceEquals(bridge, targetBridge) &&
            (preview || bridge is not null) && CanRegisterSlot(slot) && bankGeneration == expectedBank && caps.BankGeneration == expectedBank;
        AbortLearning(); if (!await PrepareLearning() || !SameTarget()) return;
        using RegistrationWizard dialog = new(slot, caps!, fields, PrepareLearning, async p =>
        {
            CheckDuplicate(slot, p); working = true; await WaitForPoll();
            try {
                if (!SameTarget()) throw new IOException(L.T("登録対象の接続または保存先が変更されました。登録をやり直してください。", "The controller connection or destination changed. Start registration again."));
                p.ProfileId = Guid.NewGuid();
                if (!preview) bankGeneration = observedBankGeneration = await targetBridge!.SaveAsync(slot, p, expectedBank, input);
                profiles[slot] = p; persistedProfiles[slot] = p.Clone(); dirty[slot] = false; }
            finally { working = false; }
        });
        wizard = dialog;
        try { if (dialog.ShowDialog(this) == DialogResult.OK) { selected = slot; if (!preview) await RefreshDevice(); RefreshSlots(); BindSelected(); SetText(saved, L.T("コントローラーを登録しました。", "Controller added.")); } }
        catch (Exception ex) { Ui.Error(this, ex); }
        finally { wizard = null; UpdateEnabled(); }
    }
    private void ConfigureLed()
    {
        if (working || !profiles[selected].Enabled) return;
        using LedRgbDialog dialog = new(profiles[selected]);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        ApplyLedSettings(dialog);
    }
    private void ApplyLedSettings(LedRgbDialog dialog)
    {
        DeviceProfile before = profiles[selected].Clone();
        dialog.ApplyTo(profiles[selected]);
        if (!profiles[selected].SameContent(before)) MarkDirty();
    }
    private void DeleteProfile()
    {
        if (MessageBox.Show(this, L.F("スロット {0} の「{1}」を削除しますか？\n本体への反映は保存時に行います。", "Delete “{1}” from slot {0}?\nThis takes effect when you save the slot.", selected + 1, profiles[selected].Name),
            L.T("プロファイル削除", "Delete profile"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        profiles[selected] = new(); MarkDirty(); RefreshSlots(); BindSelected();
    }
    private void DuplicateProfile()
    {
        int destination = ProfileSlots.EmptyBelow(profiles, selected); if (destination < 0 || !profiles[selected].Enabled) return;
        try
        {
            DeviceProfile copy = profiles[selected].Clone(); copy.ProfileId = Guid.NewGuid();
            copy.Name = ProfileSlots.DuplicateName(profiles, copy.Name);
            profiles[destination] = copy; selected = destination; MarkDirty(); RefreshSlots(); BindSelected();
            SetText(saved, L.F("スロット {0} に複製しました。「スロットを本体に保存」で反映します。", "Copied to slot {0}. Choose “Save slot to device” to apply it.", destination + 1));
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    private async Task<SlotBank> ReadCurrentBank()
    {
        if (preview) return SlotBank.FromProfiles(persistedProfiles);
        SlotBank bank = await bridge!.ReadBankAsync();
        if (bank.Generation != bankGeneration || !Enumerable.Range(0, profiles.Length).All(i => persistedProfiles[i].SameContent(bank.Profiles[i])))
            throw new IOException(L.T("本体の設定が変わっています。再読込してください。", "The device settings have changed. Reload them first."));
        return bank;
    }
    private async Task BackupAll()
    {
        if (working || dirty.Any(x => x) || bridge is null && !preview) return;
        using SaveFileDialog dialog = new() { Filter = L.T("ControllerBridge バックアップ (*.json)|*.json", "ControllerBridge backup (*.json)|*.json"), FileName = $"ControllerBridge-slots-{DateTime.Now:yyyyMMdd-HHmmss}.json", OverwritePrompt = true };
        tips.Hide(backup);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        working = true; UpdateEnabled(); await WaitForPoll();
        try { SlotBackup.Save(dialog.FileName, await ReadCurrentBank()); SetText(saved, L.T("全16スロットと切替設定をバックアップしました。", "Backed up all 16 slots and profile switching settings.")); }
        catch (Exception ex) { Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
    private async Task RestoreAll()
    {
        if (working || dirty.Any(x => x) || bridge is null && !preview) return;
        using OpenFileDialog dialog = new() { Filter = L.T("ControllerBridge バックアップ (*.json)|*.json", "ControllerBridge backup (*.json)|*.json"), Title = L.T("全スロットを復帰", "Restore all slots") };
        tips.Hide(restore);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            SlotBank loaded = SlotBackup.Load(dialog.FileName);
            if (MessageBox.Show(this, L.F("全16スロットをこのバックアップで置き換えます。\n登録済み: {0} / 16\n切替方法と選択設定も復帰します。続行しますか？",
                "Replace all 16 slots with this backup?\nRegistered profiles: {0} / 16\nProfile switching settings and saved selections will also be restored.", loaded.Profiles.Count(p => p.Enabled)),
                L.T("全スロットを復帰", "Restore all slots"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
            SetText(saved, L.T("全スロットを復帰し、読み戻して照合しています…", "Restoring and verifying all slots…"));
            SlotBank actual = preview ? loaded : await bridge!.WriteBankAsync(loaded, bankGeneration);
            ApplyBank(actual.Profiles, actual.Generation); if (!preview) await RefreshDevice();
            if (caps?.ActiveSlot < profiles.Length) selected = caps.ActiveSlot; RefreshSlots(); BindSelected();
            SetText(saved, L.T("全16スロットと切替設定を復帰しました。", "Restored all 16 slots and profile switching settings."));
        }
        catch (Exception ex) { Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
    private async Task MoveSlot(int source, int destination)
    {
        if (slots.CanMove?.Invoke(source) != true) return;
        working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
        try
        {
            SlotBank bank = await ReadCurrentBank(), moved = bank.Move(source, destination);
            SetText(saved, L.T("スロットの順番を保存しています…", "Saving the slot order…"));
            SlotBank actual = preview ? moved : await bridge!.WriteBankAsync(moved, bank.Generation);
            ApplyBank(actual.Profiles, actual.Generation); selected = destination;
            if (!preview) await RefreshDevice(); RefreshSlots(); BindSelected();
            SetText(saved, L.T("スロットの順番を保存しました。BOOT・ショートカットもこの順で切り替わります。", "Slot order saved. BOOT and shortcuts follow this order."));
        }
        catch (Exception ex) { SetText(saved, L.T("並べ替えの結果を確認できません。本体から再読込してください。", "The new order could not be verified. Reload the device.")); Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closing) return; if (working) { e.Cancel = true; return; }
        if (dirty.Any(x => x) && MessageBox.Show(this, L.T("本体に保存していない編集があります。終了しますか？", "There are unsaved edits. Close ControllerBridge?"),
            L.T("未保存の編集", "Unsaved edits"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel = true; return; }
        e.Cancel = true; closing = true; timer.Stop(); while (polling) await Task.Delay(5);
        bridge?.Dispose(); bridge = null; timer.Dispose(); tips.Dispose(); BeginInvoke(Close);
    }
    private void LoadPreview()
    {
        ApplyBank(Enumerable.Range(0, 16).Select(_ => new DeviceProfile()).ToArray(), 1);
        string[] names = ["EGRET II mini", "X68000 Z JOYCARD", L.T("M30 標準", "M30 Standard"), "JOYCARD " + L.T("別設定", "Alternate"), "EGRET II mini Paddle Controller", "Cyber Stick G-LOC"];
        for (int i = 0; i < names.Length; i++)
        {
            DeviceProfile p = new() { Flags = 3, Vid = (ushort)(i == 4 ? 0x0ae4 : 0x1234 + i), Pid = (ushort)(i == 4 ? 0x0701 : i + 1),
                Name = names[i], InputInterval = 1, OutputMode = (byte)(i == 4 ? 2 : 0), DescriptorHash = 0x12345678, ProfileId = Guid.NewGuid() };
            for (int b = 0; b < 18; b++) p.Buttons[b] = new() { BitOffset = (ushort)b, Bits = 1, Mode = 1, Low = 1, High = 1 };
            profiles[i] = p; persistedProfiles[i] = p.Clone();
        }
        selected = 4; fields.Clear();
        for (ushort i = 0; i < 18; i++) fields.Add(new() { Index = i, Bits = 1, BitOffset = i, Minimum = 0, Maximum = 1, Value = 0, Valid = true, UsagePage = 9, Usage = (ushort)(i + 1) });
        DeviceProfile active = profiles[selected]; caps = new(4, 18, active.Vid, active.Pid, 0, 5, 1, active.DescriptorHash, 1, 0, 1, 5, Features: 8191);
        outputState = new(active.Output, active.Output, true);
        SyncOutputControls(); UpdateConnection(); RefreshSlots(); BindSelected(); ConnectedSuccessfully = true;
        SetText(connection, L.T("画面確認用サンプル  •  本体設定は変更しません", "Preview sample  •  Device settings are not changed"));
    }
    internal void CapturePreview(string path, bool minimumSize = false)
    {
        if (minimumSize) { Size = MinimumSize; PerformLayout(); Application.DoEvents(); }
        using Bitmap bitmap = new(Width, Height); DrawToBitmap(bitmap, new(0, 0, Width, Height)); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        MappingEditor mapping = ActiveEditor;
        File.WriteAllText(Path.ChangeExtension(path, ".layout.json"), System.Text.Json.JsonSerializer.Serialize(new {
            title = Text, DeviceDpi, window = new { Width, Height }, client = new { ClientSize.Width, ClientSize.Height },
            workingArea = Screen.FromControl(this).WorkingArea.ToString(),
            editor = new { mapping.Width, mapping.Height, horizontalScroll = mapping.HorizontalScroll.Visible,
                verticalScroll = mapping.VerticalScroll.Visible, contentHeight = mapping.Controls[0].Height + mapping.Padding.Vertical }
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    internal void CaptureDialogs(string directory)
    {
        if (caps is null) return;
        static void Capture(Form form, string path)
        {
            form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); form.PerformLayout(); Application.DoEvents();
            using Bitmap bitmap = new(form.Width, form.Height); form.DrawToBitmap(bitmap, new(0, 0, form.Width, form.Height)); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        Directory.CreateDirectory(directory);
        using RegistrationWizard dialog = new(1, caps, fields, () => Task.FromResult(true), _ => throw new InvalidOperationException("Preview cannot save"));
        Capture(dialog, Path.Combine(directory, "wizard-1.png"));
        dialog.PreviewStep(1); Capture(dialog, Path.Combine(directory, "wizard-2.png"));
        dialog.PreviewStep(2); Capture(dialog, Path.Combine(directory, "wizard-3.png")); dialog.Close();
        List<InputField> axisFields = [.. fields];
        if (preview) axisFields.Add(new() { Index = 18, Bits = 16, Flags = 1, BitOffset = 32, Minimum = -32768, Maximum = 32767, Value = 0, Valid = true, UsagePage = 1, Usage = 0x30 });
        InputField? field = axisFields.FirstOrDefault(f => f.Learnable && !f.Binary && !f.Hat);
        AxisBinding axis = field is null ? new() : new() { Mode = 1, Analog = field.Analog(field.Minimum, field.Value, field.Maximum, 5, false) };
        using AxisDialog analog = new(0, axis, axisFields, true); Capture(analog, Path.Combine(directory, "axis.png")); analog.Close();
        using AxisDialog pair = new(0, new() { Mode = 2 }, fields, true); Capture(pair, Path.Combine(directory, "axis-pair.png")); pair.Close();
        using AxisDialog unused = new(0, new(), fields, true); Capture(unused, Path.Combine(directory, "axis-unused.png")); unused.Close();
        using RapidFireDialog rapid = new(profiles[selected]); Capture(rapid, Path.Combine(directory, "rapid-fire.png")); rapid.Close();
        using SwitchMethodDialog switchMethod = new([profiles[selected]], caps.SwitchKeys, caps.SupportsImmediateSwitch);
        Capture(switchMethod, Path.Combine(directory, "switch-method.png")); switchMethod.Close();
        CaptureOutputTabs(directory);
    }
    internal void CaptureOutputTabs(string directory)
    {
        Directory.CreateDirectory(directory); DeviceProfile original = profiles[selected].Clone();
        syncingOutput = true;
        try
        {
            for (int i = 0; i < 3; i++)
            {
                profiles[selected].OutputMode = (byte)i;
                if (i == 2)
                {
                    profiles[selected].ControllerButtons = false; BindSelected(); PerformLayout(); Application.DoEvents();
                    CapturePreview(Path.Combine(directory, "combined-clicks-tab.png"));
                    profiles[selected].ControllerButtons = true;
                }
                BindSelected(); PerformLayout(); Application.DoEvents();
                CapturePreview(Path.Combine(directory, i == 0 ? "gamepad-tab.png" : i == 1 ? "mouse-tab.png" : "combined-tab.png"));
            }
        }
        finally { profiles[selected] = original; syncingOutput = false; BindSelected(); }
        if (preview)
        {
            int previous = selected;
            selected = Array.FindIndex(profiles, p => !p.Enabled); RefreshSlots(); BindSelected(); PerformLayout(); Application.DoEvents();
            CapturePreview(Path.Combine(directory, "unused-slot.png"));
            selected = previous; RefreshSlots(); BindSelected();
            ActiveEditor.SelectButton(2); PerformLayout(); Application.DoEvents();
            CapturePreview(Path.Combine(directory, "button-learning.png"));
        }
    }
    internal async Task CheckOutputTabs(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!preview) throw new InvalidOperationException("UI checks use preview data only.");
        List<string> checks = [];
        void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); checks.Add(message); }
        profiles[1].OutputMode = 1; persistedProfiles[1] = profiles[1].Clone();
        slots.SelectedIndex = 0;
        Check(outputTabs.SelectedIndex == 0 && !buttonBehavior.Visible, "gamepad slot selects gamepad tab and hides button behavior");
        slots.SelectedIndex = 1;
        Check(outputTabs.SelectedIndex == 1 && !buttonBehavior.Visible, "mouse slot selects mouse tab and hides button behavior");
        slots.SelectedIndex = 4;
        Check(outputTabs.SelectedIndex == 2 && buttonBehavior.Visible && mouseButtons.Checked && !gamepadButtons.Checked, "combined slot selects its tab and mouse radio");
        Check(!dirty.Any(x => x), "browsing slots does not create edits");
        byte[] other = profiles[1].Encode();
        gamepadButtons.Checked = true;
        Check(profiles[4].ControllerButtons && !mouseButtons.Checked && dirty[4], "controller radio edits only the selected profile");
        Check(!working && profiles[1].Encode().SequenceEqual(other) && !dirty[1], "button edit does not save or change another slot");
        outputTabs.SelectedIndex = 0;
        Check(profiles[4].OutputMode == 0 && !buttonBehavior.Visible && profiles[4].ControllerButtons, "tab editing keeps the combined-mode preference");
        outputTabs.SelectedIndex = 2;
        Check(buttonBehavior.Visible && gamepadButtons.Checked && !mouseButtons.Checked, "returning to combined mode restores its radio selection");
        slots.SelectedIndex = 1;
        Check(outputTabs.SelectedIndex == 1 && !buttonBehavior.Visible && !gamepadButtons.Checked, "other profile keeps its own tab and radio preference");
        slots.SelectedIndex = 4;
        Check(outputTabs.SelectedIndex == 2 && gamepadButtons.Checked && dirty[4], "unsaved mode edits survive slot navigation");
        await SaveSelected();
        Check(!dirty[4] && persistedProfiles[4].OutputMode == 2 && persistedProfiles[4].ControllerButtons, "save commits output mode and radio together");
        mouseButtons.Checked = true;
        Check(!profiles[4].ControllerButtons && !gamepadButtons.Checked && dirty[4], "mouse radio is mutually exclusive and marks the slot dirty");
        profiles[4] = persistedProfiles[4].Clone(); dirty[4] = false; BindSelected();
        Check(gamepadButtons.Checked && !mouseButtons.Checked, "reloading a profile restores saved button behavior");
        DuplicateProfile();
        Check(selected == 6 && profiles[6].OutputMode == 2 && profiles[6].ControllerButtons && profiles[6].ProfileId != profiles[4].ProfileId, "duplicate keeps mode and behavior with a new identity");
        await SaveSelected();
        SlotBank bank = SlotBank.FromProfiles(persistedProfiles);
        string backupPath = Path.Combine(directory, "ui-slots.json"); SlotBackup.Save(backupPath, bank);
        SlotBank restored = SlotBackup.Load(backupPath).Move(6, 2);
        ApplyBank(restored.Profiles, restored.Generation); selected = 2; RefreshSlots(); BindSelected();
        Check(outputTabs.SelectedIndex == 2 && gamepadButtons.Checked, "backup restore and reorder retain profile output controls");
        selected = 15; RefreshSlots(); BindSelected();
        Check(!details.Visible && !buttonBehavior.Visible && !dirty.Any(x => x), "unused slot hides tabs and button behavior without creating edits");
        selected = 2; RefreshSlots(); BindSelected();
        CapturePreview(Path.Combine(directory, "profile-tabs.png"));
        File.WriteAllText(Path.Combine(directory, "tab-check.json"), System.Text.Json.JsonSerializer.Serialize(new { passed = true, checks }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    internal async Task CheckEmptyInput(string directory)
    {
        Directory.CreateDirectory(directory);
        if (bridge is null || !NoInput(caps)) throw new IOException("Disconnect only the input controller from USB-A for this read-only check.");
        int textChanges = 0, bindings = selectionBindings;
        uint initialBank = bankGeneration; string initialConnection = connection.Text, initialState = connectionDetail;
        EventHandler changed = (_, _) => textChanges++;
        connection.TextChanged += changed;
        HashSet<uint> generations = []; HashSet<byte> hostStates = [];
        try
        {
            for (int i = 0; i < 120; i++)
            {
                await Task.Delay(100);
                if (!NoInput(caps) || bridge is null) throw new IOException("Input or bridge connection changed during the check.");
                generations.Add(caps!.ConnectionGeneration); hostStates.Add(caps.HostStatus);
            }
            bool passed = textChanges == 0 && selectionBindings == bindings && bankGeneration == initialBank &&
                initialConnection == connection.Text && initialState == connectionDetail;
            File.WriteAllText(Path.Combine(directory, "empty-input-check.json"), System.Text.Json.JsonSerializer.Serialize(new
            { passed, durationMs = 12000, textChanges, editorRebindings = selectionBindings - bindings, initialBank, bankGeneration,
              connectionGenerations = generations.Order().ToArray(), hostStates = hostStates.Order().ToArray(), connection = connection.Text, state = connectionDetail }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            CapturePreview(Path.Combine(directory, "empty-input.png"));
            if (!passed) throw new InvalidDataException("Empty-input status changed or rebound the editor during host retries.");
        }
        finally { connection.TextChanged -= changed; }
    }
}
