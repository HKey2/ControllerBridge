using System.Text.Json;
using System.Text.Json.Nodes;

namespace ControllerBridge;

internal sealed partial class MainForm
{
    internal static void RunDirectionChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        using MainForm form = new(preview: true, autoConnect: false) { Opacity = 0, ShowInTaskbar = false };
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try { await form.CheckDirections(directory); }
            catch (Exception ex) { failure = ex; }
            finally { Array.Clear(form.dirty); form.Close(); }
        };
        Application.Run(form); if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private async Task CheckDirections(string directory)
    {
        List<string> checks = [];
        void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); checks.Add(message); }
        void Reject(Action action, string message)
        {
            bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, message);
        }
        int slot = selected;
        Check(profiles.All(p => !p.Mouse.TrackballToDpad && !p.Mouse.PaddleToDpad), "old and new profiles keep conversion disabled");
        Check(mouseGain.Maximum == 256 && mouseGain.Increment == .1m && mouseGain.Value == 16, "trackball gain expands to 256 with existing step and default");
        outputTabs.SelectedIndex = 1;
        byte[] original = profiles[slot].Encode();
        trackballKeys.Checked = true;
        Check(outputTabs.SelectedIndex == 2 && profiles[slot].OutputMode == 2, "enabling direction output selects the combined USB layout from mouse-only");
        Check(profiles[slot].Mouse.TrackballToDpad && !profiles[slot].Mouse.PaddleToDpad && dirty[slot], "trackball checkbox edits only its conversion bit");
        Check(profiles[slot].Encode()[480] == (original[480] | 64), "trackball conversion occupies bit 6");
        paddleKeys.Checked = true;
        Check(profiles[slot].Encode()[480] == (original[480] | 192) && paddleAxis.Enabled, "paddle conversion uses bit 7 and retains the shared axis selector");
        Check(mouseGain.Enabled && paddleGain.Enabled && invertX.Enabled && invertPaddle.Enabled, "cursor conversion leaves separate pointer sensitivity editable");
        Check(trackballKeys.Text.Contains("↑↓←→") && paddleKeys.Text.Contains("←→"), "profile conversion labels use arrows");
        paddleAxis.SelectedIndex = 1;
        Check(paddleKeys.Text.Contains("↑↓") && profiles[slot].Mouse.PaddleAxis == PaddleOutputAxis.Y, "Y changes the conversion label and saved axis together");
        paddleAxis.SelectedIndex = 0;
        Check(paddleKeys.Text.Contains("←→") && profiles[slot].Mouse.PaddleAxis == PaddleOutputAxis.X, "X restores horizontal conversion");
        bool testType = typeof(MainForm).Assembly.GetType("ControllerBridge.UdlrTestDialog") is not null;
        static IEnumerable<Control> AllControls(Control parent) => parent.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(AllControls(c)));
        bool testButton = AllControls(this).OfType<Button>().Any(b => b.Text == L.T("UDLRテスト…", "UDLR test…"));
#if UDLR_TEST
        Check(testType && testButton && Text.Contains("UDLR TEST"), "dedicated build contains the test dialog and button");
#else
        Check(!testType && !testButton && !Text.Contains("UDLR TEST"), "release assembly excludes the test dialog and button");
