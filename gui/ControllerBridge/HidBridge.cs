using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace ControllerBridge;

public sealed record Capabilities(byte ActiveSlot, ushort FieldCount, ushort Vid, ushort Pid, ushort Revision,
    ushort Flags, uint ConnectionGeneration, uint DescriptorHash, uint BankGeneration, uint EventHead, byte Interval, byte HostStatus, byte InputProtocol = 0, uint Features = 0,
    byte ProfileNumber = 0, byte ProfileCount = 0, uint SwitchKeys = 0)
{
    public bool Connected => (Flags & 1) != 0;
    public bool Partial => (Flags & 2) != 0;
    public bool Persisted => (Flags & 4) != 0;
    public bool InvalidBinding => (Flags & 8) != 0;
    public bool SupportsRapidFire => (Features & 1) != 0;
    public bool SupportsProfileIdentity => (Features & 2) != 0;
    public bool SupportsOnBoardVariants => (Features & 4) != 0;
    public bool SupportsSwitchKeys => (Features & 8) != 0;
    public bool SupportsImmediateSwitch => (Features & 16) != 0;
    public bool SupportsMouseProfiles => (Features & 64) != 0;
    public bool SupportsMouseOutput => (Features & 32) != 0;
    public bool SupportsCombinedOutput => (Features & 128) != 0;
    public bool SupportsGamepadButtons => (Features & 256) != 0;
    public bool SupportsExtendedTrackballGain => (Features & 512) != 0;
    public bool SupportsBankTransfer => (Features & 2048) != 0;
    public bool SupportsProfileOutput => (Features & 4096) != 0;
    public bool SelectionUnsaved => (Flags & 16) != 0;
    public string ProtocolLabel => InputProtocol switch { 2 => "XInput (Xbox 360)", 3 => "HID / XInput", 4 => "Switch USB", _ => "HID" };
    internal static Capabilities Read(byte[] b)
    {
        if (!b.AsSpan(0, 4).SequenceEqual("UPG1"u8) || Wire.U16(b, 4) != 1 || Wire.U16(b, 6) != DeviceProfile.ByteCount || b[8] != DeviceProfile.SlotCount || (Wire.U32(b, 44) & 4096) == 0)
            throw new InvalidDataException(L.T("この本体は未対応の設定形式です。GUI付属のControllerBridge.uf2へ更新してください。", "This device uses an unsupported settings format. Update it with the included ControllerBridge.uf2."));
        return new(b[9], Wire.U16(b, 10), Wire.U16(b, 12), Wire.U16(b, 14), Wire.U16(b, 16), Wire.U16(b, 18),
            Wire.U32(b, 20), Wire.U32(b, 24), Wire.U32(b, 28), Wire.U32(b, 32), b[36], b[37], b[38], Wire.U32(b, 44), b[50], b[51], Wire.U32(b, 52));
    }
}

