using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ControllerBridge;

// Keep the exact Flash payload so a backup or reorder preserves even bytes the
// editor does not change. Device generation is assigned afresh when committing.
internal sealed class SlotBank
{
    public const int ByteCount = 13908;
    public const int LegacyByteCount = 8148;
    private const int ProfilesAt = 16;
    internal const int IdsAt = ProfilesAt + DeviceProfile.SlotCount * DeviceProfile.ByteCount;
    private const int PreferredAt = IdsAt + 256, KeysAt = PreferredAt + 4;
    private readonly byte[] bytes;
    public uint Generation => Wire.U32(bytes, 8);
    public uint PreferredSlots => Wire.U32(bytes, PreferredAt);
    public uint Crc => Wire.Crc(bytes);
    public DeviceProfile[] Profiles { get; }
    public bool LegacyConversionApplied { get; }
    public SlotBank(byte[] data, MouseUdlrSettings? legacyDefaults = null)
    {
        LegacyConversionApplied = data.Length >= 16 && Wire.U32(data, 4) is 5 or 6;
        if (data.Length == LegacyByteCount && Wire.U32(data, 0) == 0x31425055 && Wire.U32(data, 4) == 5 && Wire.U32(data, 12) == LegacyByteCount)
        {
            byte[] migrated = new byte[ByteCount]; data.AsSpan(0, 16).CopyTo(migrated);
            for (int i = 0; i < DeviceProfile.SlotCount; i++)
                DeviceProfile.Decode(data.AsSpan(16 + i * DeviceProfile.LegacyByteCount, DeviceProfile.LegacyByteCount).ToArray(), legacyDefaults).Encode().CopyTo(migrated, 16 + i * DeviceProfile.ByteCount);
            data.AsSpan(7824, 324).CopyTo(migrated.AsSpan(IdsAt));
            Wire.W32(migrated, 4, 8); Wire.W32(migrated, 12, ByteCount); data = migrated;
        }
        if (data.Length == ByteCount && Wire.U32(data, 0) == 0x31425055 && Wire.U32(data, 4) == 6 && Wire.U32(data, 12) == ByteCount)
        {
            byte[] migrated = (byte[])data.Clone();
            for (int i = 0; i < DeviceProfile.SlotCount; i++)
            {
                if (Wire.U16(data, ProfilesAt + i * DeviceProfile.ByteCount) != Wire.U32(data, 4)) throw Invalid();
                DeviceProfile.Decode(data.AsSpan(ProfilesAt + i * DeviceProfile.ByteCount, DeviceProfile.ByteCount).ToArray(), legacyDefaults).Encode().CopyTo(migrated, ProfilesAt + i * DeviceProfile.ByteCount);
            }
            Wire.W32(migrated, 4, 8); data = migrated;
        }
        if (data.Length != ByteCount || Wire.U32(data, 0) != 0x31425055 || Wire.U32(data, 4) != 8 || Wire.U32(data, 12) != ByteCount)
            throw new InvalidDataException(L.T("未対応のスロット保存形式です。", "This slot backup format is not supported."));
        bytes = (byte[])data.Clone();
        Profiles = Enumerable.Range(0, DeviceProfile.SlotCount).Select(i =>
        {
            if (Wire.U16(bytes, ProfilesAt + i * DeviceProfile.ByteCount) != 8) throw Invalid();
            DeviceProfile p = DeviceProfile.Decode(bytes.AsSpan(ProfilesAt + i * DeviceProfile.ByteCount, DeviceProfile.ByteCount).ToArray());
            p.ProfileId = new Guid(bytes.AsSpan(IdsAt + i * 16, 16)); return p;
        }).ToArray();
        Validate();
    }
    private void Validate()
    {
        if ((PreferredSlots & ~0xffffu) != 0) throw Invalid();
        HashSet<Guid> ids = [];
        for (int i = 0; i < Profiles.Length; i++)
        {
            DeviceProfile p = Profiles[i]; uint keys = SwitchKeys(i);
            p.Shift.ValidateSwitchKeys(p, keys);
            if (!p.Enabled && (p.ProfileId != Guid.Empty || keys != 0 || (PreferredSlots & (1u << i)) != 0)) throw Invalid();
            if (p.ProfileId != Guid.Empty && !ids.Add(p.ProfileId)) throw Invalid();
            if (keys != 0 && ((keys & ~0x3ffffu) != 0 || (keys & (keys - 1)) == 0 ||
                (keys & 0x14000u) == 0x14000u || (keys & 0x28000u) == 0x28000u)) throw Invalid();
            for (int j = 0; j < i; j++) if (p.SameController(Profiles[j]))
            {
                if (keys != SwitchKeys(j)) throw Invalid();
                if ((PreferredSlots & (1u << i)) != 0 && (PreferredSlots & (1u << j)) != 0) throw Invalid();
            }
        }
    }
    private static InvalidDataException Invalid() => new(L.T("スロットのID・切替設定が不正です。", "The slot IDs or profile switching settings are invalid."));
    public uint SwitchKeys(int slot) => Wire.U32(bytes, KeysAt + slot * 4);
    public byte[] Encode() => (byte[])bytes.Clone();
    public SlotBank WithGeneration(uint generation)
    {
        byte[] copy = Encode(); Wire.W32(copy, 8, generation); return new(copy);
    }
    public SlotBank Move(int source, int destination)
    {
        if (source is < 0 or >= DeviceProfile.SlotCount || destination is < 0 or >= DeviceProfile.SlotCount) throw new ArgumentOutOfRangeException();
        List<int> order = Enumerable.Range(0, DeviceProfile.SlotCount).ToList(); order.RemoveAt(source); order.Insert(destination, source);
        byte[] copy = Encode(); uint preferred = 0;
        for (int i = 0; i < order.Count; i++)
        {
            int old = order[i];
            bytes.AsSpan(ProfilesAt + old * DeviceProfile.ByteCount, DeviceProfile.ByteCount).CopyTo(copy.AsSpan(ProfilesAt + i * DeviceProfile.ByteCount));
            bytes.AsSpan(IdsAt + old * 16, 16).CopyTo(copy.AsSpan(IdsAt + i * 16));
            Wire.W32(copy, KeysAt + i * 4, SwitchKeys(old));
            if ((PreferredSlots & (1u << old)) != 0) preferred |= 1u << i;
        }
        Wire.W32(copy, PreferredAt, preferred); return new(copy);
    }
    internal static SlotBank FromProfiles(DeviceProfile[] profiles, uint preferred = 0, uint[]? keys = null)
    {
        if (profiles.Length != DeviceProfile.SlotCount || keys is not null && keys.Length != profiles.Length) throw new ArgumentException("slots");
        byte[] data = new byte[ByteCount]; Wire.W32(data, 0, 0x31425055); Wire.W32(data, 4, 8); Wire.W32(data, 12, ByteCount);
        for (int i = 0; i < profiles.Length; i++)
        {
            profiles[i].Encode().CopyTo(data, ProfilesAt + i * DeviceProfile.ByteCount);
            profiles[i].ProfileId.TryWriteBytes(data.AsSpan(IdsAt + i * 16)); Wire.W32(data, KeysAt + i * 4, keys?[i] ?? 0);
        }
        Wire.W32(data, PreferredAt, preferred); return new(data);
    }
}

