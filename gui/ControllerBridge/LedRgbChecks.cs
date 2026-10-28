using System.Text.Json;

namespace ControllerBridge;

internal sealed partial class MainForm
{
    internal static void RunLedChecks(string directory)
    {
        Directory.CreateDirectory(directory);
        using MainForm form = new(preview: true, autoConnect: false) { Opacity = 0, ShowInTaskbar = false };
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try { await form.CheckLedSettings(directory); }
            catch (Exception ex) { failure = ex; }
            finally { Array.Clear(form.dirty); form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) throw failure;
    }
    private async Task CheckLedSettings(string directory)
    {
        List<string> checks = [];
        void Check(bool ok, string description) { if (!ok) throw new InvalidDataException(description); checks.Add(description); }
        LedPreview.CheckCurve(Check);
        Check(profiles.All(p => p.LedRgb == 1 && !p.CustomLed), "existing/new profiles retain dim blue");
        Check(ledSettings.Parent == delete.Parent && ledSettings.Top >= delete.Bottom && ledSettings.Visible, "LED button sits immediately below Delete profile");
        Rectangle buttonBounds = RectangleToClient(ledSettings.RectangleToScreen(ledSettings.ClientRectangle));
        Check(ClientRectangle.Contains(buttonBounds), "LED button fits the normal window");
        CapturePreview(Path.Combine(directory, "main.png"));
        Size = MinimumSize; PerformLayout(); Application.DoEvents();
        Check(ClientRectangle.Contains(RectangleToClient(ledSettings.RectangleToScreen(ledSettings.ClientRectangle))), "LED button fits the minimum window");
        CapturePreview(Path.Combine(directory, "main-minimum.png"));
        DeviceProfile original = profiles[selected].Clone(); int slot = selected;
        LedRgbDialog.CheckDialog(this, original, directory, Check);
        Check(profiles[slot].SameContent(original) && !dirty[slot], "dialog exercise and cancellation leave the original untouched");
        using (LedRgbDialog previewOnly = new(original)) ApplyLedSettings(previewOnly);
        Check(!dirty[slot] && profiles[slot].SameContent(original), "fixed preview curve alone does not mark the profile dirty");
        using (LedRgbDialog dialog = new(original))
        {
            dialog.CheckSetColor(255, 128, 17); ApplyLedSettings(dialog);
        }
        Check(profiles[slot].LedRgb == 0xff8011 && dirty[slot] && persistedProfiles[slot].LedRgb == 1, "OK stages exact RGB without saving the device");
        byte[] raw = profiles[slot].Encode();
        Check(raw.Length == 488 && (raw[2] & 8) != 0 && raw[485] == 255 && raw[486] == 128 && raw[487] == 17, "RGB888 uses reserved bytes and flag with unchanged wire length");
        Check(DeviceProfile.Decode(raw).LedRgb == 0xff8011, "binary roundtrip preserves all channel bits");
        await SaveSelected();
        Check(!dirty[slot] && persistedProfiles[slot].LedRgb == 0xff8011, "slot save retains RGB");
        DuplicateProfile(); int copy = selected;
        Check(profiles[copy].LedRgb == 0xff8011 && profiles[copy].ProfileId != profiles[slot].ProfileId, "duplicate retains color with a new identity");
        await SaveSelected();
        string profileFile = Path.Combine(directory, "profile.json"); ProfileFile.Save(profileFile, profiles[copy]);
        Check(ProfileFile.Load(profileFile).SameContent(profiles[copy]), "individual JSON preserves color");
        SlotBank bank = SlotBank.FromProfiles(persistedProfiles).Move(copy, 1);
        Check(bank.Profiles[1].LedRgb == 0xff8011, "reordering carries RGB with the profile");
        string bankFile = Path.Combine(directory, "slots.json"); SlotBackup.Save(bankFile, bank);
        Check(SlotBackup.Load(bankFile).Encode().SequenceEqual(bank.Encode()), "whole-bank backup and restore preserve exact RGB bytes");
        DeviceProfile black = original.Clone(); black.SetLedColor(true, 0, 0, 0);
        Check(black.Clone().CustomLed && black.Clone().LedRgb == 0, "explicit black remains distinct from default blue");
        black.SetLedColor(true, 255, 255, 255); Check(black.Clone().LedRgb == 0xffffff, "full white is not brightness limited");
        black.SetLedColor(false); Check(black.Encode().SequenceEqual(original.Encode()), "default reset reproduces the original profile bytes");
        DeviceProfile next = new() { Flags = 1, Vid = 1, Name = "New profile" };
        Check(next.LedRgb == 1 && !next.CustomLed, "new registration defaults to dim blue");
        selected = Array.FindIndex(profiles, p => !p.Enabled); BindSelected();
        Check(!ledSettings.Visible && !ledSettings.Enabled, "unused slots do not offer LED settings");
        File.WriteAllText(Path.Combine(directory, "led-check.json"), JsonSerializer.Serialize(new { passed = true, count = checks.Count, checks, hardwareWrites = false, DeviceDpi }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

internal sealed partial class LedRgbDialog
{
    internal void CheckSetColor(byte red, byte green, byte blue)
    { custom.Checked = true; values[0].Value = red; values[1].Value = green; values[2].Value = blue; }

    internal static void CheckDialog(Form owner, DeviceProfile original, string directory, Action<bool, string> check)
    {
        static IEnumerable<Control> All(Control control)
        { foreach (Control child in control.Controls) { yield return child; foreach (Control nested in All(child)) yield return nested; } }
        using LedRgbDialog dialog = new(original) { Opacity = 0, ShowInTaskbar = false };
        dialog.Show(owner); Application.DoEvents();
        void Capture(string name)
        {
            dialog.PerformLayout(); Application.DoEvents();
            foreach (Control control in All(dialog).Where(c => c.Visible))
            {
                Rectangle bounds = dialog.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
                check(dialog.ClientRectangle.Contains(bounds), $"dialog contains {control.GetType().Name}: {control.Text}");
            }
            using Bitmap bitmap = new(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new(0, 0, dialog.Width, dialog.Height));
            bitmap.Save(Path.Combine(directory, name));
        }
        check(dialog.standard.Checked && dialog.values.All(v => !v.Enabled) && !dialog.hex.Enabled && dialog.hex.Text == "000001", "default controls retain dim blue and disable custom entry");
        check(dialog.swatch.BackColor.ToArgb() == Color.FromArgb(0, 0, 64).ToArgb(), "fixed gamma 4 lifts default blue 1 to display 64");
        check(dialog.hex.PointToScreen(Point.Empty).Y >= dialog.values[0].PointToScreen(new Point(0, dialog.values[0].Height)).Y, "hex entry is directly below the RGB spinners");
        Capture("led-default.png");
        dialog.CheckSetColor(255, 128, 17);
        check(dialog.values.All(v => v.Enabled && v.Minimum == 0 && v.Maximum == 255), "all channels accept 0 through 255");
        check(dialog.hex.Text == "FF8011" && dialog.hex.Enabled, "spinner edits synchronize the six-digit uppercase code");
        check(dialog.swatch.BackColor.ToArgb() == Color.FromArgb(255, 215, 130).ToArgb(), "fixed approximation converts unequal RGB channels independently");
        Capture("led-custom.png");
        dialog.hex.SelectAll(); dialog.hex.Paste("000004");
        check(dialog.values[0].Value == 0 && dialog.values[1].Value == 0 && dialog.values[2].Value == 4 && dialog.accept.Enabled, "pasting 000004 updates all three spinners");
        check(dialog.swatch.BackColor.ToArgb() == Color.FromArgb(0, 0, 90).ToArgb(), "pasted low blue uses fixed gamma 4");
        Capture("led-hex.png");
        DeviceProfile unchanged = original.Clone(); dialog.ApplyTo(unchanged);
        check(unchanged.LedRgb == 4 && dialog.hex.Text == "000004", "preview correction does not alter saved values or the code");
        dialog.hex.SelectAll(); dialog.hex.Paste("#000004"); dialog.ApplyTo(unchanged);
        check(dialog.hex.Text == "000004" && unchanged.LedRgb == 4 && dialog.accept.Enabled, "pasting #000004 strips the leading prefix and accepts the exact color");
        check(dialog.hex.SelectionStart == 6 && dialog.hex.SelectionLength == 0, "prefix removal preserves the paste caret at the end");
        dialog.hex.SelectAll(); dialog.hex.Paste("#aBcDeF"); dialog.ApplyTo(unchanged);
        check(dialog.hex.Text == "ABCDEF" && unchanged.LedRgb == 0xabcdef, "prefixed mixed-case color paste synchronizes all RGB channels");
        dialog.hex.SelectAll(); dialog.hex.Paste("aBcDeF"); dialog.ApplyTo(unchanged);
        check(unchanged.LedRgb == 0xabcdef && dialog.hex.Text == "ABCDEF", "native paste accepts mixed-case hex and displays uppercase");
        int lastColor = dialog.swatch.BackColor.ToArgb();
        foreach (string invalid in new[] { "", "00004", "1234567", "11223344", "GG0000", "##000004", "00#004", "#11223344", " FF0011", "FF0011 ", "0x0004" })
        {
            dialog.hex.SelectAll(); dialog.hex.Paste(invalid);
            bool rejected = false; try { dialog.ApplyTo(unchanged); } catch (InvalidDataException) { rejected = true; }
            check(!dialog.accept.Enabled && rejected && unchanged.LedRgb == 0xabcdef && dialog.swatch.BackColor.ToArgb() == lastColor, "invalid or incomplete paste cannot commit or silently truncate: " + invalid);
        }
        Capture("led-invalid.png");
        dialog.values[0].Value = 1;
        check(dialog.hex.Text == "01CDEF" && dialog.accept.Enabled && dialog.hexError.Text == "", "editing a spinner replaces an invalid draft with a valid synchronized code");
        dialog.standard.Checked = true;
        DeviceProfile applied = original.Clone(); dialog.ApplyTo(applied);
        check(applied.SameContent(original), "default selection clears custom data and preserves the profile");
        check(dialog.hex.Text == "000001" && !dialog.hex.Enabled, "default mode displays its exact code");
        dialog.CheckSetColor(0, 0, 0); dialog.ApplyTo(applied);
        check(applied.CustomLed && applied.LedRgb == 0, "dialog accepts black as explicit off");
        check(dialog.swatch.BackColor.ToArgb() == Color.Black.ToArgb(), "off remains black under correction");
        dialog.hex.Text = "F"; dialog.cancel.PerformClick(); check(dialog.DialogResult == DialogResult.Cancel, "Cancel works with an incomplete code without changing the source");
        using LedRgbDialog accepted = new(original) { Opacity = 0, ShowInTaskbar = false };
        accepted.Shown += (_, _) => { accepted.custom.Checked = true; accepted.hex.SelectAll(); accepted.hex.Paste("0d2ac8"); accepted.accept.PerformClick(); };
        check(accepted.ShowDialog(owner) == DialogResult.OK, "OK confirms the edited RGB");
        accepted.ApplyTo(applied); check(applied.LedRgb == 0x0d2ac8, "confirmed RGB is exact");
        using LedRgbDialog reopened = new(applied);
        check(reopened.hex.Text == "0D2AC8" && reopened.custom.Checked, "reopening restores the saved RGB code");
    }
}

internal static partial class LedPreview
{
    internal static void CheckCurve(Action<bool, string> check)
    {
        byte[] inputs = [0, 1, 4, 8, 32, 128, 255], expected = [0, 64, 90, 107, 152, 215, 255];
        check(inputs.Select(Channel).SequenceEqual(expected), "fixed gamma 4 reference points preserve endpoints and lift low PWM values");
        byte[] range = Enumerable.Range(0, 256).Select(i => (byte)i).ToArray();
        check(range.Zip(range.Skip(1), (a, b) => Channel(a) <= Channel(b)).All(v => v), "fixed preview is monotonic across all 256 levels");
    }
}
