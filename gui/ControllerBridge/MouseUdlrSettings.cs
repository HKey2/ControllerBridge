namespace ControllerBridge;

public sealed record MouseUdlrSettings(uint Threshold, uint HoldMs, uint WaitMs, bool Persisted, uint ButtonMap = 0x1012110F, uint PaddleButtonMap = 0x1012110F, uint Rolling = 10, uint Trackball = 1, uint RollingStepDeg = 30, uint InertiaThreshold = 8, uint InertiaDecayPercent = 90, uint InertiaIntervalMs = 100)
{
    internal const uint DefaultButtonMap = 0x1012110F; // Explicit Switch Up/Down/Left/Right targets.
    internal bool VirtualRolling => ((Rolling >> 16) & 255) == 1;
    internal uint RollingGain10 => Rolling & 65535;
    internal bool RollingInvert => (Rolling & 0x01000000) != 0;
    internal bool RollingLeft => (Rolling & 0x02000000) != 0;
    internal static void ValidateRolling(uint value)
    {
        if ((value & 65535) is < 1 or > 100 || ((value >> 16) & 255) > 1 || (value >> 24) > 3)
            throw new InvalidDataException(L.T("Virtual Rollingの設定が不正です。", "Invalid Virtual Rolling settings."));
    }
    internal bool TrackballHold => ((Trackball >> 8) & 255) == 1;
    internal uint TrackballHoldThreshold => Trackball & 255;
    internal static void ValidateTrackball(uint value)
    {
        if ((value & 255) is < 1 or > 127 || (value >> 8) > 1)
            throw new InvalidDataException(L.T("Holdの移動しきい値は1～127ptで指定してください。", "Set the Hold movement threshold to 1–127 pt."));
    }
    internal static void ValidateRollingStep(uint value)
    {
        if (value is not (5 or 10 or 15 or 20 or 30 or 40 or 45 or 60 or 90))
            throw new InvalidDataException(L.T("回転刻みは5・10・15・20・30・40・45・60・90°から選択してください。",
                "Select a rotation step of 5, 10, 15, 20, 30, 40, 45, 60 or 90 degrees."));
    }
    internal uint ModeOptions
    {
        get
        {
            ValidateRolling(Rolling); ValidateTrackball(Trackball); ValidateRollingStep(RollingStepDeg);
            return RollingGain10 | ((Rolling >> 16) & 1) << 8 | (Rolling >> 24) << 9 |
                (Trackball >> 8) << 11 | TrackballHoldThreshold << 16 | RollingStepDeg << 24;
        }
    }
    internal MouseUdlrSettings WithModeOptions(uint options)
    {
        if ((options & 0x0000F000u) != 0)
            throw new InvalidDataException(L.T("マウス UDLRのモード設定が不正です。", "Invalid Mouse UDLR mode settings."));
        uint rolling = (options & 255) | ((options >> 8) & 1) << 16 | ((options >> 9) & 3) << 24;
        uint trackball = ((options >> 16) & 255) | ((options >> 11) & 1) << 8;
        uint step = options >> 24;
        ValidateRolling(rolling); ValidateTrackball(trackball); ValidateRollingStep(step);
        return this with { Rolling = rolling, Trackball = trackball, RollingStepDeg = step };
    }
    internal static MouseUdlrSettings Defaults => new(64, 30, 20, true);
    internal static void ValidateEdit(uint threshold, uint holdMs, uint waitMs)
    {
        if (threshold is < 1 or > 256 || holdMs is < 1 or > 512 || waitMs is < 1 or > 512)
            throw new InvalidDataException(L.T("移動しきい値は1～256pt、ホールド時間とウェイト時間は1～512msで指定してください。",
                "Set movement threshold to 1–256 pt and hold/wait times to 1–512 ms."));
    }
    internal static void ValidateTargets(uint buttonMap)
    {
        for (int direction = 0; direction < 4; direction++)
            if (Target(buttonMap, direction) > 18)
                throw new InvalidDataException(L.T("マウス UDLRの割り当てが不正です。", "Invalid Mouse UDLR assignment."));
    }
    internal static byte Target(uint buttonMap, int direction) => (byte)(buttonMap >> (8 * direction));
    internal static uint Assign(uint buttonMap, int direction, byte target)
    {
        if (direction is < 0 or > 3 || target > 18) throw new ArgumentOutOfRangeException();
        int shift = 8 * direction;
        return (buttonMap & ~(255u << shift)) | ((uint)target << shift);
    }
    internal uint InertiaOptions => InertiaThreshold | InertiaDecayPercent << 16;
    internal static void ValidateInertia(uint threshold, uint decayPercent, uint intervalMs)
    {
        if (threshold is < 1 or > 65535 || decayPercent > 99 || intervalMs is < 1 or > 1000)
            throw new InvalidDataException(L.T("疑似慣性は発生値1～65535、減衰率0.00～0.99、減衰間隔1～1000msで指定してください。", "Set pseudo-inertia threshold to 1–65535, decay factor to 0.00–0.99 and interval to 1–1000 ms."));
    }
    internal static MouseUdlrSettings Read(byte[] data)
    {
        if (data.Length != 56 || !data.AsSpan(0, 4).SequenceEqual("UDS9"u8) || Wire.U32(data, 4) != 9)
            throw new InvalidDataException(L.T("共通設定を読み込めません。本体のファームウェアを更新してください。", "Cannot read shared settings. Update the device firmware."));
        uint threshold = Wire.U32(data, 8), hold = Wire.U32(data, 12), wait = Wire.U32(data, 16), flags = Wire.U32(data, 20);
        uint buttonMap = Wire.U32(data, 24), paddleButtonMap = Wire.U32(data, 28); ValidateTargets(buttonMap); ValidateTargets(paddleButtonMap);
        if (threshold is < 1 or > 65535 || hold is < 1 or > 1000 || wait is < 1 or > 1000 || flags > 1 || Wire.U32(data, 36) != 0 ||
            (Wire.U32(data, 40) & 0xFF000000u) != 0 || Wire.U32(data, 48) != 0 || Wire.U32(data, 52) != 0)
            throw new InvalidDataException(L.T("本体から受信した共通設定が不正です。", "The device returned invalid shared settings."));
        uint start = Wire.U32(data, 40) & 65535, decay = (Wire.U32(data, 40) >> 16) & 255, interval = Wire.U32(data, 44);
        ValidateInertia(start, decay, interval);
        return new MouseUdlrSettings(threshold, hold, wait, flags == 1, buttonMap, paddleButtonMap,
            InertiaThreshold: start, InertiaDecayPercent: decay, InertiaIntervalMs: interval).WithModeOptions(Wire.U32(data, 32));
    }
}

