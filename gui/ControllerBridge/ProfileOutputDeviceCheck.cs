using System.ComponentModel;
using System.Text.Json;

namespace ControllerBridge;

// Explicit developer check (--profile-output-check). Uses four temporary copies
// of the connected profile, then restores the exact original bank and selection.
internal static class ProfileOutputDeviceCheck
{
    internal static async Task Run(string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        HidBridge? bridge = null; SlotBank? original = null; bool restoreNeeded = false;
        List<string> checks = []; List<object> stages = [];
        void Check(bool ok, string name) { if (!ok) throw new InvalidDataException(name); checks.Add(name); }
        async Task ConnectUntil(int? slot = null, OutputSettings? expected = null)
        {
            Exception? last = null;
            for (int attempt = 0; attempt < 48; attempt++)
            {
                try
                {
                    bridge ??= await HidBridge.ConnectAsync();
                    Capabilities c = await bridge.GetCapsAsync(); OutputState o = await bridge.ReadOutputStateAsync();
                    if (c.Connected && c.ActiveSlot < 16 && (slot is null || c.ActiveSlot == slot) &&
                        (expected is null || o.Active == expected && o.Desired == expected)) return;
                }
                catch (Exception ex) when (ex is IOException or Win32Exception or TimeoutException)
                { last = ex; bridge?.Dispose(); bridge = null; }
                await Task.Delay(250);
            }
            throw new IOException("The expected profile output did not become ready.", last);
        }
        async Task ReconnectIfNeeded(int slot, OutputSettings expected)
        {
            if (bridge!.OutputReconnectPending) { bridge.Dispose(); bridge = null; await Task.Delay(750); }
            await ConnectUntil(slot, expected);
        }
        try
        {
            await ConnectUntil(); original = await bridge!.ReadBankAsync();
            Capabilities initial = await bridge.GetCapsAsync(); byte[] config = await bridge.RawAsync(2);
            int source = initial.ActiveSlot;
            DeviceProfile[] profiles = original.Profiles.Select(p => p.Clone()).ToArray();
            int[] empty = Enumerable.Range(0, 16).Where(i => !profiles[i].Enabled).Take(4).ToArray();
            if (empty.Length != 4) throw new InvalidDataException("Four unused slots are required for this check.");
            SlotBackup.Save(Path.Combine(directory, "ControllerBridge-original-slots.json"), original);
            File.WriteAllBytes(Path.Combine(directory, "original-bank.bin"), original.Encode());
            uint[] keys = Enumerable.Range(0, 16).Select(original.SwitchKeys).ToArray();
            string[] names = ["Check Gamepad", "Check USB Mouse", "Check Mouse Buttons", "Check Controller Buttons"];
            for (int i = 0; i < 4; i++)
            {
                DeviceProfile p = profiles[source].Clone(); p.ProfileId = Guid.NewGuid(); p.Name = names[i];
                p.OutputMode = (byte)Math.Min(i, 2); p.ControllerButtons = i == 3;
                profiles[empty[i]] = p; keys[empty[i]] = keys[source];
            }
            SlotBank temporary = SlotBank.FromProfiles(profiles, original.PreferredSlots, keys);
            restoreNeeded = true;
            SlotBank installed = await bridge.WriteBankAsync(temporary, original.Generation);
            await ReconnectIfNeeded(source, profiles[source].Output);
            Check((await bridge!.ReadBankAsync()).Encode().SequenceEqual(installed.Encode()), "temporary profiles persist with independent output settings");
            for (int i = 0; i < 4; i++)
            {
                int slot = empty[i]; OutputSettings expected = profiles[slot].Output;
                OutputState before = await bridge!.ReadOutputStateAsync(); Capabilities c = await bridge.GetCapsAsync();
                bool reconnect = before.Active.Mouse != expected.Mouse || before.Active.Combined != expected.Combined;
                await bridge.SelectProfileAsync(slot, c.BankGeneration);
                Check(bridge.OutputReconnectPending == reconnect, $"slot {slot + 1}: USB reconnect only when the layout changes");
                await ReconnectIfNeeded(slot, expected);
                c = await bridge!.GetCapsAsync(); OutputState after = await bridge.ReadOutputStateAsync();
                Check(c.ActiveSlot == slot && after.Active == expected && after.Desired == expected, $"slot {slot + 1}: saved and active output agree");
                Check(c.ConnectionGeneration == initial.ConnectionGeneration, $"slot {slot + 1}: USB-A input host was not restarted");
                Check((await bridge.RawAsync(2)).SequenceEqual(config), $"slot {slot + 1}: common configuration remains unchanged");
                stages.Add(new { slot = slot + 1, reconnect, active = after.Active, desired = after.Desired, c.BankGeneration, c.ConnectionGeneration });
            }
            SlotBank beforeReboot = await bridge!.ReadBankAsync();
            File.WriteAllBytes(Path.Combine(directory, "temporary-bank.bin"), beforeReboot.Encode());
            await bridge.RawAsync(14); bridge.Dispose(); bridge = null; await Task.Delay(1000);
            await ConnectUntil(empty[3], profiles[empty[3]].Output);
            Check((await bridge!.ReadBankAsync()).Encode().SequenceEqual(beforeReboot.Encode()), "all profile output settings and selection survive a full reboot");
            Check((await bridge.ReadOutputStateAsync()).Active == profiles[empty[3]].Output, "startup applies the selected profile output without a PC write");

            SlotBank restored = await bridge.WriteBankAsync(original, beforeReboot.Generation);
            await ReconnectIfNeeded(source, original.Profiles[source].Output);
            SlotBank final = await bridge!.ReadBankAsync();
            Check(final.Encode().SequenceEqual(original.WithGeneration(restored.Generation).Encode()), "all original bank bytes, IDs, order and shortcuts restored");
            Check((await bridge.RawAsync(2)).SequenceEqual(config), "original common settings restored unchanged");
            restoreNeeded = false;
            File.WriteAllBytes(Path.Combine(directory, "restored-bank.bin"), final.Encode());
            File.WriteAllText(Path.Combine(directory, "profile-output-device-check.json"), JsonSerializer.Serialize(new
            { passed = true, initialGeneration = original.Generation, finalGeneration = final.Generation, source = source + 1, checks, stages }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            if (restoreNeeded && original is not null)
            {
                try
                {
                    bridge?.Dispose(); bridge = null; await Task.Delay(750); await ConnectUntil();
                    SlotBank current = await bridge!.ReadBankAsync();
                    await bridge.WriteBankAsync(original, current.Generation);
                    if (bridge.OutputReconnectPending) { bridge.Dispose(); bridge = null; await Task.Delay(750); await ConnectUntil(); }
                    SlotBank restored = await bridge!.ReadBankAsync();
                    if (!restored.Encode().SequenceEqual(original.WithGeneration(restored.Generation).Encode())) throw new IOException("Recovery bank verification failed.");
                    File.WriteAllText(Path.Combine(directory, "recovery.txt"), "Original bank restored after interrupted verification.");
                }
                catch (Exception ex) { File.WriteAllText(Path.Combine(directory, "recovery-error.txt"), ex.ToString()); }
            }
            bridge?.Dispose();
        }
    }
}
