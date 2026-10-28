using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace ControllerBridge;

internal static class Wire
{
    public static ushort U16(byte[] b, int p) => BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(p));
    public static uint U32(byte[] b, int p) => BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(p));
    public static int I32(byte[] b, int p) => BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(p));
    public static void W16(byte[] b, int p, ushort v) => BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(p), v);
    public static void W32(byte[] b, int p, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p), v);
    public static void WI32(byte[] b, int p, int v) => BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(p), v);
    public static uint Crc(byte[] b)
    {
        uint c = uint.MaxValue;
        foreach (byte v in b) { c ^= v; for (int i = 0; i < 8; i++) c = (c >> 1) ^ (0xedb88320u & (0u - (c & 1))); }
        return ~c;
    }
    public static void Source(byte iface, byte id, ushort offset, byte bits, byte flags)
    {
        if (bits is 0 or > 32 || offset + bits > ((flags & 2) != 0 ? 504 : 512) ||
            ((flags & 2) != 0 ? id == 0 : id != 0)) throw new InvalidDataException(L.T("HID入力位置が不正です。", "The HID input location is invalid."));
    }
    public static bool Fits(int value, byte bits, byte flags) => (flags & 1) != 0
        ? bits == 32 || (value >= -(1L << (bits - 1)) && value < (1L << (bits - 1)))
        : value >= 0 && (bits == 32 || value < (1L << bits));
}

public sealed class DigitalBinding
{
    public byte Interface { get; set; }
    public byte ReportId { get; set; }
    public ushort BitOffset { get; set; }
    public byte Bits { get; set; }
    public byte Mode { get; set; }
    public byte Flags { get; set; }
    public byte RapidHz { get; set; }
    public int Low { get; set; }
    public int High { get; set; }
    public void Validate(bool allowRapid = false)
    {
        if (RapidHz != 0 && (!allowRapid || Mode == 0 || RapidHz is < 2 or > 30))
            throw new InvalidDataException(L.T("連射は割り当て済みの出力ボタンに、毎秒2～30回で設定してください。", "Set rapid fire to 2–30 presses per second on an assigned output button."));
        if (Mode == 0) return;
        Wire.Source(Interface, ReportId, BitOffset, Bits, Flags);
        if (Mode > 5 || Flags > 3 || Low > High || !Wire.Fits(Low, Bits, Flags) || !Wire.Fits(High, Bits, Flags) ||
            (Mode > 1 && (long)High - Low is not (3 or 7))) throw new InvalidDataException(L.T("ボタン割り当てが不正です。", "The button mapping is invalid."));
    }
    internal void Write(byte[] b, int p, bool allowRapid = false)
    {
        Validate(allowRapid); b[p] = Interface; b[p + 1] = ReportId; Wire.W16(b, p + 2, BitOffset);
        b[p + 4] = Bits; b[p + 5] = Mode; b[p + 6] = Flags; b[p + 7] = RapidHz; Wire.WI32(b, p + 8, Low); Wire.WI32(b, p + 12, High);
    }
    internal static DigitalBinding Read(byte[] b, int p, bool allowRapid = false)
    {
        DigitalBinding result = new() { Interface = b[p], ReportId = b[p + 1], BitOffset = Wire.U16(b, p + 2), Bits = b[p + 4],
            Mode = b[p + 5], Flags = b[p + 6], RapidHz = b[p + 7], Low = Wire.I32(b, p + 8), High = Wire.I32(b, p + 12) };
        result.Validate(allowRapid); return result;
    }
    public override string ToString() => Mode == 0 ? L.T("未割り当て", "Unassigned") : $"IF{Interface} / ID{ReportId} / bit{BitOffset}  " +
        (Mode > 1 ? new[] { "", "", "Hat ↑", "Hat →", "Hat ↓", "Hat ←" }[Mode] : Low == High ? L.T($"値 {Low}", $"Value {Low}") : $"{Low} ～ {High}") +
        (RapidHz == 0 ? "" : L.T($" / 連射 {RapidHz}回/秒", $" / Rapid fire {RapidHz}/s"));
}

