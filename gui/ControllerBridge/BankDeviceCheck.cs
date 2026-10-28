using System.Text.Json;

namespace ControllerBridge;

// Explicit developer verification, only invoked with --bank-roundtrip.
// All original bytes except the monotonic generation are restored afterward.
internal static class BankDeviceCheck
{
    internal static async Task Run(string directory)
    {
        directory = Path.GetFullPath(directory); Directory.CreateDirectory(directory);
        HidBridge? bridge = null; SlotBank? original = null; bool restoreNeeded = false;
        List<string> checks = [];
        void Check(bool ok, string name) { if (!ok) throw new InvalidDataException(name); checks.Add(name); }
        try
        {
            bridge = await HidBridge.ConnectAsync(); original = await bridge.ReadBankAsync();
            var profiles = await bridge.ReadProfilesAsync(); byte[] config = await bridge.RawAsync(2);
            string backup = Path.Combine(directory, "ControllerBridge-all-slots-before.json");
            SlotBackup.Save(backup, original); File.WriteAllBytes(Path.Combine(directory, "original-bank.bin"), original.Encode());
            Check(SlotBackup.Load(backup).Encode().SequenceEqual(original.Encode()), "exact whole-bank backup");
            int source = profiles.Caps.ActiveSlot < 16 ? profiles.Caps.ActiveSlot : Array.FindIndex(original.Profiles, p => p.Enabled);
            if (source < 0) throw new InvalidDataException("No registered profile for the move check.");
            int destination = source == 15 ? source - 1 : source + 1;
            SlotBank expected = original.Move(source, destination); restoreNeeded = true;
            SlotBank moved = await bridge.WriteBankAsync(expected, original.Generation);
            File.WriteAllBytes(Path.Combine(directory, "moved-bank.bin"), moved.Encode());
            Check(moved.Encode().SequenceEqual(expected.WithGeneration(original.Generation + 1).Encode()), "insertion preserves all mappings, IDs, preferences and shortcuts");
            Check((await bridge.RawAsync(2)).SequenceEqual(config), "reordering preserves common output settings");
            var movedProfiles = await bridge.ReadProfilesAsync();
            if (profiles.Caps.ActiveSlot < 16)
                Check(movedProfiles.Caps.ActiveSlot == destination && movedProfiles.Profiles[destination].ProfileId == original.Profiles[source].ProfileId, "active profile follows its new slot");
            SlotBank restored = await bridge.WriteBankAsync(SlotBackup.Load(backup), moved.Generation); restoreNeeded = false;
            File.WriteAllBytes(Path.Combine(directory, "restored-bank.bin"), restored.Encode());
            Check(restored.Encode().SequenceEqual(original.WithGeneration(original.Generation + 2).Encode()), "backup restores every bank byte except generation");
            await bridge.RawAsync(14); bridge.Dispose(); bridge = null; await Task.Delay(1500);
            for (int i = 0; i < 20 && bridge is null; i++)
            { try { bridge = await HidBridge.ConnectAsync(); } catch (IOException) { await Task.Delay(250); } }
            if (bridge is null) throw new IOException("Device did not reconnect after reboot.");
            SlotBank rebooted = await bridge.ReadBankAsync(); var after = await bridge.ReadProfilesAsync();
            File.WriteAllBytes(Path.Combine(directory, "rebooted-bank.bin"), rebooted.Encode());
            Check(rebooted.Encode().SequenceEqual(restored.Encode()), "restored bank survives reboot byte for byte");
            Check((await bridge.RawAsync(2)).SequenceEqual(config), "common output settings survive reboot unchanged");
            Check(after.Caps.Persisted && after.Caps.ActiveSlot == profiles.Caps.ActiveSlot, "original active slot is retained after reboot");
            Check(Enumerable.Range(0, 16).All(i => original.Profiles[i].SameContent(after.Profiles[i])), "all 16 profile contents and identities match the baseline");
            File.WriteAllText(Path.Combine(directory, "bank-device-check.json"), JsonSerializer.Serialize(new
            { passed = true, initialGeneration = original.Generation, finalGeneration = rebooted.Generation, source, destination, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            try
            {
                if (restoreNeeded && original is not null && bridge is not null)
                {
                    SlotBank current = await bridge.ReadBankAsync();
                    if (!current.Encode().SequenceEqual(original.WithGeneration(current.Generation).Encode()))
                        await bridge.WriteBankAsync(original, current.Generation);
                }
            }
            finally { bridge?.Dispose(); }
        }
    }
}
