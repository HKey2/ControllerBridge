namespace ControllerBridge;

internal sealed class GyroRecenterDialog : Form
{
    private readonly CheckBox[] buttons = new CheckBox[17];
    private readonly Label summary = Ui.Label("", 11, true);
    internal uint Result { get; private set; }
    internal static string Describe(uint mask, IEnumerable<InputField> fields)
    {
        string[] names = Enumerable.Range(1, 17).Where(i => (mask & (1u << (i - 1))) != 0)
            .Select(i => fields.FirstOrDefault(f => f.DualShock3 && f.UsagePage == 9 && f.Binary && f.Usage == i)?.Label ?? $"Button {i}").ToArray();
        return names.Length == 0 ? L.T("未設定", "Not set") : string.Join(" + ", names);
    }
    internal GyroRecenterDialog(uint current, List<InputField> fields)
    {
        Result = current;
        using IDisposable layout = Ui.Form(this, L.T("Recenterボタン設定（左右共通）", "Recenter buttons (shared by both sticks)"), new(680, 500), new(620, 470));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) root.RowStyles.Add(new(i == 2 ? SizeType.Percent : SizeType.AutoSize, i == 2 ? 100 : 0));
        root.Controls.Add(Ui.Label(L.T("押した瞬間の向きを基準位置にします。", "Use the orientation at the moment of pressing as the reference position."), 12, true), 0, 0);
        Label help = Ui.Label(L.T("複数を選ぶと同時押しになります。短押しで基準位置を戻します。\n静止したまま約2秒長押しすると、ゼロ点も1回取り直します。\n離して押し直すと再び使えます。左右のジャイロで共通です。",
            "Select several buttons for a chord. A short press resets the reference.\nHold still for about 2 seconds to relearn the zero point once.\nRelease to rearm. Shared by both gyro assignments."), 10);
        help.Dock = DockStyle.Fill; help.Margin = new(3, 8, 3, 12); root.Controls.Add(help, 0, 1);
        TableLayoutPanel grid = new() { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 5, AutoScroll = true };
        for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new(SizeType.Percent, 25));
        for (int i = 0; i < 5; i++) grid.RowStyles.Add(new(SizeType.AutoSize));
        for (int i = 0; i < buttons.Length; i++)
        {
            int bit = i;
            CheckBox box = buttons[i] = new() { Text = Describe(1u << i, fields), AutoSize = true,
                Checked = (current & (1u << i)) != 0, Margin = new(6, 9, 6, 9) };
            box.CheckedChanged += (_, _) =>
            {
                if (box.Checked) Result |= 1u << bit; else Result &= ~(1u << bit);
                summary.Text = Describe(Result, fields);
            };
            grid.Controls.Add(box, i % 4, i / 4);
        }
        root.Controls.Add(grid, 0, 2); summary.Text = Describe(current, fields); summary.Dock = DockStyle.Fill; summary.Margin = new(4, 10, 4, 10); root.Controls.Add(summary, 0, 3);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        Button apply = Ui.Button(L.T("設定を反映", "Apply settings"), (_, _) => { DialogResult = DialogResult.OK; Close(); }, true);
        Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        Button clear = Ui.Button(L.T("解除", "Clear"), (_, _) => { foreach (CheckBox box in buttons) box.Checked = false; });
        footer.Controls.Add(apply); footer.Controls.Add(cancel); footer.Controls.Add(clear); root.Controls.Add(footer, 0, 4);
        Controls.Add(root); AcceptButton = apply; CancelButton = cancel; Ui.ThemeInputs(this);
    }
}