public sealed class AnalogBinding
{
    public byte Interface { get; set; }
    public byte ReportId { get; set; }
    public ushort BitOffset { get; set; }
    public byte Bits { get; set; }
    public byte Flags { get; set; }
    public int Minimum { get; set; }
    public int Center { get; set; }
    public int Maximum { get; set; }
    public ushort Deadzone { get; set; } = 5;
    internal void Write(byte[] b, int p)
    {
        Wire.Source(Interface, ReportId, BitOffset, Bits, Flags);
        if (Flags > 7 || Minimum >= Center || Center >= Maximum || Deadzone > 50 ||
            !Wire.Fits(Minimum, Bits, Flags) || !Wire.Fits(Maximum, Bits, Flags)) throw new InvalidDataException(L.T("アナログの最小 < 中心 < 最大、デッドゾーン0～50%を確認してください。", "Check that minimum < center < maximum, with a dead zone of 0–50%."));
        b[p] = Interface; b[p + 1] = ReportId; Wire.W16(b, p + 2, BitOffset); b[p + 4] = Bits; b[p + 5] = Flags;
        Wire.WI32(b, p + 8, Minimum); Wire.WI32(b, p + 12, Center); Wire.WI32(b, p + 16, Maximum); Wire.W16(b, p + 20, Deadzone);
    }
    internal static AnalogBinding Read(byte[] b, int p) => new() { Interface = b[p], ReportId = b[p + 1],
        BitOffset = Wire.U16(b, p + 2), Bits = b[p + 4], Flags = b[p + 5], Minimum = Wire.I32(b, p + 8),
        Center = Wire.I32(b, p + 12), Maximum = Wire.I32(b, p + 16), Deadzone = Wire.U16(b, p + 20) };
}

public sealed class AxisBinding
{
    public byte Mode { get; set; }
    public AnalogBinding Analog { get; set; } = new();
    public DigitalBinding Negative { get; set; } = new();
    public DigitalBinding Positive { get; set; } = new();
    public override string ToString() => Mode switch { 1 => L.T($"アナログ IF{Analog.Interface}/ID{Analog.ReportId}/bit{Analog.BitOffset}", $"Analog IF{Analog.Interface}/ID{Analog.ReportId}/bit{Analog.BitOffset}") + ((Analog.Flags & 4) != 0 ? L.T(" (反転)", " (inverted)") : ""),
        2 => L.T("2つのボタン（負 / 正）", "Two buttons (negative / positive)"), _ => L.T("未使用・中心固定", "Unused / centered") };
}

public sealed class MouseProfileSettings
{
    public bool TrackballEnabled { get; set; } = true;
    public bool PaddleEnabled { get; set; } = true;
    public decimal TrackballGain { get; set; } = 16.0m;
    public decimal PaddleGain { get; set; } = 1.0m;
    public bool InvertX { get; set; }
    public bool InvertY { get; set; }
    public bool InvertPaddle { get; set; }
    internal void Write(byte[] b, int at)
    {
        if (TrackballGain < 1 || TrackballGain > 64 || TrackballGain * 10 != decimal.Truncate(TrackballGain * 10))
            throw new InvalidDataException(L.T("トラックボールの感度は1.0～64.0倍、0.1刻みで設定してください。", "Set trackball gain to 1.0–64.0 in steps of 0.1."));
        if (PaddleGain < 1 || PaddleGain > 10 || PaddleGain * 10 != decimal.Truncate(PaddleGain * 10))
            throw new InvalidDataException(L.T("パドルの感度は1.0～10.0倍、0.1刻みで設定してください。", "Set paddle gain to 1.0–10.0 in steps of 0.1."));
        b[at] = (byte)((TrackballEnabled ? 1 : 0) | (PaddleEnabled ? 2 : 0) | (InvertX ? 4 : 0) | (InvertY ? 8 : 0) | (InvertPaddle ? 16 : 0));
        ushort gain = (ushort)(TrackballGain * 10);
        b[at + 1] = (byte)gain; b[at + 2] = (byte)(PaddleGain * 10); b[at + 3] = (byte)(gain >> 8);
    }
    internal static MouseProfileSettings Read(byte[] b, int at)
    {
        int gain = b[at + 1] | b[at + 3] << 8;
        if (b[at] > 31) throw new InvalidDataException(L.T("未対応のマウス設定です。", "These mouse settings are not supported."));
        return new() { TrackballEnabled = (b[at] & 1) != 0, PaddleEnabled = (b[at] & 2) != 0,
            TrackballGain = gain / 10m, PaddleGain = b[at + 2] / 10m,
            InvertX = (b[at] & 4) != 0, InvertY = (b[at] & 8) != 0, InvertPaddle = (b[at] & 16) != 0 };
    }
}

