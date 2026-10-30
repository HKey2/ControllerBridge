using System.Text.Json;

namespace ControllerBridge;

internal sealed partial class RegistrationWizard
{
    internal InputLogForm? LogForCheck => inputLog;
    internal void ChooseButtonForCheck(int index) => editor.SelectButton(index);
    internal bool LogButtonVisibleForCheck => logButton.Visible;
}

internal sealed partial class InputLogForm
{
    internal InputLogBuffer HistoryForCheck => buffer;
    internal bool FinishedForCheck => finished;
    internal void PauseForCheck(bool value) => pause.Checked = value;
    internal void ClearForCheck() => ClearHistory();
    internal void CaptureForCheck(string path)
    {
        Render(); PerformLayout(); Application.DoEvents();
        using Bitmap bitmap = new(Width, Height); DrawToBitmap(bitmap, new(0, 0, Width, Height)); bitmap.Save(path);
    }
    internal void CheckLayout(Action<bool, string> check)
    {
        Render(); PerformLayout(); Application.DoEvents();
        check(view.VirtualListSize == buffer.Count, "virtual view tracks the bounded history");
        check(ClientRectangle.Contains(RectangleToClient(view.RectangleToScreen(view.ClientRectangle))), "log grid is within the window");
        check(ClientRectangle.Contains(RectangleToClient(status.RectangleToScreen(status.ClientRectangle))), "status and notices are within the window");
        check(view.ClientSize.Height >= 200, "log keeps useful vertical space at default size");
        check(pause.Enabled && Owner is MainForm && !Modal, "log is modeless alongside the wizard");
    }
}

