using System.Globalization;
using System.Text;

namespace ControllerBridge;

// PC-only, bounded history of parsed input-field changes before mapping.
// Immutable cell strings keep old rows independent of the mutable live fields.
internal sealed class InputLogBuffer(int capacity = 2000)
{
    private readonly string[]?[] rows = new string[capacity][];
    private int first;
    internal int Count { get; private set; }
    internal long Evicted { get; private set; }
    internal long Revision { get; private set; }
    internal long ResetGeneration { get; private set; }
    internal string LastNotice { get; private set; } = "";
    internal int Capacity => rows.Length;
    internal string[] this[int index] => index >= 0 && index < Count
        ? rows[(first + index) % rows.Length]! : throw new ArgumentOutOfRangeException(nameof(index));
    internal void Add(string[] row)
    {
        if (Count == rows.Length) { rows[first] = row; first = (first + 1) % rows.Length; Evicted++; }
        else { rows[(first + Count) % rows.Length] = row; Count++; }
        Revision++;
    }
    internal void Clear() { Array.Clear(rows); first = Count = 0; Evicted = 0; LastNotice = ""; ResetGeneration++; Revision++; }
    internal static string RawHex(InputField field, int value)
    {
        int bits = Math.Clamp((int)field.Bits, 1, 32);
        uint raw = unchecked((uint)value);
        if (bits < 32) raw &= (1u << bits) - 1;
        return "0x" + raw.ToString("X" + ((bits + 3) / 4), CultureInfo.InvariantCulture);
    }
    internal void Field(InputField field, int value, bool valid, uint? deviceTime, bool snapshot)
    {
        string number(int v) => v.ToString(CultureInfo.InvariantCulture);
        string state = !valid ? L.T("未受信", "Waiting") : field.Binary
            ? value == field.Minimum ? L.T("離した", "Released") : value == field.Maximum ? L.T("押した", "Pressed") : L.T("範囲外", "Out of range")
            : field.Hat && (value < field.Minimum || value > field.Maximum) ? L.T("ニュートラル", "Neutral")
            : (field.HidFlags & 4) != 0 ? L.T("相対値", "Relative") : L.T("絶対値", "Absolute");
        Add([DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
            deviceTime?.ToString(CultureInfo.InvariantCulture) ?? "—", snapshot ? L.T("現在値", "Snapshot") : L.T("変化", "Change"),
            number(field.Index), field.Label, number(field.Interface), number(field.ReportId), number(field.BitOffset), number(field.Bits),
            $"{field.UsagePage:X4}:{field.Usage:X4}", valid ? number(value) : "—", valid ? RawHex(field, value) : "—",
            $"{state}  [{number(field.Minimum)}..{number(field.Maximum)}]"]);
    }
    internal void Note(string message)
    {
        LastNotice = message;
        Add([DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture), "—",
            L.T("情報", "Notice"), "", message, "", "", "", "", "", "", "", ""]);
    }
}

internal sealed class InputLogListView : ListView
{
    private const int LvmSetItemCount = 0x102f;
    private const int NoInvalidateAll = 1, NoScroll = 2;
    private bool appending;
    internal InputLogListView() => DoubleBuffered = true;
    internal void SetRowCount(int count, bool appendOnly)
    {
        appending = appendOnly;
        try { VirtualListSize = count; }
        finally { appending = false; }
    }
    internal void FollowLatest()
    {
        if (VirtualListSize == 0 || !IsHandleCreated) return;
        Rectangle row = GetItemRect(VirtualListSize - 1);
        if (row.Top < 0 || row.Bottom > ClientSize.Height) EnsureVisible(VirtualListSize - 1);
    }
    protected override void WndProc(ref Message message)
    {
        // WinForms normally sends LVM_SETITEMCOUNT without flags, repainting
        // the whole virtual list and resetting/restoring its scroll position.
        // Keep its managed count in sync, but limit native append invalidation
        // to affected visible rows and preserve scrolling (LVSICF_* = 1 | 2).
        if (appending && message.Msg == LvmSetItemCount)
            message.LParam = (IntPtr)(message.LParam.ToInt64() | NoInvalidateAll | NoScroll);
        base.WndProc(ref message);
    }
}