public sealed class InputField
{
    public ushort Index { get; init; }
    public byte Interface { get; init; }
    public byte ReportId { get; init; }
    public byte Bits { get; init; }
    public byte Flags { get; init; }
    public ushort BitOffset { get; init; }
    public ushort UsagePage { get; init; }
    public ushort Usage { get; init; }
    public ushort HidFlags { get; init; }
    public int Minimum { get; init; }
    public int Maximum { get; init; }
    public int Value { get; set; }
    public bool Valid { get; set; }
    public bool Learnable => (HidFlags & 4) == 0 && Maximum > Minimum && Bits is > 0 and <= 32 &&
        (!(UsagePage == 1 && Usage == 0x39) || Hat);
    public bool Hat => UsagePage == 1 && Usage == 0x39 && (long)Maximum - Minimum is 3 or 7;
    public bool Binary => (long)Maximum - Minimum == 1;
    public string Label => UsagePage == 9 ? L.T($"ボタン {Usage}", $"Button {Usage}") : UsagePage == 1 ? Usage switch
        { 0x30 => L.T("X軸", "X axis"), 0x31 => L.T("Y軸", "Y axis"), 0x32 => L.T("Z軸", "Z axis"), 0x33 => L.T("Rx軸", "Rx axis"), 0x34 => L.T("Ry軸", "Ry axis"), 0x35 => L.T("Rz軸", "Rz axis"), 0x36 => L.T("スライダー", "Slider"), 0x38 => L.T("パドル (Wheel)", "Paddle (Wheel)"), 0x39 => L.T("十字キー (Hat)", "D-pad (Hat)"),
          0x90 => L.T("十字キー 上", "D-pad up"), 0x91 => L.T("十字キー 下", "D-pad down"), 0x92 => L.T("十字キー 右", "D-pad right"), 0x93 => L.T("十字キー 左", "D-pad left"), _ => $"HID {UsagePage:X2}:{Usage:X2}" }
        : $"HID {UsagePage:X2}:{Usage:X2}";
    public override string ToString() => $"{Label}  [IF{Interface}/ID{ReportId}/bit{BitOffset}]";
    internal static InputField Read(byte[] b) => new() { Index = Wire.U16(b, 6), Interface = b[8], ReportId = b[9], Bits = b[10], Flags = b[11],
        BitOffset = Wire.U16(b, 12), UsagePage = Wire.U16(b, 14), Usage = Wire.U16(b, 16), HidFlags = Wire.U16(b, 18),
        Minimum = Wire.I32(b, 20), Maximum = Wire.I32(b, 24), Value = Wire.I32(b, 28), Valid = b[36] != 0 };
    public DigitalBinding Digital(byte mode, int low, int high) => new() { Interface = Interface, ReportId = ReportId,
        Bits = Bits, Flags = Flags, BitOffset = BitOffset, Mode = mode, Low = low, High = high };
    public AnalogBinding Analog(int minimum, int center, int maximum, ushort deadzone, bool invert) => new()
        { Interface = Interface, ReportId = ReportId, Bits = Bits, Flags = (byte)(Flags | (invert ? 4 : 0)), BitOffset = BitOffset,
            Minimum = minimum, Center = center, Maximum = maximum, Deadzone = deadzone };
}

internal sealed record InputChange(ushort Index, int Value, uint Time, bool Lost);
internal sealed record DeviceState(Capabilities Caps, List<InputField> Fields, uint Cursor);
internal sealed record PollResult(Capabilities Caps, uint Cursor, List<InputChange> Changes, bool GenerationChanged);
internal sealed class BridgeCommandException(byte status, string message) : IOException(message)
{
    public byte Status { get; } = status;
}
internal sealed class BridgeDiscoveryException(int matches, string message) : IOException(message)
{
    internal int Matches { get; } = matches;
}

