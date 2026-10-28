using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ControllerBridge;

internal sealed record FirmwareInfo(string Version)
{
    internal static bool ValidVersion(string value) => Regex.IsMatch(value, @"^\d+\.\d+\.\d+(?:-[A-Za-z0-9.]+)?$", RegexOptions.CultureInvariant);
    internal static FirmwareInfo? Read(byte[] data)
    {
        if (data.Length != 56 || !data.AsSpan(0, 4).SequenceEqual("CBV1"u8) || data[4] != 1 || data[5] != 1 ||
            Text(data, 32, 24) != "ControllerBridge") return null;
        string version = Text(data, 8, 24);
        return ValidVersion(version) ? new(version) : null;
    }
    private static string Text(byte[] data, int offset, int count)
    {
        int end = Array.IndexOf(data, (byte)0, offset, count);
        return end < 0 ? "" : Encoding.ASCII.GetString(data, offset, end - offset);
    }
}

internal sealed record FirmwarePackage(string Version, byte[] Image)
{
    internal static FirmwarePackage Load()
    {
        Assembly assembly = typeof(FirmwarePackage).Assembly;
        using Stream manifest = assembly.GetManifestResourceStream("ControllerBridge.Firmware.json") ?? throw Invalid();
        using JsonDocument json = JsonDocument.Parse(manifest);
        JsonElement root = json.RootElement;
        string version = root.GetProperty("version").GetString() ?? "";
        if (root.GetProperty("product").GetString() != "ControllerBridge" || root.GetProperty("board").GetString() != "RP2350-USB-A" ||
            !FirmwareInfo.ValidVersion(version) || version != assembly.GetName().Version?.ToString(3)) throw Invalid();
        using Stream resource = assembly.GetManifestResourceStream("ControllerBridge.Firmware.uf2") ?? throw Invalid();
        using MemoryStream buffer = new(); resource.CopyTo(buffer);
        byte[] image = buffer.ToArray();
        Validate(image, root.GetProperty("sha256").GetString() ?? "");
        return new(version, image);
    }

    internal static void Validate(byte[] image, string sha256)
    {
        if (image.Length == 0 || image.Length % 512 != 0 || image.Length > 4 * 1024 * 1024 ||
            !Convert.ToHexString(SHA256.HashData(image)).Equals(sha256, StringComparison.OrdinalIgnoreCase)) throw Invalid();
        uint count = (uint)(image.Length / 512);
        for (uint index = 0; index < count; index++)
        {
            int offset = checked((int)index * 512);
            uint At(int field) => BitConverter.ToUInt32(image, offset + field);
            // Only the bundled ARM secure Flash image is accepted. Its addresses must
            // be consecutive and remain below the last 32 KiB reserved for settings.
            if (At(0) != 0x0a324655 || At(4) != 0x9e5d5157 || At(508) != 0x0ab16f30 || At(8) != 0x2000 ||
                At(12) != 0x10000000u + index * 256 || At(16) != 256 || At(20) != index || At(24) != count ||
                At(28) != 0xe48bff59 || (ulong)At(12) + 256 > 0x101f8000) throw Invalid();
        }
    }
    private static InvalidDataException Invalid() => new(L.T("同梱ファームウェアの検証に失敗しました。配布ファイルを入手し直してください。", "The bundled firmware failed validation. Obtain a fresh copy of the release."));
}
