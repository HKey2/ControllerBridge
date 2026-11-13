namespace ControllerBridge;

internal sealed class GyroWizard : Form
{
    private readonly GyroCalibration session;
    private readonly List<InputField> fields;
    private readonly GyroSteeringView wheel = new();
    private readonly Label status = Ui.Label("", 11, true), output = Ui.Label("", 14, true), recenterLabel = Ui.Label("", 10);
    private readonly Button apply, calibrate, range, finishRange, center;
    private readonly CheckBox invert = new() { Text = L.T("向きを反転", "Invert direction"), AutoSize = true, Margin = new(10, 10, 6, 4) };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Maximum = 100, Height = 12, Margin = new(4, 3, 4, 6) };
    private readonly string axisName;
    private bool updating;
    private string? interrupted;
    private GyroRecenterDialog? recenterDialog;
    internal AxisBinding Result => session.Result;
    internal uint RecenterButtons => session.RecenterButtons;

    internal GyroWizard(int side, AxisBinding original, uint recenter, List<InputField> fields, byte inputInterval = 0)
    {
        this.fields = fields; session = new(original, recenter, fields, inputInterval); axisName = side == 0 ? "LX" : "RX";
        using IDisposable layout = Ui.Form(this, side == 0 ? L.T("左スティックのジャイロ設定", "Left stick gyro settings") : L.T("右スティックのジャイロ設定", "Right stick gyro settings"), new(780, 760), new(700, 700));
        TableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 1, RowCount = 9 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 9; i++) root.RowStyles.Add(new(i == 0 ? SizeType.Percent : SizeType.AutoSize, i == 0 ? 100 : 0));
        FlowLayoutPanel steps = new() { Dock = DockStyle.Fill, AutoSize = true };
        calibrate = Ui.Button(L.T("1  中立を校正", "1  Calibrate neutral"), (_, _) => { interrupted = null; session.StartCalibration(); RefreshView(); }, true);
        steps.Controls.Add(calibrate); steps.Controls.Add(Ui.Label(L.T("2  左右に回して動作確認", "2  Turn left and right to check"), 12, true));
        root.Controls.Add(steps, 0, 2); status.Dock = DockStyle.Fill; status.AutoSize = false; status.MinimumSize = new(0, 94); status.Margin = new(4, 8, 4, 6); root.Controls.Add(status, 0, 3);
        root.Controls.Add(progress, 0, 4); root.Controls.Add(wheel, 0, 0);
        output.AutoSize = false; output.Height = 34; output.Dock = DockStyle.Fill; output.TextAlign = ContentAlignment.MiddleCenter; output.Margin = new(4, 8, 4, 8); root.Controls.Add(output, 0, 1);
        FlowLayoutPanel actions = new() { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        center = Ui.Button(L.T("この向きを基準位置にする", "Use this orientation as reference"), (_, _) => { session.Recenter(); RefreshView(); });
        range = Ui.Button(L.T("中立から範囲を取り直す", "Record range from neutral"), (_, _) => { session.RestartRange(); RefreshView(); });
        finishRange = Ui.Button(L.T("この範囲で確認", "Check with this range"), (_, _) => { session.FinishRange(); RefreshView(); });
        actions.Controls.Add(center); actions.Controls.Add(range); actions.Controls.Add(finishRange); actions.Controls.Add(invert); root.Controls.Add(actions, 0, 5);
        TableLayoutPanel recenterRow = new() { Dock = DockStyle.Fill, AutoSize = true, ColumnCount = 1, RowCount = 2, Padding = new(0, 8, 0, 8) };
        recenterRow.ColumnStyles.Add(new(SizeType.Percent, 100));
        recenterRow.RowStyles.Add(new(SizeType.AutoSize)); recenterRow.RowStyles.Add(new(SizeType.AutoSize));
        recenterRow.Controls.Add(Ui.Button(L.T("Recenterボタン設定…", "Recenter button settings…"), (_, _) => EditRecenter()), 0, 0);
        recenterLabel.Dock = DockStyle.Fill; recenterLabel.Margin = new(4, 5, 4, 2); recenterRow.Controls.Add(recenterLabel, 0, 1); root.Controls.Add(recenterRow, 0, 6);
        Label notice = Ui.Label(L.T("強めのドリフト補正が有効です。小さくゆっくりした回転は抑えられます。\nRecenterを静止したまま約2秒長押しすると、ゼロ点も取り直します。\n", "Strong drift protection is active; slow, small turns are suppressed.\nHold Recenter still for about 2 seconds to relearn the zero point.\n") + L.ProfileSaveNotice, 9); notice.ForeColor = Ui.Muted; notice.Dock = DockStyle.Fill; notice.Margin = new(4, 4, 4, 9); root.Controls.Add(notice, 0, 7);
        FlowLayoutPanel footer = new() { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        apply = Ui.Button(L.T("設定を反映", "Apply settings"), (_, _) =>
        {
            if (!session.CanApply) return;
            byte[] check = new byte[32]; session.Binding.Write(check, 0);
            DialogResult = DialogResult.OK; Close();
        }, true);
        Button cancel = Ui.Button(L.T("キャンセル", "Cancel"), (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        footer.Controls.Add(apply); footer.Controls.Add(cancel); root.Controls.Add(footer, 0, 8);
        Controls.Add(root); CancelButton = cancel; AcceptButton = apply;
        invert.CheckedChanged += (_, _) => { if (!updating) { session.Inverted = invert.Checked; RefreshView(); } };
        Ui.ThemeInputs(this); RefreshView();
    }
    internal void OnInput(InputField field, int value, uint time) { if (!IsDisposed) session.Observe(field, value, time); }
    internal void RefreshView()
    {
        if (IsDisposed) return;
        updating = true; invert.Checked = session.Inverted; updating = false;
        invert.Enabled = session.Calibrated && !session.RangeLearning;
        apply.Enabled = session.CanApply; range.Enabled = center.Enabled = session.Calibrated && session.Fresh;
        finishRange.Visible = session.RangeLearning; finishRange.Enabled = session.BothDirections;
        calibrate.Text = session.Calibrating ? L.T("校正をやり直す", "Restart calibration") : L.T("1  中立を校正", "1  Calibrate neutral");
        progress.Visible = session.Calibrating;
        progress.Value = session.Fresh ? (int)(session.CalibrationProgress * 100) : 0;
        status.ForeColor = Ui.Ink;
        if (session.Calibrating)
        {
            status.Text = CalibrationStatus();
            if (!session.Fresh || !session.SensorReliable || session.CalibrationIssue != GyroStillnessIssue.None || session.CalibrationDelayed)
                status.ForeColor = Color.FromArgb(255, 207, 112);
        }
        else if (!session.Fresh)
            status.Text = L.T("ジャイロの入力待ちです。DS3のPSボタンと接続を確認してください。", "Waiting for gyro input. Check the DS3 PS button and connection.");
        else if (!session.SensorReliable)
            status.Text = L.T("センサー値が測定範囲の端にあるため、操舵量を保持しています。\n接続し直して中立を校正してください。", "Sensor readings are near the measurement limit; steering is held.\nReconnect the controller and calibrate neutral.");
        else if (!session.Calibrated)
            status.Text = interrupted ?? L.T("入力に合わせてハンドルが動きます。\n中立の向きで、グラフの下の「中立を校正」を押してください。", "The wheel already responds to your movement.\nHold it at neutral, then choose Calibrate neutral below the graph.");
        else if (session.RangeLearning)
            status.Text = session.BothDirections ? L.T("中央へ戻して止めるか、「この範囲で確認」を押してください。\n以後は回した位置で止めても操舵量を保持します。", "Return to center and stop, or choose Check with this range.\nSteering stays at the position you hold.") :
                L.T("まず左、次に右へ、最大入力にしたい位置まで回してください。\n最初に回した方向を左として記録します。", "Turn left first, then right, as far as you want for full input.\nThe first direction is recorded as left.");
        else status.Text = L.T("左右に回して、向きと操舵量を確認してください。\n回した位置で止めると操舵量を保持します。設定後は基準の向きへ戻してください。", "Turn left and right to check direction and steering.\nThe input stays where you hold it. Return to neutral after setup.");
        wheel.Steering = session.DisplayOutput; wheel.Invalidate();
        output.Text = axisName + (session.Calibrated ?
            L.T($"  操舵量  {wheel.Steering:+0%;-0%;0%}", $"  Steering  {wheel.Steering:+0%;-0%;0%}") :
            L.T("  入力プレビュー（未校正）", "  Input preview (not calibrated)"));
        recenterLabel.Text = L.T("左右共通: ", "Shared by both sticks: ") + GyroRecenterDialog.Describe(session.RecenterButtons, fields);
    }
    private string CalibrationStatus()
    {
        string instruction = L.T("中立の向きで3秒間静止してください。", "Keep the controller at neutral and still for 3 seconds.");
        if (!session.Fresh)
            return instruction + L.T("\n入力が届いていないため、静止時間を計測できません。\nDS3のPSボタンと接続を確認してください。", "\nNo input is arriving; stillness cannot be measured.\nCheck the DS3 PS button and connection.");
        string progressText = L.T($"静止の継続 {session.CalibrationSeconds:0.0} / 3.0 秒　再計測 {session.CalibrationResets} 回",
            $"Still for {session.CalibrationSeconds:0.0} / 3.0 s   Restarts: {session.CalibrationResets}");
        GyroStillnessIssue issue = session.CalibrationIssue;
        string reason;
        if (!session.SensorReliable || (issue & GyroStillnessIssue.SensorLimit) != 0)
            reason = L.T("センサー値が測定範囲の端にあり、校正できません。", "Sensor readings are at the measurement limit; calibration is blocked.");
        else if ((issue & GyroStillnessIssue.MissingAcceleration) != 0)
            reason = L.T("加速度の入力が不足し、静止を確認できません。", "Acceleration input is missing; stillness cannot be confirmed.");
        else if ((issue & GyroStillnessIssue.InputGap) != 0)
            reason = L.T("入力が途切れたため、静止時間を数え直しています。", "Input was interrupted; the stillness timer restarted.");
        else if ((issue & (GyroStillnessIssue.GyroVariation | GyroStillnessIssue.AccelerationVariation)) ==
            (GyroStillnessIssue.GyroVariation | GyroStillnessIssue.AccelerationVariation))
            reason = L.T("ジャイロと加速度が不安定なため、静止時間を数え直しています。", "Gyro and acceleration are unstable; the stillness timer restarted.");
        else if ((issue & GyroStillnessIssue.GyroVariation) != 0)
            reason = L.T("ドリフトや動きによる変動が大きく、静止時間を数え直しています。", "Gyro drift or movement is too large; the stillness timer restarted.");
        else if ((issue & GyroStillnessIssue.AccelerationVariation) != 0)
            reason = L.T("加速度の変動が大きく、静止時間を数え直しています。", "Acceleration varies too much; the stillness timer restarted.");
        else reason = L.T("静止を確認中です。この状態を維持してください。", "Stillness is being measured. Keep the controller steady.");
        string action = session.CalibrationDelayed ?
            L.T("校正が進んでいません。台に置いても続く場合は、再接続してやり直してください。", "Calibration is taking longer. Rest it on a surface; reconnect if it persists.") :
            L.T("手を離して安定した台に置くと、振動を抑えられます。", "Rest it on a stable surface to reduce hand movement.");
        return instruction + "\n" + progressText + "\n" + reason + "\n" + action;
    }
    private void EditRecenter()
    {
        using GyroRecenterDialog dialog = new(session.RecenterButtons, fields); recenterDialog = dialog;
        try { if (dialog.ShowDialog(this) == DialogResult.OK) session.SetRecenter(dialog.Result); }
        finally { recenterDialog = null; }
        RefreshView();
    }
    internal void InputEventsLost()
    {
        session.EventsLost(); interrupted = L.T("入力履歴が欠けたため、もう一度中立を校正してください。", "Input history was interrupted. Calibrate neutral again."); RefreshView();
    }
    internal void ConnectionLost()
    {
        recenterDialog?.Close(); DialogResult = DialogResult.Cancel; Close();
    }
}
