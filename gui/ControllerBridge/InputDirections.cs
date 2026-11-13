namespace ControllerBridge;

internal static class InputDirections
{
    internal const byte Reversed = 32, SourceFlags = 51;
    private static bool LegacyDs3(byte iface, byte id, ushort offset, byte bits, byte flags) =>
        iface == 0 && id == 1 && offset is 48 or 64 && bits == 8 && (flags & SourceFlags) == 2;

    // Migrate the editor copy, preserving the physical response of stored profiles.
    // Firmware continues to read legacy bindings in their original coordinates.
    internal static void NormalizeProfile(DeviceProfile p)
    {
        if (p.Vid != 0x054c || p.Pid != 0x0268) return;
        void Digital(DigitalBinding b)
        {
            if (b.Mode != 1 || !LegacyDs3(b.Interface, b.ReportId, b.BitOffset, b.Bits, b.Flags)) return;
            (b.Low, b.High) = (255 - b.High, 255 - b.Low); b.Flags |= Reversed;
        }
        void Half(AnalogHalfBinding a)
        {
            if (!LegacyDs3(a.Interface, a.ReportId, a.BitOffset, a.Bits, a.Flags)) return;
            a.Neutral = 255 - a.Neutral; a.Maximum = 255 - a.Maximum; a.Flags |= Reversed;
        }
        foreach (DigitalBinding b in p.Buttons.Concat(p.Shift.Keys).Concat(p.Shift.Buttons)) Digital(b);
        foreach (AxisBinding axis in p.Axes)
        {
            if (axis.Mode == 1)
            {
                AnalogBinding a = axis.Analog;
                if (!a.Gyro && LegacyDs3(a.Interface, a.ReportId, a.BitOffset, a.Bits, a.Flags))
                {
                    (a.Minimum, a.Center, a.Maximum) = (255 - a.Maximum, 255 - a.Center, 255 - a.Minimum);
                    a.Flags = (byte)((a.Flags ^ 4) | Reversed);
                }
            }
            else if (axis.Mode == 2) { Digital(axis.Negative); Digital(axis.Positive); }
            else if (axis.Mode == 3) { Half(axis.NegativeAnalog); Half(axis.PositiveAnalog); }
        }
    }
    internal static bool UsesNormalizedInput(DeviceProfile p) =>
        p.Buttons.Concat(p.Shift.Keys).Concat(p.Shift.Buttons).Any(b => b.Mode != 0 && (b.Flags & Reversed) != 0) ||
        p.Axes.Any(a => a.Mode switch
        {
            1 => (a.Analog.Flags & Reversed) != 0,
            2 => (a.Negative.Mode != 0 && (a.Negative.Flags & Reversed) != 0) || (a.Positive.Mode != 0 && (a.Positive.Flags & Reversed) != 0),
            3 => ((a.NegativeAnalog.Flags | a.PositiveAnalog.Flags) & Reversed) != 0,
            _ => false
        });
    internal static void RequireCurrentInput(IEnumerable<InputField> fields)
    {
        if (fields.Any(f => f.DualShock3 && LegacyDs3(f.Interface, f.ReportId, f.BitOffset, f.Bits, f.Flags)))
            throw new InvalidDataException(UpdateMessage);
    }
    internal static string UpdateMessage => L.T("DS3の軸方向を統一するには、画面上部の「ファームウェア更新」で同梱ファームウェアへ更新してください。", "To normalize DS3 axis directions, use Firmware update at the top of the window to install the bundled firmware.");
}
