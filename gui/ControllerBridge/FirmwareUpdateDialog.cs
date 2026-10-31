namespace ControllerBridge;

internal sealed class FirmwareUpdateDialog : Form
{
    private readonly Label status = Ui.Label(FirmwareWorkflow.StageText(FirmwareStage.Checking, 0), 11);
    private readonly Label detail = Ui.Label("", 10);
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Style = ProgressBarStyle.Marquee, Height = 20 };
    private readonly Button yes, no, cancelWait, close;
    private readonly FirmwarePackage package;
    private readonly IFirmwareDevice device;
    private readonly string? introduction;
    private TaskCompletionSource<bool>? answer;
    private readonly CancellationTokenSource bootWaitCancellation = new();
    private bool finished, prompted, waitingForBootsel;
    internal FirmwareResult? Result { get; private set; }
    internal bool ProgrammingStarted { get; private set; }

    internal FirmwareUpdateDialog(FirmwarePackage package, IFirmwareDevice device, string? introduction = null)
    {
        this.package = package; this.device = device; this.introduction = introduction;
        Ui.Form(this, L.T("ファームウェア更新", "Firmware update"), new(640, introduction is null ? 250 : 360));
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; ControlBox = false;
        BufferedTableLayoutPanel layout = new() { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent, 100));
        layout.RowStyles.Add(new(SizeType.Absolute, 28)); layout.RowStyles.Add(new(SizeType.AutoSize));
        status.Dock = detail.Dock = DockStyle.Fill; detail.MaximumSize = new(580, 0); detail.ForeColor = Ui.Muted;
        layout.Controls.Add(status, 0, 0); layout.Controls.Add(detail, 0, 1); layout.Controls.Add(progress, 0, 2);
        FlowLayoutPanel buttons = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        no = Ui.Button(L.T("いいえ", "No"), (_, _) => Answer(false));
        yes = Ui.Button(L.T("はい", "Yes"), (_, _) => Answer(true), true);
        cancelWait = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => CancelBootWait());
        close = Ui.Button(L.T("閉じる", "Close"), (_, _) => Close());
        yes.Visible = no.Visible = cancelWait.Visible = close.Visible = false;
        buttons.Controls.Add(close); buttons.Controls.Add(cancelWait); buttons.Controls.Add(no); buttons.Controls.Add(yes); layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);
        FormClosing += (_, e) =>
        {
            if (finished) return;
            e.Cancel = true;
            if (waitingForBootsel) CancelBootWait();
            else if (prompted) Answer(false);
        };
        Shown += async (_, _) => await Run();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !IsDisposed) { bootWaitCancellation.Cancel(); bootWaitCancellation.Dispose(); }
        base.Dispose(disposing);
    }

    private void CancelBootWait()
    {
        if (!waitingForBootsel || bootWaitCancellation.IsCancellationRequested) return;
        cancelWait.Enabled = false; ControlBox = false; AcceptButton = CancelButton = null;
        status.Text = L.T("キャンセル中…", "Cancelling…");
        bootWaitCancellation.Cancel();
    }

    private void Answer(bool value)
    {
        TaskCompletionSource<bool>? pending = answer;
        answer = null; prompted = false; yes.Visible = no.Visible = false;
        AcceptButton = CancelButton = null; ControlBox = false;
        pending?.TrySetResult(value);
    }

    private Task<bool> Confirm(FirmwarePrompt prompt)
    {
        waitingForBootsel = false; cancelWait.Visible = false;
        status.Text = prompt == FirmwarePrompt.ConfirmOverwrite
            ? L.T("書き込みの最終確認", "Confirm firmware write") : L.T("ファームウェア更新", "Firmware update");
        detail.Text = (introduction is null || prompt == FirmwarePrompt.ConfirmOverwrite ? "" : introduction + "\r\n\r\n") + FirmwareWorkflow.PromptText(prompt, package.Version);
        progress.Visible = false; yes.Visible = no.Visible = true; prompted = true; ControlBox = true;
        answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        AcceptButton = no; CancelButton = no; no.Focus();
        return answer.Task;
    }

    private void Report(FirmwareStage stage, int percent)
    {
        if (IsDisposed) return;
        if (InvokeRequired) { BeginInvoke(() => Report(stage, percent)); return; }
        if (finished) return;
        string text = FirmwareWorkflow.StageText(stage, percent);
        if (status.Text != text) status.Text = text;
        progress.Visible = true;
        ProgressBarStyle style = stage is FirmwareStage.Writing or FirmwareStage.Complete ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
        if (progress.Style != style) progress.Style = style;
        int value = Math.Clamp(percent, 0, 100);
        if (progress.Value != value) progress.Value = value;
        waitingForBootsel = stage == FirmwareStage.WaitingForBootsel;
        cancelWait.Visible = waitingForBootsel;
        if (waitingForBootsel)
        {
            ClientSize = new(ClientSize.Width, Math.Max(ClientSize.Height, 360));
            detail.Text = (introduction is null ? "" : introduction + "\r\n\r\n") + FirmwareWorkflow.BootWaitInstructions;
            yes.Visible = no.Visible = close.Visible = false;
            cancelWait.Enabled = true; ControlBox = true; AcceptButton = null; CancelButton = cancelWait; cancelWait.Focus();
        }
        else if (stage != FirmwareStage.Checking)
        {
            ProgrammingStarted = true; ControlBox = false; AcceptButton = CancelButton = null;
            detail.Text = L.T("完了するまでUSBケーブルを抜かないでください。", "Keep the USB cable connected until the update finishes.");
        }
    }

    private async Task Run()
    {
        try
        {
            Result = await FirmwareWorkflow.RunAsync(package, device, Confirm, Report, bootWaitCancellation.Token);
            if (IsDisposed) return;
            if (Result == FirmwareResult.Cancelled)
            {
                // Cancellation can complete inside FormClosing itself. Queue
                // closure after that event so its e.Cancel cannot undo Close.
                finished = true; BeginInvoke(() => Close()); return;
            }
            detail.Text = Result == FirmwareResult.AlreadyCurrent
                ? L.F("既にツール同梱の v{0} です", "The device already has the bundled v{0}.", package.Version)
                : L.F("v{0} の起動を確認しました。", "Confirmed that v{0} is running.", package.Version);
            status.Text = Result == FirmwareResult.AlreadyCurrent ? L.T("バージョン確認完了", "Version check complete") : L.T("更新が完了しました。", "Update complete.");
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                status.Text = L.T("ファームウェア更新を完了できませんでした。", "Firmware update could not be completed.");
                detail.Text = ex.Message;
            }
        }
        finally
        {
            finished = true; waitingForBootsel = false;
            if (!IsDisposed)
            {
                ControlBox = true; progress.Visible = false;
                yes.Visible = no.Visible = cancelWait.Visible = false; close.Visible = true; AcceptButton = CancelButton = close;
            }
        }
    }

    internal static void CheckDeclinedGuide(IWin32Window owner, FirmwarePackage package, IFirmwareDevice device,
        string introduction, string path, Action<bool, string> check)
    {
        using FirmwareUpdateDialog dialog = new(package, device, introduction) { Opacity = 0 };
        Exception? failure = null;
        dialog.Shown += async (_, _) =>
        {
            try
            {
                for (int i = 0; i < 100 && !dialog.prompted && !dialog.waitingForBootsel && !dialog.finished; i++) await Task.Delay(20);
                check((dialog.prompted || dialog.waitingForBootsel) && !dialog.ProgrammingStarted,
                    "automatic guide waits for physical BOOT or confirmation before programming");
                dialog.PerformLayout();
                using Bitmap bitmap = new(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(bitmap, new(0, 0, dialog.Width, dialog.Height)); bitmap.Save(path);
                int needed = TextRenderer.MeasureText(dialog.detail.Text, dialog.detail.Font, new(dialog.detail.ClientSize.Width, int.MaxValue), TextFormatFlags.WordBreak).Height;
                check(needed <= dialog.detail.ClientSize.Height && dialog.detail.Text.Contains(introduction), "automatic guide explanation and prompt fit");
                if (dialog.waitingForBootsel)
                {
                    check(dialog.cancelWait.Visible && !dialog.yes.Visible && !dialog.no.Visible && !dialog.close.Visible && dialog.CancelButton == dialog.cancelWait,
                        "physical BOOT waiting displays only Cancel and supports Escape");
                    dialog.cancelWait.PerformClick();
                }
                else
                {
                    check(dialog.AcceptButton == dialog.no && dialog.CancelButton == dialog.no, "automatic confirmation defaults to No");
                    dialog.no.PerformClick();
                }
            }
            catch (Exception ex) { failure = ex; dialog.bootWaitCancellation.Cancel(); dialog.finished = true; dialog.Close(); }
        };
        dialog.ShowDialog(owner);
        if (failure is not null) throw failure;
        check(dialog.Result == FirmwareResult.Cancelled && !dialog.ProgrammingStarted, "cancelling automatic guidance exits without programming");
    }

    internal static void RunChecks(string directory, FirmwarePackage package, Action<bool, string> check)
    {
        foreach (string scenario in new[] { "same", "different", "manual", "boot-present", "cancel-wait", "close-wait", "cancel-final", "failure" })
        {
            using FirmwareChecks.FakeDevice device = new(scenario == "same" ? package.Version : scenario == "different" || scenario == "failure" ? "0.1.0" : null, package.Version);
            bool manual = scenario is "manual" or "boot-present" or "cancel-wait" or "close-wait" or "cancel-final";
            bool cancelled = scenario is "cancel-wait" or "close-wait" or "cancel-final";
            TaskCompletionSource bootReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (manual && scenario != "boot-present") device.OnPrepare = token => bootReady.Task.WaitAsync(token);
            if (scenario == "failure") { device.FailureAt = "verify"; device.Failure = new TimeoutException(L.T("書き込み後の起動を確認できませんでした。USBケーブルを確認してください。再接続しても認識されない場合は、BOOTボタンを押しながら接続し、ファームウェア更新をやり直してください。", "The device did not restart with identifiable firmware. Check the USB cable. If reconnecting does not help, connect while holding BOOT and run Firmware update again.")); }
            using FirmwareUpdateDialog dialog = new(package, device) { Opacity = 0 };
            Exception? failure = null;
            void Capture(string name)
            {
                dialog.PerformLayout();
                using Bitmap bitmap = new(dialog.Width, dialog.Height);
                dialog.DrawToBitmap(bitmap, new(0, 0, dialog.Width, dialog.Height)); bitmap.Save(Path.Combine(directory, name + ".png"));
                int needed = TextRenderer.MeasureText(dialog.detail.Text, dialog.detail.Font, new(dialog.detail.ClientSize.Width, int.MaxValue), TextFormatFlags.WordBreak).Height;
                check(needed <= dialog.detail.ClientSize.Height, name + " dialog text fits");
            }
            device.OnWrite = async () =>
            {
                await Task.Delay(30);
                check(dialog.ProgrammingStarted && !dialog.ControlBox && !dialog.yes.Visible && !dialog.no.Visible && !dialog.cancelWait.Visible,
                    scenario + " writing locks confirmation, cancellation and close controls");
                Capture(scenario + "-writing");
                dialog.Close(); check(!dialog.IsDisposed, scenario + " window close cannot interrupt writing");
            };
            dialog.Shown += async (_, _) =>
            {
                try
                {
                    await Task.Delay(30);
                    if (manual && scenario != "boot-present")
                    {
                        check(dialog.waitingForBootsel && !dialog.prompted && !dialog.ProgrammingStarted && device.Calls.SequenceEqual(["probe", "manual"]),
                            scenario + " starts monitoring BOOT without an initial Yes/No question");
                        check(dialog.cancelWait.Visible && !dialog.yes.Visible && !dialog.no.Visible && !dialog.close.Visible && dialog.CancelButton == dialog.cancelWait && dialog.ControlBox,
                            scenario + " waiting offers Cancel, Escape and window close");
                        Capture(scenario + "-waiting");
                        if (scenario == "cancel-wait") { dialog.cancelWait.PerformClick(); return; }
                        if (scenario == "close-wait") { dialog.Close(); return; }
                        bootReady.SetResult();
                        for (int i = 0; i < 100 && !dialog.prompted && !dialog.finished; i++) await Task.Delay(20);
                    }
                    if (manual)
                    {
                        check(dialog.prompted && !dialog.waitingForBootsel && !dialog.cancelWait.Visible && !dialog.ProgrammingStarted && device.Written is null,
                            scenario + " detected BOOT changes to final Yes/No confirmation without writing");
                        check(dialog.status.Text == L.T("書き込みの最終確認", "Confirm firmware write") && dialog.detail.Text.Contains(package.Version),
                            scenario + " final confirmation names the firmware version");
                    }
                    if (dialog.prompted)
                    {
                        Capture(scenario + (manual ? "-overwrite" : "-confirmation"));
                        check(dialog.AcceptButton == dialog.no && dialog.CancelButton == dialog.no, scenario + " Enter and Escape default to No");
                        if (scenario == "cancel-final") { dialog.no.PerformClick(); return; }
                        dialog.yes.PerformClick();
                    }
                    for (int i = 0; i < 100 && !dialog.finished; i++) await Task.Delay(20);
                    check(dialog.finished, scenario + " dialog finishes");
                    if (!dialog.IsDisposed && !cancelled)
                    {
                        Capture(scenario + "-result");
                        check(dialog.close.Visible && dialog.ControlBox, scenario + " result can be closed");
                    }
                    check(dialog.Result == (scenario == "same" ? FirmwareResult.AlreadyCurrent : scenario == "failure" ? null : FirmwareResult.Updated), scenario + " result matches the workflow");
                }
                catch (Exception ex) { failure = ex; }
                finally
                {
                    if (failure is not null || !cancelled)
                    { dialog.bootWaitCancellation.Cancel(); dialog.finished = true; if (!dialog.IsDisposed) dialog.Close(); }
                }
            };
            dialog.ShowDialog();
            if (failure is not null) throw failure;
            if (cancelled) check(device.Written is null && dialog.Result == FirmwareResult.Cancelled && device.Calls.SequenceEqual(["probe", "manual"]),
                scenario + " dialog cancellation stops waiting or declines the final confirmation without writing");
        }
    }
}