#endif
        mouseGain.Value = 256;
        byte[] encoded = profiles[slot].Encode();
        Check(encoded.Length == 848 && encoded[481] == 0 && encoded[483] == 10 && profiles[slot].Clone().Mouse.TrackballGain == 256, "256x uses the existing 16-bit tenths field");
        Check(persistedProfiles[slot].Mouse.TrackballGain == 16 && !persistedProfiles[slot].Mouse.DirectionKeysEnabled, "edits do not change saved settings");
        Capabilities old = caps! with { Features = 65535 }, current = old with { Features = 262143 };
        Reject(() => old.ValidatePointingSettings(profiles[slot]), "older firmware rejects the new settings before upload");
        current.ValidatePointingSettings(profiles[slot]); Check(current.SupportsGain256 && current.SupportsDirectionKeys, "current capability accepts both additions");
        DeviceProfile gainOnly = profiles[slot].Clone(); gainOnly.Mouse.TrackballToDpad = gainOnly.Mouse.PaddleToDpad = false;
        Reject(() => old.ValidatePointingSettings(gainOnly), "gain-only edits above 64 also require current firmware");
        DeviceProfile keysOnly = profiles[slot].Clone(); keysOnly.Mouse.TrackballGain = 16;
        Reject(() => old.ValidatePointingSettings(keysOnly), "conversion requires its own capability at the old gain");
        DeviceProfile invalid = profiles[slot].Clone(); invalid.OutputMode = 1;
        Reject(() => invalid.Encode(), "wire codec rejects a mouse-only layout that cannot carry enabled D-pad conversion");
        invalid.OutputMode = 0; invalid.Mouse.TrackballGain = 256.1m;
        Reject(() => invalid.Encode(), "gain above 256 is rejected");
        outputTabs.SelectedIndex = 0;
        Check(trackballKeys.Visible && paddleKeys.Visible && pointingTuning.Visible, "gamepad-only mode exposes the pointing controls");
        Check(profiles[slot].Mouse.DirectionKeysEnabled && paddleKeys.Checked, "gamepad-only mode preserves conversions");
        outputTabs.SelectedIndex = 1;
        Check(outputTabs.SelectedIndex == 2 && trackballKeys.Checked && paddleKeys.Checked, "requesting mouse-only with conversion retains settings in combined mode");
        trackballOn.Checked = false; paddleOn.Checked = false;
        Check(!trackballKeys.Enabled && !paddleKeys.Enabled && !profiles[slot].Mouse.DirectionKeysEnabled, "turning sources off disables controls while retaining preferences");
        Check(profiles[slot].Mouse.TrackballToDpad && profiles[slot].Mouse.PaddleToDpad, "source Off retains conversion bits");
        trackballOn.Checked = paddleOn.Checked = true;
        await SaveSelected(); Check(!dirty[slot] && persistedProfiles[slot].Mouse.TrackballGain == 256 && persistedProfiles[slot].Mouse.DirectionKeysEnabled, "preview save/readback retains gain and both flags");
        slots.SelectedIndex = 1; Check(!trackballKeys.Checked && !paddleKeys.Checked && mouseGain.Value == 16, "other profile keeps its original settings");
        slots.SelectedIndex = slot; Check(trackballKeys.Checked && paddleKeys.Checked && mouseGain.Value == 256, "returning restores the selected profile");
        DuplicateProfile(); int copy = selected; await SaveSelected();
        Check(profiles[copy].Mouse.DirectionKeysEnabled && profiles[copy].ProfileId != profiles[slot].ProfileId, "duplicate preserves settings with a new identity");
        string path = Path.Combine(directory, "directions.json"); ProfileFile.Save(path, profiles[slot]);
        Check(ProfileFile.Load(path).SameContent(profiles[slot]), "individual JSON roundtrip preserves 256 and flags");
        SlotBank bank = SlotBank.FromProfiles(persistedProfiles).Move(copy, 2);
        string backup = Path.Combine(directory, "bank.json"); SlotBackup.Save(backup, bank);
        Check(SlotBackup.Load(backup).Encode().SequenceEqual(bank.Encode()), "whole-bank restore/reorder preserves all bytes");
        DeviceProfile legacy = new() { Flags = 1, Vid = 0x1234, Name = "Legacy" }; ProfileFile.Save(path, legacy);
        JsonNode json = JsonNode.Parse(File.ReadAllText(path))!;
        json["Profile"]!["Mouse"]!.AsObject().Remove("TrackballToDpad"); json["Profile"]!["Mouse"]!.AsObject().Remove("PaddleToDpad");
        File.WriteAllText(path, json.ToJsonString()); Check(ProfileFile.Load(path).SameContent(legacy), "JSON lacking both flags retains old behavior");
        selected = slot; RefreshSlots(); BindSelected();
        Check(outputStatus.Text.Contains("256.0×") && outputStatus.Text.Contains("1.0×"), "summary retains pointer gains and does not substitute hidden cursor gains");
        Check(mouseGain.Enabled && paddleGain.Enabled, "pointer gains remain editable with both cursor conversions on");
        paddleGain.Value = 2.5m;
        Check(profiles[slot].Mouse.PaddleGain == 2.5m && profiles[slot].Mouse.PaddleToDpad, "editing pointer sensitivity retains cursor conversion");
        await SaveSelected();
        Check(persistedProfiles[slot].Mouse.PaddleGain == 2.5m && persistedProfiles[slot].Mouse.TrackballGain == 256, "pointer gains persist independently while conversion is enabled");
        trackballKeys.Checked = false; paddleKeys.Checked = false;
        Check(mouseGain.Enabled && paddleGain.Enabled && mouseGain.Value == 256, "disabling conversion restores the retained mouse sensitivity");
        trackballKeys.Checked = true; paddleKeys.Checked = true;
        foreach (byte mode in new byte[] { 0, 2 })
        {
            profiles[slot].OutputMode = mode; BindSelected(); Size = MinimumSize; PerformLayout(); Application.DoEvents();
            foreach (Control row in pointingTuning.Controls)
                foreach (Control c in row.Controls)
                    if (c.Visible) Check(c.Parent!.ClientRectangle.Contains(c.Bounds), $"pointing control fits mode {mode}: {c.GetType().Name}");
            CapturePreview(Path.Combine(directory, mode == 0 ? "direction-gamepad.png" : "direction-combined.png"));
        }
        File.WriteAllText(Path.Combine(directory, "direction-checks.json"), JsonSerializer.Serialize(new { passed = true, count = checks.Count, checks, hardwareOpened = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
