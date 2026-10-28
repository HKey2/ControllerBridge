namespace ControllerBridge;

internal sealed class RapidFireDialog : Form
{
    private readonly CheckBox[] enabled = new CheckBox[18];
    private readonly NumericUpDown[] rates = new NumericUpDown[18];
    public byte[] Rates { get; private set; } = new byte[18];

    public RapidFireDialog(DeviceProfile profile)
    {
        Ui.Form(this, L.T("ボタン別の連射設定", "Per-button rapid fire settings"), new(820, 620)); MaximizeBox = false;
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.Controls.Add(Ui.Label(L.T("ボタン別の連射", "Per-button rapid fire"), 19, true), 0, 0);
        Label description = Ui.Label(L.T("押している間だけ連射します。1回は押下＋解放、速度は毎秒2～30回（1回刻み）。\nONのチェックを外すと通常の押しっぱなしになります。", "Rapid fire runs only while a button is held. Each cycle is a press and release, at 2–30 cycles per second.\nUncheck ON for a normal held input."));
        description.Dock = DockStyle.Fill; description.ForeColor = Ui.Muted; root.Controls.Add(description, 0, 1);
        TableLayoutPanel groups = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        for (int i = 0; i < 3; i++) groups.ColumnStyles.Add(new(SizeType.Percent, 100f / 3));
        (string Title, int[] Indices)[] sections = [
            (L.T("ABXY・肩ボタン", "ABXY / shoulder buttons"), [3, 0, 2, 1, 4, 5, 6, 7]),
            (L.T("機能・スティック押込", "Functions / stick buttons"), [8, 9, 10, 11, 13, 12]),
            (L.T("十字キー", "D-pad"), [14, 17, 16, 15])
        ];
        for (int group = 0; group < sections.Length; group++)
        {
            (string title, int[] indices) = sections[group];
            Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new(4, 6, 12, 6) };
            TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = indices.Length + 2 };
            table.ColumnStyles.Add(new(SizeType.Percent, 100)); table.ColumnStyles.Add(new(SizeType.AutoSize)); table.ColumnStyles.Add(new(SizeType.Absolute, 74));
            for (int row = 0; row < table.RowCount; row++) table.RowStyles.Add(new(SizeType.AutoSize));
            Label heading = Ui.Label(title, 11, true); table.Controls.Add(heading, 0, 0); table.SetColumnSpan(heading, 3);
            table.Controls.Add(Ui.Label(L.T("出力", "Output"), 9), 0, 1); table.Controls.Add(Ui.Label(L.T("連射", "Rapid fire"), 9), 1, 1); table.Controls.Add(Ui.Label(L.T("回 / 秒", "Cycles / s"), 9), 2, 1);
            for (int row = 0; row < indices.Length; row++)
            {
                int index = indices[row]; DigitalBinding binding = profile.Buttons[index];
                Label label = Ui.Label(DeviceProfile.Targets[index], 10); label.Anchor = AnchorStyles.Left;
                CheckBox check = new() { Text = "ON", AutoSize = true, Checked = binding.RapidHz != 0, Enabled = binding.Mode != 0, Anchor = AnchorStyles.Left, Margin = new(4, 5, 4, 5), AccessibleName = DeviceProfile.Targets[index] + L.T(" 連射 ON/OFF", " rapid fire ON/OFF") };
                NumericUpDown rate = new() { Minimum = 2, Maximum = 30, Value = binding.RapidHz == 0 ? 10 : binding.RapidHz,
                    Width = 68, Enabled = check.Checked && check.Enabled, Anchor = AnchorStyles.Left, Margin = new(4, 5, 4, 5), AccessibleName = DeviceProfile.Targets[index] + L.T(" 連射 回/秒", " rapid fire cycles/s") };
                check.CheckedChanged += (_, _) => rate.Enabled = check.Checked && check.Enabled;
                enabled[index] = check; rates[index] = rate;
                table.Controls.Add(label, 0, row + 2); table.Controls.Add(check, 1, row + 2); table.Controls.Add(rate, 2, row + 2);
            }
            scroll.Controls.Add(table); groups.Controls.Add(scroll, group, 0);
        }
        root.Controls.Add(groups, 0, 2);
        Label notice = Ui.Label(L.T("未割り当ての出力は先に割り当ててください。「適用」のあと、本体への保存で反映します。", "Assign unmapped outputs first. Choose Apply, then save to the device."), 9);
        notice.Dock = DockStyle.Fill; notice.ForeColor = Ui.Muted; notice.Padding = new(0, 8, 0, 8); root.Controls.Add(notice, 0, 3);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft };
        Button apply = Ui.Button(L.T("適用", "Apply"), (_, _) =>
        {
            Rates = Enumerable.Range(0, 18).Select(i => enabled[i].Checked && enabled[i].Enabled ? (byte)rates[i].Value : (byte)0).ToArray();
            DialogResult = DialogResult.OK; Close();
        }, true);
        Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(apply); footer.Controls.Add(cancel); footer.Controls.Add(Ui.Button(L.T("すべてOFF", "All OFF"), (_, _) => { foreach (CheckBox check in enabled) check.Checked = false; }));
        CancelButton = cancel; root.Controls.Add(footer, 0, 4); Controls.Add(root); Ui.ThemeInputs(this);
    }
}