internal sealed class HidBridge : IDisposable
{
    internal bool OutputReconnectPending { get; private set; }
    private readonly SafeFileHandle handle;
    internal string DevicePath { get; private init; } = "";
    private byte sequence;
    private readonly SemaphoreSlim gate = new(1, 1);
    private HidBridge(SafeFileHandle handle, byte sequence) { this.handle = handle; this.sequence = sequence; }
    [StructLayout(LayoutKind.Sequential)] private struct DeviceInterface { public int Size; public Guid Guid; public uint Flags; public IntPtr Reserved; }
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SetupDiGetClassDevsW(ref Guid guid, string? enumerator, IntPtr parent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr device, ref Guid guid, uint index, ref DeviceInterface iface);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiGetDeviceInterfaceDetailW(IntPtr set, ref DeviceInterface iface, IntPtr detail, uint size, out uint required, IntPtr device);
    [DllImport("setupapi.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_GetFeature(SafeFileHandle handle, [Out] byte[] data, uint size);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)] private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] data, uint size);
    private static byte[] Feature(SafeFileHandle handle)
    {
        byte[] b = new byte[65];
        if (!HidD_GetFeature(handle, b, 65)) throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("本体のUSB通信が切れました。USB-Cの接続を確認してください。", "USB communication was interrupted. Check the USB-C connection."));
        return b[1..];
    }
    public static Task<HidBridge> ConnectAsync() => Task.Run(() =>
    {
        Guid guid = new("4d1e55b2-f16f-11cf-88cb-001111000030");
        IntPtr set = SetupDiGetClassDevsW(ref guid, null, IntPtr.Zero, 0x12);
        if (set == new IntPtr(-1)) throw new Win32Exception(Marshal.GetLastWin32Error());
        List<HidBridge> matches = [];
        try
        {
            for (uint index = 0; ; index++)
            {
                DeviceInterface iface = new() { Size = Marshal.SizeOf<DeviceInterface>() };
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref iface))
                {
                    if (Marshal.GetLastWin32Error() == 259) break;
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }
                SetupDiGetDeviceInterfaceDetailW(set, ref iface, IntPtr.Zero, 0, out uint size, IntPtr.Zero);
                IntPtr detail = Marshal.AllocHGlobal(checked((int)size));
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetailW(set, ref iface, detail, size, out _, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                    string path = Marshal.PtrToStringUni(detail + 4) ?? "";
                    if (!path.Contains("vid_20d6&pid_a713", StringComparison.OrdinalIgnoreCase) &&
                        !path.Contains("vid_cafe&pid_4004", StringComparison.OrdinalIgnoreCase)) continue;
                    SafeFileHandle h = CreateFileW(path, 0xc0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                    if (h.IsInvalid) { h.Dispose(); continue; }
                    bool keep = false;
                    try
                    {
                        byte[] b = Feature(h);
                        if (b.AsSpan(0, 5).SequenceEqual("UC2R\x01"u8)) { matches.Add(new(h, b[6]) { DevicePath = path }); keep = true; }
                    }
                    catch (Win32Exception) { }
                    finally { if (!keep) h.Dispose(); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
            if (matches.Count != 1) throw new BridgeDiscoveryException(matches.Count, matches.Count == 0 ? L.T("変換本体が見つかりません。USB-CをPCへ接続し、再接続を押してください。", "No bridge was found. Connect USB-C to this PC and choose Reconnect.") : L.T("変換本体が複数あります。設定する本体を1台だけ接続してください。", "Multiple bridges were found. Connect only the bridge you want to configure."));
            HidBridge result = matches[0]; matches.Clear(); return result;
        }
        finally { SetupDiDestroyDeviceInfoList(set); foreach (HidBridge m in matches) m.Dispose(); }
    });
    internal async Task<FirmwareInfo?> ReadFirmwareAsync()
    {
        try { return FirmwareInfo.Read(await RawAsync(47)); }
        catch (BridgeCommandException ex) when (ex.Status == 1) { return null; }
    }
    private byte[] Command(byte cmd, byte[]? payload = null)
    {
        if (payload?.Length > 56) throw new ArgumentException("payload");
        byte[] b = new byte[65]; "UC2C"u8.CopyTo(b.AsSpan(1)); b[5] = 1; b[6] = cmd; b[7] = ++sequence; payload?.CopyTo(b, 9);
        if (!HidD_SetFeature(handle, b, 65)) throw new Win32Exception(Marshal.GetLastWin32Error(), L.T("本体へ送信できません。接続を確認してください。", "Could not send to the device. Check the connection."));
        Stopwatch watch = Stopwatch.StartNew();
        do
        {
            byte[] r = Feature(handle);
            if (r.AsSpan(0, 5).SequenceEqual("UC2R\x01"u8) && r[5] == cmd && r[6] == sequence)
            {
                if (r[7] != 0) throw new BridgeCommandException(r[7], r[7] switch
                {
                    1 when cmd == 20 => L.T("本体のファームウェアが古いため、GUI付属のControllerBridge.uf2へ更新してください。", "The device firmware is too old. Update it with the included ControllerBridge.uf2."),
                    1 => L.T("本体が設定データを受け付けませんでした。形式・入力範囲を確認してください。", "The device rejected the settings. Check the format and input ranges."),
                    2 => L.T("本体は計測中です。計測終了後に保存してください。", "The device is recording. Save after recording finishes."), 3 => L.T("Flashへの保存または読み出しに失敗しました。", "Flash storage could not be written or read."),
                    4 => L.T("本体の設定が別の操作で変更されました。再読込してから保存してください。", "Another operation changed the device settings. Reload before saving."), _ => L.T($"本体エラー {r[7]}", $"Device error {r[7]}")
                });
                return r[8..];
            }
            Thread.Sleep(1);
        } while (watch.ElapsedMilliseconds < 5000);
        throw new TimeoutException(L.T("本体の応答待ちがタイムアウトしました。", "The device response timed out."));
    }
    private async Task<T> Run<T>(Func<T> work)
    {
        await gate.WaitAsync();
        try { return await Task.Run(work); }
        finally { gate.Release(); }
    }
    private Capabilities Caps() => Capabilities.Read(Command(20));
    public Task<Capabilities> GetCapsAsync() => Run(Caps);
    public Task<OutputState> ReadOutputStateAsync() => Run(() =>
    {
        byte[] b = Command(36);
        if (!b.AsSpan(0, 4).SequenceEqual("MSE1"u8)) throw new InvalidDataException(L.T("出力切替にはファームウェア0.9.0-dev以降が必要です。", "Output switching requires firmware 0.9.0-dev or later."));
        return new OutputState(OutputSettings.Decode(b[4]), OutputSettings.Decode(b[5]), b[6] != 0);
    });
    private void ApplyProfileOutput(uint expectedGeneration)
    {
        Capabilities current = Caps(); byte[] args = new byte[5];
        Wire.W32(args, 0, expectedGeneration); args[4] = current.ActiveSlot;
        byte[] reply = Command(46, args);
        if (!reply.AsSpan(0, 4).SequenceEqual("OUT1"u8)) throw new InvalidDataException("Profile output ABI");
        OutputReconnectPending = reply[6] != 0;
    }
    private DeviceProfile ReadProfile(int slot, Capabilities caps)
    {
        byte[] b = new byte[DeviceProfile.ByteCount];
        for (int off = 0; off < DeviceProfile.ByteCount; off += 56)
        {
            byte[] args = [(byte)slot, (byte)off, (byte)(off >> 8)];
            Command(23, args).AsSpan(0, Math.Min(56, DeviceProfile.ByteCount - off)).CopyTo(b.AsSpan(off));
        }
        DeviceProfile profile = DeviceProfile.Decode(b);
        if (caps.SupportsProfileIdentity)
        {
            byte[] identity = Command(32, [(byte)slot]);
            if (Wire.U32(identity, 16) != caps.BankGeneration || Wire.U32(identity, 20) != Wire.Crc(b))
                throw new IOException(L.T("読込中に本体の設定が変わりました。再読込してください。", "The device settings changed while reading. Reload the device."));
            profile.ProfileId = new Guid(identity.AsSpan(0, 16));
        }
        return profile;
    }
    public Task<(Capabilities Caps, DeviceProfile[] Profiles)> ReadProfilesAsync() => Run(() =>
    {
        Capabilities before = Caps(); DeviceProfile[] profiles = new DeviceProfile[DeviceProfile.SlotCount];
        for (int i = 0; i < profiles.Length; i++) profiles[i] = ReadProfile(i, before);
        Capabilities after = Caps();
        if (before.BankGeneration != after.BankGeneration) throw new IOException(L.T("読込中に本体の設定が変わりました。再読込してください。", "The device settings changed while reading. Reload the device."));
        return (after, profiles);
    });
    public Task<DeviceState> ReadDeviceAsync() => Run(() =>
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Capabilities before = Caps(); List<InputField> fields = [];
            bool restart = false;
            for (ushort i = 0; i < before.FieldCount; i++)
            {
                byte[] b;
                try { b = Command(21, [(byte)i, (byte)(i >> 8)]); }
                catch (BridgeCommandException ex) when (ex.Status == 1)
                {
                    if (Caps().ConnectionGeneration == before.ConnectionGeneration) throw;
                    restart = true; break;
                }
                if (Wire.U32(b, 0) != before.ConnectionGeneration || Wire.U16(b, 4) != before.FieldCount) { restart = true; break; }
                fields.Add(InputField.Read(b));
            }
            Capabilities after = Caps();
            if (!restart && after.ConnectionGeneration == before.ConnectionGeneration && after.FieldCount == before.FieldCount && after.DescriptorHash == before.DescriptorHash)
                return new DeviceState(after, fields, before.EventHead);
        }
        throw new IOException(L.T("入力コントローラーの接続が安定していません。接続後に再試行してください。", "The input controller connection is unstable. Try again after it connects."));
    });
    public Task<PollResult> PollAsync(uint cursor, uint generation) => Run(() =>
    {
        Capabilities caps = Caps(); List<InputChange> changes = [];
        if (caps.ConnectionGeneration != generation) return new PollResult(caps, cursor, changes, true);
        for (int batch = 0; batch < 32 && cursor != caps.EventHead; batch++)
        {
            byte[] args = new byte[4]; Wire.W32(args, 0, cursor); byte[] b = Command(22, args);
            if (Wire.U32(b, 4) != generation) return new PollResult(caps, cursor, [], true);
            uint next = Wire.U32(b, 0);
            for (int i = 0; i < 4; i++)
            {
                int p = 8 + 12 * i; ushort flags = Wire.U16(b, p + 2);
                if ((flags & 1) != 0) changes.Add(new(Wire.U16(b, p), Wire.I32(b, p + 4), Wire.U32(b, p + 8), (flags & 2) != 0));
            }
            if (next == cursor) break;
            cursor = next;
            // The ring can advance while reading; after catching the original
            // head, a new timer tick gets a fresh bounded snapshot.
            if (unchecked((int)(cursor - caps.EventHead)) >= 0) break;
        }
        return new PollResult(caps, cursor, changes, false);
    });
    public Task<uint> SaveAsync(int slot, DeviceProfile profile, uint expectedGeneration) => Run(() =>
    {
        Capabilities before = Caps();
        if ((profile.Version >= 2 || profile.HasRapidFire) && !before.SupportsRapidFire)
            throw new InvalidDataException(L.T("連射設定にはファームウェア0.6.0-dev以降が必要です。先に本体を更新してください。", "Rapid fire requires firmware 0.6.0-dev or later. Update the device first."));
        if (profile.ProfileId != Guid.Empty && !before.SupportsProfileIdentity)
            throw new InvalidDataException(L.T("ゲーム別プロファイルにはファームウェア0.7.0-dev以降が必要です。先に本体を更新してください。", "Per-game profiles require firmware 0.7.0-dev or later. Update the device first."));
        if (slot < 0 || slot >= DeviceProfile.SlotCount || !before.SupportsMouseProfiles) throw new InvalidDataException(L.T("16スロットと個別感度にはファームウェア0.10.0以降が必要です。", "16 slots and per-profile gain require firmware 0.10.0 or later."));
        if (profile.Mouse.TrackballGain > 10 && !before.SupportsExtendedTrackballGain)
            throw new InvalidDataException(L.T("10.0倍を超えるトラックボール感度にはファームウェア0.13.0以降が必要です。", "Trackball gain above 10.0 requires firmware 0.13.0 or later."));
        byte[] data = profile.Encode(), begin = new byte[before.SupportsProfileIdentity ? 26 : 9]; begin[0] = checked((byte)slot);
        if (before.SupportsProfileIdentity) { begin[9] = 1; (profile.Enabled ? profile.ProfileId : Guid.Empty).TryWriteBytes(begin.AsSpan(10)); }
        Wire.W32(begin, 1, Wire.Crc(data)); Wire.W32(begin, 5, expectedGeneration); Command(24, begin);
        for (int off = 0; off < data.Length; off += 52)
        {
            int n = Math.Min(52, data.Length - off); byte[] chunk = new byte[n + 4]; Wire.W16(chunk, 0, (ushort)off); chunk[2] = (byte)n;
            data.AsSpan(off, n).CopyTo(chunk.AsSpan(4)); Command(25, chunk);
        }
        uint generation = Wire.U32(Command(26), 0);
        Capabilities after = Caps();
        if (after.BankGeneration != generation || !ReadProfile(slot, after).SameContent(profile))
            throw new IOException(L.T("保存後の設定・IDの照合に失敗しました。再読込してください。", "The saved settings or ID could not be verified. Reload the device."));
        ApplyProfileOutput(generation);
        return generation;
    });
    internal Task<byte[]> RawAsync(byte command, byte[]? payload = null) => Run(() => Command(command, payload));
    public Task<uint> SelectProfileAsync(int slot, uint expectedGeneration) => Run(() =>
    {
        if (!Caps().SupportsOnBoardVariants) throw new InvalidDataException(L.T("本体でのプロファイル切替にはFW 0.8.0以降が必要です。", "On-device profile switching requires firmware 0.8.0 or later."));
        byte[] args = new byte[5]; args[0] = checked((byte)slot); Wire.W32(args, 1, expectedGeneration);
        uint generation = Wire.U32(Command(33, args), 0); Capabilities after = Caps();
        if (after.BankGeneration != generation || after.ActiveSlot != slot || after.SelectionUnsaved)
            throw new IOException(L.T("使用するプロファイルの照合に失敗しました。再読込してください。", "The active profile could not be verified. Reload the device."));
        ApplyProfileOutput(generation);
        return generation;
    });
    public Task<uint> ReadSwitchKeysAsync(int slot) => Run(() => Wire.U32(Command(35, [0, checked((byte)slot)]), 0));
    public Task<uint> SaveSwitchKeysAsync(int slot, uint keys, uint expectedGeneration) => Run(() =>
    {
        byte[] args = new byte[10]; args[0] = 1; args[1] = checked((byte)slot); Wire.W32(args, 2, keys); Wire.W32(args, 6, expectedGeneration);
        byte[] reply = Command(35, args); uint generation = Wire.U32(reply, 4);
        byte[] check = Command(35, [0, (byte)slot]);
        if (Wire.U32(check, 0) != keys || Wire.U32(check, 4) != generation) throw new IOException(L.T("切替方法の保存照合に失敗しました。", "The saved switching method could not be verified."));
        return generation;
    });
    public void Dispose() => handle.Dispose();

    private SlotBank ReadBank()
    {
        if (!Caps().SupportsBankTransfer) throw new InvalidDataException(L.T("スロットのバックアップ・復帰・並べ替えにはファームウェア0.14.0以降が必要です。", "Slot backup, restore and reordering require firmware 0.14.0 or later."));
        byte[] info = Command(40);
        if (!info.AsSpan(0, 4).SequenceEqual("UPB1"u8) || Wire.U32(info, 4) != SlotBank.ByteCount) throw new InvalidDataException("Slot bank ABI");
        uint generation = Wire.U32(info, 8); byte[] data = new byte[SlotBank.ByteCount];
        for (int off = 0; off < data.Length; off += 56)
        {
            byte[] args = new byte[6]; Wire.W16(args, 0, (ushort)off); Wire.W32(args, 2, generation);
            Command(41, args).AsSpan(0, Math.Min(56, data.Length - off)).CopyTo(data.AsSpan(off));
        }
        if (Caps().BankGeneration != generation || Wire.U32(data, 8) != generation || Wire.Crc(data) != Wire.U32(info, 12))
            throw new IOException(L.T("読込中にスロット設定が変わりました。再読込してください。", "The slots changed while reading. Reload the device and try again."));
        return new SlotBank(data);
    }
    internal Task<SlotBank> ReadBankAsync() => Run(ReadBank);
    internal Task<SlotBank> WriteBankAsync(SlotBank bank, uint expectedGeneration) => Run(() =>
    {
        Capabilities before = Caps();
        if (!before.SupportsBankTransfer || before.BankGeneration != expectedGeneration)
            throw new IOException(L.T("本体のスロット設定を再読込してから保存してください。", "Reload the device's slots before saving."));
        byte[] data = bank.Encode(), begin = new byte[12];
        Wire.W32(begin, 0, expectedGeneration); Wire.W32(begin, 4, Wire.Crc(data)); Wire.W32(begin, 8, (uint)data.Length);
        bool committed = false;
        try
        {
            Command(42, begin);
            for (int off = 0; off < data.Length; off += 52)
            {
                int count = Math.Min(52, data.Length - off); byte[] chunk = new byte[count + 4];
                Wire.W16(chunk, 0, (ushort)off); chunk[2] = (byte)count; data.AsSpan(off, count).CopyTo(chunk.AsSpan(4)); Command(43, chunk);
            }
            byte[] reply = Command(44); committed = true;
            SlotBank actual = ReadBank(), expected = bank.WithGeneration(unchecked(expectedGeneration + 1));
            if (Wire.U32(reply, 0) != actual.Generation || Wire.U32(reply, 4) != actual.Crc || !actual.Encode().SequenceEqual(expected.Encode()))
                throw new IOException(L.T("保存後の全スロット照合に失敗しました。本体から再読込してください。", "The saved slots could not be verified. Reload the device."));
            ApplyProfileOutput(actual.Generation);
            return actual;
        }
        finally
        {
            if (!committed) try { Command(45); } catch (Exception) { /* A reconnect can begin a new upload safely. */ }
        }
    });
}
