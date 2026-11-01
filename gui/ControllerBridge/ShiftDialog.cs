namespace ControllerBridge;

internal sealed partial class ShiftDialog : Form
{
    private readonly DeviceProfile draft;
    private readonly List<InputField> fields;
    private readonly List<InputField> keyFields;
    private readonly Func<Task<bool>>? prepare;
    private readonly ListBox keys = new() { Dock = DockStyle.Fill, IntegralHeight = false, Height = 100 };
    private readonly Label emptyKeys = new() { Dock = DockStyle.Fill, Text = L.T("シフトキーが登録されていません。", "No shift keys registered."),
        ForeColor = Color.Gray, BackColor = Ui.Surface, Padding = new(4), TextAlign = ContentAlignment.TopLeft, UseMnemonic = false };
    private readonly Label status = Ui.Label("", 10);
    private readonly Button add, remove;
    private readonly MappingEditor editor = new(false, true);
    private ShiftKeyLearner? learner;
    private bool canLearn;
    private int request;
    public ShiftSettings Result => draft.Shift;
    internal ShiftDialog(DeviceProfile profile, List<InputField> fields, bool canLearn, bool rapid, Func<Task<bool>>? prepare)
    {
        draft = profile.Clone(); this.fields = fields; this.canLearn = canLearn; this.prepare = prepare;
        // Only this profile's matching controller can supply its button names.
        // Retain that descriptor snapshot if the live field list is later cleared.
        keyFields = canLearn ? [.. fields] : [];
        using IDisposable formLayout = Ui.Form(this, L.T("ボタンのシフト設定", "Button shift settings"), new(960, 880));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 1, RowCount = 7 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 7; i++) root.RowStyles.Add(new(i == 4 ? SizeType.Percent : SizeType.AutoSize, i == 4 ? 100 : 0));
        Label intro = Ui.Label(L.T("登録したキー(最大4つ)をすべて押している間、裏ページに登録したキーアサインに変化します。\n未変更のボタンとアナログスティックは表ページから継承します。\nシフト時に押したキーはシフトが解除されても離すまでは継続します。", "Hold all registered keys (up to four) to switch to the mappings registered on the shifted page.\nUnchanged buttons and analog sticks inherit from the main page.\nKeys pressed while shifted remain active until released, even after shift ends."), 11);
        intro.Dock = DockStyle.Fill; root.Controls.Add(intro, 0, 0);
        TableLayoutPanel row = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 2, RowCount = 1, Padding = new(0, 8, 0, 4) };
        row.ColumnStyles.Add(new(SizeType.Percent, 100)); row.ColumnStyles.Add(new(SizeType.AutoSize));
        Panel keyList = new() { Dock = DockStyle.Fill, Height = 100, BorderStyle = BorderStyle.Fixed3D, BackColor = Ui.Surface };
        keys.BorderStyle = BorderStyle.None;
        keyList.Controls.Add(keys); keyList.Controls.Add(emptyKeys); emptyKeys.BringToFront(); row.Controls.Add(keyList, 0, 0);
        FlowLayoutPanel actions = new() { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        add = Ui.Button(L.T("シフトキーを追加", "Learn shift keys"), async (_, _) => await Learn());
        remove = Ui.Button(L.T("選択キーを削除", "Remove selected"), (_, _) => { StopLearning(); int i = keys.SelectedIndex; if (i >= 0) draft.Shift.Keys.RemoveAt(i); RefreshKeys(); });
        actions.Controls.Add(add); actions.Controls.Add(remove); row.Controls.Add(actions, 1, 0); root.Controls.Add(row, 0, 1);
        status.Dock = DockStyle.Fill; status.ForeColor = Ui.Muted; root.Controls.Add(status, 0, 2);
        Label heading = Ui.Label(L.T("裏ページのボタン割り当て（* は裏ボタン登録あり）", "Shifted button mappings (* indicates a registered shifted mapping)"), 13, true); heading.Padding = new(0, 10, 0, 0); root.Controls.Add(heading, 0, 3);
        editor.Bind(draft, fields, canLearn, rapid);
        editor.PrepareLearningAsync = async () => { StopLearning(); RefreshKeys(); return prepare is null || await prepare(); };
        root.Controls.Add(editor, 0, 4);
        Label notice = Ui.Label(L.ProfileSaveNotice, 9); notice.Dock = DockStyle.Fill; notice.ForeColor = Ui.Muted;
        notice.Padding = new(0, 8, 0, 8); root.Controls.Add(notice, 0, 5);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        Button ok = Ui.Button(L.T("編集に反映", "Apply to draft"), (_, _) => {
            if (learner is { Inputs.Count: > 0 }) { ShowCapturedKeys(); return; }
            try { _ = draft.Encode(); DialogResult = DialogResult.OK; Close(); } catch (Exception ex) { Ui.Error(this, ex); }
        }, true);
        Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(ok); footer.Controls.Add(cancel); CancelButton = cancel; root.Controls.Add(footer, 0, 6);
        keys.SelectedIndexChanged += (_, _) => remove.Enabled = keys.SelectedIndex >= 0;
        FormClosing += (_, _) => StopLearning();
        Controls.Add(root); Ui.ThemeInputs(this); RefreshKeys();
    }
    private void RefreshKeys()
    {
        keys.Items.Clear(); foreach (DigitalBinding key in draft.Shift.Keys) keys.Items.Add(ShiftSettings.DescribeKey(key, keyFields));
        keys.Visible = keys.Items.Count != 0;
        emptyKeys.Visible = keys.Items.Count == 0;
        keys.AccessibleDescription = keys.Items.Count == 0 ? emptyKeys.Text : "";
        add.Enabled = canLearn && draft.Shift.Keys.Count < ShiftSettings.MaxKeys; remove.Enabled = false;
        status.Text = canLearn ? L.T("「シフトキーを追加」の後、登録するボタンを同時に押し、すべて離してください。1つずつの追加も可能です。", "Choose Learn shift keys, press the buttons together, then release them all. You can also add one at a time.") : L.T("このプロファイルのコントローラーを接続すると、ボタン番号の確認とキーの追加ができます。", "Connect this profile's controller to identify button numbers and learn shift keys.");
    }
    private void StopLearning() { request++; learner = null; }
    private void ShowCapturedKeys()
    {
        if (learner is null) return;
        string names = string.Join(" + ", learner.Inputs.Select(i => i.Field.Label));
        status.Text = L.T($"検出: {names}\n押したボタンをすべて離すと登録します。", $"Detected: {names}\nRelease all pressed buttons to register them.");
    }
    private async Task Learn()
    {
        int current = ++request; learner = null; editor.AbortLearning();
        if (!canLearn || draft.Shift.Keys.Count >= ShiftSettings.MaxKeys) return;
        if (prepare is not null && !await prepare()) return;
        if (current != request || IsDisposed || !canLearn) return;
        learner = new(fields); status.Text = L.T("シフトキーにするボタンを同時に押し、すべて離してください（合計最大4つ）。", "Press the shift buttons together, then release them all (up to four keys in total).");
    }
    public void OnInput(InputField field, int value)
    {
        editor.OnInput(field, value);
        if (learner is null) return;
        if (!learner.Observe(field, value)) { if (learner.Inputs.Count != 0) ShowCapturedKeys(); return; }
        List<LearnedInput> additions = learner.Inputs.Where(i => !draft.Shift.Keys.Any(k => ShiftSettings.SameSource(k, i.Binding))).ToList();
        StopLearning();
        if (draft.Shift.Keys.Count + additions.Count > ShiftSettings.MaxKeys)
        {
            status.Text = L.T("シフトキーは合計4つまでです。今回の入力は登録していません。キー数を減らして追加し直してください。", "Shift supports four keys in total. This gesture was not added. Learn again with fewer keys."); return;
        }
        if (additions.Count == 0) { status.Text = L.T("この入力は登録済みです。別のボタンを追加してください。", "These inputs are already shift keys. Choose other buttons."); return; }
        draft.Shift.Keys.AddRange(additions.Select(i => i.Binding)); RefreshKeys();
        string names = string.Join(" + ", additions.Select(i => i.Field.Label));
        status.Text = L.T($"登録しました: {names}", $"Registered: {names}");
    }
    public void ConnectionLost()
    {
        StopLearning(); canLearn = false; editor.AbortLearning(); editor.Bind(draft, fields, false, true); RefreshKeys();
    }
}
