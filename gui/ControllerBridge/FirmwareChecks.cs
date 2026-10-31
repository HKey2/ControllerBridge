using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ControllerBridge;

internal static class FirmwareChecks
{
    internal sealed class FakeDevice(string? initial, string installed) : IFirmwareDevice
    {
        internal readonly List<string> Calls = [];
        internal Exception? Failure;
        internal string FailureAt = "";
        internal byte[]? Written;
        internal Func<CancellationToken, Task>? OnPrepare;
        internal Func<Task>? OnWrite;
        private void Call(string value) { Calls.Add(value); if (FailureAt == value) throw Failure ?? new IOException(value); }
        public Task<FirmwareInfo?> ProbeAsync() { Call("probe"); return Task.FromResult(initial is null ? null : new FirmwareInfo(initial)); }
        public async Task PrepareAsync(FirmwareInfo? original, CancellationToken cancellationToken = default)
        {
            Call(original is null ? "manual" : "boot");
            cancellationToken.ThrowIfCancellationRequested();
            if (OnPrepare is not null) await OnPrepare(cancellationToken);
        }
        public async Task WriteAsync(byte[] image, Action<int> progress) { Call("write"); Written = image; progress(50); if (OnWrite is not null) await OnWrite(); progress(100); }
        public Task<FirmwareInfo> VerifyAsync() { Call("verify"); return Task.FromResult(new FirmwareInfo(installed)); }
        public void Dispose() { }
    }

