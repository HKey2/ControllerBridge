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
        using IDisposable formLayout = Ui.Form(this, L.T("ファームウェア更新", "Firmware update"), new(640, introduction is null ? 250 : 360));
        DoubleBuffered = true;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; ShowInTaskbar = false; ControlBox = false;
        BufferedTableLayoutPanel layout = new() { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 4 };
        layout.ColumnStyles.Add(new(SizeType.Percent, 100));
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
            ClientSize = new(ClientSize.Width, Math.Max(ClientSize.Height, LogicalToDeviceUnits(360)));
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
}
