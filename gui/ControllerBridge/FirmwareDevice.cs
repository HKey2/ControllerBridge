using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace ControllerBridge;

internal sealed record BootDrive(string Root, string Info, UsbDeviceLocation Usb);

internal sealed class FirmwareDevice : IFirmwareDevice
{
    private BootDrive? target;
    private string? probePath;
    private UsbDeviceLocation? probeUsb;
    internal Exception? TransferWarning { get; private set; }

    public async Task<FirmwareInfo?> ProbeAsync()
    {
        try
        {
            using HidBridge device = await HidBridge.ConnectAsync();
            probePath = device.DevicePath;
            probeUsb = UsbDeviceLocation.ForHid(device.DevicePath);
            return await device.ReadFirmwareAsync();
        }
        catch (BridgeDiscoveryException ex) when (ex.Matches == 0) { return null; }
    }

    internal static bool IsRp2350Info(string text) => text.Length <= 4096 &&
        Regex.IsMatch(text, @"^UF2 Bootloader\b", RegexOptions.CultureInvariant) &&
        Regex.IsMatch(text, @"^Board-ID:\s*RP2350\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    internal static BootDrive? SingleDrive(IReadOnlyList<BootDrive> drives)
    {
        if (drives.Count > 1) throw new IOException(L.T("BOOT接続のRP2350が複数あります。更新するRP2350-USB-Aを1台だけ接続してください。", "Multiple RP2350 boards are in BOOT mode. Connect only the RP2350-USB-A to be updated."));
        return drives.Count == 1 ? drives[0] : null;
    }

    private static Task<List<BootDrive>> DrivesAsync() => Task.Run(() =>
    {
        List<BootDrive> drives = [];
        foreach (DriveInfo drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.DriveType is not (DriveType.Removable or DriveType.Fixed) || !drive.IsReady) continue;
                string path = Path.Combine(drive.RootDirectory.FullName, "INFO_UF2.TXT");
                FileInfo file = new(path);
                if (!file.Exists || file.Length > 4096) continue;
                string info = File.ReadAllText(path);
                if (IsRp2350Info(info) && UsbDeviceLocation.ForDrive(drive.RootDirectory.FullName) is { IsRp2350Boot: true } usb)
                    drives.Add(new(drive.RootDirectory.FullName, info, usb));
            }
            catch (IOException) { /* A USB volume can disappear during enumeration. */ }
            catch (UnauthorizedAccessException) { }
        }
        return drives;
    });

    public async Task PrepareAsync(FirmwareInfo? original)
    {
        if (original is not null)
        {
            if ((await DrivesAsync()).Count != 0)
                throw new IOException(L.T("別のRP2350がBOOT接続されています。更新する基板以外を取り外して、もう一度実行してください。", "Another RP2350 is in BOOT mode. Disconnect other boards and try again."));
            using HidBridge device = await HidBridge.ConnectAsync();
            FirmwareInfo? now = FirmwareInfo.Read(await device.RawAsync(47));
            if (probeUsb is null || device.DevicePath != probePath || now != original || UsbDeviceLocation.ForHid(device.DevicePath) != probeUsb)
                throw new IOException(L.T("確認中に接続先が変わりました。もう一度実行してください。", "The connected device changed during confirmation. Try again."));
            await device.RawAsync(9);
        }
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(30))
        {
            target = SingleDrive(await DrivesAsync());
            if (target is not null)
            {
                if (original is not null && target.Usb.Location != probeUsb?.Location)
                    throw new IOException(L.T("別のUSBポートでBOOT接続を検出しました。同じポートに接続したまま実行してください。", "BOOT mode appeared on a different USB port. Keep the board connected to the same port."));
                return;
            }
            await Task.Delay(250);
        }
        throw new TimeoutException(L.T("BOOT接続のRP2350が見つかりません。BOOTボタンを押しながらTYPE-C側を接続し直し、もう一度実行してください。", "No RP2350 was found in BOOT mode. Reconnect its TYPE-C port while holding BOOT, then try again."));
    }

    public async Task WriteAsync(byte[] image, Action<int> progress)
    {
        BootDrive? current = SingleDrive(await DrivesAsync());
        if (target is null || current != target)
            throw new IOException(L.T("書き込み先の接続が変わりました。もう一度実行してください。", "The programming drive changed. Try again."));
        await Task.Run(() =>
        {
            int sent = 0, attempted = 0;
            try
            {
                // The ROM may remove the disk as soon as its final block arrives.
                using FileStream output = new(Path.Combine(target.Root, "ControllerBridge.uf2"), FileMode.Create,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
                while (sent < image.Length)
                {
                    int count = Math.Min(4096, image.Length - sent);
                    attempted = sent + count;
                    output.Write(image, sent, count); sent += count;
                    progress(sent * 100 / image.Length);
                }
                output.Flush(true);
            }
            catch (IOException ex) when (attempted == image.Length)
            {
                // A disconnect is never itself success. Verification must observe
                // the programmed application and match its exact bundled version.
                TransferWarning = ex;
            }
            catch (IOException ex)
            {
                throw new IOException(L.T("書き込みが途中で中断されました。USBケーブルを確認し、BOOTボタンを押しながら接続して更新をやり直してください。", "Writing was interrupted. Check the USB cable, reconnect while holding BOOT, and run the update again."), ex);
            }
        });
    }

    public async Task<FirmwareInfo> VerifyAsync()
    {
        Stopwatch watch = Stopwatch.StartNew();
        while (watch.Elapsed < TimeSpan.FromSeconds(45))
        {
            // Do not accept a second board while the target remains in BOOT mode.
            if ((await DrivesAsync()).Count == 0)
            {
                try
                {
                    using HidBridge device = await HidBridge.ConnectAsync();
                    FirmwareInfo? info = FirmwareInfo.Read(await device.RawAsync(47));
                    if (info is not null && UsbDeviceLocation.ForHid(device.DevicePath)?.Location == target?.Usb.Location) return info;
                }
                catch (BridgeDiscoveryException ex) when (ex.Matches == 0) { }
                catch (BridgeCommandException ex) when (ex.Status == 1) { }
                catch (Win32Exception) { }
                catch (TimeoutException) { }
            }
            await Task.Delay(350);
        }
        throw new TimeoutException(L.T("書き込み後の起動を確認できませんでした。USBケーブルを確認してください。再接続しても認識されない場合は、BOOTボタンを押しながら接続し、ファームウェア更新をやり直してください。", "The device did not restart with identifiable firmware. Check the USB cable. If reconnecting does not help, connect while holding BOOT and run Firmware update again."), TransferWarning);
    }
    public void Dispose() { }

    internal static async Task InspectAsync(string path)
    {
        // Read-only diagnostics: no reboot, write, profile save or input generation.
        using FirmwareDevice device = new();
        FirmwareInfo? info = await device.ProbeAsync();
        List<BootDrive> drives = await DrivesAsync();
        File.WriteAllText(path, System.Text.Json.JsonSerializer.Serialize(new
        {
            bundledVersion = FirmwarePackage.Load().Version, firmware = info,
            connectedBridge = device.probePath is not null, usb = device.probeUsb,
            bootDrives = drives, physicalDeviceWritten = false
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
