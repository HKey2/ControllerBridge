using System.ComponentModel;

namespace ControllerBridge;

internal static class FirmwareDataMigration
{
    // Legacy firmware exposes the loaded bank and whether it came from valid
    // saved storage. Read only that metadata; do not decode through the v8-only
    // editing client or start a bank upload during update discovery.
    private static bool SavedBank(byte[] caps, out ushort profileBytes, out uint generation)
    {
        profileBytes = 0; generation = 0;
        if (caps.Length != 56 || !caps.AsSpan(0, 4).SequenceEqual("UPG1"u8) || Wire.U16(caps, 4) != 1 ||
            caps[8] != 16 || (Wire.U16(caps, 18) & 4) == 0 || (Wire.U32(caps, 44) & 2048) == 0) return false;
        profileBytes = Wire.U16(caps, 6); generation = Wire.U32(caps, 28);
        return profileBytes is 488 or 848;
    }

    internal static async Task<bool> ReadAsync(Func<byte, byte[]?, Task<byte[]>> command)
    {
        try
        {
            byte[] before = await command(20, null);
            if (!SavedBank(before, out ushort profileBytes, out uint generation)) return false;
            // Profile-conversion firmware is current or an unpublished intermediate format.
            // Only published v5/v6 automatic migration is advertised here.
            if ((Wire.U32(before, 44) & 16777216) != 0) return false;
            uint bankBytes = profileBytes == 488 ? 8148u : 13908u;
            uint version = profileBytes == 488 ? 5u : 6u;
            byte[] info = await command(40, null);
            if (info.Length != 56 || !info.AsSpan(0, 4).SequenceEqual("UPB1"u8) ||
                Wire.U32(info, 4) != bankBytes || Wire.U32(info, 8) != generation) return false;
            byte[] args = new byte[6]; Wire.W32(args, 2, generation); // Offset 0, guarded by generation.
            byte[] header = await command(41, args);
            if (header.Length != 56 || !header.AsSpan(0, 4).SequenceEqual("UPB1"u8) ||
                Wire.U32(header, 4) != version || Wire.U32(header, 8) != generation ||
                Wire.U32(header, 12) != bankBytes || Wire.U16(header, 16) != version) return false;
            byte[] after = await command(20, null);
            if (!SavedBank(after, out ushort afterBytes, out uint afterGeneration) ||
                afterBytes != profileBytes || afterGeneration != generation ||
                Wire.U32(after, 44) != Wire.U32(before, 44)) return false;
            byte[] finalInfo = await command(40, null);
            return finalInfo.Length == 56 && info.AsSpan(0, 16).SequenceEqual(finalInfo.AsSpan(0, 16));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or TimeoutException or Win32Exception)
        {
            // Unknown/unsupported metadata must not claim a conversion or block
            // firmware recovery. The update still has its own identity checks.
            return false;
        }
    }
}
