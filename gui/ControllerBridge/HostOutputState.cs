namespace ControllerBridge;

internal sealed record HostOutputState(uint Selected, uint Active, uint State, bool Gamepad)
{
    internal static HostOutputState Read(byte[] data)
    {
        if (data.Length != 56 || !data.AsSpan(0, 4).SequenceEqual("HOS1"u8) ||
            data[4] > 3 || data[5] is not (0 or 1 or 3) || data[6] > 4 || data[7] > 1 || Wire.U32(data, 8) != 5000)
            throw new InvalidDataException(L.T("出力モードを読み込めません。ファームウェアを更新してください。", "Cannot read the host mode. Update the firmware."));
        return new(data[4], data[5], data[6], data[7] != 0);
    }
}
