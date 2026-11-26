namespace ControllerBridge;

internal sealed class ToggleSettingsDialog : Form
{
    private readonly CheckBox[] enabled = new CheckBox[18];
    public bool[] Toggles { get; private set; } = new bool[18];

    internal ToggleSettingsDialog(DeviceProfile profile, Func<Task<bool>>? release)
    {
        using IDisposable layout = Ui.Form(this, L.T("トグル設定", "Toggle settings"), new(820, 620));
        MaximizeBox = false;
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        Label intro = Ui.Label(L.T("ボタンを押す毎に押した状態と離した状態が交互に切り替わります。",
            "Each button press alternates between the pressed and released states."));
        intro.Dock = DockStyle.Fill; intro.Padding = new(0, 0, 0, 12); root.Controls.Add(intro, 0, 0);
        TableLayoutPanel groups = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        for (int i = 0; i < 3; i++) groups.ColumnStyles.Add(new(SizeType.Percent, 100f / 3));
        (string Title, int[] Indices)[] sections = [
            (L.T("ABXY・肩ボタン", "ABXY / shoulder buttons"), [3, 0, 2, 1, 4, 5, 6, 7]),
            (L.T("機能・スティック押込", "Functions / stick buttons"), [8, 9, 10, 11, 13, 12]),
            (L.T("十字キー", "D-pad"), [14, 17, 16, 15])
        ];
        for (int group = 0; group < sections.Length; group++)
        {
            Panel scroll = new() { Dock = DockStyle.Fill, AutoScroll = true, Padding = new(4, 6, 12, 6) };
            TableLayoutPanel table = new() { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2, RowCount = sections[group].Indices.Length + 2 };
            table.ColumnStyles.Add(new(SizeType.Percent, 100)); table.ColumnStyles.Add(new(SizeType.AutoSize));
            for (int i = 0; i < table.RowCount; i++) table.RowStyles.Add(new(SizeType.AutoSize));
            Label title = Ui.Label(sections[group].Title, 11, true); table.Controls.Add(title, 0, 0); table.SetColumnSpan(title, 2);
            table.Controls.Add(Ui.Label(L.T("出力", "Output"), 9), 0, 1);
            table.Controls.Add(Ui.Label(L.T("トグル", "Toggle"), 9), 1, 1);
            for (int row = 0; row < sections[group].Indices.Length; row++)
            {
                int index = sections[group].Indices[row]; DigitalBinding binding = profile.Buttons[index];
                Label label = Ui.Label(profile.TargetName(index), 10); label.Anchor = AnchorStyles.Left;
                CheckBox check = new() { Text = "ON", AutoSize = true, Checked = binding.Toggle, Enabled = binding.Mode != 0,
                    Anchor = AnchorStyles.Left, Margin = new(4, 5, 4, 5), AccessibleName = profile.TargetName(index) + L.T(" トグル ON/OFF", " toggle ON/OFF") };
                enabled[index] = check; table.Controls.Add(label, 0, row + 2); table.Controls.Add(check, 1, row + 2);
            }
            scroll.Controls.Add(table); groups.Controls.Add(scroll, group, 0);
        }
        root.Controls.Add(groups, 0, 1);
        Label notice = Ui.Label(L.T("未割り当てのボタンには設定できません。電源・接続・プロファイルの切替時は保持を解除します。",
            "Unassigned buttons cannot use toggle. Power, connection and profile changes release held toggles.") + "\n" + L.ProfileSaveNotice, 9);
        notice.Dock = DockStyle.Fill; notice.Padding = new(0, 8, 0, 8); root.Controls.Add(notice, 0, 2);
        FlowLayoutPanel releaseRow = new() { AutoSize = true, Dock = DockStyle.Fill };
        Label releaseStatus = Ui.Label(release is null ? L.T("使用中のプロファイルで保持を解除できます。", "Open the active profile to release held toggles.") :
            L.T("保持の解除はすぐに反映され、保存設定は変わりません。", "Release takes effect immediately without changing saved settings."), 9);
        releaseStatus.AutoSize = true; releaseStatus.Anchor = AnchorStyles.Left;
        Button releaseButton = Ui.Button(L.T("トグル保持をすべて解除", "Release all held toggles"));
        releaseButton.Enabled = release is not null;
        releaseButton.Click += async (_, _) =>
        {
            if (release is null) return;
            releaseButton.Enabled = false;
            try
            {
                bool ok = await release();
                if (!IsDisposed) releaseStatus.Text = ok ? L.T("トグル保持を解除しました。", "Held toggles released.") :
                    L.T("接続と使用中のプロファイルを確認してください。", "Check the connection and active profile.");
            }
            catch (Exception ex) { if (!IsDisposed) Ui.Error(this, ex); }
            finally { if (!IsDisposed) releaseButton.Enabled = true; }
        };
        releaseRow.Controls.Add(releaseButton); releaseRow.Controls.Add(releaseStatus); root.Controls.Add(releaseRow, 0, 3);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        Button apply = Ui.Button(L.T("適用", "Apply"), (_, _) =>
        {
            Toggles = enabled.Select(c => c.Enabled && c.Checked).ToArray(); DialogResult = DialogResult.OK; Close();
        }, true);
        Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(apply); footer.Controls.Add(cancel);
        footer.Controls.Add(Ui.Button(L.T("設定をすべてOFF", "Set all OFF"), (_, _) => { foreach (CheckBox check in enabled) check.Checked = false; }));
        CancelButton = cancel; root.Controls.Add(footer, 0, 4); Controls.Add(root); Ui.ThemeInputs(this);
    }
}
