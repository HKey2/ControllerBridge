namespace ControllerBridge;

internal sealed record MouseUdlrSettings(uint Threshold, uint HoldMs, uint WaitMs, bool Persisted)
{
    internal static MouseUdlrSettings Defaults => new(64, 16, 16, true);
    internal static void ValidateEdit(uint threshold, uint holdMs, uint waitMs)
    {
        if (threshold is < 1 or > 256 || holdMs is < 1 or > 512 || waitMs is < 1 or > 512)
            throw new InvalidDataException(L.T("移動しきい値は1～256pt、ホールド時間とウェイト時間は1～512msで指定してください。",
                "Set movement threshold to 1–256 pt and hold/wait times to 1–512 ms."));
    }
    internal static MouseUdlrSettings Read(byte[] data)
    {
        if (data.Length != 56 || !data.AsSpan(0, 4).SequenceEqual("UDS2"u8) || Wire.U32(data, 4) != 2)
            throw new InvalidDataException(L.T("マウス UDLR設定を読み込めません。本体のファームウェアを更新してください。",
                "Cannot read Mouse UDLR settings. Update the device firmware."));
        uint threshold = Wire.U32(data, 8), hold = Wire.U32(data, 12), wait = Wire.U32(data, 16), flags = Wire.U32(data, 20);
        // Retain the previous internal ranges on readback. The dialog clearly
        // shows if an old stored value needs an explicit edit to the new range.
        if (threshold is < 1 or > 65535 || hold is < 1 or > 1000 || wait is < 1 or > 1000 || flags > 1)
            throw new InvalidDataException(L.T("本体から受信したマウス UDLR設定が不正です。", "The device returned invalid Mouse UDLR settings."));
        return new(threshold, hold, wait, flags == 1);
    }
}

internal sealed class MouseUdlrSettingsClient(Func<byte, byte[]?, Task<byte[]>> command)
{
    internal async Task<MouseUdlrSettings> ReadAsync() => MouseUdlrSettings.Read(await command(52, null));
    internal async Task<MouseUdlrSettings> SaveAsync(MouseUdlrSettings original, MouseUdlrSettings edited)
    {
        MouseUdlrSettings.ValidateEdit(edited.Threshold, edited.HoldMs, edited.WaitMs);
        byte[] args = new byte[32]; "UDS2"u8.CopyTo(args); Wire.W32(args, 4, 2);
        Wire.W32(args, 8, original.Threshold); Wire.W32(args, 12, original.HoldMs); Wire.W32(args, 16, original.WaitMs);
        Wire.W32(args, 20, edited.Threshold); Wire.W32(args, 24, edited.HoldMs); Wire.W32(args, 28, edited.WaitMs);
        MouseUdlrSettings reply = MouseUdlrSettings.Read(await command(53, args));
        Verify(reply, edited);
        MouseUdlrSettings readback = await ReadAsync(); Verify(readback, edited); return readback;
    }
    private static void Verify(MouseUdlrSettings actual, MouseUdlrSettings expected)
    {
        if (!actual.Persisted || actual.Threshold != expected.Threshold || actual.HoldMs != expected.HoldMs || actual.WaitMs != expected.WaitMs)
            throw new IOException(L.T("本体への保存結果を確認できませんでした。値を読み直して確認してください。",
                "Could not verify the saved settings. Read the values from the device again."));
    }
}

internal sealed partial class MouseUdlrSettingsDialog : Form
{
    private readonly NumericUpDown threshold = new() { Minimum = 1, Maximum = 256, Value = 64, Width = 90, Anchor = AnchorStyles.Left,
        AccessibleName = L.T("移動しきい値", "Movement threshold") };
    private readonly NumericUpDown hold = new() { Minimum = 1, Maximum = 512, Value = 16, Width = 90, Anchor = AnchorStyles.Left,
        AccessibleName = L.T("ホールド時間", "Hold time") };
    private readonly NumericUpDown wait = new() { Minimum = 1, Maximum = 512, Value = 16, Width = 90, Anchor = AnchorStyles.Left,
        AccessibleName = L.T("ウェイト時間", "Wait time") };
    private readonly Label status = Ui.Label("", 9);
    private readonly Button save, cancel;
    private readonly MouseUdlrSettings original;
    private readonly Func<MouseUdlrSettings, MouseUdlrSettings, Task<MouseUdlrSettings>> persist;
    private bool saving;
    internal bool Saved { get; private set; }

