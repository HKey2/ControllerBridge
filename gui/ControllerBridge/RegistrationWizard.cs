namespace ControllerBridge;

internal sealed class RegistrationWizard : Form
{
    private readonly DeviceProfile profile;
    private readonly MappingEditor editor = new();
    private readonly Label steps = Ui.Label("", 11, true), hint = Ui.Label("", 10);
    private readonly Panel page = new() { Dock = DockStyle.Fill, BackColor = Ui.Surface, Padding = new(12) };
    private readonly Button next, back;
    private readonly TextBox name = new() { Width = 380, MaxLength = 47 };
    private readonly Func<DeviceProfile, Task> save;
    private readonly Capabilities caps;
    private int step;
    private bool saving;
    public DeviceProfile Registered => profile;
    internal void PreviewStep(int number) { step = number; ShowStep(); }
    public RegistrationWizard(int slot, Capabilities caps, List<InputField> fields, Func<Task<bool>> prepare, Func<DeviceProfile, Task> save)
    {
        this.caps = caps; this.save = save;
        profile = new() { Flags = 3, Vid = caps.Vid, Pid = caps.Pid, Revision = caps.Revision, DescriptorHash = caps.DescriptorHash,
            Name = $"{(caps.InputProtocol == 2 ? "XInput" : caps.InputProtocol == 4 ? "Switch" : "HID")} {caps.Vid:X4}:{caps.Pid:X4}", InputInterval = 0 };
        Ui.Form(this, L.T("コントローラーを追加", "Add controller"), new(1000, 680));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(22), RowCount = 4, ColumnCount = 1 };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.Controls.Add(steps, 0, 0); root.Controls.Add(page, 0, 1);
        hint.Dock = DockStyle.Fill; hint.ForeColor = Ui.Muted; hint.Padding = new(0, 6, 0, 6); root.Controls.Add(hint, 0, 2);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft };
        next = Ui.Button(L.T("次へ", "Next"), async (_, _) => await Next(), true); back = Ui.Button(L.T("戻る", "Back"), (_, _) => { step--; ShowStep(); });
        Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); }); CancelButton = cancel;
        footer.Controls.Add(next); footer.Controls.Add(back); footer.Controls.Add(cancel); root.Controls.Add(footer, 0, 3); Controls.Add(root);
        editor.PrepareLearningAsync = prepare; editor.Bind(profile, fields, true, caps.SupportsRapidFire); name.Text = profile.Name;
        Tag = slot; ShowStep(); Ui.ThemeInputs(this);
        FormClosing += (_, e) => { if (saving) e.Cancel = true; };
    }
    private void ShowStep()
    {
        page.Controls.Clear(); back.Enabled = step > 0; next.Text = step == 2 ? L.T("本体に保存して完了", "Save to device and finish") : L.T("次へ", "Next");
        steps.Text = (step == 0 ? "●" : "✓") + L.T(" 1 機器を確認   →   ", " 1 Check device   →   ") + (step == 1 ? "●" : step > 1 ? "✓" : "○") + L.T(" 2 入力を割り当て   →   ", " 2 Map inputs   →   ") + (step == 2 ? "●" : "○") + L.T(" 3 保存", " 3 Save");
        editor.AbortLearning();
        if (step == 0)
        {
            FlowLayoutPanel intro = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new(20) };
            intro.Controls.Add(Ui.Label(L.T("USB-Aのコントローラーを登録", "Register the USB-A controller"), 21, true));
            intro.Controls.Add(Ui.Label(L.T($"VID {caps.Vid:X4}   /   PID {caps.Pid:X4}   /   {caps.ProtocolLabel} {caps.FieldCount}項目", $"VID {caps.Vid:X4}   /   PID {caps.Pid:X4}   /   {caps.ProtocolLabel}, {caps.FieldCount} fields"), 13));
            intro.Controls.Add(Ui.Label(L.T($"本体のスロット {(int)Tag! + 1} を使用します。保存後はPCなしで自動選択されます。", $"Uses slot {(int)Tag! + 1} on the device. After saving, the profile is selected automatically without a PC."), 11));
            intro.Controls.Add(Ui.Label(L.T("表示名", "Display name"), 11, true)); intro.Controls.Add(name);
            intro.Controls.Add(Ui.Label(L.T("次の画面でSwitchのボタンを選び、実際の入力を押します。\r\n使わないボタン・軸は未割り当てのままで構いません。", "On the next page, select a Switch button, then press the physical input.\r\nYou can leave unused buttons and axes unassigned."), 11));
            if (caps.Partial) intro.Controls.Add(Ui.Label(L.T("一部の入力形式は未対応です。検出できた絶対値入力だけを登録できます。", "Some input formats are not supported. Only detected absolute inputs can be mapped."), 11));
            page.Controls.Add(intro); hint.Text = L.T("機器IDと入力形式を照合するため、別の機器へ誤適用されるのを防ぎます。", "The device ID and input format are checked to prevent applying the profile to a different controller.");
        }
        else if (step == 1)
        {
            page.Controls.Add(editor); hint.Text = L.T("ボタン・方向キー・トリガーを割り当て、必要なら下段からスティック軸を校正します。", "Assign buttons, directions, and triggers. Use the lower section to calibrate stick axes if needed.");
        }
        else
        {
            ListView review = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, GridLines = false };
            review.Columns.Add(L.T("Switch出力", "Switch output"), 180); review.Columns.Add(L.T("入力コントローラーの割り当て", "Input controller mapping"), 680);
            for (int i = 0; i < 18; i++) review.Items.Add(new ListViewItem([DeviceProfile.Targets[i], profile.Buttons[i].ToString()]));
            for (int i = 0; i < 4; i++) review.Items.Add(new ListViewItem([DeviceProfile.AxisNames[i], profile.Axes[i].ToString()]));
            page.Controls.Add(review); hint.Text = L.T($"「{profile.Name}」を本体のスロット {(int)Tag! + 1} に保存します。保存完了後、SwitchへUSB-Cを差し替えて使用できます。", $"Save “{profile.Name}” to device slot {(int)Tag! + 1}. After saving, reconnect USB-C to the Switch to use it.");
        }
        Ui.ThemeInputs(page);
    }
    private async Task Next()
    {
        try
        {
            profile.Name = name.Text.Trim(); profile.Encode();
            if (step < 2) { step++; ShowStep(); return; }
            if (!profile.Buttons.Any(b => b.Mode != 0) && !profile.Axes.Any(a => a.Mode != 0)) throw new InvalidDataException(L.T("少なくとも1つのボタンまたは軸を割り当ててください。", "Assign at least one button or axis."));
            saving = true; next.Enabled = back.Enabled = page.Enabled = false; hint.Text = L.T("本体に保存し、読み戻して照合しています…", "Saving to the device and reading back to verify…");
            await save(profile.Clone()); saving = false; DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) { saving = false; next.Enabled = page.Enabled = true; back.Enabled = step > 0; Ui.Error(this, ex); hint.Text = L.T("保存できませんでした。内容はこの画面に保持されています。", "Could not save. Your edits remain on this page."); }
    }
    public void OnInput(InputField f, int value) { if (step == 1) editor.OnInput(f, value); }
    public void ConnectionLost()
    {
        editor.AbortLearning();
        if (!saving) { DialogResult = DialogResult.Cancel; Close(); }
    }
}
