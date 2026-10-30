using System.Text.Json;

namespace ControllerBridge;

internal sealed partial class MainForm
{
    internal static void RunControllerArrivalChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        using MainForm form = new(preview: true, autoConnect: false) { Opacity = 0, ShowInTaskbar = false };
        Exception? failure = null;
        form.Shown += (_, _) =>
        {
            try { form.CheckControllerArrival(directory); }
            catch (Exception ex) { failure = ex; }
            finally { Array.Clear(form.dirty); form.working = false; form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) throw failure;
    }

    private void CheckControllerArrival(string directory)
    {
        List<string> checks = [];
        void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); checks.Add(message); }
        DeviceProfile Occupied(int number) => new() { Flags = 3, Vid = 0x2222, Pid = (ushort)number,
            DescriptorHash = 9, Name = "Existing " + number, ProfileId = Guid.NewGuid() };
        Capabilities input = new(255, 1, 0x1234, 1, 0x100, 5, 17, 0x12345678, 10, 0, 1, 5, Features: 16383);
        void Setup()
        {
            DeviceProfile[] bank = Enumerable.Range(0, 16).Select(_ => new DeviceProfile()).ToArray();
            bank[0] = Occupied(1); bank[1] = Occupied(2);
            ApplyBank(bank, 10); caps = input; generation = input.ConnectionGeneration; controllerArrivals.Reset();
            fields.Clear(); fields.Add(new() { Index = 0, Bits = 8, Minimum = 0, Maximum = 255, Valid = true, UsagePage = 9, Usage = 1 });
            selected = 7; RefreshSlots(); BindSelected(); UpdateConnection();
        }
        Setup();
        byte[][] original = profiles.Select(p => p.Encode()).ToArray(); uint originalBank = bankGeneration;
        HandleControllerArrival();
        Check(selected == 2 && slots.SelectedIndex == 2 && !details.Visible && delete.Enabled, "new input selects the first genuinely empty slot and exposes Add controller");
        Check(caps!.ActiveSlot == 255 && originalBank == bankGeneration && !dirty.Any(x => x) &&
            Enumerable.Range(0, 16).All(i => original[i].SequenceEqual(profiles[i].Encode())), "arrival changes only PC selection, not active firmware slot, bank, or drafts");
        Check(!connection.Text.Contains(NoProfileSpace), "available slot produces no no-space message");
        CapturePreview(Path.Combine(directory, "first-empty.png"));
        slots.SelectedIndex = 8; int bindings = selectionBindings;
        for (int i = 0; i < 4; i++) HandleControllerArrival();
        Check(selected == 8 && selectionBindings == bindings, "repeated polls preserve the user's later manual selection without rebinding");
        caps = input with { Flags = 4, Vid = 0, FieldCount = 0 }; HandleControllerArrival();
        caps = input with { ConnectionGeneration = 18 }; generation = 18; HandleControllerArrival();
        Check(selected == 2, "same-model reconnection is a new arrival");
        Setup(); profiles[0].Vid = input.Vid; profiles[0].Pid = input.Pid; profiles[0].DescriptorHash = input.DescriptorHash;
        persistedProfiles[0] = profiles[0].Clone(); HandleControllerArrival();
        Check(selected == 7, "matching saved profile prevents empty-slot navigation regardless of active slot");
        Setup(); profiles[4] = new() { Flags = 3, Vid = input.Vid, Pid = input.Pid, DescriptorHash = input.DescriptorHash, Name = "Pending import" };
        dirty[4] = true; HandleControllerArrival(); Check(selected == 7, "a matching unsaved draft does not cause duplicate creation");
        Setup(); caps = input with { Flags = 4 }; HandleControllerArrival(); Check(selected == 7, "initialization is not an input arrival");
        caps = input; HandleControllerArrival(); Check(selected == 2, "completed initialization is handled even within the same generation");
        Setup(); fields.Clear(); HandleControllerArrival(); Check(selected == 7, "unsupported input without assignable fields does not navigate");
        Setup(); caps = input with { Flags = 13 }; HandleControllerArrival(); Check(selected == 7, "invalid saved mappings are not treated as a profile-free controller");
        Setup(); profiles[0].Name = "Unsaved edit"; dirty[0] = true; HandleControllerArrival();
        Check(selected == 7 && profiles[0].Name == "Unsaved edit" && dirty[0], "arrival waits while any draft is dirty");
        persistedProfiles[0] = profiles[0].Clone(); dirty[0] = false; HandleControllerArrival();
        Check(selected == 2 && profiles[0].Name == "Unsaved edit", "deferred selection resumes after drafts are settled");
        foreach (string blocker in new[] { "working", "connecting", "polling", "closing", "modal" })
        {
            Setup(); working = blocker == "working"; connecting = blocker == "connecting";
            polling = blocker == "polling"; closing = blocker == "closing"; Enabled = blocker != "modal";
            HandleControllerArrival(); Check(selected == 7, blocker + " defers navigation");
            working = connecting = polling = closing = false; Enabled = true; HandleControllerArrival();
            Check(selected == 2, blocker + " releases the pending arrival");
        }
        Setup(); caps = input with { BankGeneration = 11 }; HandleControllerArrival(); Check(selected == 7, "stale bank snapshot defers arrival");
        bankGeneration = observedBankGeneration = 11; HandleControllerArrival(); Check(selected == 2, "refreshed snapshot releases pending arrival");
        Setup(); caps = input with { ConnectionGeneration = 18 }; HandleControllerArrival();
        Check(selected == 7, "new capability snapshot cannot use the preceding attachment's input fields");
        generation = 18; HandleControllerArrival(); Check(selected == 2, "arrival resumes after its input snapshot is adopted");
        Setup(); persistedProfiles[2] = Occupied(3); profiles[3] = Occupied(4); dirty[4] = true;
        Check(FirstAvailableSlot == 5 && !CanRegisterSlot(2) && !CanRegisterSlot(3) && !CanRegisterSlot(4), "pending deletions, unsaved profiles, and dirty empty slots cannot be registration destinations");
        Setup(); for (int i = 2; i < 16; i++) { profiles[i] = Occupied(i + 1); persistedProfiles[i] = profiles[i].Clone(); }
        RefreshSlots(); BindSelected(); UpdateConnection(); HandleControllerArrival();
        Check(selected == 7 && connection.Text.Contains(NoProfileSpace) && connectionDetail == NoProfileSpace,
            "no profile plus no empty slot displays the exact message without selecting or overwriting anything");
        CapturePreview(Path.Combine(directory, "no-empty-slot.png"));
        string fullText = connection.Text; bindings = selectionBindings;
        for (int i = 0; i < 4; i++) { UpdateConnection(); HandleControllerArrival(); }
        Check(connection.Text == fullText && selectionBindings == bindings, "full-bank retries neither repeat a popup nor rebind the editor");
        profiles[3] = new(); persistedProfiles[3] = new(); UpdateConnection(); HandleControllerArrival();
        Check(selected == 3 && !connection.Text.Contains(NoProfileSpace), "a newly freed usable slot resolves the pending arrival and clears the message");
        Setup(); for (int i = 0; i < 16; i++) { profiles[i] = Occupied(i + 1); persistedProfiles[i] = profiles[i].Clone(); }
        profiles[0].Vid = input.Vid; profiles[0].Pid = input.Pid; profiles[0].DescriptorHash = input.DescriptorHash;
        persistedProfiles[0] = profiles[0].Clone(); UpdateConnection(); HandleControllerArrival();
        Check(!connection.Text.Contains(NoProfileSpace) && selected == 7, "full bank with a matching profile does not show a capacity warning");
        Setup(); InputConnection target = InputConnection.From(input); byte[] guard = new byte[41]; target.WriteRegistrationGuard(guard);
        Check(Wire.U32(guard, 26) == input.ConnectionGeneration && Wire.U32(guard, 30) == input.DescriptorHash &&
            Wire.U16(guard, 34) == input.Vid && Wire.U16(guard, 36) == input.Pid && Wire.U16(guard, 38) == input.Revision && guard[40] == input.InputProtocol,
            "registration packet encodes the captured attachment and complete identity at firmware-defined offsets");
        Check(target.Matches(input), "captured registration target accepts its own connected snapshot");
        foreach (Capabilities changed in new[] { input with { ConnectionGeneration = 18 }, input with { Vid = 2 }, input with { Pid = 2 },
            input with { Revision = 2 }, input with { DescriptorHash = 2 }, input with { InputProtocol = 2 }, input with { Flags = 4 } })
            Check(!target.Matches(changed), "registration target rejects changed identity, generation, protocol, or disconnection");
        Check(input.SupportsGuardedRegistration && !(input with { Features = 8191 }).SupportsGuardedRegistration, "guarded registration requires the explicit firmware capability");
        File.WriteAllText(Path.Combine(directory, "controller-arrival-checks.json"), JsonSerializer.Serialize(new
            { passed = true, count = checks.Count, hardwareWrites = false, checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
