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
    private DateTime reconnectAfter;
    private int selected, displayedActiveSlot = -2;
    private RegistrationWizard? wizard;
    private readonly ToolTip tips = new();
    private readonly SlotList slots = new();
    private readonly MappingEditor editor = new(), mouseEditor = new(true), combinedEditor = new();
    private readonly DarkTabControl outputTabs = new() { Dock = DockStyle.Fill, Padding = new(23, 12) };
    private readonly BufferedTableLayoutPanel outputContent, timing;
    private readonly BufferedTableLayoutPanel body, details;
    private readonly TextBox name = new() { Dock = DockStyle.Fill, MaxLength = 47 };
    private readonly NumericUpDown interval = new() { Minimum = 0, Maximum = 255, Width = 78, Anchor = AnchorStyles.Left };
    private readonly NumericUpDown mouseGain = new() { Minimum = 1, Maximum = 256, Increment = .1m, DecimalPlaces = 1, Width = 78, Anchor = AnchorStyles.Left };
    private readonly NumericUpDown paddleGain = new() { Minimum = 1, Maximum = 10, Increment = .1m, DecimalPlaces = 1, Width = 78, Anchor = AnchorStyles.Left };
    private readonly CheckBox trackballOn = new() { Text = L.T("トラックボール ON", "Trackball ON"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox paddleOn = new() { Text = L.T("パドル ON", "Paddle ON"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox invertX = new() { Text = L.T("X方向を反転", "Invert X"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox invertY = new() { Text = L.T("Y方向を反転", "Invert Y"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly BufferedTableLayoutPanel pointingTuning;
    private readonly ComboBox paddleAxis = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 88, Anchor = AnchorStyles.Left, AccessibleName = L.T("パドルの出力軸", "Paddle output axis") };
    private readonly CheckBox trackballKeys = new DarkCheckBox() { Text = L.T("↑↓←→に変換", "Convert to ↑↓←→"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox paddleKeys = new DarkCheckBox() { Text = L.T("変換を有効", "Enable conversion"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox invertPaddle = new() { Text = L.T("回転方向を反転", "Reverse rotation"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly RadioButton mouseButtons = new() { Text = L.T("マウス扱い", "Mouse"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly RadioButton gamepadButtons = new() { Text = L.T("コントローラー扱い", "Controller"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly FlowLayoutPanel buttonBehavior = new() { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = Padding.Empty };
    private readonly Label connection = Ui.Label(L.T("デバイスのUSB-CをPCへ接続してください。", "Connect the bridge's USB-C port to this PC."), 10);
    private string connectionDetail = "";
    private readonly Label live = Ui.Label(L.T("入力: —", "Input: —"), 9);
    private readonly Label identity = Ui.Label("", 9);
    private readonly Label saved = Ui.Label(L.T("16スロットをデバイスに保存できます。", "Store up to 16 profiles on the bridge."), 9);
    private readonly Label outputStatus = Ui.Label("", 9);
    private readonly Label combinedXNotice = Ui.Label(L.T("Xモードでは接続出来なくなります", "Cannot connect in X mode."), 9);
    private readonly Button connect, reload, saveButton, useProfile, switchMethod, mouseUdlrSettings, delete, duplicate, ledSettings, conversionSettings, backup, restore, firmwareUpdate;
    private MappingEditor ActiveEditor => outputTabs.SelectedIndex == 0 ? editor :
        outputTabs.SelectedIndex == 2 && gamepadButtons.Checked ? combinedEditor : mouseEditor;

    public MainForm()
    {
        const string productTitle = "ControllerBridge v";
        using IDisposable formLayout = Ui.Form(this, productTitle + typeof(MainForm).Assembly.GetName().Version?.ToString(3) + "", new(1290, 1100), new(1290, 700));
        // Keep the full initial layout, but let the editor scroll on shorter screens.
        StartPosition = FormStartPosition.CenterScreen;
        DoubleBuffered = true;
        BufferedTableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(16, 12, 16, 12), ColumnCount = 1, RowCount = 3 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize));
        BufferedTableLayoutPanel header = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 2, RowCount = 1, Padding = new(2, 0, 2, 8) };
        header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.AutoSize));
        header.RowStyles.Add(new(SizeType.AutoSize));
        connection.Dock = DockStyle.Fill; connection.ForeColor = Ui.Muted;
        header.Controls.Add(connection, 0, 0);
        BufferedTableLayoutPanel headerActions = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4, RowCount = 1, Margin = Padding.Empty };
        for (int i = 0; i < 4; i++) headerActions.ColumnStyles.Add(new(SizeType.Percent, 25));
        headerActions.RowStyles.Add(new(SizeType.AutoSize));
        header.Controls.Add(headerActions, 1, 0);
        firmwareUpdate = Ui.Button(L.T("ファームウェア更新", "Firmware update"), async (_, _) => await UpdateFirmware()); firmwareUpdate.Dock = DockStyle.Fill; headerActions.Controls.Add(firmwareUpdate, 0, 0);
        connect = Ui.Button(L.T("接続 / 再接続", "Connect / Reconnect"), async (_, _) => await Connect()); connect.Dock = DockStyle.Fill; headerActions.Controls.Add(connect, 1, 0);
        switchMethod = Ui.Button(L.T("プロファイル切替方法…", "Profile switching…"), async (_, _) => await ConfigureSwitchMethod()); switchMethod.Dock = DockStyle.Fill; headerActions.Controls.Add(switchMethod, 2, 0);
        mouseUdlrSettings = Ui.Button(L.T("共通設定", "Shared settings"), async (_, _) => await ConfigureMouseUdlr()); mouseUdlrSettings.Dock = DockStyle.Fill; headerActions.Controls.Add(mouseUdlrSettings, 3, 0);
        root.Controls.Add(header, 0, 0);

        body = new() { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        body.RowStyles.Add(new(SizeType.Percent, 100));
        body.ColumnStyles.Add(new(SizeType.Absolute, 400)); body.ColumnStyles.Add(new(SizeType.Percent, 100));
        BufferedTableLayoutPanel left = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new(0, 0, 12, 0), Margin = Padding.Empty };
        left.ColumnStyles.Add(new(SizeType.Percent, 100));
        left.RowStyles.Add(new(SizeType.Percent, 100)); left.RowStyles.Add(new(SizeType.AutoSize));
        RaisedTableLayoutPanel listPane = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        listPane.ColumnStyles.Add(new(SizeType.Percent, 100));
        listPane.RowStyles.Add(new(SizeType.AutoSize)); listPane.RowStyles.Add(new(SizeType.Percent, 100)); listPane.RowStyles.Add(new(SizeType.AutoSize));
        listPane.Controls.Add(Ui.Label(L.T("プロファイルリスト", "Profile list"), 12, true), 0, 0); listPane.Controls.Add(slots, 0, 1);
        Label dragHint = Ui.Label(L.T("ドラッグで順番を変更", "Drag profiles to change their order"), 9); dragHint.ForeColor = Ui.Muted; listPane.Controls.Add(dragHint, 0, 2);
        left.Controls.Add(listPane, 0, 0);
        RaisedTableLayoutPanel files = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
        files.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) files.RowStyles.Add(new(SizeType.AutoSize));
        files.Controls.Add(Ui.Label(L.T("保存／復帰", "Backup / Restore"), 12, true), 0, 0);
        backup = Ui.Button(L.T("全スロットをバックアップ", "Back up all slots"), async (_, _) => await BackupAll());
        restore = Ui.Button(L.T("全スロットを復帰", "Restore all slots"), async (_, _) => await RestoreAll());
        backup.Dock = restore.Dock = DockStyle.Fill; files.Controls.Add(backup, 0, 1); files.Controls.Add(restore, 0, 2); left.Controls.Add(files, 0, 1);

        RaisedTableLayoutPanel right = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        right.ColumnStyles.Add(new(SizeType.Percent, 100));
        right.RowStyles.Add(new(SizeType.AutoSize)); right.RowStyles.Add(new(SizeType.Percent, 100));
        BufferedTableLayoutPanel metadata = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new(2, 0, 2, 4) };
        metadata.ColumnStyles.Add(new(SizeType.Percent, 100)); metadata.ColumnStyles.Add(new(SizeType.Absolute, 190));
        metadata.RowStyles.Add(new(SizeType.AutoSize));
        BufferedTableLayoutPanel nameRow = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 1, RowCount = 4, Margin = Padding.Empty };
        nameRow.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) nameRow.RowStyles.Add(new(SizeType.AutoSize));
        nameRow.Controls.Add(Ui.Label(L.T("プロファイル名", "Profile name"), 9), 0, 0); nameRow.Controls.Add(name, 0, 1); name.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        nameRow.Controls.Add(identity, 0, 2); nameRow.Controls.Add(outputStatus, 0, 3);
        metadata.Controls.Add(nameRow, 0, 0);
        BufferedTableLayoutPanel profileActions = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 4, Padding = new(8, 0, 0, 0) };
        profileActions.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 4; i++) profileActions.RowStyles.Add(new(SizeType.AutoSize));
        delete = Ui.Button(L.T("プロファイル削除", "Delete profile"), async (_, _) => { if (profiles[selected].Enabled) DeleteProfile(); else await AddController(); });
        duplicate = Ui.Button(L.T("プロファイル複製", "Duplicate profile"), (_, _) => DuplicateProfile());
        delete.Dock = duplicate.Dock = DockStyle.Fill; profileActions.Controls.Add(duplicate, 0, 0); profileActions.Controls.Add(delete, 0, 1);
        ledSettings = Ui.Button(L.T("LED RGB設定", "LED RGB settings"), (_, _) => ConfigureLed());
        ledSettings.Dock = DockStyle.Fill; profileActions.Controls.Add(ledSettings, 0, 2);
        conversionSettings = Ui.Button(L.T("方向変換の割り当て", "Conversion mapping"), (_, _) => ConfigureConversion());
        conversionSettings.Dock = DockStyle.Fill; profileActions.Controls.Add(conversionSettings, 0, 3);
        // Four aligned actions share the existing header space, preserving room for combined output.
        foreach (Button action in new[] { duplicate, delete, ledSettings, conversionSettings })
        {
            action.MinimumSize = new(90, 32);
            action.Padding = new(10, 2, 10, 2);
            action.Margin = new(4, 2, 4, 2);
        }
        metadata.Controls.Add(profileActions, 1, 0); right.Controls.Add(metadata, 0, 0);
        details = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        details.ColumnStyles.Add(new(SizeType.Percent, 100));
        details.RowStyles.Add(new(SizeType.Percent, 100)); details.RowStyles.Add(new(SizeType.AutoSize));
        identity.Dock = outputStatus.Dock = live.Dock = DockStyle.Fill;
        identity.ForeColor = outputStatus.ForeColor = live.ForeColor = Ui.Muted;
        details.Controls.Add(live, 0, 1); right.Controls.Add(details, 0, 1);
        outputTabs.ShowToolTips = true;
        string[] titles = [L.T("ゲームパッド", "Gamepad"), L.T("USBマウス", "USB mouse"), L.T("マウス＋ゲームパッド", "Mouse + Gamepad")];
        for (int i = 0; i < 3; i++)
        {
            outputTabs.TabPages.Add(new TabPage(titles[i]) { BackColor = Ui.Surface });
        }
        outputContent = new() { ColumnCount = 1, RowCount = 3, Margin = Padding.Empty, BackColor = Ui.Surface };
        outputContent.ColumnStyles.Add(new(SizeType.Percent, 100));
        outputContent.RowStyles.Add(new(SizeType.AutoSize)); outputContent.RowStyles.Add(new(SizeType.AutoSize));
        outputContent.RowStyles.Add(new(SizeType.Percent, 100));
        details.Controls.Add(new SharedTabContentHost(outputTabs, outputContent), 0, 0);
        timing = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, Padding = new(10, 4, 8, 4) };
        timing.ColumnStyles.Add(new(SizeType.AutoSize)); timing.ColumnStyles.Add(new(SizeType.AutoSize)); timing.ColumnStyles.Add(new(SizeType.Percent, 100));
        timing.ColumnStyles.Add(new(SizeType.AutoSize));
        combinedXNotice.Anchor = AnchorStyles.Right; combinedXNotice.ForeColor = Ui.Muted;
        combinedXNotice.Margin = new(12, 0, 2, 0); combinedXNotice.Visible = false;
        timing.Controls.Add(combinedXNotice, 3, 0);
        Label intervalLabel = Ui.Label(L.T("入力間隔 (ms)", "Input interval (ms)"), 10); intervalLabel.Anchor = AnchorStyles.Left;
        Label intervalHelp = Ui.Label(L.T("0 = 標準値（X出力は互換動作）", "0 = default (X: compatibility)"), 9); intervalHelp.Anchor = AnchorStyles.Left; intervalHelp.ForeColor = Ui.Muted;
        timing.Controls.Add(intervalLabel, 0, 0); timing.Controls.Add(interval, 1, 0); timing.Controls.Add(intervalHelp, 2, 0);
        string intervalTip = L.T("入力間隔を指定します。X出力にも適用し、0は従来の互換動作、1～255は指定したms間隔を要求します。X出力の間隔変更を保存・適用するとUSBが再接続されます。実際の取得周期は接続先にも依存します。",
            "Sets the input interval and X gamepad output polling. Zero retains compatibility timing; 1–255 requests that interval in ms. Applying a changed X interval reconnects USB. The host also determines the actual polling cadence.");
        SetTip(interval, intervalTip); SetTip(intervalHelp, intervalTip);
        outputContent.Controls.Add(timing, 0, 0);
        BufferedTableLayoutPanel tuning = pointingTuning = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 3, Padding = new(10, 2, 6, 4) };
        tuning.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 3; i++) tuning.RowStyles.Add(new(SizeType.AutoSize));
        BufferedTableLayoutPanel ballRow = TuningRow(trackballOn, mouseGain, invertX, invertY, trackballKeys);
        paddleAxis.Items.AddRange([L.T("X軸", "X axis"), L.T("Y軸", "Y axis")]); paddleAxis.SelectedIndex = 0;
        Label paddleAxisLabel = Ui.Label(L.T("出力軸", "Output axis"), 10); paddleAxisLabel.Anchor = AnchorStyles.Left;
        BufferedTableLayoutPanel paddleRow = TuningRow(paddleOn, paddleGain, paddleAxisLabel, paddleAxis, invertPaddle, paddleKeys);
        SetTip(paddleAxis, L.T("パドルの出力軸を選択します。方向キーへの変換時もX軸は←→、Y軸は↑↓になります。", "Choose the paddle output axis. Direction conversion also follows X (←→) or Y (↑↓)."));
        SetTip(mouseGain, L.T("通常のポインタ移動だけに適用します。カーソル移動用の内部感度には影響しません。", "Applies only to pointer movement; it does not change the hidden cursor gain."));
        SetTip(paddleGain, L.T("通常のポインタ移動だけに適用します。カーソル移動用の内部感度には影響しません。", "Applies only to pointer movement; it does not change the hidden cursor gain."));
        Label buttonBehaviorLabel = Ui.Label(L.T("ボタン動作", "Button behavior"), 10); buttonBehaviorLabel.Anchor = AnchorStyles.Left;
        buttonBehavior.Controls.Add(buttonBehaviorLabel); buttonBehavior.Controls.Add(mouseButtons); buttonBehavior.Controls.Add(gamepadButtons);
        tuning.Controls.Add(ballRow, 0, 0); tuning.Controls.Add(paddleRow, 0, 1); tuning.Controls.Add(buttonBehavior, 0, 2);
        SetTip(trackballKeys, L.T("ポインタ移動を継続しながら、カーソル移動用の内部感度で方向キーも送ります。画面のポインタ感度とは独立しています。反転適用後のX/Yを個別に蓄積し、デバイス共通の64で↑↓←→を1回押して離します。超過分は捨てます。", "Pointer movement continues while direction clicks use a hidden cursor gain, separate from the pointer gain shown here. Accumulate inverted X/Y independently; at the device-wide threshold of 64, press/release ↑↓←→ once and discard excess."));
        SetTip(paddleKeys, L.T("マウス UDLR設定のパドルモードを有効にします。ボタンクリックは選択軸の方向入力、Virtual Rollingは選択スティックへ出力します。通常のポインタ移動も継続します。", "Enable the paddle mode selected in Mouse UDLR settings: button clicks along the selected axis, or Virtual Rolling on the selected stick. Pointer movement continues."));
        SetTip(invertPaddle, L.T("ポインタ移動とボタンクリックを反転します。Virtual Rollingの反転はマウス UDLR設定で指定します。", "Reverse pointer movement and button clicks. Virtual Rolling has its own reversal in Mouse UDLR settings."));
        // Keep both editors in the control tree while hidden so they receive
        // the same font and monitor-DPI changes as the visible editor.
        Panel mappingPages = new() { Dock = DockStyle.Fill, Margin = Padding.Empty };
        mappingPages.Controls.Add(editor); mappingPages.Controls.Add(mouseEditor); mappingPages.Controls.Add(combinedEditor);
        combinedEditor.Visible = false;
        outputContent.Controls.Add(pointingTuning, 0, 1);
        outputContent.Controls.Add(mappingPages, 0, 2);
        body.Controls.Add(left, 0, 0); body.Controls.Add(right, 1, 0); root.Controls.Add(body, 0, 1);
        BufferedTableLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 4, RowCount = 1, Padding = new(0, 12, 0, 0) };
        footer.ColumnStyles.Add(new(SizeType.Percent, 100)); for (int i = 0; i < 3; i++) footer.ColumnStyles.Add(new(SizeType.AutoSize));
        saved.Dock = DockStyle.Fill; saved.ForeColor = Ui.Muted; footer.Controls.Add(saved, 0, 0);
        reload = Ui.Button(L.T("デバイスから再読込", "Reload device"), async (_, _) => await Reload());
        useProfile = Ui.Button(L.T("この設定を使用", "Use this profile"), async (_, _) => await UseProfile());
        saveButton = Ui.Button(L.ProfileSaveButton, async (_, _) => await SaveSelected(), true);
        footer.Controls.Add(reload, 1, 0); footer.Controls.Add(useProfile, 2, 0); footer.Controls.Add(saveButton, 3, 0); root.Controls.Add(footer, 0, 2); Controls.Add(root);

        slots.DrawItem += DrawSlot;
        slots.SelectedIndexChanged += (_, _) => { if (loading || slots.SelectedIndex < 0) return; AbortLearning(); selected = slots.SelectedIndex; BindSelected(); };
        slots.CanMove = i => i >= 0 && i < profiles.Length && profiles[i].Enabled && !working && !dirty.Any(x => x) && (bridge is not null && caps?.SupportsBankTransfer == true);
        slots.MoveRequested += async (_, e) => await MoveSlot(e.Source, e.Destination);
        InitializeProfileMenu();
        name.TextChanged += (_, _) => { if (loading || !profiles[selected].Enabled) return; profiles[selected].Name = name.Text; MarkDirty(); };
        interval.ValueChanged += (_, _) => { if (loading) return; profiles[selected].InputInterval = (byte)interval.Value; MarkDirty(); };
        foreach (MappingEditor mapping in new[] { editor, mouseEditor, combinedEditor }) { mapping.Modified += (_, _) => MarkDirty(); mapping.PrepareLearningAsync = PrepareLearning; }
        outputTabs.Selecting += (_, e) =>
        {
            if (syncingOutput || loading) return;
            // Reject the selection before controls move or the profile becomes dirty.
            e.Cancel = OutputModeUnavailableReason() is not null;
        };
        outputTabs.SelectedIndexChanged += (_, _) =>
        {
            if (syncingOutput || loading || outputTabs.SelectedIndex < 0) return;
            DeviceProfile profile = profiles[selected];
            profile.OutputMode = (byte)outputTabs.SelectedIndex;
            AbortLearning(); MarkDirty(); BindSelected();
        };
        mouseGain.ValueChanged += (_, _) => MouseProfileChanged(); paddleGain.ValueChanged += (_, _) => MouseProfileChanged();
        paddleAxis.SelectedIndexChanged += (_, _) => MouseProfileChanged();
        foreach (CheckBox toggle in new[] { trackballOn, paddleOn, invertX, invertY, invertPaddle, trackballKeys, paddleKeys }) toggle.CheckedChanged += (_, _) => MouseProfileChanged();
        foreach (RadioButton option in new[] { mouseButtons, gamepadButtons }) option.CheckedChanged += (_, _) =>
        {
            if (syncingOutput || loading || !option.Checked || outputTabs.SelectedIndex != 2) return;
            profiles[selected].ControllerButtons = gamepadButtons.Checked;
            AbortLearning(); MarkDirty(); BindSelected();
        };
        timer.Tick += async (_, _) => await Poll();
        Shown += async (_, _) => await Connect(false);
        Shown += (_, _) => SizeSlotRows();
        DpiChanged += (_, _) => BeginInvoke(() => { if (!IsDisposed) SizeSlotRows(); }); FormClosing += OnClosing;
        Ui.ThemeInputs(this); SyncMousePage(); RefreshSlots(); BindSelected(); UpdateEnabled();
    }
    private static BufferedTableLayoutPanel TuningRow(CheckBox toggle, NumericUpDown gain, params Control[] options)
    {
        RaisedTableLayoutPanel row = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3 + options.Length, RowCount = 1, Padding = new(6, 4, 6, 4), Margin = new(0, 0, 0, 4) };
        for (int i = 0; i < row.ColumnCount; i++) row.ColumnStyles.Add(new(SizeType.AutoSize));
        Label label = Ui.Label(L.T("ポインタ感度（倍）", "Pointer gain (×)"), 10); label.Anchor = AnchorStyles.Left;
        row.Controls.Add(toggle, 0, 0); row.Controls.Add(label, 1, 0); row.Controls.Add(gain, 2, 0);
        for (int i = 0; i < options.Length; i++) row.Controls.Add(options[i], i + 3, 0);
        return row;
    }
    private static void SetText(Control control, string value) { if (control.Text != value) control.Text = value; }
    private void SetTip(Control control, string value) { if (tips.GetToolTip(control) != value) tips.SetToolTip(control, value); }
    private void SetConnectionDetail(string value) { connectionDetail = value; SetTip(connection, value); }
    private void SizeSlotRows()
    {
        using Graphics g = slots.CreateGraphics(); using Font small = new(Font.FontFamily, Font.SizeInPoints * .9f);
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
        using Font small = new(Font.FontFamily, Font.SizeInPoints * .9f, connected ? FontStyle.Bold : FontStyle.Regular);
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
        using IDisposable layout = Ui.BatchLayout(this, deferPaint: true);
        loading = true; DeviceProfile p = profiles[selected];
        SyncOutputControls();
        name.Text = p.Enabled ? p.Name : L.T("未使用", "Unused"); name.ReadOnly = !p.Enabled;
        delete.Text = p.Enabled ? L.T("プロファイル削除", "Delete profile") : L.T("コントローラーを追加", "Add controller"); Ui.ButtonColors(delete, !p.Enabled);
        interval.Value = p.InputInterval; trackballOn.Checked = p.Mouse.TrackballEnabled; paddleOn.Checked = p.Mouse.PaddleEnabled;
        mouseGain.Value = p.Mouse.TrackballGain; paddleGain.Value = p.Mouse.PaddleGain; paddleAxis.SelectedIndex = (int)p.Mouse.PaddleAxis;
        invertX.Checked = p.Mouse.InvertX; invertY.Checked = p.Mouse.InvertY; invertPaddle.Checked = p.Mouse.InvertPaddle; trackballKeys.Checked = p.Mouse.TrackballToDpad; paddleKeys.Checked = p.Mouse.PaddleToDpad;
        identity.Text = p.Enabled ? $"VID {p.Vid:X4} / PID {p.Pid:X4}" + ((p.Flags & 2) != 0 ? L.T($"  •  入力形式 {p.DescriptorHash:X8}", $"  •  Input layout {p.DescriptorHash:X8}") : "") : "";
        loading = false;
        bool learn = bridge is not null && caps is { Connected: true } && p.Matches(caps);
        foreach (MappingEditor mapping in new[] { editor, mouseEditor, combinedEditor }) mapping.Bind(p, fields, learn, caps?.SupportsRapidFire == true);
        UpdateProfileOutputStatus();
        UpdateEnabled();
        details.Visible = identity.Visible = outputStatus.Visible = p.Enabled; duplicate.Visible = ledSettings.Visible = p.Enabled;
    }
    private void MarkDirty()
    {
        dirty[selected] = true; slots.Invalidate(slots.GetItemRectangle(selected));
        SetText(saved, L.T("* は未保存の編集です。「プロファイルをデバイスに保存」で反映されます。", "* marks unsaved edits. Choose “Save profile to device” to apply them.")); UpdateEnabled();
    }
    private void UpdateEnabled()
    {
        bool online = bridge is not null, enabled = profiles[selected].Enabled, clean = !dirty.Any(x => x);
        body.Enabled = !working; connect.Enabled = !working; firmwareUpdate.Enabled = !working;
        reload.Enabled = online && !working; saveButton.Enabled = online && !working && (dirty[selected] || caps is { Persisted: false });
        UpdateOutputAvailability();
        name.Enabled = enabled && !working; name.BackColor = enabled ? Ui.Input : Ui.Surface;
        interval.Enabled = enabled && !working;
        delete.Enabled = !working && (enabled || online && CanRegisterSlot(selected) && (CanRegisterInput && caps!.SupportsGuardedRegistration && fields.Any(f => f.Learnable && f.Valid)));
        duplicate.Enabled = enabled && !working && ProfileSlots.EmptyBelow(profiles, selected) >= 0;
        ledSettings.Enabled = conversionSettings.Enabled = enabled && !working;
        SetTip(duplicate, ProfileSlots.EmptyBelow(profiles, selected) >= 0 ? L.T("下側の最も近い未使用スロットへ複製します。", "Copy to the nearest unused slot below this profile.") : L.T("このスロットより下に未使用スロットがありません。", "There are no unused slots below this profile."));
        backup.Enabled = restore.Enabled = online && !working && clean && (caps?.SupportsBankTransfer == true);
        string backupTip = caps?.SupportsBankTransfer == true ? L.T("全16スロットと切替設定を保存・復帰します。", "Back up or restore all 16 slots and profile switching settings.") :
            L.T("ファームウェア0.14.0以降が必要です。", "Requires firmware 0.14.0 or later.");
        SetTip(backup, backupTip); SetTip(restore, backupTip);
        switchMethod.Enabled = online && !working && clean && enabled && caps?.SupportsSwitchKeys == true;
        mouseUdlrSettings.Enabled = online && !working && (caps?.SupportsProfileConversion == true);
        SetTip(mouseUdlrSettings, caps?.SupportsProfileConversion == true
            ? L.T("全プロファイル共通のしきい値・時間・感度を調整します。", "Adjust thresholds, timings and sensitivity shared by all profiles.")
            : L.T("同梱ファームウェアへの更新が必要です。", "Update to the bundled firmware."));
        trackballOn.Enabled = paddleOn.Enabled = enabled && !working;
        invertX.Enabled = invertY.Enabled = enabled && !working && trackballOn.Checked;
        mouseGain.Enabled = invertX.Enabled;
        invertPaddle.Enabled = enabled && !working && paddleOn.Checked;
        paddleGain.Enabled = invertPaddle.Enabled;
        bool conversionAvailable = profiles[selected].OutputMode != 1;
        trackballKeys.Enabled = enabled && !working && conversionAvailable && trackballOn.Checked;
        paddleKeys.Enabled = enabled && !working && conversionAvailable && paddleOn.Checked;
        paddleAxis.Enabled = enabled && !working && paddleOn.Checked;
        paddleKeys.Text = L.T("変換を有効", "Enable conversion");
        mouseButtons.Enabled = gamepadButtons.Enabled = enabled && !working && (caps?.SupportsProfileOutput == true);
        useProfile.Enabled = online && !working && !dirty[selected] && caps is { SupportsOnBoardVariants: true, Connected: true } && caps.ActiveSlot != selected && profiles[selected].Matches(caps);
    }
    private bool ConfirmDiscard() => !dirty.Any(x => x) || MessageBox.Show(this, L.T("未保存の編集を破棄して、デバイスから読み直しますか？", "Discard unsaved edits and reload the device?"),
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
            using ConnectionRead read = await ConnectionRead.OpenAsync(bundledFirmware.Value.Version);
            if (closing || !showErrors && (working || ModalEditorOpen)) return;
            if (read.Status.Kind != ConnectionKind.Ready)
            {
                ClearDeviceState();
                string detail = read.Status.Guidance(bundledFirmware.Value.Version);
                string? startupError = read.Status.Kind == ConnectionKind.NotDetected ? null : detail;
                SetText(connection, read.Status.Summary); SetConnectionDetail(detail);
                if (firmwareNotices.Observe(read.Status, allowFirmwareNotice)) guidance = startupError;
                return;
            }
            firmwareNotices.Observe(read.Status, allowFirmwareNotice);
            bridge = read.TakeBridge(); var bank = read.Bank;
            bool retain = dirty.Any(x => x), unchanged = Enumerable.Range(0, profiles.Length).All(i => persistedProfiles[i].SameContent(bank.Profiles[i]));
            if (!retain) ApplyBank(bank.Profiles, bank.Caps.BankGeneration); else if (unchanged) bankGeneration = bank.Caps.BankGeneration;
            observedBankGeneration = bank.Caps.BankGeneration;
            DeviceState device = read.Device!; caps = device.Caps; generation = caps.ConnectionGeneration; cursor = device.Cursor;
            fields.Clear(); fields.AddRange(device.Fields); outputState = read.Output;
            OutputPresentation.HostMode = read.HostMode; OutputPresentation.SelectedHostMode = read.SelectedHostMode;
            if (!retain && caps.ActiveSlot < profiles.Length) selected = caps.ActiveSlot;
            UpdateConnection();
            RefreshSlots(); BindSelected();
            SetText(saved, retain ? L.T("再接続しました。未保存の編集を保持しています。", "Reconnected. Unsaved edits have been kept.") : L.T("デバイスから16スロットを読み込みました。", "Loaded all 16 slots from the device."));
        }
        catch (Exception ex)
        {
            bridge?.Dispose(); bridge = null;
            if (closing || !showErrors && (working || ModalEditorOpen)) return;
            ClearDeviceState();
            SetConnectionDetail(ex.Message); SetText(connection, L.T("デバイスの接続を確認できません。接続・ファームウェアを確認してください。", "Could not verify the device connection. Check USB and firmware.")); if (showErrors) Ui.Error(this, ex);
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
                    await UpdateFirmware(guidance);
                }
                if (!working) timer.Start();
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
        if (bridge is null) return;
        working = true; AbortLearning(); UpdateEnabled(); await WaitForPoll();
        try { var bank = await bridge.ReadProfilesAsync(); ApplyBank(bank.Profiles, bank.Caps.BankGeneration); await RefreshDevice(); RefreshSlots(); BindSelected(); SetText(saved, L.T("デバイスのスロットを再読込しました。", "Reloaded the device's slots.")); }
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
        HostOutputState host = await bridge.ReadHostOutputAsync();
        OutputPresentation.HostMode = host.Active; OutputPresentation.SelectedHostMode = host.Selected;
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
        string connectionText = caps.Connected ? L.T($"デバイス 接続済み  •  入力 {caps.Vid:X4}:{caps.Pid:X4}  •  {caps.ProtocolLabel} {caps.FieldCount}項目", $"Device connected  •  Input {caps.Vid:X4}:{caps.Pid:X4}  •  {caps.ProtocolLabel}, {caps.FieldCount} fields") :
            caps.Vid != 0 ? L.T($"デバイス 接続済み  •  入力 {caps.Vid:X4}:{caps.Pid:X4} を検出", $"Device connected  •  Input {caps.Vid:X4}:{caps.Pid:X4} detected") :
            L.T("デバイス 接続済み  •  入力コントローラーをUSB-Aへ接続してください。", "Device connected  •  Connect an input controller to USB-A.");
        connectionText += L.T("  •  出力 ", "  •  Output ") + OutputPresentation.ModeLabel;
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
            stateText = L.T("デバイスの設定が変更されています。「デバイスから再読込」を押してください。", "The device settings have changed. Choose “Reload device”.");
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
        string text = p.OutputMode == 0 ? L.T($"出力: ゲームパッド ({OutputPresentation.ControllerName})", $"Output: gamepad ({OutputPresentation.ControllerName})") : p.OutputMode == 1 ? L.T("出力: USBマウス", "Output: USB mouse") :
            p.ControllerButtons ? L.T("出力: マウス移動＋コントローラー操作", "Output: mouse movement + controller inputs") : L.T("出力: マウス＋−／＋／HOME", "Output: mouse + − / + / HOME");
        if (p.OutputMode != 0 || p.Mouse.DirectionKeysEnabled)
            text += L.T($"  •  ポインタ感度: ボール {(p.Mouse.TrackballEnabled ? p.Mouse.TrackballGain.ToString("0.0") + "×" : "OFF")} / パドル {(p.Mouse.PaddleEnabled ? p.Mouse.PaddleGain.ToString("0.0") + "× " + p.Mouse.PaddleAxis : "OFF")}",
                $"  •  Pointer gain: ball {(p.Mouse.TrackballEnabled ? p.Mouse.TrackballGain.ToString("0.0") + "×" : "OFF")} / Paddle {(p.Mouse.PaddleEnabled ? p.Mouse.PaddleGain.ToString("0.0") + "× " + p.Mouse.PaddleAxis : "OFF")}");
        if (p.OutputMode != 1 && p.Mouse.DirectionKeysEnabled) text += L.T("  •  移動変換ON", "  •  Movement conversion ON");
        SetText(outputStatus, p.Enabled ? text : "");
    }
    private void ApplyChanges(IEnumerable<InputChange> changes, bool deliver)
    {
        string? latest = null;
        foreach (InputChange change in changes)
        {
            InputField? f = fields.FirstOrDefault(f => f.Index == change.Index);
            // A PC-side backlog may overwrite device history. Continue with
            // received values without interrupting learning or its axis range.
            // Baseline draining still reaches the log but not learning.
            wizard?.ObserveInput(f, change with { Lost = false });
            if (f is null) continue;
            f.Value = change.Value; f.Valid = true;
            latest = L.T($"入力: {f.Label} = {change.Value}", $"Input: {f.Label} = {change.Value}");
            if (deliver)
            {
                ActiveEditor.OnInput(f, change.Value); wizard?.OnInput(f, change.Value);
                ActiveEditor.ObserveAxisWizard(f, change.Value, change.Time); wizard?.ObserveAxisWizard(f, change.Value, change.Time);
            }
        }
        // Deliver every event to learning, but paint only the final visible value.
        if (latest is not null) SetText(live, latest);
    }
    private async Task Poll()
    {
        if (polling || working || connecting || closing) return;
        if (bridge is null) { if (DateTime.UtcNow >= reconnectAfter) await Connect(false); return; }
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
                if (!emptyRetry && !activeChanged && unchecked((int)(cursor - caps.EventHead)) >= 0)
                {
                    ActiveEditor.RefreshAxisWizard(); wizard?.RefreshAxisWizard();
                }
                UpdateConnection();
            }
        }
        catch (Exception ex)
        {
            AbortLearning(); wizard?.ConnectionLost(); bridge?.Dispose(); bridge = null; reconnectAfter = DateTime.UtcNow.AddSeconds(2);
            ClearDeviceState();
            SetConnectionDetail(ex.Message); SetText(connection, L.T("デバイスの再接続待ち。編集内容は保持しています。", "Waiting for the device to reconnect. Your edits are kept.")); UpdateEnabled();
        }
        finally { polling = false; HandleControllerArrival(); }
    }
    private async Task<bool> PrepareLearning()
    {
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
        if (working || bridge is null) return; int slot = selected;
        try
        {
            DeviceProfile p = profiles[slot].Clone(); CheckDuplicate(slot, p);
            if (p.Enabled && p.ProfileId == Guid.Empty) p.ProfileId = Guid.NewGuid();
            working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
            SetText(saved, L.T("スロットを保存し、読み戻して照合しています…", "Saving and verifying the slot…"));
            if (p.Enabled)
            {
                int groupSlot = Array.FindIndex(persistedProfiles, old => old.SameController(p));
                if (groupSlot >= 0) p.Shift.ValidateSwitchKeys(p, await bridge!.ReadSwitchKeysAsync(groupSlot));
            }
            bankGeneration = await bridge!.SaveAsync(slot, p, bankGeneration);
            profiles[slot] = p; persistedProfiles[slot] = p.Clone(); dirty[slot] = false; observedBankGeneration = bankGeneration;
            await RefreshDevice(); RefreshSlots(); BindSelected();
            SetText(saved, L.F("スロット {0} をデバイスに保存しました。", "Saved slot {0} to the device.", slot + 1));
        }
        catch (Exception ex) { SetText(saved, L.T("保存結果を確認できませんでした。編集内容は保持しています。", "The save could not be verified. Your edits are kept.")); Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
    private void CheckDuplicate(int slot, DeviceProfile profile) => ProfileSlots.Validate(persistedProfiles, slot, profile, caps?.SupportsOnBoardVariants == true);
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
    private string? OutputModeUnavailableReason()
    {
        DeviceProfile profile = profiles[selected];
        if (!profile.Enabled) return L.T("コントローラーを登録すると出力方式を選択できます。", "Register a controller to choose its output mode.");
        if (working) return L.T("処理中は出力方式を変更できません。完了後に選択してください。", "Output modes cannot be changed while an operation is in progress. Wait for it to finish.");
        if (caps is null) return L.T("出力方式を変更するには、デバイスのUSB-CをPCへ接続し、「接続 / 再接続」を押してください。", "To change the output mode, connect the bridge's USB-C port to this PC and choose Connect / Reconnect.");
        if (!caps.SupportsProfileOutput) return L.T("このデバイスでは出力方式を変更できません。「ファームウェア更新」から同梱ファームウェアへ更新してください。", "This firmware cannot change profile output modes. Install the bundled firmware using Firmware update.");
        return null;
    }
    private void UpdateOutputAvailability()
    {
        string? reason = OutputModeUnavailableReason();
        for (int i = 0; i < outputTabs.TabCount; i++)
        {
            outputTabs.SetTabMuted(i, reason is not null);
            string description = reason ?? (i == 1
                ? L.T("マウス専用の出力です。移動からゲームパッドへの変換設定は保持しますが、このモードでは変換せず、変換項目も変更できません。",
                    "Mouse-only output. Movement-to-gamepad conversion settings are retained, but conversion is inactive and its controls are disabled in this mode.")
                : "");
            TabPage page = outputTabs.TabPages[i];
            if (page.ToolTipText != description) page.ToolTipText = description;
            if (page.AccessibleDescription != description) page.AccessibleDescription = description;
        }
    }
    private void SyncMousePage()
    {
        int index = Math.Max(0, outputTabs.SelectedIndex);
        bool combined = index == 2; buttonBehavior.Visible = combined; combinedXNotice.Visible = combined;
        bool controller = combined && gamepadButtons.Checked;
        editor.Visible = index == 0;
        mouseEditor.Visible = index > 0 && !controller; combinedEditor.Visible = controller;
        ActiveEditor.BringToFront();
        mouseEditor.SetNavigationMode(combined);
        string mouseHint = combined && gamepadButtons.Checked ? L.T("マウス移動と、下のボタン・方向・スティックを送信します。マウスクリックは出力しません。", "Sends mouse movement and the buttons, D-pad and sticks below. No mouse clicks.") :
            combined ? L.T("B→左クリック、A→右クリック。コントローラー側は−／＋／HOMEのみ送信します。", "B → left click, A → right click. The controller sends only − / + / HOME.") :
            L.T("パドルは選択したX軸／Y軸の移動に変換します。両方ONで同時に使えます。", "The paddle moves along the selected X or Y axis. Enable both to use them together.");
        SetTip(buttonBehavior, mouseHint); SetTip(mouseButtons, mouseHint); SetTip(gamepadButtons, mouseHint);
    }
    private void MouseProfileChanged()
    {
        if (loading) return;
        profiles[selected].Mouse = new() { TrackballEnabled = trackballOn.Checked, PaddleEnabled = paddleOn.Checked, TrackballGain = mouseGain.Value, PaddleGain = paddleGain.Value,
            InvertX = invertX.Checked, InvertY = invertY.Checked, InvertPaddle = invertPaddle.Checked, PaddleAxis = (PaddleOutputAxis)Math.Max(0, paddleAxis.SelectedIndex), TrackballToDpad = trackballKeys.Checked, PaddleToDpad = paddleKeys.Checked };
        SyncOutputControls(); MarkDirty();
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
        if (working || closing || !CanRegisterSlot(selected) || (!CanRegisterInput || !caps!.SupportsGuardedRegistration || !fields.Any(f => f.Learnable && f.Valid))) return;
        int slot = selected; HidBridge? targetBridge = bridge; InputConnection input = InputConnection.From(caps!);
        uint expectedBank = bankGeneration;
        bool SameTarget() => !closing && caps is not null && input.Matches(caps) && ReferenceEquals(bridge, targetBridge) &&
            (bridge is not null) && CanRegisterSlot(slot) && bankGeneration == expectedBank && caps.BankGeneration == expectedBank;
        AbortLearning(); if (!await PrepareLearning() || !SameTarget()) return;
        using RegistrationWizard dialog = new(slot, caps!, fields, PrepareLearning, async p =>
        {
            CheckDuplicate(slot, p); working = true; await WaitForPoll();
            try {
                if (!SameTarget()) throw new IOException(L.T("登録対象の接続または保存先が変更されました。登録をやり直してください。", "The controller connection or destination changed. Start registration again."));
                p.ProfileId = Guid.NewGuid();
                bankGeneration = observedBankGeneration = await targetBridge!.SaveAsync(slot, p, expectedBank, input);
                profiles[slot] = p; persistedProfiles[slot] = p.Clone(); dirty[slot] = false; }
            finally { working = false; }
        });
        wizard = dialog;
        try { if (dialog.ShowDialog(this) == DialogResult.OK) { selected = slot; await RefreshDevice(); RefreshSlots(); BindSelected(); SetText(saved, L.T("コントローラーを登録しました。", "Controller added.")); } }
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
        if (MessageBox.Show(this, L.F("スロット {0} の「{1}」を削除しますか？\n\n削除は「プロファイルをデバイスに保存」で反映されます。\n未保存の変更をデバイスに反映するまで、リストをドラッグして並べ替えることはできません。", "Delete “{1}” from slot {0}?\n\nDeletion is applied when you choose “Save profile to device”.\nDragging profiles to reorder the list is disabled until all unsaved changes are saved to the device.", selected + 1, profiles[selected].Name),
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
            SetText(saved, L.F("スロット {0} に複製しました。「プロファイルをデバイスに保存」で反映されます。", "Copied to slot {0}. Choose “Save profile to device” to apply it.", destination + 1));
        }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
    private async Task<SlotBank> ReadCurrentBank()
    {
        SlotBank bank = await bridge!.ReadBankAsync();
        if (bank.Generation != bankGeneration || !Enumerable.Range(0, profiles.Length).All(i => persistedProfiles[i].SameContent(bank.Profiles[i])))
            throw new IOException(L.T("デバイスの設定が変わっています。再読込してください。", "The device settings have changed. Reload them first."));
        return bank;
    }
    private async Task BackupAll()
    {
        if (working || dirty.Any(x => x) || bridge is null) return;
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
        if (working || dirty.Any(x => x) || bridge is null) return;
        using OpenFileDialog dialog = new() { Filter = L.T("ControllerBridge バックアップ (*.json)|*.json", "ControllerBridge backup (*.json)|*.json"), Title = L.T("全スロットを復帰", "Restore all slots") };
        tips.Hide(restore);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
            MouseUdlrSettings migrationDefaults = await new MouseUdlrSettingsClient(bridge.RawAsync).ReadAsync();
            SlotBank loaded = SlotBackup.Load(dialog.FileName, migrationDefaults);
            if (loaded.LegacyConversionApplied) MessageBox.Show(this, LegacyImportNotice(true), L.T("旧設定の変換", "Legacy settings conversion"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (MessageBox.Show(this, L.F("全16スロットをこのバックアップで置き換えます。\n登録済み: {0} / 16\n切替方法と選択設定も復帰します。続行しますか？",
                "Replace all 16 slots with this backup?\nRegistered profiles: {0} / 16\nProfile switching settings and saved selections will also be restored.", loaded.Profiles.Count(p => p.Enabled)),
                L.T("全スロットを復帰", "Restore all slots"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
            SetText(saved, L.T("全スロットを復帰し、読み戻して照合しています…", "Restoring and verifying all slots…"));
            SlotBank actual = await bridge!.WriteBankAsync(loaded, bankGeneration);
            ApplyBank(actual.Profiles, actual.Generation); await RefreshDevice();
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
            SlotBank actual = await bridge!.WriteBankAsync(moved, bank.Generation);
            ApplyBank(actual.Profiles, actual.Generation); selected = destination;
            await RefreshDevice(); RefreshSlots(); BindSelected();
            SetText(saved, L.T("スロットの順番を保存しました。BOOT・ショートカットもこの順で切り替わります。", "Slot order saved. BOOT and shortcuts follow this order."));
        }
        catch (Exception ex) { SetText(saved, L.T("並べ替えの結果を確認できません。デバイスから再読込してください。", "The new order could not be verified. Reload the device.")); Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
    private async void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (closing) return; if (working) { e.Cancel = true; return; }
        if (dirty.Any(x => x) && MessageBox.Show(this, L.T("デバイスに保存していない編集があります。終了しますか？", "There are unsaved edits. Close ControllerBridge?"),
            L.T("未保存の編集", "Unsaved edits"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) { e.Cancel = true; return; }
        e.Cancel = true; closing = true; timer.Stop(); while (polling) await Task.Delay(5);
        bridge?.Dispose(); bridge = null; timer.Dispose(); tips.Dispose(); BeginInvoke(Close);
    }
}
