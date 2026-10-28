namespace ControllerBridge;

internal sealed class SwitchMethodDialog : Form
{
    private readonly RadioButton boot = new() { Text = L.T("BOOTボタンを長押し", "Hold the BOOT button"), AutoSize = true };
    private readonly RadioButton chord = new() { Text = L.T("コントローラーのホットキー", "Controller shortcut"), AutoSize = true };
    private readonly CheckBox[] keys = new CheckBox[18];
    public uint SwitchKeys { get; private set; }
    internal static void Validate(uint mask)
    {
        if (mask == 0 || (mask & (mask - 1)) == 0 || (mask & ~0x3ffffu) != 0)
            throw new InvalidDataException(L.T("ホットキーは2つ以上のボタン・方向を選んでください。", "Select at least two buttons or directions for the shortcut."));
        if ((mask & 0x14000) == 0x14000 || (mask & 0x28000) == 0x28000)
            throw new InvalidDataException(L.T("上下または左右を同時に選ぶことはできません。", "Up and down, or left and right, cannot be selected together."));
    }
    public SwitchMethodDialog(IReadOnlyList<DeviceProfile> group, uint current, bool immediate)
    {
        Ui.Form(this, L.T("プロファイルの切替方法", "Profile switching method"), new(760, 650)); MaximizeBox = false;
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 6 };
        for (int i = 0; i < 6; i++) root.RowStyles.Add(new(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        root.Controls.Add(Ui.Label(L.T("プロファイルの切替方法", "Profile switching method"), 19, true), 0, 0);
        Label intro = Ui.Label(L.T("このコントローラーのスロットすべてに適用します。\n", "Applies to all slots for this controller.\n") + (immediate
            ? L.T("BOOTは約1秒の長押し成立時に切り替わります。\nホットキーは全ボタンを100ms以内に押しそろえると即時切替。毎回押し直しが必要です。", "Hold BOOT for about one second to switch.\nFor a shortcut, press all buttons within 100 ms to switch immediately. Release before using it again.")
            : L.T("接続中の旧FWは、約1.5秒押し続けて、すべて離すと切り替わります。\n即時切替にはファームウェア0.8.1以降へ更新してください。", "This older firmware switches after holding for about 1.5 seconds and releasing all buttons.\nUpdate to firmware 0.8.1 or later for immediate switching.")));
        intro.Dock = DockStyle.Fill; root.Controls.Add(intro, 0, 1);
        FlowLayoutPanel modes = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true, Padding = new(0, 8, 0, 8) };
        boot.Margin = chord.Margin = new(8); boot.Checked = current == 0; chord.Checked = current != 0;
        modes.Controls.Add(boot); modes.Controls.Add(chord); root.Controls.Add(modes, 0, 2);
        TableLayoutPanel choices = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 6, Padding = new(8), AutoScroll = true };
        for (int i = 0; i < 3; i++) choices.ColumnStyles.Add(new(SizeType.Percent, 100f / 3));
        for (int i = 0; i < 6; i++) choices.RowStyles.Add(new(SizeType.AutoSize));
        int[] order = [3, 0, 2, 1, 4, 5, 6, 7, 8, 9, 10, 11, 14, 17, 16, 15, 12, 13];
        for (int position = 0; position < order.Length; position++)
        {
            int index = order[position]; bool assigned = group.All(p => p.Buttons[index].Mode != 0);
            CheckBox check = new() { AutoSize = true,
                Margin = new(0, 5, 5, 5), Checked = (current & (1u << index)) != 0, Enabled = assigned,
                AccessibleName = DeviceProfile.Targets[index] + L.T(" ホットキー", " shortcut"), Tag = assigned };
            Label label = Ui.Label(DeviceProfile.Targets[index] + (assigned ? "" : L.T("（未割当）", " (unassigned)")), 10);
            label.ForeColor = assigned ? Ui.Ink : Ui.Muted;
            FlowLayoutPanel item = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new(8, 7, 8, 7) };
            item.Controls.Add(check); item.Controls.Add(label);
            keys[index] = check; choices.Controls.Add(item, position % 3, position / 3);
        }
        void RefreshKeys() { foreach (CheckBox check in keys) check.Enabled = chord.Checked && (bool)check.Tag!; }
        chord.CheckedChanged += (_, _) => RefreshKeys(); RefreshKeys(); root.Controls.Add(choices, 0, 3);
        Label notice = Ui.Label(L.T("出力ボタン名で選びます。各設定で対応する実ボタンを押してください。\nホットキーは2つ以上。余分なボタンを含む組み合わせでは切り替えません。\n組み合わせの入力はゲームにも通常どおり送信されます。連射ONでも判定は押下状態を使います。\nホットキー方式ではBOOT監視を停止します。切替番号は紫の点滅回数で表示します。", "Select output button names and press the corresponding physical buttons.\nUse at least two buttons. Extra buttons prevent switching.\nThe shortcut inputs also reach the game. Rapid fire does not affect shortcut detection.\nShortcut mode disables BOOT switching. Purple flashes indicate the profile number."), 9);
        notice.Dock = DockStyle.Fill; notice.ForeColor = Ui.Muted; root.Controls.Add(notice, 0, 4);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new(0, 8, 0, 0) };
        Button save = Ui.Button(L.T("本体に保存", "Save to device"), (_, _) =>
        {
            try {
                uint mask = 0; if (chord.Checked) { for (int i = 0; i < keys.Length; i++) if (keys[i].Checked && (bool)keys[i].Tag!) mask |= 1u << i; Validate(mask); }
                SwitchKeys = mask; DialogResult = DialogResult.OK; Close();
            } catch (Exception ex) { Ui.Error(this, ex); }
        }, true);
        Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(save); footer.Controls.Add(cancel); CancelButton = cancel; root.Controls.Add(footer, 0, 5);
        Controls.Add(root); Ui.ThemeInputs(this);
    }
}
