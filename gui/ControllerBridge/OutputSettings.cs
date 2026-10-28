namespace ControllerBridge;

public sealed record OutputSettings(bool Mouse, int Gain, bool InvertX, bool InvertY, bool Combined = false, bool GamepadButtons = false)
{
    public byte Encode()
    {
        if (Gain is < 1 or > 8) throw new InvalidDataException(L.T("マウス感度は1～8倍で設定してください。", "Set mouse gain to 1–8."));
        if (Combined && !Mouse) throw new InvalidDataException(L.T("同時出力にはマウス出力が必要です。", "Combined output requires mouse output."));
        if (GamepadButtons && !Combined) throw new InvalidDataException(L.T("ゲームパッドへのボタン出力には同時出力が必要です。", "Gamepad button routing requires combined output."));
        return (byte)((Mouse ? 1 : 0) | ((Gain - 1) << 1) | (InvertX ? 16 : 0) | (InvertY ? 32 : 0) | (Combined ? 64 : 0) | (GamepadButtons ? 128 : 0));
    }
    public static OutputSettings Decode(byte value)
    {
        if ((value & 65) == 64 || ((value & 128) != 0 && (value & 65) != 65)) throw new InvalidDataException(L.T("未対応の出力設定です。", "These output settings are not supported."));
        return new((value & 1) != 0, 1 + ((value >> 1) & 7), (value & 16) != 0, (value & 32) != 0, (value & 64) != 0, (value & 128) != 0);
    }
}
public sealed record OutputState(OutputSettings Active, OutputSettings Desired, bool RelativeReady);