    internal MouseUdlrSettingsDialog(MouseUdlrSettings current, Func<MouseUdlrSettings, MouseUdlrSettings, Task<MouseUdlrSettings>> persist)
    {
        original = current; this.persist = persist;
        using IDisposable layout = Ui.Form(this, L.T("マウス UDLR設定", "Mouse UDLR settings"), new(560, 350));
        MaximizeBox = false; MinimizeBox = false;
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 5 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 5; i++) root.RowStyles.Add(new(i == 3 ? SizeType.Percent : SizeType.AutoSize, i == 3 ? 100 : 0));
        root.Controls.Add(Ui.Label(L.T("マウス UDLR設定", "Mouse UDLR settings"), 18, true), 0, 0);
        root.Controls.Add(Ui.Label(L.T("ポインタ移動量を上下左右キー操作として送信します。\nすべてのプロファイルで共通です。",
            "Send pointer movement as up/down/left/right key input.\nShared by all profiles.")), 0, 1);
        TableLayoutPanel values = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 3, RowCount = 3, Padding = new(0, 14, 0, 10) };
        values.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 2; i++) values.ColumnStyles.Add(new(SizeType.AutoSize));
        for (int row = 0; row < 3; row++) values.RowStyles.Add(new(SizeType.AutoSize));
        void AddRow(int row, string label, NumericUpDown input, string unit)
        {
            Label name = Ui.Label(label, 11); name.Anchor = AnchorStyles.Left;
            Label units = Ui.Label(unit); units.Anchor = AnchorStyles.Left; units.Margin = new(0, 3, 0, 3);
            input.Margin = new(6, 7, 0, 7); name.Margin = new(4, 7, 4, 7);
            values.Controls.Add(name, 0, row); values.Controls.Add(input, 1, row); values.Controls.Add(units, 2, row);
        }
        AddRow(0, L.T("移動しきい値", "Movement threshold"), threshold, "pt");
        AddRow(1, L.T("ホールド時間", "Hold time"), hold, "ms");
        AddRow(2, L.T("ウェイト時間", "Wait time"), wait, "ms");
        root.Controls.Add(values, 0, 2);
        status.Dock = DockStyle.Fill; status.MaximumSize = new(510, 0); status.ForeColor = Ui.Muted; root.Controls.Add(status, 0, 3);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Padding = new(0, 8, 0, 0) };
        save = Ui.Button(L.T("本体に保存", "Save to device"), async (_, _) => await SaveSettings(), true);
        cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(save); footer.Controls.Add(cancel); AcceptButton = save; CancelButton = cancel; root.Controls.Add(footer, 0, 4);
        threshold.Value = Math.Clamp(current.Threshold, 1u, 256u); hold.Value = Math.Clamp(current.HoldMs, 1u, 512u); wait.Value = Math.Clamp(current.WaitMs, 1u, 512u);
        if (threshold.Value != current.Threshold || hold.Value != current.HoldMs || wait.Value != current.WaitMs)
            status.Text = L.T("旧設定が範囲外のため、保存すると表示中の値へ更新します。", "The old setting is outside this range. Saving applies the values shown.");
        else if (!current.Persisted)
            status.Text = L.T("本体に保存されていません。「本体に保存」で確定してください。", "These settings are not stored yet. Choose Save to device.");
        FormClosing += (_, e) => { if (saving) e.Cancel = true; };
        Controls.Add(root); Ui.ThemeInputs(this);
    }
    private async Task SaveSettings()
    {
        if (saving) return;
        bool close = false;
        try
        {
            MouseUdlrSettings next = new((uint)threshold.Value, (uint)hold.Value, (uint)wait.Value, true);
            MouseUdlrSettings.ValidateEdit(next.Threshold, next.HoldMs, next.WaitMs);
            saving = true; threshold.Enabled = hold.Enabled = wait.Enabled = save.Enabled = cancel.Enabled = false;
            status.Text = L.T("本体に保存し、読み戻して確認しています…", "Saving to the device and verifying…");
            MouseUdlrSettings actual = await persist(original, next);
            if (actual != next) throw new IOException(L.T("保存結果が一致しません。", "Saved values do not match."));
            Saved = true; close = true;
        }
        catch (Exception ex) { status.Text = ex.Message; }
        finally { saving = false; threshold.Enabled = hold.Enabled = wait.Enabled = save.Enabled = cancel.Enabled = true; }
        if (close) { DialogResult = DialogResult.OK; Close(); }
    }
}

internal sealed partial class MainForm
{
    private async Task ConfigureMouseUdlr()
    {
        if (working || !preview && (bridge is null || caps?.SupportsDirectionSettings != true)) return;
        working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
        try
        {
            MouseUdlrSettingsClient? client = bridge is null ? null : new(bridge.RawAsync);
            MouseUdlrSettings current = client is null ? MouseUdlrSettings.Defaults : await client.ReadAsync();
            using MouseUdlrSettingsDialog dialog = new(current, client is null ? (_, next) => Task.FromResult(next) : client.SaveAsync);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Saved)
                SetText(saved, L.T("マウス UDLR設定を本体に保存しました。", "Saved Mouse UDLR settings to the device.") +
                    (dirty.Any(x => x) ? L.T(" プロファイルの未保存の編集は残っています。", " Profile edits are still unsaved.") : ""));
        }
        catch (Exception ex) { Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }
}
