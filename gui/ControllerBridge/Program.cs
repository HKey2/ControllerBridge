using System.Text.Json;

namespace ControllerBridge;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        L.Initialize(ref args);
        ApplicationConfiguration.Initialize();
        // .NET 9's native dark control support also covers title bars, menus,
        // scrollbars and dialogs. Explicit Fluent colors are supplied by Ui.
#pragma warning disable WFO5001
        Application.SetColorMode(SystemColorMode.Dark);
#pragma warning restore WFO5001
        using Mutex mutex = new(true, @"Local\ControllerBridge.Configurator", out bool first);
        if (!first) { MessageBox.Show(L.T("ControllerBridgeは既に起動しています。先に開いた画面を使用してください。", "ControllerBridge is already running. Use its existing window."), "ControllerBridge"); return 2; }
        try
        {
            if (args.Length >= 2 && args[0] == "--self-test") { SelfTest.Run(args[1]); return 0; }
            if (args.Length >= 2 && args[0] == "--check-led-rgb") { MainForm.RunLedChecks(Path.GetFullPath(args[1])); return 0; }
            if (args.Length >= 2 && args[0] == "--check-controller-arrival") { MainForm.RunControllerArrivalChecks(Path.GetFullPath(args[1])); return 0; }
            if (args.Length >= 2 && args[0] == "--check-firmware-update") { FirmwareChecks.Run(Path.GetFullPath(args[1])); return 0; }
            if (args.Length >= 2 && args[0] is "--check-connection-ui" or "--check-startup-readonly")
            { MainForm.RunConnectionChecks(Path.GetFullPath(args[1]), args[0] == "--check-startup-readonly"); return 0; }
            if (args.Length >= 2 && args[0] == "--inspect-firmware") { FirmwareDevice.InspectAsync(Path.GetFullPath(args[1])).GetAwaiter().GetResult(); return 0; }
            if (args.Length >= 2 && args[0] == "--check-axis-dialog") { AxisDialog.RunChecks(Path.GetFullPath(args[1])); return 0; }
            if (args.Length >= 2 && args[0] == "--check-input-log") { MainForm.RunInputLogChecks(Path.GetFullPath(args[1])); return 0; }
            if (args.Length >= 2 && args[0] == "--check-input-log-paint") { InputLogForm.RunPaintChecks(Path.GetFullPath(args[1])); return 0; }
            if (args.Length >= 3 && args[0] == "--mouse-check")
            {
                using MouseCheck check = new(args[1], int.Parse(args[2])); Application.Run(check);
                if (check.Failure is not null) throw check.Failure; return 0;
            }
            if (args.Length >= 2 && args[0] is "--device-check" or "--device-roundtrip")
            { DeviceCheck.Run(args[1], args[0] == "--device-roundtrip").GetAwaiter().GetResult(); return 0; }
            if (args.Length >= 2 && args[0] == "--bank-roundtrip")
            { BankDeviceCheck.Run(args[1]).GetAwaiter().GetResult(); return 0; }
            if (args.Length >= 2 && args[0] == "--profile-output-check")
            { ProfileOutputDeviceCheck.Run(args[1]).GetAwaiter().GetResult(); return 0; }
            bool capture = args.Length >= 2 && args[0] is "--capture" or "--capture-minimum" or "--render-preview" or "--render-preview-tabs" or "--render-preview-dialogs" or "--capture-dialogs" or "--capture-tabs" or "--check-output-tabs" or "--check-empty-input" or "--check-profile-menu";
            using MainForm form = new(args.Length > 0 && args[0] is "--preview" or "--render-preview" or "--render-preview-tabs" or "--render-preview-dialogs" or "--check-output-tabs" or "--check-profile-menu", automaticFirmwareGuidance: !capture);
            if (capture)
            {
                form.ShowInTaskbar = false; form.Opacity = 0;
                form.Shown += async (_, _) =>
                {
                    for (int i = 0; i < 120 && !form.ConnectedSuccessfully && form.StartupError is null; i++) await Task.Delay(100);
                    if (form.StartupError is not null) File.WriteAllText(args[1] + ".error.txt", form.StartupError);
                    else
                    {
                        await Task.Delay(100);
                        if (args[0] is "--capture-dialogs" or "--render-preview-dialogs") form.CaptureDialogs(Path.GetFullPath(args[1]));
                        else if (args[0] is "--capture-tabs" or "--render-preview-tabs") form.CaptureOutputTabs(Path.GetFullPath(args[1]));
                        else if (args[0] is "--check-output-tabs" or "--check-empty-input" or "--check-profile-menu")
                        {
                            try { if (args[0] == "--check-profile-menu") await form.CheckProfileMenu(Path.GetFullPath(args[1])); else if (args[0] == "--check-empty-input") await form.CheckEmptyInput(Path.GetFullPath(args[1])); else await form.CheckOutputTabs(Path.GetFullPath(args[1])); }
                            catch (Exception ex) { File.WriteAllText(Path.Combine(args[1], "error.txt"), ex.ToString()); Environment.ExitCode = 1; }
                        }
                        else form.CapturePreview(Path.GetFullPath(args[1]), args[0] == "--capture-minimum");
                    }
                    form.Close();
                };
            }
            Application.Run(form); return Environment.ExitCode != 0 ? Environment.ExitCode : form.StartupError is null || !capture ? 0 : 1;
        }
        catch (Exception ex)
        {
            if (args.Length >= 2)
            {
                string path = Path.GetFullPath(args[1]);
                if (Directory.Exists(path)) File.WriteAllText(Path.Combine(path, "error.txt"), ex.ToString());
                else File.WriteAllText(path + ".error.txt", ex.ToString());
            }
            else MessageBox.Show(ex.Message, "ControllerBridge", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}

internal static class SelfTest
{
    public static void Run(string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory); int checks = 0;
        void Check(bool ok, string description) { checks++; if (!ok) throw new InvalidDataException("Self-test: " + description); }
        Check(OutputSettings.Decode(0) == new OutputSettings(false, 1, false, false), "legacy output byte remains gamepad");
        Check(new OutputSettings(true, 8, true, true).Encode() == 63, "mouse settings bit layout");
        Check(OutputSettings.Decode(39) == new OutputSettings(true, 4, false, true), "mouse gain and inversion roundtrip");
        Check(OutputSettings.Decode(71) == new OutputSettings(true, 4, false, false, true), "combined output preserves legacy gain bits");
        Check(new OutputSettings(true, 4, false, false, true).Encode() == 71, "combined output bit layout");
        Check(OutputSettings.Decode(199) == new OutputSettings(true, 4, false, false, true, true), "full gamepad button routing keeps legacy gain bits");
        Check(new OutputSettings(true, 8, true, true, true, true).Encode() == 255, "all output bits encode without truncation");
        foreach (byte invalid in new byte[] { 128, 129, 192 })
        {
            bool rejectedRoute = false; try { OutputSettings.Decode(invalid); } catch (InvalidDataException) { rejectedRoute = true; }
            Check(rejectedRoute, "full gamepad buttons require both mouse and combined mode");
        }
        bool invalidOutput = false; try { OutputSettings.Decode(64); } catch (InvalidDataException) { invalidOutput = true; }
        Check(invalidOutput, "reserved output bits rejected");
        InputField button = new() { Index = 0, Interface = 1, ReportId = 2, Flags = 2, Bits = 1, Minimum = 0, Maximum = 1, Value = 1, Valid = true, UsagePage = 9, Usage = 1 };
        ButtonLearner learn = new([button], false);
        Check(learn.Observe(button, 0) is null, "release of held button must not assign");
        Check(learn.Observe(button, 1)?.Binding is { Mode: 1, Low: 1, High: 1, Interface: 1, ReportId: 2 }, "new press assigns exact source");
        InputField hat = new() { Index = 1, Bits = 4, Minimum = 0, Maximum = 7, Value = 8, Valid = true, UsagePage = 1, Usage = 0x39 };
        learn = new([hat], true); Check(learn.Observe(hat, 1) is null, "diagonal must not become cardinal");
        Check(learn.Observe(hat, 2)?.Binding is { Mode: 3, Low: 0, High: 7 }, "right hat includes neighboring diagonals");
        hat.Value = 2; learn = new([hat], true); Check(learn.Observe(hat, 4) is null, "held hat requires neutral first");
        Check(learn.Observe(hat, 8) is null, "hat neutral not assigned"); Check(learn.Observe(hat, 0)?.Binding.Mode == 2, "hat rearmed");
        InputField axis = new() { Index = 2, Interface = 1, ReportId = 2, BitOffset = 8, Flags = 3, Bits = 16, Minimum = -32768, Maximum = 32767, Value = 0, Valid = true, UsagePage = 1, Usage = 0x30 };
        learn = new([axis], false); Check(learn.Observe(axis, 100) is null, "analog jitter ignored");
        Check(learn.Observe(axis, -32768)?.Binding is { Low: -32768, High: -16384 }, "negative analog to digital threshold");
        InputField relative = new() { Index = 3, Bits = 8, Minimum = 0, Maximum = 255, Value = 0, Valid = true, HidFlags = 4 };
        learn = new([relative], false); Check(learn.Observe(relative, 255) is null, "relative controls not learned");
        InputField trigger = new() { Index = 4, Bits = 10, Minimum = 0, Maximum = 1023, Value = 0, Valid = true };
        learn = new([trigger], false); Check(learn.Observe(trigger, 900)?.Binding is { Low: 450, High: 1023 }, "trigger threshold");
        Check(new DeviceProfile().Mouse.TrackballGain == 16 && new DeviceProfile().Mouse.PaddleGain == 1, "trackball 16x and paddle 1x defaults");
        DeviceProfile p = new() { Flags = 3, Vid = 0x1234, Pid = 0x5678, Name = "校正テスト", DescriptorHash = 0x11223344, InputInterval = 1, Mouse = new() { TrackballGain = 1 } };
        p.Buttons[2] = button.Digital(1, 1, 1); p.Buttons[14] = hat.Digital(2, 0, 7);
        p.Axes[0] = new() { Mode = 1, Analog = axis.Analog(-32768, 0, 32767, 5, false) };
        byte[] bytes = p.Encode(); Check(bytes.Length == DeviceProfile.ByteCount, "wire length"); Check(DeviceProfile.Decode(bytes).Encode().SequenceEqual(bytes), "wire roundtrip");
        Check(Wire.Crc("123456789"u8.ToArray()) == 0xcbf43926, "CRC32 standard vector");
        File.WriteAllBytes(Path.Combine(directory, "gui-profile.bin"), bytes);
        string json = Path.Combine(directory, "gui-profile.json"); ProfileFile.Save(json, p); Check(ProfileFile.Load(json).Encode().SequenceEqual(bytes), "JSON persistence");
        bool rejected = false; p.Axes[0].Analog.Center = 32767; try { p.Encode(); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "invalid calibration rejected"); p.Axes[0].Analog.Center = 0;
        p.Name = new string('あ', 16); rejected = false; try { p.Encode(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "UTF8 field bound");
        p.Name = "rapid test"; p.Buttons[2].RapidHz = 7;
        byte[] rapidBytes = p.Encode();
        Check(Wire.U16(rapidBytes, 0) == 5 && rapidBytes[64 + 2 * 16 + 7] == 7, "v5 keeps the rapid-fire wire offset");
        Check(p.Clone().Buttons[2].RapidHz == 7, "rapid profile clone");
        File.WriteAllBytes(Path.Combine(directory, "gui-rapid-profile.bin"), rapidBytes);
        string rapidJson = Path.Combine(directory, "gui-rapid-profile.json"); ProfileFile.Save(rapidJson, p);
        DeviceProfile imported = ProfileFile.Load(rapidJson);
        Check(imported.Version == 5 && imported.Encode().SequenceEqual(rapidBytes), "JSON preserves rapid feature and v5 settings");
        File.WriteAllText(rapidJson, JsonSerializer.Serialize(new ProfileFile { Version = 1, Profile = p }));
        rejected = false; try { ProfileFile.Load(rapidJson); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "previous JSON export format is rejected");
        foreach (byte hz in new byte[] { 2, 30 }) { p.Buttons[2].RapidHz = hz; Check(DeviceProfile.Decode(p.Encode()).Buttons[2].RapidHz == hz, "rapid endpoints"); }
        foreach (byte hz in new byte[] { 1, 31, 255 })
        { p.Buttons[2].RapidHz = hz; rejected = false; try { p.Encode(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "invalid rapid rate rejected"); }
        p.Buttons[2].RapidHz = 7;
        foreach (ushort version in new ushort[] { 1, 2, 3, 4 })
        {
            byte[] unsupported = version < 3 ? p.Encode()[..480] : p.Encode(); Wire.W16(unsupported, 0, version);
            rejected = false; try { DeviceProfile.Decode(unsupported); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "old binary profile versions are rejected without migration");
            DeviceProfile old = p.Clone(); old.Version = version;
            File.WriteAllText(rapidJson, JsonSerializer.Serialize(new ProfileFile { Profile = old }));
            rejected = false; try { ProfileFile.Load(rapidJson); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "old JSON profile versions are rejected without conversion");
        }
        p.Mouse = new() { TrackballEnabled = false, TrackballGain = 3.5m, PaddleGain = 9.9m, InvertPaddle = true };
        byte[] mouseBytes = p.Encode(); Check(mouseBytes[480] == 18 && mouseBytes[481] == 35 && mouseBytes[482] == 99, "decimal mouse wire and toggles");
        Check(p.Clone().Mouse.TrackballGain == 3.5m && !p.Clone().Mouse.TrackballEnabled, "decimal mouse clone");
        ProfileFile.Save(rapidJson, p); Check(ProfileFile.Load(rapidJson).SameContent(p), "decimal and ON/OFF JSON persistence");
        foreach (decimal gain in new[] { 1m, 10m, 10.1m, 16m, 25.5m, 25.6m, 63.9m, 64m })
        {
            p.Mouse.TrackballGain = gain; byte[] encoded = p.Encode();
            Check(p.Clone().Mouse.TrackballGain == gain && Wire.U16(encoded, 0) == 5, "v5 gain boundaries preserve gain values");
            Check((encoded[481] | encoded[483] << 8) == gain * 10 && encoded[482] == 99, "extended gain bytes preserve paddle");
            ProfileFile.Save(rapidJson, p); Check(ProfileFile.Load(rapidJson).SameContent(p), "extended gain JSON persistence");
        }
        foreach (decimal gain in new[] { 0.9m, 64.1m, 3.55m })
        { p.Mouse.TrackballGain = gain; rejected = false; try { p.Encode(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "invalid decimal gain rejected"); }
        p.Mouse.TrackballGain = 3.5m; mouseBytes[487] = 1; rejected = false;
        try { DeviceProfile.Decode(mouseBytes); } catch (InvalidDataException) { rejected = true; } Check(rejected, "RGB data without the custom-color flag is rejected");
        foreach (byte mode in new byte[] { 0, 1, 2 }) foreach (bool controller in new[] { false, true })
        {
            DeviceProfile outputProfile = p.Clone(); outputProfile.OutputMode = mode; outputProfile.ControllerButtons = controller;
            byte[] encoded = outputProfile.Encode(); DeviceProfile decoded = DeviceProfile.Decode(encoded);
            Check(encoded.Length == 488 && encoded[484] == (mode | (controller ? 4 : 0)) && Wire.U16(encoded, 0) == 5, "output mode fits the v5 reserved byte");
            Check(decoded.OutputMode == mode && decoded.ControllerButtons == controller && decoded.Output.Encode() == (mode == 0 ? 0 : mode == 1 ? 1 : controller ? 193 : 65), "mode and button preference survive codec roundtrip independently");
        }
        foreach (byte invalidMode in new byte[] { 3, 7, 8, 255 })
        {
            byte[] encoded = p.Encode(); encoded[484] = invalidMode; rejected = false;
            try { DeviceProfile.Decode(encoded); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid output mode bits are rejected");
        }
        p.Buttons[0].RapidHz = 2; rejected = false; try { p.Encode(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "unassigned rapid output rejected"); p.Buttons[0].RapidHz = 0;
        p.Axes[1].Mode = 2; p.Axes[1].Negative = p.Buttons[2];
        rejected = false; try { p.Encode(); } catch (InvalidDataException) { rejected = true; } Check(rejected, "rapid is not applied to analog button pairs");
        p.Axes[1] = new(); p.ProfileId = Guid.NewGuid();
        Check(p.Clone().SameContent(p), "clone preserves identity and mapping");
        Check(DeviceProfile.Decode(p.Encode()).ProfileId == Guid.Empty, "profile wire does not embed ID");
        DeviceProfile variant = p.Clone(); variant.ProfileId = Guid.NewGuid(); variant.Name = "game variant"; variant.Buttons[2].RapidHz = 30;
        variant.OutputMode = 2; variant.ControllerButtons = true;
        string exported = Path.Combine(directory, "gui-identified-profile.json"); ProfileFile.Save(exported, p);
        Check(ProfileFile.Load(exported).SameContent(p), "JSON export preserves stable identity");
        DeviceProfile differentMode = p.Clone(); differentMode.DescriptorHash++; differentMode.OutputMode = 1;
        Check(!differentMode.SameController(p), "controller mode is part of controller identity");
        DeviceProfile[] slots = Enumerable.Range(0, DeviceProfile.SlotCount).Select(_ => new DeviceProfile()).ToArray(); slots[2] = p.Clone();
        Check(ProfileSlots.Name(slots, 2) == p.Name, "single profile has no number suffix");
        slots[6] = variant.Clone(); slots[4] = differentMode;
        Check(ProfileSlots.Name(slots, 2) == p.Name && ProfileSlots.Name(slots, 6) == variant.Name, "list shows persisted names without adding display-only numbers");
        Check(ProfileSlots.Name(slots, 4) == differentMode.Name && ProfileSlots.Name(slots, 0) == L.T("未使用", "Unused"), "other mode and empty slot not numbered");
        ProfileSlots.Validate(slots, 6, variant, true);
        rejected = false; try { ProfileSlots.Validate(slots, 1, p.Clone(), true); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "same profile ID cannot be registered twice");
        rejected = false; try { ProfileSlots.Validate(slots, 6, variant, false); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "old firmware cannot register variants");
        slots[2] = new(); Check(ProfileSlots.Name(slots, 6) == variant.Name, "removing another slot does not change the name");
        Check(ProfileSlots.EmptyBelow(slots, 4) == 5, "duplicate selects closest unused slot below source");
        Check(ProfileSlots.EmptyBelow(slots, 15) == -1, "duplicate never wraps to unused slots above source");
        slots[2] = p.Clone(); differentMode.ProfileId = Guid.NewGuid();
        uint[] shortcuts = new uint[16]; shortcuts[2] = shortcuts[6] = 0x4300;
        SlotBank originalBank = SlotBank.FromProfiles(slots, (1u << 6) | (1u << 4), shortcuts).WithGeneration(91);
        SlotBank reordered = originalBank.Move(6, 1);
        Check(reordered.Profiles[1].SameContent(variant) && reordered.Profiles[3].SameContent(p), "insertion shifts intervening profiles and preserves IDs");
        Check(reordered.Profiles[1].OutputMode == 2 && reordered.Profiles[1].ControllerButtons && reordered.Profiles[5].OutputMode == 1, "reordering carries each profile's own output and button behavior");
        Check(reordered.SwitchKeys(1) == 0x4300 && reordered.SwitchKeys(3) == 0x4300 && reordered.PreferredSlots == ((1u << 1) | (1u << 5)), "switching settings and saved selections move with profiles");
        Check(reordered.Move(1, 6).Encode().SequenceEqual(originalBank.Encode()), "inverse move restores every bank byte");
        Check(originalBank.Move(6, 15).Move(15, 6).Encode().SequenceEqual(originalBank.Encode()), "move to last slot preserves empty slots and roundtrips");
        byte[] invalidBank = originalBank.Encode(); invalidBank[7824 + 6 * 16] ^= 1;
        Check(new SlotBank(invalidBank).Profiles[6].ProfileId != variant.ProfileId, "profile identity is carried separately from mappings");
        p.ProfileId.TryWriteBytes(invalidBank.AsSpan(7824 + 6 * 16)); rejected = false;
        try { _ = new SlotBank(invalidBank); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "bank rejects duplicate nonempty profile IDs");
        string allSlots = Path.Combine(directory, "all-slots.json"); SlotBackup.Save(allSlots, originalBank);
        Check(SlotBackup.Load(allSlots).Encode().SequenceEqual(originalBank.Encode()), "backup preserves exact bank payload and metadata");
        SlotBackup namedBackup = JsonSerializer.Deserialize<SlotBackup>(File.ReadAllText(allSlots))!;
        Check(namedBackup.Format == "ControllerBridge.Slots" && namedBackup.Version == 2, "new backups use the current product and format");
        namedBackup.Version = 1; File.WriteAllText(allSlots, JsonSerializer.Serialize(namedBackup)); rejected = false;
        try { SlotBackup.Load(allSlots); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "previous whole-bank backup format is rejected"); namedBackup.Version = 2;
        namedBackup.Format = "Unrelated.Slots"; File.WriteAllText(allSlots, JsonSerializer.Serialize(namedBackup)); rejected = false;
        try { SlotBackup.Load(allSlots); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "unrelated backup format names remain rejected"); SlotBackup.Save(allSlots, originalBank);
        Check(SlotBackup.Load(allSlots).Profiles[6].ControllerButtons && SlotBackup.Load(allSlots).Profiles[4].OutputMode == 1, "backup restores different output modes and button behaviors");
        SlotBackup.Save(allSlots, reordered);
        Check(SlotBackup.Load(allSlots).Encode().SequenceEqual(reordered.Encode()), "backup replaces an existing file atomically");
        SlotBackup corrupt = JsonSerializer.Deserialize<SlotBackup>(File.ReadAllText(allSlots))!; corrupt.Bank[100] ^= 1;
        File.WriteAllText(allSlots, JsonSerializer.Serialize(corrupt)); rejected = false;
        try { SlotBackup.Load(allSlots); } catch (InvalidDataException) { rejected = true; }
        Check(rejected, "backup rejects a changed payload before sending anything"); SlotBackup.Save(allSlots, originalBank);
        Capabilities absent = new(255, 0, 0, 0, 0, 4, 1, 0, 91, 0, 0, 0);
        string waiting = MainForm.WaitingState(absent);
        foreach (byte host in new byte[] { 0, 1, 2, 4, 6, 10 })
            Check(MainForm.NoInput(absent with { HostStatus = host }) && MainForm.WaitingState(absent with { HostStatus = host }) == waiting, "empty USB-A retry state has a stable display");
        Check(!MainForm.NoInput(absent with { Vid = 0x1234 }), "detected devices retain initialization and failure status");
        SwitchMethodDialog.Validate(0x4300); Check(true, "direction plus two-button hotkey is valid");
        foreach (uint mask in new uint[] { 0, 4, 0x14000, 0x28000, 0x80000003 })
        {
            rejected = false; try { SwitchMethodDialog.Validate(mask); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "invalid or opposing hotkey rejected");
        }
        File.WriteAllText(Path.Combine(directory, "gui-self-test.json"), JsonSerializer.Serialize(new { checks, passed = true, fixture = "gui-profile.bin" }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal static class DeviceCheck
{
    public static async Task Run(string directory, bool roundtrip)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        HidBridge? bridge = null;
        try
        {
            bridge = await HidBridge.ConnectAsync(); var bank = await bridge.ReadProfilesAsync(); DeviceState device = await bridge.ReadDeviceAsync();
            for (int i = 0; i < DeviceProfile.SlotCount; i++) File.WriteAllBytes(Path.Combine(directory, $"profile-{i + 1}.bin"), bank.Profiles[i].Encode());
            File.WriteAllText(Path.Combine(directory, "before.json"), JsonSerializer.Serialize(new { bank.Caps, bank.Profiles, device.Fields }, new JsonSerializerOptions { WriteIndented = true }));
            bool verified = false; uint savedGeneration = bank.Caps.BankGeneration;
            if (roundtrip)
            {
                int slot = bank.Caps.ActiveSlot < DeviceProfile.SlotCount ? bank.Caps.ActiveSlot : Array.FindIndex(bank.Profiles, p => p.Enabled);
                if (slot < 0) throw new InvalidDataException(L.T("保存確認できるプロファイルがありません。", "No profile is available for the save check."));
                savedGeneration = await bridge.SaveAsync(slot, bank.Profiles[slot], bank.Caps.BankGeneration);
                await bridge.RawAsync(14); bridge.Dispose(); bridge = null;
                await Task.Delay(1500);
                for (int i = 0; i < 20 && bridge is null; i++)
                { try { bridge = await HidBridge.ConnectAsync(); } catch (IOException) { await Task.Delay(250); } }
                if (bridge is null) throw new IOException(L.T("再起動後に本体が見つかりません。", "The device was not found after restarting."));
                var after = await bridge.ReadProfilesAsync();
                verified = after.Caps.BankGeneration == savedGeneration && after.Caps.Persisted && Enumerable.Range(0, DeviceProfile.SlotCount).All(i => bank.Profiles[i].SameContent(after.Profiles[i]));
                if (!verified) throw new InvalidDataException(L.T("再起動後の16枠照合に失敗しました。", "Verification of all 16 slots failed after restarting."));
                device = await bridge.ReadDeviceAsync();
            }
            byte[] info = await bridge.RawAsync(1);
            File.WriteAllText(Path.Combine(directory, "device-check.json"), JsonSerializer.Serialize(new
            {
                timestamp = DateTimeOffset.Now, roundtrip, rebootVerifiedAllEightSlots = verified, savedGeneration,
                device.Caps, device.Fields, rawInput = Convert.ToHexString(info[28..32]), output = Convert.ToHexString(info[32..40])
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { bridge?.Dispose(); }
    }
}