public sealed class DeviceProfile
{
    // Stored beside the profile on the board; included in JSON exports.
    public Guid ProfileId { get; set; }
    public const int ByteCount = 488;
    public const int SlotCount = 16;
    public static readonly string[] Targets = ["Y", "B", "A", "X", "L", "R", "ZL", "ZR", "−", "+", L.T("L押込", "L stick"), L.T("R押込", "R stick"), "HOME", "CAPTURE", "↑", "→", "↓", "←"];
    public static readonly string[] AxisNames = [L.T("左スティック X", "Left stick X"), L.T("左スティック Y", "Left stick Y"), L.T("右スティック X", "Right stick X"), L.T("右スティック Y", "Right stick Y")];
    public ushort Version { get; set; } = 5;
    public MouseProfileSettings Mouse { get; set; } = new();
    public byte OutputMode { get; set; } // 0 gamepad, 1 USB mouse, 2 combined.
    public bool ControllerButtons { get; set; }
    public byte LedRed { get; set; }
    public byte LedGreen { get; set; }
    public byte LedBlue { get; set; }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool CustomLed => (Flags & 8) != 0;
    [System.Text.Json.Serialization.JsonIgnore]
    public int LedRgb => CustomLed ? (LedRed << 16) | (LedGreen << 8) | LedBlue : 1;
    public void SetLedColor(bool custom, byte red = 0, byte green = 0, byte blue = 1)
    {
        Flags = (ushort)((Flags & ~8) | (custom ? 8 : 0));
        LedRed = custom ? red : (byte)0; LedGreen = custom ? green : (byte)0; LedBlue = custom ? blue : (byte)0;
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public OutputSettings Output => new(OutputMode != 0, 1, false, false, OutputMode == 2, OutputMode == 2 && ControllerButtons);
    public ushort Flags { get; set; }
    public ushort Vid { get; set; }
    public ushort Pid { get; set; }
    public ushort Revision { get; set; }
    public byte InputInterval { get; set; }
    public uint DescriptorHash { get; set; }
    public string Name { get; set; } = "";
    public DigitalBinding[] Buttons { get; set; } = Enumerable.Range(0, 18).Select(_ => new DigitalBinding()).ToArray();
    public AxisBinding[] Axes { get; set; } = Enumerable.Range(0, 4).Select(_ => new AxisBinding()).ToArray();
    public bool Enabled => (Flags & 1) != 0;
    public bool HasRapidFire => Buttons.Any(b => b is { RapidHz: > 0 });
    public byte[] Encode()
    {
        if (Version != 5 || OutputMode > 2 || Flags > 15 || (!CustomLed && (LedRed != 0 || LedGreen != 0 || LedBlue != 0)) || Name is null || Encoding.UTF8.GetByteCount(Name) > 47 || Name.Contains('\0') ||
            (Enabled && (Vid == 0 || string.IsNullOrWhiteSpace(Name))) || Buttons is null || Buttons.Length != 18 || Axes is null || Axes.Length != 4)
            throw new InvalidDataException(L.T("プロファイルの形式、名前（UTF-8で47バイトまで）、VIDを確認してください。", "Check the profile format, name (up to 47 UTF-8 bytes), and VID."));
        // v5 stores per-profile output in one former reserved byte, without
        // enlarging the 488-byte profile or the 16-slot Flash bank.
        const ushort wireVersion = 5;
        byte[] b = new byte[ByteCount]; Wire.W16(b, 0, wireVersion); Wire.W16(b, 2, Flags); Wire.W16(b, 4, Vid);
        Wire.W16(b, 6, Pid); Wire.W16(b, 8, Revision); b[10] = InputInterval; Wire.W32(b, 12, DescriptorHash);
        Encoding.UTF8.GetBytes(Name).CopyTo(b, 16);
        for (int i = 0; i < 18; i++) (Buttons[i] ?? throw new InvalidDataException(L.T("割り当てが欠落しています。", "A button mapping is missing."))).Write(b, 64 + i * 16, true);
        for (int i = 0; i < 4; i++)
        {
            AxisBinding a = Axes[i] ?? throw new InvalidDataException(L.T("軸が欠落しています。", "An axis is missing."));
            if (a.Mode > 2) throw new InvalidDataException(L.T("未対応の軸モードです。", "This axis mode is not supported."));
            b[11] |= (byte)(a.Mode << (2 * i)); int p = 352 + 32 * i;
            if (a.Mode == 1) (a.Analog ?? throw new InvalidDataException(L.T("アナログ設定が欠落しています。", "Analog settings are missing."))).Write(b, p);
            if (a.Mode == 2) { (a.Negative ?? throw new InvalidDataException()).Write(b, p); (a.Positive ?? throw new InvalidDataException()).Write(b, p + 16); }
        }
        (Mouse ?? throw new InvalidDataException(L.T("マウス設定が欠落しています。", "Mouse settings are missing."))).Write(b, 480);
        b[484] = (byte)(OutputMode | (ControllerButtons ? 4 : 0));
        b[485] = LedRed; b[486] = LedGreen; b[487] = LedBlue;
        return b;
    }
    public static DeviceProfile Decode(byte[] b)
    {
        if (b.Length != ByteCount || Wire.U16(b, 0) != 5)
            throw new InvalidDataException(L.T("プロファイル形式・長さが不正です。", "The profile format or length is invalid."));
        int end = Array.IndexOf(b, (byte)0, 16, 48);
        if (end < 16) throw new InvalidDataException(L.T("プロファイル名が不正です。", "The profile name is invalid."));
        DeviceProfile p = new() { Version = Wire.U16(b, 0), Flags = Wire.U16(b, 2), Vid = Wire.U16(b, 4), Pid = Wire.U16(b, 6),
            Revision = Wire.U16(b, 8), InputInterval = b[10], DescriptorHash = Wire.U32(b, 12), Name = new UTF8Encoding(false, true).GetString(b, 16, end - 16) };
        for (int i = 0; i < 18; i++) p.Buttons[i] = DigitalBinding.Read(b, 64 + i * 16, true);
        for (int i = 0; i < 4; i++)
        {
            byte mode = (byte)((b[11] >> (2 * i)) & 3); int at = 352 + i * 32; p.Axes[i].Mode = mode;
            if (mode == 1) p.Axes[i].Analog = AnalogBinding.Read(b, at);
            if (mode == 2) { p.Axes[i].Negative = DigitalBinding.Read(b, at); p.Axes[i].Positive = DigitalBinding.Read(b, at + 16); }
        }
        p.Mouse = MouseProfileSettings.Read(b, 480);
        if ((b[484] & ~7) != 0) throw new InvalidDataException(L.T("未対応の出力方式です。", "This output mode is not supported."));
        p.OutputMode = (byte)(b[484] & 3); p.ControllerButtons = (b[484] & 4) != 0;
        p.LedRed = b[485]; p.LedGreen = b[486]; p.LedBlue = b[487];
        _ = p.Encode(); return p;
    }
    public DeviceProfile Clone() { DeviceProfile p = Decode(Encode()); p.ProfileId = ProfileId; return p; }
    public bool SameContent(DeviceProfile p) => ProfileId == p.ProfileId && Encode().SequenceEqual(p.Encode());
    public bool SameController(DeviceProfile p) => Enabled && p.Enabled && Vid == p.Vid && Pid == p.Pid &&
        (Flags & 6) == (p.Flags & 6) && ((Flags & 2) == 0 || DescriptorHash == p.DescriptorHash) &&
        ((Flags & 4) == 0 || Revision == p.Revision);
    public bool Matches(Capabilities c) => Enabled && Vid == c.Vid && Pid == c.Pid &&
        ((Flags & 2) == 0 || DescriptorHash == c.DescriptorHash) && ((Flags & 4) == 0 || Revision == c.Revision);
}

internal sealed class ProfileFile
{
    public string Format { get; set; } = "ControllerBridge.Profile";
    public int Version { get; set; } = 2;
    public DeviceProfile Profile { get; set; } = new();
    public static void Save(string path, DeviceProfile p)
    {
        DeviceProfile copy = p.Clone();
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new ProfileFile { Profile = copy }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            if (!Load(temporary).SameContent(copy)) throw new IOException(L.T("プロファイルの照合に失敗しました。", "The profile could not be verified."));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static DeviceProfile Load(string path)
    {
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException(L.T("プロファイルファイルが大きすぎます。", "The profile file is too large."));
        ProfileFile f = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(path)) ?? throw new InvalidDataException(L.T("JSON形式が不正です。", "The JSON format is invalid."));
        if (f.Format != "ControllerBridge.Profile" || f.Version != 2 || f.Profile is null) throw new InvalidDataException(L.T("未対応のプロファイル形式です。", "This profile format is not supported."));
        return f.Profile.Clone();
    }
}
