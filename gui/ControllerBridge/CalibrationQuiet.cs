namespace ControllerBridge;

// A short-lived device lease protects all samples consumed by an input setup
// dialog. If this process exits, the device expires it without changing a profile.
internal sealed record CalibrationQuietState(byte State, byte Backend, byte Flags,
    uint Epoch, uint Pad, uint Bank, uint Token, uint Features)
{
    internal bool Supported => (Features & (1u << 9)) != 0;
    internal bool Settled => State == 0 && (Flags & 8) == 0 && (Flags & 16) != 0;
    internal static CalibrationQuietState Read(byte[] b)
    {
        if (b.Length < 44 || !b.AsSpan(0, 4).SequenceEqual("RMB1"u8) || b[4] != 1)
            throw new InvalidDataException(L.T("振動状態を確認できません。", "Unable to read rumble status."));
        return new(b[5], b[6], b[7], Wire.U32(b, 8), Wire.U32(b, 12), Wire.U32(b, 16), Wire.U32(b, 20), Wire.U32(b, 40));
    }
    internal byte[] Command(byte action)
    {
        byte[] b = new byte[20]; b[0] = action;
        Wire.W32(b, 4, Epoch); Wire.W32(b, 8, Pad); Wire.W32(b, 12, Bank); Wire.W32(b, 16, action == 0 ? 0 : Token);
        return b;
    }
}

internal sealed partial class MainForm
{
    private CalibrationQuietState? calibrationQuiet;
    private long calibrationRenewed, calibrationOpened;
    private bool InputSetupOpen => wizard is not null || editor.LearningActive || mouseEditor.LearningActive || combinedEditor.LearningActive;

    private async Task<bool> PrepareCalibrationQuiet()
    {
        if (bridge is null || caps is not { Vid: 0x054c, Pid: 0x0268, SupportsRumble: true }) return true;
        if (calibrationQuiet is not null)
        {
            await MaintainCalibrationQuiet(true); calibrationOpened = Environment.TickCount64; return true;
        }
        CalibrationQuietState status = CalibrationQuietState.Read(await bridge.RawAsync(55));
        // Earlier firmware cannot run DS3 rumble, so it needs no quiet lease.
        if (!status.Supported) return true;
        status = CalibrationQuietState.Read(await bridge.RawAsync(56, status.Command(0)));
        calibrationQuiet = status; calibrationRenewed = calibrationOpened = Environment.TickCount64;
        for (int attempt = 0; attempt < 30; attempt++)
        {
            if (status.Settled) return true;
            if (status.State == 5) break;
            await Task.Delay(20);
            await MaintainCalibrationQuiet(true);
            status = CalibrationQuietState.Read(await bridge.RawAsync(55));
            ValidateCalibrationQuiet(status);
        }
        await ReleaseCalibrationQuiet();
        throw new IOException(L.T("振動の停止を確認できないため、校正を開始できません。コントローラーを接続し直してください。", "Calibration cannot start because rumble has not stopped. Reconnect the controller."));
    }
    private void ValidateCalibrationQuiet(CalibrationQuietState state)
    {
        if (calibrationQuiet is not { } lease || state.Token != lease.Token || state.Epoch != lease.Epoch ||
            state.Pad != lease.Pad || state.Bank != lease.Bank || (state.Flags & 16) == 0)
            throw new IOException(L.T("接続または振動設定が変わったため、入力の調整を終了しました。もう一度開いてください。", "Input setup closed because the connection or rumble settings changed. Open it again."));
    }
    private async Task MaintainCalibrationQuiet(bool preparing = false)
    {
        if (bridge is null || calibrationQuiet is not { } lease) return;
        long now = Environment.TickCount64;
        if (!preparing && !InputSetupOpen && now - calibrationOpened >= 500)
        {
            await ReleaseCalibrationQuiet(); return;
        }
        if (now - calibrationRenewed < 250) return;
        CalibrationQuietState status = CalibrationQuietState.Read(await bridge.RawAsync(56, lease.Command(1)));
        ValidateCalibrationQuiet(status); calibrationRenewed = Environment.TickCount64;
    }
    private async Task ReleaseCalibrationQuiet()
    {
        CalibrationQuietState? lease = calibrationQuiet; calibrationQuiet = null;
        if (bridge is null || lease is null) return;
        try { await bridge.RawAsync(56, lease.Command(2)); }
        // A changed session cannot be released using a token from its predecessor.
        catch (IOException) { }
    }
}
