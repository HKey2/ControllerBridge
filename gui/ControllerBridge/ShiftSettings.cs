namespace ControllerBridge;

public sealed class ShiftSettings
{
    public const int MaxKeys = 3;
    public uint Overrides { get; set; }
    public List<DigitalBinding> Keys { get; set; } = [];
    public DigitalBinding[] Buttons { get; set; } = Enumerable.Range(0, 18).Select(_ => new DigitalBinding()).ToArray();
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsEmpty => Overrides == 0 && Keys.Count == 0 && Buttons.All(b => b.Mode == 0 && b.RapidHz == 0);
    internal static DigitalBinding Copy(DigitalBinding binding)
    {
        byte[] data = new byte[16]; binding.Write(data, 0, true); return DigitalBinding.Read(data, 0, true);
    }
    internal static bool SameSource(DigitalBinding a, DigitalBinding b) =>
        a.Interface == b.Interface && a.ReportId == b.ReportId && a.BitOffset == b.BitOffset && a.Bits == b.Bits && (a.Flags & 19) == (b.Flags & 19);
    internal static string DescribeKey(DigitalBinding key, IEnumerable<InputField> fields)
    {
        InputField? field = fields.FirstOrDefault(f => f.Interface == key.Interface && f.ReportId == key.ReportId &&
            f.BitOffset == key.BitOffset && f.Bits == key.Bits && (f.Flags & 19) == (key.Flags & 19));
        return (field?.Label ?? L.T("入力名未確認", "Input name unavailable")) + $"  [{key}]";
    }
    internal static bool Pressed(DigitalBinding b, int value)
    {
        if (b.Mode == 0 || value < b.Low || value > b.High) return false;
        if (b.Mode == 1) return true;
        int position = value - b.Low, direction = b.Mode - 2;
        return b.High - b.Low == 3 ? position == direction : position == 2 * direction || position == ((2 * direction + 1) & 7) || position == ((2 * direction + 7) & 7);
    }
    internal static bool Overlap(DigitalBinding a, DigitalBinding b)
    {
        if (a.Mode == 0 || b.Mode == 0 || !SameSource(a, b)) return false;
        if (((a.Flags ^ b.Flags) & InputDirections.Reversed) != 0 && b.Bits == 8 && b.Mode == 1)
        {
            b = Copy(b); (b.Low, b.High) = (255 - b.High, 255 - b.Low); b.Flags ^= InputDirections.Reversed;
        }
        if (a.Mode == 1 && b.Mode == 1) return a.Low <= b.High && b.Low <= a.High;
        DigitalBinding hat = a.Mode == 1 ? b : a;
        for (long v = hat.Low; v <= hat.High; v++) if (Pressed(a, (int)v) && Pressed(b, (int)v)) return true;
        return false;
    }
    internal void ValidateSwitchKeys(DeviceProfile profile, uint mask)
    {
        for (int i = 0; i < 18; i++) if ((mask & (1u << i)) != 0 && Keys.Any(k => Overlap(k, profile.Buttons[i])))
            throw new InvalidDataException(L.T("シフトキーとプロファイル切替ホットキーが同じ入力を使用しています。どちらかのキーを変更してください。", "Button shift and profile switching use the same input. Change one of these shortcuts."));
    }
    internal void Write(byte[] bytes)
    {
        if (Keys is null || Keys.Count > MaxKeys || Buttons is null || Buttons.Length != 18 || (Overrides & ~0x3ffffu) != 0) throw new InvalidDataException(L.T("シフト設定が不正です。", "The shift settings are invalid."));
        Wire.W32(bytes, 488, Overrides); bytes[492] = (byte)Keys.Count;
        for (int i = 0; i < Keys.Count; i++)
        {
            DigitalBinding key = Keys[i] ?? throw new InvalidDataException(); key.Validate();
            if (key.Mode == 0 || Keys.Take(i).Any(k => SameSource(k, key))) throw new InvalidDataException(L.T("シフトキーは異なる入力から選んでください。", "Choose distinct physical inputs for the shift keys."));
            key.Write(bytes, 496 + 16 * i);
        }
        for (int i = 0; i < 18; i++) (Buttons[i] ?? throw new InvalidDataException()).Write(bytes, 560 + 16 * i, true);
    }
    internal static ShiftSettings Read(byte[] bytes, bool legacy = false)
    {
        int count = bytes[492];
        if (count > (legacy ? 4 : MaxKeys) || bytes[495] > 1 || (legacy && (bytes[493] != 0 || bytes[494] != 0 || bytes[495] != 0))) throw new InvalidDataException(L.T("未対応のシフト設定です。", "These shift settings are not supported."));
        ShiftSettings result = new() { Overrides = Wire.U32(bytes, 488) };
        for (int i = 0; i < (legacy ? 4 : MaxKeys); i++)
        {
            DigitalBinding key = DigitalBinding.Read(bytes, 496 + 16 * i);
            if (i < count)
            {
                if (key.Mode == 0 || result.Keys.Any(k => SameSource(k, key))) throw new InvalidDataException("Invalid shift key.");
                if (i < MaxKeys) result.Keys.Add(key);
            }
        }
        for (int i = 0; i < 18; i++) result.Buttons[i] = DigitalBinding.Read(bytes, 560 + 16 * i, true);
        return result;
    }
}