internal sealed partial class MainForm
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);
    internal static void RunInputLogChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        using MainForm form = new(preview: true, autoConnect: false) { Opacity = 0, ShowInTaskbar = false };
        Exception? failure = null;
        form.Shown += (_, _) =>
        {
            try { form.CheckInputLog(directory); }
            catch (Exception ex) { failure = ex; }
            finally { form.wizard = null; Array.Clear(form.dirty); form.working = false; form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private void CheckInputLog(string directory)
    {
        List<string> checks = [];
        void Step(string description) => File.AppendAllText(Path.Combine(directory, "progress.txt"), description + "\n");
        void Check(bool condition, string description) { if (!condition) throw new InvalidDataException("Input log: " + description); checks.Add(description); Step(description); }
        InputLogBuffer bounded = new(3);
        for (int i = 0; i < 5; i++) bounded.Note(i.ToString());
        Check(bounded.Count == 3 && bounded.Evicted == 2 && bounded[0][4] == "2" && bounded[2][4] == "4", "ring evicts only the oldest rows and preserves order");
        bounded.Clear(); Check(bounded.Count == 0 && bounded.Evicted == 0 && bounded.LastNotice.Length == 0, "clear resets bounded history and eviction count");
        InputField first = new() { Index = 7, Interface = 0, ReportId = 1, BitOffset = 0, Bits = 1, UsagePage = 9, Usage = 1, Minimum = 0, Maximum = 1, Value = 0, Valid = true };
        InputField duplicate = new() { Index = 42, Interface = 1, ReportId = 2, BitOffset = 13, Bits = 1, UsagePage = 9, Usage = 1, Minimum = 0, Maximum = 1, Value = 0, Valid = true };
        InputField analog = new() { Index = 99, Interface = 0, ReportId = 1, BitOffset = 16, Bits = 16, Flags = 3, UsagePage = 1, Usage = 0x30, Minimum = -32768, Maximum = 32767, Value = 0, Valid = true };
        InputField pending = new() { Index = 103, Interface = 0, ReportId = 1, BitOffset = 32, Bits = 8, UsagePage = 1, Usage = 0x31, Minimum = 0, Maximum = 255 };
        fields.Clear(); fields.AddRange([first, duplicate, analog, pending]);
        caps = new(255, 4, 0x1234, 0xabcd, 1, 5, 7, 0xabcdef01, 2, 0, 1, 5, Features: 16383);
        Check(InputLogBuffer.RawHex(analog, -32768) == "0x8000" && InputLogBuffer.RawHex(analog, -1) == "0xFFFF" &&
            InputLogBuffer.RawHex(first, 1) == "0x1" && InputLogBuffer.RawHex(new() { Bits = 32 }, int.MinValue) == "0x80000000", "raw hexadecimal preserves field bit width and signed bit patterns");
        int saves = 0;
        using RegistrationWizard dialog = new(4, caps, fields, () => Task.FromResult(true), _ => { saves++; return Task.CompletedTask; }) { Opacity = 0, ShowInTaskbar = false };
        wizard = dialog; dialog.Show(this); dialog.PreviewStep(1); Application.DoEvents();
        Check(dialog.LogButtonVisibleForCheck, "mapping step exposes the Input log button");
        byte[] original = dialog.Registered.Encode();
        dialog.ShowInputLog(); InputLogForm log = dialog.LogForCheck!;
        try
        {
            Check(log.HistoryForCheck.Count == fields.Count && log.HistoryForCheck[3][10] == "—", "opening snapshots every field without inventing a value for unreceived input");
            Check(log.HistoryForCheck[0][4] == log.HistoryForCheck[1][4] && log.HistoryForCheck[0][3] == "7" && log.HistoryForCheck[1][3] == "42" &&
                log.HistoryForCheck[0][5] != log.HistoryForCheck[1][5] && log.HistoryForCheck[0][7] != log.HistoryForCheck[1][7], "identical button Usage names retain distinct internal indexes and report locations");
            dialog.ShowInputLog(); Check(ReferenceEquals(dialog.LogForCheck, log), "opening again reuses the same log window");
            ApplyChanges([new(7, 1, uint.MaxValue - 5, false), new(42, 1, uint.MaxValue - 5, false), new(7, 0, uint.MaxValue, false), new(99, -32768, 0, false)], true);
            Check(log.HistoryForCheck.Count == 8 && log.HistoryForCheck[4][10] == "1" && log.HistoryForCheck[6][10] == "0", "button press and release are both logged without choosing an output button");
            Check(log.HistoryForCheck[4][1] == (uint.MaxValue - 5).ToString() && log.HistoryForCheck[7][1] == "0" && log.HistoryForCheck[7][10] == "-32768" && log.HistoryForCheck[7][11] == "0x8000", "device timestamp wrap and signed analog raw values remain exact");
            Check(original.SequenceEqual(dialog.Registered.Encode()) && saves == 0, "opening and streaming the log changes no mappings and saves nothing");
            log.CheckLayout(Check); log.CaptureForCheck(Path.Combine(directory, "input-log.png"));
            using (Bitmap bitmap = new(dialog.Width, dialog.Height)) { dialog.DrawToBitmap(bitmap, new(0, 0, dialog.Width, dialog.Height)); bitmap.Save(Path.Combine(directory, "wizard-input-log.png")); }
            dialog.ChooseButtonForCheck(0);
            int count = log.HistoryForCheck.Count;
            ApplyChanges([new(7, 1, 10, false)], false);
            Check(log.HistoryForCheck.Count == count + 1 && dialog.Registered.Buttons[0].Mode == 0, "baseline draining reaches the log while remaining excluded from mapping");
            ApplyChanges([new(7, 0, 11, false), new(7, 1, 12, false)], true);
            Check(dialog.Registered.Buttons[0].Mode != 0 && dialog.Registered.Buttons[0].BitOffset == first.BitOffset && saves == 0, "normal button assignment still works while the log streams");
            log.PauseForCheck(true); count = log.HistoryForCheck.Count;
            ApplyChanges([new(42, 0, 13, false)], true); dialog.ChooseButtonForCheck(1); ApplyChanges([new(42, 1, 14, false)], true);
            Check(log.HistoryForCheck.Count == count && dialog.Registered.Buttons[1].Mode != 0 && dialog.Registered.Buttons[1].Interface == 1, "pausing only the log leaves input updates and mapping active");
            log.PauseForCheck(false);
            Check(log.HistoryForCheck.Count == count + 1 + fields.Count && log.HistoryForCheck[log.HistoryForCheck.Count - 3][10] == "1" &&
                log.ExportText().Contains(L.F("記録を再開。一時停止中の {0} 件は記録していません。", "Recording resumed; {0} changes were not recorded while paused.", 2)), "resume marks the pause gap and snapshots current values");
            Check(log.HistoryForCheck[0][10] == "0", "later live updates cannot mutate old snapshot rows");
            dialog.PreviewStep(2); Check(!dialog.LogButtonVisibleForCheck && !log.IsDisposed, "save-review step keeps an already-open log alive");
            dialog.PreviewStep(1); log.ClearForCheck();
            Check(log.HistoryForCheck.Count == fields.Count && log.HistoryForCheck.Evicted == 0, "clear retains a fresh current-field snapshot");
            for (int i = 0; i < 2100; i++) ApplyChanges([new(99, i, (uint)(100 + i), false)], false);
            Check(log.HistoryForCheck.Count == 2000 && log.HistoryForCheck.Evicted == 104 && log.HistoryForCheck[1999][10] == "2099", "long analog streams stay bounded at 2000 rows without coalescing retained changes");
            string exported = log.ExportText();
            Check(exported.Contains("VID 1234 / PID ABCD") && exported.Contains("Older", StringComparison.OrdinalIgnoreCase) && exported.Contains("2099") && exported.Contains("0x0833"), "export includes controller identity, retention information and exact raw data");
            File.WriteAllText(Path.Combine(directory, "sample-input-log.tsv"), exported);
            Step("Closing the first log"); log.Close(); Step("Opening the replacement log"); dialog.ShowInputLog(); log = dialog.LogForCheck!;
            Check(log.HistoryForCheck.Count == fields.Count && !log.FinishedForCheck, "closing and reopening starts a new snapshot and live recording");
            Step("Delivering an overrun"); ApplyChanges([new(7, 0, 5000, true)], true); Step("Overrun delivered");
            Check(log.FinishedForCheck && !log.IsDisposed && log.Owner == this && dialog.IsDisposed, "overrun stops registration but keeps the log open under the main window");
            Check(log.ExportText().Contains(L.T("入力イベントの欠落", "Input events were lost")) && log.ExportText().Contains("5000"), "overrun marker and final received event are retained before wizard closure");
            count = log.HistoryForCheck.Count; log.Observe(first, new(7, 1, 5001, false));
            Check(log.HistoryForCheck.Count == count, "finished logs do not mix input from a later connection");
            log.CaptureForCheck(Path.Combine(directory, "input-log-ended.png"));
            Check(saves == 0, "all log and input checks performed zero profile saves");
        }
        finally { log.Close(); wizard = null; }
        // Exercise the real modal ownership setup, not just modeless test forms.
        using RegistrationWizard modal = new(4, caps, fields, () => Task.FromResult(true), _ => { saves++; return Task.CompletedTask; }) { Opacity = 0, ShowInTaskbar = false };
        InputLogForm? modalLog = null; Exception? modalFailure = null; wizard = modal;
        modal.Shown += (_, _) => modal.BeginInvoke((Action)(() =>
        {
            try
            {
                modal.PreviewStep(1); modal.ShowInputLog(); modalLog = modal.LogForCheck!;
                Step($"Modal state: modal={modal.Modal}, wizardEnabled={modal.Enabled}, logEnabled={modalLog.Enabled}, mainEnabled={Enabled}");
                Check(modal.Modal && IsWindowEnabled(modal.Handle) && IsWindowEnabled(modalLog.Handle) && !IsWindowEnabled(Handle),
                    "modal wizard and sibling log are usable while the main editor remains disabled");
                int before = modalLog.HistoryForCheck.Count;
                ApplyChanges([new(7, 0, 6000, false)], false); modal.ChooseButtonForCheck(2);
                ApplyChanges([new(7, 1, 6001, false)], true);
                Check(modal.Registered.Buttons[2].Mode != 0 && modalLog.HistoryForCheck.Count == before + 2, "real modal registration learns and streams the same received events");
                modal.DialogResult = DialogResult.Cancel;
            }
            catch (Exception ex) { modalFailure = ex; modal.DialogResult = DialogResult.Cancel; }
        }));
        modal.ShowDialog(this); wizard = null;
        try
        {
            if (modalFailure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(modalFailure).Throw();
            Check(modalLog is { IsDisposed: false, FinishedForCheck: true } && modalLog.Owner == this && IsWindowEnabled(Handle),
                "cancelling the actual modal wizard leaves its finished log available and reenables the main editor");
            Check(saves == 0, "modal workflow cancellation did not save a profile");
        }
        finally { modalLog?.Close(); }
        File.WriteAllText(Path.Combine(directory, "input-log-checks.json"), JsonSerializer.Serialize(new { passed = true, count = checks.Count, syntheticInput = true, hardwareWrites = false, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