internal sealed class MouseUdlrSettingsClient(Func<byte, byte[]?, Task<byte[]>> command)
{
    internal async Task<MouseUdlrSettings> ReadAsync() => MouseUdlrSettings.Read(await command(52, null));
    internal async Task<MouseUdlrSettings> SaveAsync(MouseUdlrSettings original, MouseUdlrSettings edited)
    {
        MouseUdlrSettings.ValidateEdit(edited.Threshold, edited.HoldMs, edited.WaitMs);
        MouseUdlrSettings.ValidateRolling(edited.Rolling); MouseUdlrSettings.ValidateTrackball(edited.Trackball);
        MouseUdlrSettings.ValidateInertia(edited.InertiaThreshold, edited.InertiaDecayPercent, edited.InertiaIntervalMs);
        byte[] args = new byte[56]; "UDS9"u8.CopyTo(args); Wire.W32(args, 4, 9);
        static void Values(byte[] b, int at, MouseUdlrSettings s)
        {
            Wire.W32(b, at, s.Threshold); Wire.W32(b, at + 4, s.HoldMs); Wire.W32(b, at + 8, s.WaitMs);
            Wire.W32(b, at + 12, s.RollingGain10 | s.TrackballHoldThreshold << 8);
            Wire.W32(b, at + 16, s.InertiaOptions); Wire.W32(b, at + 20, s.InertiaIntervalMs);
        }
        Values(args, 8, original); Values(args, 32, edited);
        MouseUdlrSettings reply = MouseUdlrSettings.Read(await command(53, args)); Verify(reply, edited);
        MouseUdlrSettings readback = await ReadAsync(); Verify(readback, edited); return readback;
    }
    private static void Verify(MouseUdlrSettings actual, MouseUdlrSettings expected)
    {
        if (actual != expected with { Persisted = true })
            throw new IOException(L.T("本体への保存結果を確認できませんでした。値を読み直して確認してください。", "Could not verify the saved settings. Read the values from the device again."));
    }
}
