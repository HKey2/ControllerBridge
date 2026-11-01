using System.Text.Json;
using System.Text.Json.Nodes;

namespace ControllerBridge;

internal sealed partial class MainForm
{
    internal static void RunPaddleAxisChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        using MainForm form = new(preview: true, autoConnect: false) { Opacity = 0, ShowInTaskbar = false };
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try { await form.CheckPaddleAxis(directory); }
            catch (Exception ex) { failure = ex; }
            finally { Array.Clear(form.dirty); form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) throw failure;
    }
    private async Task CheckPaddleAxis(string directory)
    {
        List<string> checks = [];
        void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); checks.Add(message); }
        void Rejected(Action action, string message)
        {
            bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, message);
        }
        int slot = selected;
        Check(profiles.All(p => p.Mouse.PaddleAxis == PaddleOutputAxis.X), "existing and new profiles default to X");
        Check(paddleAxis.DropDownStyle == ComboBoxStyle.DropDownList && paddleAxis.Items.Count == 2 && paddleAxis.SelectedIndex == 0, "axis is an X/Y selection with no arbitrary text");
        Check(paddleAxis.Visible && paddleAxis.Enabled && !dirty.Any(x => x), "combined mode exposes the default without creating edits");
        byte[] before = profiles[slot].Encode();
        paddleAxis.SelectedIndex = 1;
        byte[] after = profiles[slot].Encode();
        Check(dirty[slot] && profiles[slot].Mouse.PaddleAxis == PaddleOutputAxis.Y && persistedProfiles[slot].Mouse.PaddleAxis == PaddleOutputAxis.X, "Y selection edits the draft only");
        Check(before.Length == 848 && after.Length == 848 && after[480] == (before[480] | 32) && Enumerable.Range(0, 848).Where(i => before[i] != after[i]).SequenceEqual(new[] { 480 }), "axis uses only bit 5 at byte 480 without changing the profile layout");
        Check(profiles[slot].Clone().Mouse.PaddleAxis == PaddleOutputAxis.Y, "clone preserves Y");
        byte[] extended = (byte[])after.Clone(); extended[480] |= 64;
        Check(DeviceProfile.Decode(extended).Mouse.TrackballToDpad, "previous spare bit now represents trackball key conversion");
        DeviceProfile invalidAxis = profiles[slot].Clone(); invalidAxis.Mouse.PaddleAxis = (PaddleOutputAxis)2;
        Rejected(() => invalidAxis.Encode(), "invalid enum fails before saving");
        Capabilities oldFirmware = caps! with { Features = 32767 };
        oldFirmware.ValidatePointingSettings(persistedProfiles[slot]);
        Check(!oldFirmware.SupportsPaddleAxis, "older firmware still supports X");
        Rejected(() => oldFirmware.ValidatePointingSettings(profiles[slot]), "older firmware cannot receive Y via profile or bank writes");
        Capabilities currentFirmware = oldFirmware with { Features = 65535 };
        currentFirmware.ValidatePointingSettings(profiles[slot]);
        Check(currentFirmware.SupportsPaddleAxis, "current capability accepts Y");
        paddleOn.Checked = false;
        Check(!paddleAxis.Enabled && !paddleGain.Enabled && !invertPaddle.Enabled && profiles[slot].Mouse.PaddleAxis == PaddleOutputAxis.Y, "turning paddle off disables controls and retains the chosen axis");
        paddleOn.Checked = true; paddleGain.Value = 2.5m; invertPaddle.Checked = true;
        Check(paddleAxis.Enabled && profiles[slot].Mouse.PaddleGain == 2.5m && profiles[slot].Mouse.InvertPaddle && profiles[slot].Mouse.PaddleAxis == PaddleOutputAxis.Y, "gain and reversal preserve Y");
        slots.SelectedIndex = 1;
        Check(paddleAxis.SelectedIndex == 0 && !dirty[1], "another profile retains X");
        slots.SelectedIndex = slot;
        Check(paddleAxis.SelectedIndex == 1 && paddleGain.Value == 2.5m && invertPaddle.Checked, "slot navigation restores all unsaved paddle controls");
        outputTabs.SelectedIndex = 1;
        Check(paddleAxis.Visible && paddleAxis.SelectedIndex == 1 && profiles[slot].OutputMode == 1, "mouse mode shares the chosen axis");
        working = true; UpdateEnabled(); Check(!paddleAxis.Enabled, "busy operations disable the axis selector");
        working = false; UpdateEnabled(); Check(paddleAxis.Enabled, "idle restores the selector");
        await SaveSelected();
        Check(!dirty[slot] && persistedProfiles[slot].Mouse.PaddleAxis == PaddleOutputAxis.Y && persistedProfiles[slot].Mouse.PaddleGain == 2.5m, "preview save and readback retain Y and gain");
        paddleAxis.SelectedIndex = 0;
        profiles[slot] = persistedProfiles[slot].Clone(); dirty[slot] = false; BindSelected();
        Check(paddleAxis.SelectedIndex == 1, "reload restores the saved Y choice");
        string file = Path.Combine(directory, "paddle-profile.json"); ProfileFile.Save(file, profiles[slot]);
        Check(ProfileFile.Load(file).SameContent(profiles[slot]), "individual export/import retains the axis and all other fields");
        DuplicateProfile(); int copy = selected;
        Check(profiles[copy].Mouse.PaddleAxis == PaddleOutputAxis.Y && profiles[copy].ProfileId != profiles[slot].ProfileId, "duplicate keeps Y and gets a new identity");
        await SaveSelected();
        SlotBank bank = SlotBank.FromProfiles(persistedProfiles).Move(copy, 2);
        string backup = Path.Combine(directory, "paddle-slots.json"); SlotBackup.Save(backup, bank);
        Check(SlotBackup.Load(backup).Encode().SequenceEqual(bank.Encode()) && bank.Profiles[2].Mouse.PaddleAxis == PaddleOutputAxis.Y, "bank backup, restore and reorder preserve exact bytes");
        DeviceProfile legacy = profiles[slot].Clone(); legacy.Mouse.PaddleAxis = PaddleOutputAxis.X;
        ProfileFile.Save(file, legacy);
        JsonNode oldJson = JsonNode.Parse(File.ReadAllText(file))!;
        oldJson["Profile"]!["Mouse"]!.AsObject().Remove("PaddleAxis"); File.WriteAllText(file, oldJson.ToJsonString());
        Check(ProfileFile.Load(file).SameContent(legacy), "JSON without the new property retains the old X behavior");
        byte[] legacyBinary = legacy.Encode()[..DeviceProfile.LegacyByteCount]; Wire.W16(legacyBinary, 0, 5);
        Check(DeviceProfile.Decode(legacyBinary).Mouse.PaddleAxis == PaddleOutputAxis.X, "legacy v5 binary migration stays X");
        selected = slot; RefreshSlots(); BindSelected();
        foreach (byte mode in new byte[] { 1, 2 })
        {
            profiles[slot].OutputMode = mode; BindSelected(); Size = MinimumSize; PerformLayout(); Application.DoEvents();
            foreach (Control control in paddleAxis.Parent!.Controls)
                Check(control.Visible && control.Parent!.ClientRectangle.Contains(control.Bounds), $"paddle control {control.GetType().Name} fits minimum window in mode {mode}");
            CapturePreview(Path.Combine(directory, mode == 1 ? "paddle-mouse.png" : "paddle-combined.png"));
        }
        File.WriteAllText(Path.Combine(directory, "paddle-axis-checks.json"), JsonSerializer.Serialize(new { passed = true, count = checks.Count, checks, hardwareOpened = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
