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
        internal Func<Task>? OnWrite;
        private void Call(string value) { Calls.Add(value); if (FailureAt == value) throw Failure ?? new IOException(value); }
        public Task<FirmwareInfo?> ProbeAsync() { Call("probe"); return Task.FromResult(initial is null ? null : new FirmwareInfo(initial)); }
        public Task PrepareAsync(FirmwareInfo? original) { Call(original is null ? "manual" : "boot"); return Task.CompletedTask; }
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
        Check(FirmwareDevice.SingleDrive([]) is null && FirmwareDevice.SingleDrive([drive]) == drive, "zero or one BOOT drive handled");
        bool ambiguous = false; try { FirmwareDevice.SingleDrive([drive, drive with { Root = @"F:\" }]); } catch (IOException) { ambiguous = true; }
        Check(ambiguous, "multiple BOOT drives rejected");

        FirmwareResult Run(FakeDevice fake, bool[] answers, out List<FirmwarePrompt> prompts, out List<FirmwareStage> stages)
        {
            List<FirmwarePrompt> p = []; List<FirmwareStage> s = []; int next = 0;
            FirmwareResult result = FirmwareWorkflow.RunAsync(package, fake, prompt => { p.Add(prompt); return Task.FromResult(answers[next++]); }, (stage, _) => s.Add(stage)).GetAwaiter().GetResult();
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
        Check(Run(manual, [true, true], out prompts, out stages) == FirmwareResult.Updated && prompts.SequenceEqual([FirmwarePrompt.ConnectBootsel, FirmwarePrompt.ConfirmOverwrite]) && manual.Calls.SequenceEqual(["probe", "manual", "write", "verify"]), "3-c and 4-b require both confirmations and never issue software reboot");
        foreach (bool[] answers in new[] { new[] { false }, new[] { true, false } })
        {
            using FakeDevice fake = new(null, package.Version);
            Check(Run(fake, answers, out prompts, out stages) == FirmwareResult.Cancelled && fake.Calls.SequenceEqual(["probe"]), "No at either manual confirmation prevents any write");
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
