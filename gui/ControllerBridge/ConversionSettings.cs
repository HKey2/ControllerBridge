namespace ControllerBridge;

public sealed class ConversionSettings
{
    // New registrations use the tuned Trackball and Paddle responses. Keep constructor and
    // wire defaults unchanged so loading older profiles never retunes them.
    internal static ConversionSettings NewProfileDefaults()
    {
        ConversionSettings result = new() { AnalogOutputMinimum = 25,
            PaddleAnalogAxis = 0, PaddleAnalogSpeed100 = 300, PaddleAnalogSmoothingMs = 50,
            PaddleAnalogReturnMs = 0, PaddleAnalogOutputMinimum = 25, PaddleAnalogOutputMaximum = 100,
            PaddleAnalogOutputCurve = AnalogCurvePicker.LowSpeedCurve };
        result.SetVirtualOptions(3m, 30, 100); return result;
    }
    // Display endpoints are 1%=1ms and 100%=50ms. The saved period retains
    // one-millisecond precision; 30ms is displayed as 60%.
    internal static int SmoothingPercent(int periodMs) => 1 + ((Math.Clamp(periodMs, 1, 50) - 1) * 99 + 24) / 49;
    internal static int SmoothingPeriodMs(int percent) => 1 + ((Math.Clamp(percent, 1, 100) - 1) * 49 + 49) / 99;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsVirtualPoint => (AnalogCurve == 20 || AnalogCurve == 30) && AnalogFullSpeed is >= 60001 and <= 65000;
    [System.Text.Json.Serialization.JsonIgnore]
    public decimal VirtualGain => IsVirtualPoint ? (AnalogFullSpeed - 60000) / 100m : 1m;
    [System.Text.Json.Serialization.JsonIgnore]
    public int VirtualPeriodMs => IsVirtualPoint ? Math.Max(1, AnalogMinimumSpeed / 10) : 10;
    [System.Text.Json.Serialization.JsonIgnore]
    public int VirtualStopMs => IsVirtualPoint && AnalogCurve == 20 ? AnalogReturnMs : 250;
    internal void SetVirtualOptions(decimal gain, int periodMs, int stopMs)
    {
        if (gain < 0.01m || gain > 50m || stopMs is < 0 or > 2550 || stopMs % 10 != 0 || periodMs is < 1 or > 255)
            throw new InvalidDataException("Virtual point settings are outside their supported ranges.");
        AnalogFullSpeed = checked((ushort)(60000 + decimal.Round(gain * 100)));
        AnalogCurve = 20; AnalogMinimumSpeed = (ushort)(periodMs * 10); AnalogReturnMs = (ushort)stopMs;
    }
    public byte[] TrackballTargets { get; set; } = [15, 17, 18, 16];
    public byte[] PaddleTargets { get; set; } = [18, 16];
    public byte TrackballMode { get; set; }
    public bool InertiaEnabled { get; set; }
    public byte PaddleMode { get; set; }
    public bool RollingInvert { get; set; }
    public bool RollingLeft { get; set; }
    public byte RollingStepDeg { get; set; } = 30;
    public ushort AnalogFullSpeed { get; set; } = 1000;
    public bool AnalogRight { get; set; }
    // Preserve obsolete prototype fields in backups; main profile reversal is authoritative.
    public bool AnalogInvertX { get; set; }
    public bool AnalogInvertY { get; set; }
    public byte AnalogCurve { get; set; }
    public ushort AnalogMinimumSpeed { get; set; }
    public ushort AnalogReturnMs { get; set; }
    public byte AnalogOutputMinimum { get; set; }
    public byte AnalogOutputMaximum { get; set; } = 100;
    public byte AnalogOutputCurve { get; set; }
    public byte PaddleAnalogAxis { get; set; }
    public ushort PaddleAnalogSpeed100 { get; set; } = 300;
    public byte PaddleAnalogSmoothingMs { get; set; } = 30;
    public ushort PaddleAnalogReturnMs { get; set; } = 100;
    public byte PaddleAnalogOutputMinimum { get; set; } = 25;
    public byte PaddleAnalogOutputMaximum { get; set; } = 100;
    public byte PaddleAnalogOutputCurve { get; set; }
    internal bool HasPaddleAnalogSettings => PaddleMode == 2 || PaddleAnalogAxis != 0 || PaddleAnalogSpeed100 != 300 ||
        PaddleAnalogSmoothingMs != 30 || PaddleAnalogReturnMs != 100 || PaddleAnalogOutputMinimum != 25 ||
        PaddleAnalogOutputMaximum != 100 || PaddleAnalogOutputCurve != 0;
    internal bool RequiresAnalogAngle => PaddleMode == 2 || TrackballMode == 2 || AnalogFullSpeed != 1000 || AnalogRight ||
        AnalogInvertX || AnalogInvertY || AnalogCurve != 0 || AnalogMinimumSpeed != 0 || AnalogReturnMs != 0;
    internal static readonly int[] Steps = [5, 10, 15, 20, 30, 40, 45, 60, 90];
    internal void Validate()
    {
        if (PaddleAnalogAxis > 3 || PaddleAnalogSpeed100 is < 1 or > 5000 || PaddleAnalogSmoothingMs == 0 ||
            PaddleAnalogReturnMs > 2550 || PaddleAnalogReturnMs % 10 != 0 || PaddleAnalogOutputMaximum is < 1 or > 100 ||
            PaddleAnalogOutputMinimum >= PaddleAnalogOutputMaximum || !AnalogCurvePicker.ValidCode(PaddleAnalogOutputCurve))
            throw new InvalidDataException(L.T("PaddleのVirtual Cursor(Analog)の出力先・アナログ範囲・スピード・慣性を確認してください。",
                "Check the Paddle Virtual Cursor(Analog) output axis, analog range, speed and inertia."));
        if (AnalogOutputMaximum is < 1 or > 100 || AnalogOutputMinimum >= AnalogOutputMaximum || !AnalogCurvePicker.ValidCode(AnalogOutputCurve))
            throw new InvalidDataException(L.T("アナログ最小値は最大値より小さくしてください。最小値は0〜99%、最大値は1〜100%です。", "Analog minimum must be below maximum. Minimum is 0–99%, maximum is 1–100%."));
        if (TrackballTargets is null || TrackballTargets.Length != 4 || PaddleTargets is null || PaddleTargets.Length != 2 ||
            TrackballTargets.Concat(PaddleTargets).Any(t => t is < 1 or > 18) || TrackballMode > 2 || PaddleMode > 2 || !Steps.Contains(RollingStepDeg))
            throw new InvalidDataException(L.T("方向変換の割り当て・モード・回転刻みを確認してください。", "Check conversion targets, modes and rotation step."));
        if (AnalogFullSpeed < 10 || !AnalogCurvePicker.ValidCode(AnalogCurve) || AnalogMinimumSpeed > 2550 || AnalogMinimumSpeed % 10 != 0 ||
            AnalogMinimumSpeed >= AnalogFullSpeed || AnalogReturnMs > 2550 || AnalogReturnMs % 10 != 0)
            throw new InvalidDataException(L.T("Virtual Cursor(Analog)の微小入力しきい値は最大傾斜速度より小さくしてください。速度・戻り時間の範囲も確認してください。",
                "Virtual Cursor(Analog)'s minimum speed must be below its full-tilt speed. Check the speed and return-time ranges."));
    }
    internal void Write(byte[] bytes)
    {
        Validate(); TrackballTargets.CopyTo(bytes, 544); PaddleTargets.CopyTo(bytes, 548);
        bytes[550] = (byte)(TrackballMode | PaddleMode << 2);
        bytes[551] = (byte)((RollingInvert ? 1 : 0) | (RollingLeft ? 2 : 0)); bytes[552] = RollingStepDeg; bytes[553] = InertiaEnabled ? (byte)1 : (byte)0;
        Wire.W16(bytes, 554, AnalogFullSpeed == 1000 ? (ushort)0 : AnalogFullSpeed);
        bytes[556] = (byte)((AnalogRight ? 1 : 0) | (AnalogInvertX ? 2 : 0) | (AnalogInvertY ? 4 : 0));
        bytes[557] = AnalogCurve; bytes[558] = (byte)(AnalogMinimumSpeed / 10); bytes[559] = (byte)(AnalogReturnMs / 10);
        bytes[848] = AnalogOutputMinimum; bytes[849] = AnalogOutputMaximum == 100 ? (byte)0 : AnalogOutputMaximum;
        bytes[850] = AnalogOutputCurve; bytes.AsSpan(851, 5).Clear();
        WritePaddle(bytes);
    }
    internal void WritePaddle(byte[] bytes)
    {
        Wire.W16(bytes, 856, PaddleAnalogSpeed100); bytes[858] = PaddleAnalogSmoothingMs;
        bytes[859] = (byte)(PaddleAnalogReturnMs / 10); bytes[860] = PaddleAnalogAxis;
        bytes[861] = PaddleAnalogOutputMinimum; bytes[862] = PaddleAnalogOutputMaximum == 100 ? (byte)0 : PaddleAnalogOutputMaximum;
        bytes[863] = PaddleAnalogOutputCurve;
    }
    internal static ConversionSettings Read(byte[] bytes)
    {
        if ((bytes[550] & ~15) != 0 || (Wire.U16(bytes, 0) < 11 && (bytes[550] >> 2) > 1) || bytes[551] > 3 || bytes[553] > 1 || bytes[556] > 7 || (Wire.U16(bytes, 0) < 12 ? bytes[851] != 0 : !ButtonLabels.Valid(bytes[851])) || (Wire.U16(bytes, 0) < 13 ? bytes[852] != 0 : bytes[852] > 2) || bytes.AsSpan(853, 3).ContainsAnyExcept((byte)0))
            throw new InvalidDataException(L.T("未対応の方向変換設定です。", "Unsupported conversion settings."));
        ConversionSettings result = new() { TrackballTargets = bytes.AsSpan(544, 4).ToArray(), PaddleTargets = bytes.AsSpan(548, 2).ToArray(),
            TrackballMode = (byte)(bytes[550] & 3), PaddleMode = (byte)((bytes[550] >> 2) & 3),
            RollingInvert = (bytes[551] & 1) != 0, RollingLeft = (bytes[551] & 2) != 0, RollingStepDeg = bytes[552], InertiaEnabled = bytes[553] != 0,
            AnalogFullSpeed = Wire.U16(bytes, 554) == 0 ? (ushort)1000 : Wire.U16(bytes, 554),
            AnalogRight = (bytes[556] & 1) != 0, AnalogInvertX = (bytes[556] & 2) != 0, AnalogInvertY = (bytes[556] & 4) != 0,
            AnalogCurve = bytes[557], AnalogMinimumSpeed = (ushort)(bytes[558] * 10), AnalogReturnMs = (ushort)(bytes[559] * 10),
            AnalogOutputMinimum = bytes[848], AnalogOutputMaximum = bytes[849] == 0 ? (byte)100 : bytes[849], AnalogOutputCurve = bytes[850],
            PaddleAnalogSpeed100 = Wire.U16(bytes, 856), PaddleAnalogSmoothingMs = bytes[858], PaddleAnalogReturnMs = (ushort)(bytes[859] * 10),
            PaddleAnalogAxis = bytes[860], PaddleAnalogOutputMinimum = bytes[861],
            PaddleAnalogOutputMaximum = bytes[862] == 0 ? (byte)100 : bytes[862], PaddleAnalogOutputCurve = bytes[863] };
        result.Validate(); return result;
    }
    internal static ConversionSettings FromLegacy(MouseProfileSettings mouse, MouseUdlrSettings legacy)
    {
        MouseUdlrSettings.ValidateTargets(legacy.ButtonMap); MouseUdlrSettings.ValidateTargets(legacy.PaddleButtonMap);
        MouseUdlrSettings.ValidateRolling(legacy.Rolling); MouseUdlrSettings.ValidateTrackball(legacy.Trackball); MouseUdlrSettings.ValidateRollingStep(legacy.RollingStepDeg);
        byte Target(uint map, int i) { byte t = MouseUdlrSettings.Target(map, i); return t == 0 ? MouseUdlrSettings.Target(MouseUdlrSettings.DefaultButtonMap, i) : t; }
        int negative = mouse.PaddleAxis == PaddleOutputAxis.Y ? 0 : 2;
        return new() { TrackballTargets = Enumerable.Range(0, 4).Select(i => Target(legacy.ButtonMap, i)).ToArray(),
            PaddleTargets = [Target(legacy.PaddleButtonMap, negative), Target(legacy.PaddleButtonMap, negative + 1)],
            TrackballMode = (byte)(legacy.TrackballHold ? 1 : 0), PaddleMode = (byte)(legacy.VirtualRolling ? 1 : 0),
            RollingInvert = legacy.RollingInvert, RollingLeft = legacy.RollingLeft, RollingStepDeg = (byte)legacy.RollingStepDeg };
    }
    public ConversionSettings Clone() => new() { TrackballTargets = (byte[])TrackballTargets.Clone(), PaddleTargets = (byte[])PaddleTargets.Clone(),
        TrackballMode = TrackballMode, InertiaEnabled = InertiaEnabled, PaddleMode = PaddleMode, RollingInvert = RollingInvert, RollingLeft = RollingLeft, RollingStepDeg = RollingStepDeg,
        AnalogFullSpeed = AnalogFullSpeed, AnalogRight = AnalogRight, AnalogInvertX = AnalogInvertX, AnalogInvertY = AnalogInvertY,
        AnalogCurve = AnalogCurve, AnalogMinimumSpeed = AnalogMinimumSpeed, AnalogReturnMs = AnalogReturnMs,
        AnalogOutputMinimum = AnalogOutputMinimum, AnalogOutputMaximum = AnalogOutputMaximum, AnalogOutputCurve = AnalogOutputCurve,
        PaddleAnalogAxis = PaddleAnalogAxis, PaddleAnalogSpeed100 = PaddleAnalogSpeed100, PaddleAnalogSmoothingMs = PaddleAnalogSmoothingMs,
        PaddleAnalogReturnMs = PaddleAnalogReturnMs, PaddleAnalogOutputMinimum = PaddleAnalogOutputMinimum,
        PaddleAnalogOutputMaximum = PaddleAnalogOutputMaximum, PaddleAnalogOutputCurve = PaddleAnalogOutputCurve };
}
