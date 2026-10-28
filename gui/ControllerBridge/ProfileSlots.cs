using System.Text;

namespace ControllerBridge;

internal static class ProfileSlots
{
    public static string Name(IReadOnlyList<DeviceProfile> profiles, int slot)
    {
        DeviceProfile p = profiles[slot]; if (!p.Enabled) return L.T("未使用", "Unused");
        return p.Name;
    }
    public static string DuplicateName(IReadOnlyList<DeviceProfile> profiles, string name)
    {
        int suffix = name.LastIndexOf(" [", StringComparison.Ordinal);
        if (suffix >= 0 && name.EndsWith(']') && int.TryParse(name.AsSpan(suffix + 2, name.Length - suffix - 3), out int oldNumber) && oldNumber >= 2)
            name = name[..suffix];
        for (int number = 2; ; number++)
        {
            string tail = $" [{number}]"; int remaining = 47 - Encoding.UTF8.GetByteCount(tail);
            StringBuilder stem = new();
            foreach (Rune rune in name.EnumerateRunes())
            {
                if (rune.Utf8SequenceLength > remaining) break;
                stem.Append(rune.ToString()); remaining -= rune.Utf8SequenceLength;
            }
            string candidate = stem.ToString().TrimEnd() + tail;
            if (!profiles.Any(p => p.Enabled && string.Equals(p.Name, candidate, StringComparison.OrdinalIgnoreCase))) return candidate;
        }
    }
    public static void Validate(IReadOnlyList<DeviceProfile> saved, int slot, DeviceProfile profile, bool supportsVariants)
    {
        if (!profile.Enabled) return;
        for (int i = 0; i < saved.Count; i++)
        {
            DeviceProfile p = saved[i]; if (i == slot || !p.Enabled) continue;
            if (profile.ProfileId != Guid.Empty && profile.ProfileId == p.ProfileId)
                throw new InvalidDataException(L.T($"このプロファイルはスロット {i + 1} に登録済みです。「プロファイル複製」でコピーしてください。", $"This profile is already in slot {i + 1}. Use “Duplicate profile” to copy it."));
            if (!supportsVariants && profile.SameController(p))
                throw new InvalidDataException(L.T("同じ機器の設定を複数登録するにはFW 0.8.0以降が必要です。", "Multiple profiles for one controller require firmware 0.8.0 or later."));
        }
    }
    public static int EmptyBelow(IReadOnlyList<DeviceProfile> profiles, int source)
    {
        for (int i = source + 1; i < profiles.Count; i++) if (!profiles[i].Enabled) return i;
        return -1;
    }
}
