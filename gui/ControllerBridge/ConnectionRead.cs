namespace ControllerBridge;

internal enum ConnectionKind { Ready, DifferentFirmware, UnknownFirmware, NotDetected }

internal sealed record ConnectionStatus(ConnectionKind Kind, string DeviceKey = "", string? Version = null)
{
    internal string NoticeKey => $"{Kind}:{DeviceKey}:{Version}";
    internal string Summary => Kind switch
    {
        ConnectionKind.DifferentFirmware => L.F("本体 接続済み • ファームウェア v{0} • 更新が必要です", "Device connected • Firmware v{0} • Update required", Version!),
        ConnectionKind.UnknownFirmware => L.T("本体 未対応 • ファームウェアの書き込みが必要です", "Device unsupported • Firmware installation required"),
        _ => L.T("本体 未接続 • USB-CでPCに接続してください。", "Device not connected • Connect USB-C to this PC.")
    };
    internal string Guidance(string bundled) => Kind switch
    {
        ConnectionKind.DifferentFirmware => L.F("本体は v{0}、ツール同梱は v{1} です。本体の設定を読み書きするには同梱版に揃えてください。", "The device has v{0}; this tool includes v{1}. Install the bundled version to read or save device settings.", Version!, bundled),
        ConnectionKind.UnknownFirmware => L.T("本体からファームウェアのバージョンを取得できません。工場出荷状態のRP2350-USB-Aには、以下の手順で本ツール用のファームウェアを書き込んでください。", "The device's firmware version is unavailable. For a factory-state RP2350-USB-A, follow the steps below to install this tool's firmware."),
        _ => L.T("本体は未接続です。USB-CでPCに接続すると、自動的に接続を確認します。", "The device is not connected. Connect USB-C to this PC; the application will detect it automatically.")
    };
}

internal sealed class FirmwareNoticeTracker
{
    private readonly HashSet<string> shown = [];
    internal bool Observe(ConnectionStatus status, bool allowNotice)
    {
        // A confirmed absence rearms guidance for a later attachment. Polling a
        // still-connected unsupported board must not reopen a declined guide.
        if (status.Kind == ConnectionKind.NotDetected) shown.Clear();
        // No device is a normal waiting state, including the very first discovery.
        if (status.Kind is not (ConnectionKind.UnknownFirmware or ConnectionKind.DifferentFirmware)) return false;
        // Remember declined/manual notices until an absence is observed.
        return shown.Add(status.NoticeKey) && allowNotice;
    }
}

// All USB reads finish before the form adopts a connection. An offline editor
// can stay usable while discovery runs, without exposing a half-loaded bridge.
internal sealed class ConnectionRead(ConnectionStatus status) : IDisposable
{
    internal ConnectionStatus Status { get; } = status;
    internal HidBridge? Bridge { get; private set; }
    internal (Capabilities Caps, DeviceProfile[] Profiles) Bank { get; private init; }
    internal DeviceState? Device { get; private init; }
    internal OutputState? Output { get; private init; }
    internal HidBridge? TakeBridge() { HidBridge? value = Bridge; Bridge = null; return value; }
    public void Dispose() => Bridge?.Dispose();

    internal static ConnectionStatus WithoutBridge(IEnumerable<UsbDeviceLocation> devices)
    {
        List<UsbDeviceLocation> candidates = UsbDeviceLocation.SelectUnconfigured(devices);
        return candidates.Count == 0 ? new(ConnectionKind.NotDetected) : new(ConnectionKind.UnknownFirmware,
            string.Join("|", candidates.Select(device => (device.InstanceId + "@" + device.Location).ToUpperInvariant())));
    }

    internal static async Task<ConnectionRead> OpenAsync(string bundled)
    {
        HidBridge? device = null;
        try
        {
            try { device = await HidBridge.ConnectAsync(); }
            catch (BridgeDiscoveryException ex) when (ex.Matches == 0)
            {
                return new(WithoutBridge(await Task.Run(UsbDeviceLocation.FindUnconfigured)));
            }
            FirmwareInfo? firmware = await device.ReadFirmwareAsync();
            if (firmware is null) return new(new(ConnectionKind.UnknownFirmware, device.DevicePath));
            if (firmware.Version != bundled) return new(new(ConnectionKind.DifferentFirmware, device.DevicePath, firmware.Version));
            var bank = await device.ReadProfilesAsync();
            DeviceState state = await device.ReadDeviceAsync();
            OutputState? output = state.Caps.SupportsMouseOutput ? await device.ReadOutputStateAsync() : null;
            ConnectionRead result = new(new(ConnectionKind.Ready, device.DevicePath, firmware.Version))
                { Bridge = device, Bank = bank, Device = state, Output = output };
            device = null;
            return result;
        }
        finally { device?.Dispose(); }
    }
}