    internal static void Run(string directory)
    {
        Directory.CreateDirectory(directory); List<string> checks = [];
        void Check(bool condition, string description) { if (!condition) throw new InvalidDataException(description); checks.Add(description); }
        void Reject(Action action, string description)
        {
            bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, description);
        }
        FirmwarePackage package = FirmwarePackage.Load();
        Check(package.Image.Length > 512 && package.Version == typeof(MainForm).Assembly.GetName().Version?.ToString(3), "embedded UF2 checksum, block layout and version match the application");
        byte[] response = new byte[56]; "CBV1"u8.CopyTo(response); response[4] = response[5] = 1;
        Encoding.ASCII.GetBytes(package.Version).CopyTo(response, 8); "ControllerBridge"u8.CopyTo(response.AsSpan(32));
        Check(FirmwareInfo.Read(response)?.Version == package.Version, "normal firmware identity decodes");
        foreach (int offset in new[] { 0, 4, 5, 8, 32 })
        {
            byte[] invalid = (byte[])response.Clone(); invalid[offset] = 0xff;
            Check(FirmwareInfo.Read(invalid) is null, $"unrelated or malformed identity rejected at offset {offset}");
        }
        Check(FirmwareInfo.Read(response[..55]) is null && FirmwareInfo.Read(new byte[56]) is null, "short and old responses cannot match a firmware version");
        byte[] unclosed = (byte[])response.Clone(); Array.Fill(unclosed, (byte)'1', 8, 24);
        Check(FirmwareInfo.Read(unclosed) is null, "unterminated version rejected");
        Reject(() => FirmwarePackage.Validate(package.Image, new string('0', 64)), "package hash mismatch rejected before USB access");
        foreach (int offset in new[] { 0, 4, 8, 12, 16, 20, 24, 28, 508, 512 + 12 })
        {
            byte[] invalid = (byte[])package.Image.Clone(); invalid[offset] ^= 1;
            Reject(() => FirmwarePackage.Validate(invalid, Convert.ToHexString(SHA256.HashData(invalid))), $"UF2 structural error rejected at offset {offset} even with matching checksum");
        }
        foreach (byte[] invalid in new[] { Array.Empty<byte>(), package.Image[..^1] })
            Reject(() => FirmwarePackage.Validate(invalid, Convert.ToHexString(SHA256.HashData(invalid))), "empty or incomplete UF2 rejected");
        string info = "UF2 Bootloader v1.1\nModel: Raspberry Pi RP2350\nBoard-ID: RP2350\n";
        Check(FirmwareDevice.IsRp2350Info(info) && FirmwareDevice.IsRp2350Info(info.Replace("\n", "\r\n")), "RP2350 ROM INFO_UF2 accepts LF and CRLF");
        Check(!FirmwareDevice.IsRp2350Info(info.Replace("Board-ID: RP2350", "Board-ID: RPI-RP2")) && !FirmwareDevice.IsRp2350Info("RP2350"), "RP2040 and arbitrary text do not select a drive");
        UsbDeviceLocation usb = new(@"USB\VID_2E8A&PID_000F\TEST", "USBROOT(0)#USB(1)");
        BootDrive drive = new(@"E:\", info, usb);
        Check(usb.IsRp2350Boot && !(usb with { InstanceId = @"USB\VID_2E8A&PID_0003\TEST" }).IsRp2350Boot, "only RP2350 BOOT USB identity accepted");
        UsbDeviceLocation factory = new(@"USB\VID_2E8A&PID_0009\FACTORY", usb.Location);
        UsbDeviceLocation unrelated = new(@"USB\VID_046D&PID_C539\MOUSE", usb.Location);
        Check(factory.IsPicoSdkUsb && !factory.IsRp2350Boot, "factory Pico SDK serial USB is recognized without treating it as a programming disk");
        Check(UsbDeviceLocation.SelectUnconfigured([factory, usb, factory, unrelated]).Count == 2,
            "factory and RP2350 BOOT candidates are deduplicated and unrelated USB devices are ignored");
        foreach (string id in new[] { @"USB\VID_2E8A&PID_0003\RP2040", @"USB\VID_2E8A&PID_0009&MI_00\CDC", @"USB\VID_2E8A&PID_00090\OTHER", @"USB\VID_CAFE&PID_4004\OTHER" })
            Check(UsbDeviceLocation.SelectUnconfigured([factory with { InstanceId = id }]).Count == 0,
                "candidate matching rejects unsupported identities and composite child interfaces: " + id);
        Check(ConnectionRead.WithoutBridge([]).Kind == ConnectionKind.NotDetected && ConnectionRead.WithoutBridge([unrelated]).Kind == ConnectionKind.NotDetected,
            "no board or unrelated USB hardware stays disconnected without firmware guidance");
        Check(ConnectionRead.WithoutBridge([factory]).Kind == ConnectionKind.UnknownFirmware && ConnectionRead.WithoutBridge([usb]).Kind == ConnectionKind.UnknownFirmware,
            "factory and BOOT devices without bridge HID are unsupported, not disconnected");
        Check(ConnectionRead.WithoutBridge([factory, usb]).NoticeKey == ConnectionRead.WithoutBridge([usb, factory, factory]).NoticeKey,
            "candidate ordering and repeated interfaces do not retrigger guidance");
        Check(ConnectionRead.WithoutBridge([factory]).NoticeKey == ConnectionRead.WithoutBridge([factory with { InstanceId = factory.InstanceId.ToLowerInvariant(), Location = factory.Location.ToLowerInvariant() }]).NoticeKey,
            "Windows USB identifier casing does not retrigger guidance");
        Check(FirmwareDevice.SingleUnconfigured([]) is null && FirmwareDevice.SingleUnconfigured([factory]) == factory,
            "the updater remembers one factory board's USB location");
        bool multipleFactory = false;
        try { FirmwareDevice.SingleUnconfigured([factory, usb]); } catch (IOException) { multipleFactory = true; }
        Check(multipleFactory, "multiple unconfigured boards cannot silently select a programming target");
        FirmwareDevice.CheckBootLocation(factory, drive);
        FirmwareDevice.CheckBootLocation(factory with { Location = factory.Location.ToLowerInvariant() }, drive);
        FirmwareDevice.CheckBootLocation(null, drive);
        Check(true, "factory-to-BOOT keeps the USB port; manual discovery without a prior target remains available");
        bool changedPort = false;
        try { FirmwareDevice.CheckBootLocation(factory, drive with { Usb = usb with { Location = "USBROOT(0)#USB(2)" } }); } catch (IOException) { changedPort = true; }
        Check(changedPort, "factory installation rejects a BOOT disk on another USB port");
        Check(FirmwareDevice.SingleDrive([]) is null && FirmwareDevice.SingleDrive([drive]) == drive, "zero or one BOOT drive handled");
        bool ambiguous = false; try { FirmwareDevice.SingleDrive([drive, drive with { Root = @"F:\" }]); } catch (IOException) { ambiguous = true; }
        Check(ambiguous, "multiple BOOT drives rejected");

        FirmwareResult Run(FakeDevice fake, bool[] answers, out List<FirmwarePrompt> prompts, out List<FirmwareStage> stages)
        {
            List<FirmwarePrompt> p = []; List<FirmwareStage> s = []; int next = 0;
            FirmwareResult result = FirmwareWorkflow.RunAsync(package, fake, prompt =>
            {
                p.Add(prompt);
                if (prompt == FirmwarePrompt.ConfirmOverwrite)
                    Check(fake.Calls.SequenceEqual(["probe", "manual"]) && s.Last() == FirmwareStage.WaitingForBootsel,
                        "final overwrite confirmation opens only after BOOT preparation completes");
                return Task.FromResult(answers[next++]);
            }, (stage, _) => s.Add(stage)).GetAwaiter().GetResult();
            prompts = p; stages = s; return result;
        }
        using FakeDevice same = new(package.Version, package.Version);
        Check(Run(same, [], out var prompts, out var stages) == FirmwareResult.AlreadyCurrent && prompts.Count == 0 && same.Calls.SequenceEqual(["probe"]), "3-a same version finishes without confirmation, reboot or write");
        foreach (string different in new[] { "0.1.0", "99.0.0", package.Version + "-dev" })
        {
            using FakeDevice fake = new(different, package.Version);
            Check(Run(fake, [true], out prompts, out stages) == FirmwareResult.Updated && prompts.SequenceEqual([FirmwarePrompt.Replace]) && fake.Calls.SequenceEqual(["probe", "boot", "write", "verify"]) && ReferenceEquals(fake.Written, package.Image), $"3-b exact mismatch {different} installs bundled bytes without ordering versions");
            Check(stages.Last() == FirmwareStage.Complete && stages.IndexOf(FirmwareStage.Reconnecting) < stages.IndexOf(FirmwareStage.Complete), "completion occurs only after reconnect and version verification");
        }
        using FakeDevice decline = new("0.1.0", package.Version);
        Check(Run(decline, [false], out prompts, out stages) == FirmwareResult.Cancelled && decline.Calls.SequenceEqual(["probe"]), "3-b No ends with no device mutation");
        using FakeDevice manual = new(null, package.Version);
        Check(Run(manual, [true], out prompts, out stages) == FirmwareResult.Updated && prompts.SequenceEqual([FirmwarePrompt.ConfirmOverwrite]) && manual.Calls.SequenceEqual(["probe", "manual", "write", "verify"]),
            "manual route waits for physical BOOT, then asks one final confirmation without issuing a software reboot");
        using FakeDevice finalDecline = new(null, package.Version);
        Check(Run(finalDecline, [false], out prompts, out stages) == FirmwareResult.Cancelled && finalDecline.Calls.SequenceEqual(["probe", "manual"]) && finalDecline.Written is null,
            "No at the final confirmation leaves the detected BOOT device unwritten");
        foreach (bool detectedAtCancel in new[] { false, true })
        {
            using CancellationTokenSource cancellation = new();
            using FakeDevice fake = new(null, package.Version);
            fake.OnPrepare = token => { cancellation.Cancel(); return detectedAtCancel ? Task.CompletedTask : Task.Delay(Timeout.Infinite, token); };
            int confirmations = 0; List<FirmwareStage> progress = [];
            FirmwareResult result = FirmwareWorkflow.RunAsync(package, fake,
                _ => { confirmations++; return Task.FromResult(true); }, (stage, _) => progress.Add(stage), cancellation.Token).GetAwaiter().GetResult();
            Check(result == FirmwareResult.Cancelled && confirmations == 0 && fake.Calls.SequenceEqual(["probe", "manual"]) && fake.Written is null && !progress.Contains(FirmwareStage.Writing),
                detectedAtCancel ? "cancellation at BOOT detection cannot advance to confirmation or writing" : "cancellation during physical reconnection wait exits without confirmation or writing");
        }
        using (FakeDevice failedWait = new(null, package.Version) { FailureAt = "manual", Failure = new IOException("simulated BOOT identity failure") })
        {
            bool rejectedWait = false; int confirmations = 0;
            try { FirmwareWorkflow.RunAsync(package, failedWait, _ => { confirmations++; return Task.FromResult(true); }, (_, _) => { }).GetAwaiter().GetResult(); }
            catch (IOException) { rejectedWait = true; }
            Check(rejectedWait && confirmations == 0 && failedWait.Written is null && failedWait.Calls.SequenceEqual(["probe", "manual"]),
                "failed BOOT validation never opens the final confirmation or writes");
        }
        foreach (string stage in new[] { "probe", "boot", "write", "verify" })
        {
            using FakeDevice fake = new("0.1.0", package.Version) { FailureAt = stage, Failure = new TimeoutException("simulated " + stage) };
            List<FirmwareStage> progress = []; bool failed = false;
            try { FirmwareWorkflow.RunAsync(package, fake, _ => Task.FromResult(true), (s, _) => progress.Add(s)).GetAwaiter().GetResult(); }
            catch (TimeoutException) { failed = true; }
            Check(failed && !progress.Contains(FirmwareStage.Complete) && fake.Calls.Last() == stage, $"failure in {stage} is not reported as success or retried");
        }
        using FakeDevice wrong = new("0.1.0", "0.2.0");
        bool mismatch = false; List<FirmwareStage> wrongProgress = [];
        try { FirmwareWorkflow.RunAsync(package, wrong, _ => Task.FromResult(true), (s, _) => wrongProgress.Add(s)).GetAwaiter().GetResult(); }
        catch (IOException) { mismatch = true; }
        Check(mismatch && !wrongProgress.Contains(FirmwareStage.Complete), "wrong version after writing fails verification");
        FirmwareUpdateDialog.RunChecks(directory, package, Check);
        File.WriteAllText(Path.Combine(directory, "firmware-checks.json"), JsonSerializer.Serialize(new { passed = true, count = checks.Count, version = package.Version, physicalDeviceWritten = false, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
