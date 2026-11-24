using System.Text.Json;

namespace ControllerBridge;

internal static class SharedSettingsFile
{
    internal static string Filter => L.T("ControllerBridge 共通設定 (*.json)|*.json", "ControllerBridge shared settings (*.json)|*.json");
    private sealed record Document
    {
        public required string Format { get; init; }
        public required int Version { get; init; }
        public required uint Threshold { get; init; }
        public required uint HoldMs { get; init; }
        public required uint WaitMs { get; init; }
        public required uint TrackballHoldThreshold { get; init; }
        public required uint InertiaThreshold { get; init; }
        public required uint InertiaDecayPercent { get; init; }
        public required uint InertiaIntervalMs { get; init; }
        public required uint RollingGain10 { get; init; }
        public required uint HostMode { get; init; }
        public required uint DisplayType { get; init; }
    }
    private static void Validate(MouseUdlrSettings settings)
    {
        MouseUdlrSettings.ValidateEdit(settings.Threshold, settings.HoldMs, settings.WaitMs);
        MouseUdlrSettings.ValidateInertia(settings.InertiaThreshold, settings.InertiaDecayPercent, settings.InertiaIntervalMs);
        if (settings.TrackballHoldThreshold is < 1 or > 127 || settings.RollingGain10 is < 1 or > 100 ||
            settings.HostMode > 3 || !DisplayTypes.IsSupported(settings.DisplayType))
            throw Invalid();
    }
    private static InvalidDataException Invalid() => new(L.T("共通設定ファイルの形式または値が不正です。", "The shared settings file has an invalid format or value."));
    internal static void Save(string path, MouseUdlrSettings settings)
    {
        Validate(settings);
        Document document = new() { Format = "ControllerBridge.SharedSettings", Version = 1,
            Threshold = settings.Threshold, HoldMs = settings.HoldMs, WaitMs = settings.WaitMs,
            TrackballHoldThreshold = settings.TrackballHoldThreshold, InertiaThreshold = settings.InertiaThreshold,
            InertiaDecayPercent = settings.InertiaDecayPercent, InertiaIntervalMs = settings.InertiaIntervalMs,
            RollingGain10 = settings.RollingGain10, HostMode = settings.HostMode, DisplayType = settings.DisplayType };
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    internal static MouseUdlrSettings Load(string path, MouseUdlrSettings current, bool supportsRumble)
    {
        if (new FileInfo(path).Length > 64 * 1024) throw Invalid();
        Document document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path)) ?? throw Invalid();
        if (document.Format != "ControllerBridge.SharedSettings" || document.Version != 1 ||
            document.TrackballHoldThreshold is < 1 or > 127 || document.RollingGain10 is < 1 or > 100) throw Invalid();
        if (document.HostMode == 3 && !supportsRumble)
            throw new InvalidDataException(L.T("このデバイスではPro出力を利用できません。", "This device does not support Pro output."));
        MouseUdlrSettings loaded = current with { Threshold = document.Threshold, HoldMs = document.HoldMs, WaitMs = document.WaitMs,
            Trackball = (current.Trackball & ~255u) | document.TrackballHoldThreshold,
            InertiaThreshold = document.InertiaThreshold, InertiaDecayPercent = document.InertiaDecayPercent,
            InertiaIntervalMs = document.InertiaIntervalMs, Rolling = (current.Rolling & ~65535u) | document.RollingGain10,
            HostMode = document.HostMode, DisplayType = document.DisplayType };
        Validate(loaded);
        return loaded;
    }
}