internal sealed partial class InputLogForm : Form
{
    private readonly InputLogBuffer buffer = new();
    private readonly List<InputField> fields;
    private readonly string identity;
    private readonly InputLogListView view = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true,
        HideSelection = false, VirtualMode = true, MultiSelect = false, ShowItemToolTips = true };
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 100 };
    private readonly CheckBox pause = new() { Text = L.T("記録を一時停止", "Pause recording"), AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly CheckBox follow = new() { Text = L.T("末尾に追従", "Follow latest"), Checked = true, AutoSize = true, Anchor = AnchorStyles.Left };
    private readonly Label status = Ui.Label("", 9);
    private readonly string[] headings = [L.T("PC時刻", "PC time"), L.T("本体 µs", "Device µs"), L.T("種別", "Event"), "Index",
        L.T("入力名", "Input"), "IF", "ID", "bit", "bits", "Usage", L.T("生値", "Raw"), "Hex", L.T("状態 / 範囲", "State / range")];
    private long displayedRevision = -1, displayedReset = -1, displayedEvicted = -1, skipped;
    private bool finished;
    private string endReason = "";

    internal InputLogForm(Capabilities caps, List<InputField> fields)
    {
        this.fields = fields;
        identity = $"VID {caps.Vid:X4} / PID {caps.Pid:X4}  •  {caps.ProtocolLabel}  •  Layout {caps.DescriptorHash:X8}";
        Ui.Form(this, L.T("入力ログ", "Input log") + $" — {caps.Vid:X4}:{caps.Pid:X4}", new(1280, 540));
        MinimumSize = new(960, 420);
        BufferedTableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(12), ColumnCount = 1, RowCount = 5 };
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.AutoSize)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.AutoSize));
        root.Controls.Add(Ui.Label(identity, 10, true), 0, 0);
        Label help = Ui.Label(L.T(
            "Index = 内部フィールド番号（0始まり）。同じボタン番号でも IF / ID / bit が違えば別入力です。\n割り当て前の生値の変化を表示します。同じUSBデータを送る物理ボタンは区別できません。",
            "Index is the internal field number (zero-based). Compare IF / ID / bit when button numbers match.\nShows raw field changes before mapping. Physical buttons sending identical USB data cannot be distinguished."), 9);
        help.Dock = DockStyle.Fill; root.Controls.Add(help, 0, 1);
        FlowLayoutPanel actions = new() { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true };
        actions.Controls.Add(pause); actions.Controls.Add(follow);
        actions.Controls.Add(Ui.Button(L.T("クリア", "Clear"), (_, _) => ClearHistory()));
        actions.Controls.Add(Ui.Button(L.T("全体をコピー", "Copy all"), (_, _) =>
        { try { Clipboard.SetText(ExportText()); } catch (Exception ex) { Ui.Error(this, ex); } }));
        actions.Controls.Add(Ui.Button(L.T("ログを保存…", "Save log…"), (_, _) => SaveLog()));
        root.Controls.Add(actions, 0, 2);
        int[] widths = [100, 100, 80, 55, 115, 40, 40, 50, 40, 110, 100, 100, 190];
        for (int i = 0; i < headings.Length; i++) view.Columns.Add(headings[i], widths[i]);
        view.RetrieveVirtualItem += (_, e) => e.Item = e.ItemIndex < buffer.Count
            ? new(buffer[e.ItemIndex]) { ToolTipText = string.Join("  |  ", buffer[e.ItemIndex]) } : new("");
        root.Controls.Add(view, 0, 3); status.Dock = DockStyle.Fill; root.Controls.Add(status, 0, 4); Controls.Add(root);
        pause.CheckedChanged += (_, _) =>
        {
            if (finished) return;
            if (pause.Checked) { skipped = 0; buffer.Note(L.T("記録を一時停止。割り当て処理は継続します。", "Recording paused; input mapping continues.")); }
            else
            {
                buffer.Note(L.F("記録を再開。一時停止中の {0} 件は記録していません。", "Recording resumed; {0} changes were not recorded while paused.", skipped));
                Snapshot();
            }
            Render();
        };
        follow.CheckedChanged += (_, _) => { if (follow.Checked) view.FollowLatest(); };
        refresh.Tick += (_, _) => Render();
        Shown += (_, _) => { Render(); if (!finished) refresh.Start(); };
        FormClosed += (_, _) => refresh.Dispose();
        Ui.ThemeInputs(this); Snapshot();
    }
    private void Snapshot() { foreach (InputField field in fields) buffer.Field(field, field.Value, field.Valid, null, true); }
    internal void Observe(InputField? field, InputChange change)
    {
        if (finished || IsDisposed) return;
        if (change.Lost) buffer.Note(L.T("入力イベントの欠落を検出しました。これ以前の履歴は完全ではありません。", "Input events were lost. Earlier history is incomplete."));
        if (pause.Checked) { skipped++; return; }
        if (field is null) buffer.Note(L.F("不明なフィールド {0}: 生値 {1}、本体時刻 {2} µs", "Unknown field {0}: raw {1}, device time {2} µs", change.Index, change.Value, change.Time));
        else buffer.Field(field, change.Value, true, change.Time, false);
    }
    internal void Finish(string reason)
    {
        if (finished || IsDisposed) return;
        finished = true; endReason = reason; buffer.Note(reason); pause.Enabled = false;
        refresh.Stop(); Render();
    }
    private void ClearHistory()
    {
        buffer.Clear(); skipped = 0;
        if (!finished) { Snapshot(); if (pause.Checked) buffer.Note(L.T("記録は一時停止中です。", "Recording is paused.")); }
        else buffer.Note(endReason);
        Render();
    }
    private void Render()
    {
        if (displayedRevision == buffer.Revision || IsDisposed) return;
        bool appendOnly = displayedReset == buffer.ResetGeneration && displayedEvicted == buffer.Evicted && buffer.Count >= view.VirtualListSize;
        displayedRevision = buffer.Revision;
        displayedReset = buffer.ResetGeneration; displayedEvicted = buffer.Evicted;
        view.SetRowCount(buffer.Count, appendOnly);
        // Appending is invalidated by LVM_SETITEMCOUNT itself. Only clearing or
        // rotating the ring replaces existing row content; redraw those items.
        if (!appendOnly)
        {
            if (buffer.Count > 0) view.RedrawItems(0, buffer.Count - 1, true);
            else view.Invalidate();
        }
        if (follow.Checked) view.FollowLatest();
        string mode = finished ? L.T("記録終了", "Finished") : pause.Checked ? L.T("一時停止中", "Paused") : L.T("記録中", "Recording");
        string text = L.F("{0}  •  {1} / {2} 行  •  古い行の破棄: {3}  •  本体時刻は32bit µs（周回あり）", "{0}  •  {1} / {2} rows  •  Older rows discarded: {3}  •  Device time: 32-bit µs (wraps)", mode, buffer.Count, buffer.Capacity, buffer.Evicted);
        if (buffer.LastNotice.Length > 0) text += "\n" + buffer.LastNotice;
        if (status.Text != text) status.Text = text;
    }
    internal string ExportText()
    {
        StringBuilder text = new(); text.AppendLine("ControllerBridge input field log").AppendLine(identity);
        text.AppendLine("Index is zero-based; IF / ID / bit identifies the input source. Values are before mapping/calibration.");
        text.AppendLine("Only changed parsed fields are streamed, not every USB packet. Device microseconds wrap at 2^32.");
        text.AppendLine($"Retained rows: {buffer.Count}; older rows discarded: {buffer.Evicted}");
        text.AppendLine(string.Join('\t', headings));
        for (int i = 0; i < buffer.Count; i++) text.AppendLine(string.Join('\t', buffer[i].Select(cell => cell.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' '))));
        return text.ToString();
    }
    private void SaveLog()
    {
        using SaveFileDialog dialog = new() { Filter = L.T("タブ区切りログ (*.tsv)|*.tsv|テキスト (*.txt)|*.txt", "Tab-separated log (*.tsv)|*.tsv|Text (*.txt)|*.txt"),
            FileName = $"ControllerBridge-input-{DateTime.Now:yyyyMMdd-HHmmss}.tsv", OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try { File.WriteAllText(dialog.FileName, ExportText(), new UTF8Encoding(true)); }
        catch (Exception ex) { Ui.Error(this, ex); }
    }
}
