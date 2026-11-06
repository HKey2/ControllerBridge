namespace ControllerBridge;

public sealed class ConversionSettings
{
    public byte[] TrackballTargets { get; set; } = [15, 17, 18, 16];
    public byte[] PaddleTargets { get; set; } = [18, 16];
    public byte TrackballMode { get; set; }
    public bool InertiaEnabled { get; set; }
    public byte PaddleMode { get; set; }
    public bool RollingInvert { get; set; }
    public bool RollingLeft { get; set; }
    public byte RollingStepDeg { get; set; } = 30;
    internal static readonly int[] Steps = [5, 10, 15, 20, 30, 40, 45, 60, 90];
    internal void Validate()
    {
        if (TrackballTargets is null || TrackballTargets.Length != 4 || PaddleTargets is null || PaddleTargets.Length != 2 ||
            TrackballTargets.Concat(PaddleTargets).Any(t => t is < 1 or > 18) || TrackballMode > 1 || PaddleMode > 1 || !Steps.Contains(RollingStepDeg))
            throw new InvalidDataException(L.T("方向変換の割り当て・モード・回転刻みを確認してください。", "Check conversion targets, modes and rotation step."));
    }
    internal void Write(byte[] bytes)
    {
        Validate(); TrackballTargets.CopyTo(bytes, 544); PaddleTargets.CopyTo(bytes, 548);
        bytes[550] = (byte)(TrackballMode | PaddleMode << 2);
        bytes[551] = (byte)((RollingInvert ? 1 : 0) | (RollingLeft ? 2 : 0)); bytes[552] = RollingStepDeg; bytes[553] = InertiaEnabled ? (byte)1 : (byte)0;
    }
    internal static ConversionSettings Read(byte[] bytes)
    {
        if ((bytes[550] & ~5) != 0 || bytes[551] > 3 || bytes[553] > 1 || bytes.AsSpan(554, 6).ContainsAnyExcept((byte)0))
            throw new InvalidDataException(L.T("未対応の方向変換設定です。", "Unsupported conversion settings."));
        ConversionSettings result = new() { TrackballTargets = bytes.AsSpan(544, 4).ToArray(), PaddleTargets = bytes.AsSpan(548, 2).ToArray(),
            TrackballMode = (byte)(bytes[550] & 3), PaddleMode = (byte)((bytes[550] >> 2) & 3),
            RollingInvert = (bytes[551] & 1) != 0, RollingLeft = (bytes[551] & 2) != 0, RollingStepDeg = bytes[552], InertiaEnabled = bytes[553] != 0 };
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
        TrackballMode = TrackballMode, InertiaEnabled = InertiaEnabled, PaddleMode = PaddleMode, RollingInvert = RollingInvert, RollingLeft = RollingLeft, RollingStepDeg = RollingStepDeg };
}