internal sealed class SlotBackup
{
    public string Format { get; set; } = "ControllerBridge.Slots";
    public int Version { get; set; } = 5;
    public DateTimeOffset CreatedUtc { get; set; }
    public string Sha256 { get; set; } = "";
    public byte[] Bank { get; set; } = [];
    public static void Save(string path, SlotBank bank)
    {
        byte[] data = bank.Encode();
        string json = JsonSerializer.Serialize(new SlotBackup { CreatedUtc = DateTimeOffset.UtcNow, Bank = data, Sha256 = Convert.ToHexString(SHA256.HashData(data)) }, new JsonSerializerOptions { WriteIndented = true });
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json, new UTF8Encoding(false));
            if (!Load(temporary).Encode().SequenceEqual(data)) throw new IOException(L.T("バックアップの照合に失敗しました。", "The backup could not be verified."));
            if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static SlotBank Load(string path, MouseUdlrSettings? legacyDefaults = null)
    {
        if (new FileInfo(path).Length > 256 * 1024) throw new InvalidDataException(L.T("バックアップファイルが大きすぎます。", "The backup file is too large."));
        SlotBackup file = JsonSerializer.Deserialize<SlotBackup>(File.ReadAllText(path)) ?? throw new InvalidDataException(L.T("バックアップを読み込めません。", "The backup could not be read."));
        if (file.Format != "ControllerBridge.Slots" || file.Version is not (2 or 3 or 5) || file.Bank is null || file.Bank.Length is not (SlotBank.ByteCount or SlotBank.LegacyByteCount) ||
            !string.Equals(file.Sha256, Convert.ToHexString(SHA256.HashData(file.Bank)), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(L.T("バックアップの形式またはチェックサムが不正です。", "The backup format or checksum is invalid."));
        uint bankVersion = Wire.U32(file.Bank, 4);
        if (file.Version == 5 ? bankVersion != 8 : bankVersion is not (5 or 6)) throw new InvalidDataException("Unsupported backup version.");
        return new(file.Bank, legacyDefaults);
    }
}
