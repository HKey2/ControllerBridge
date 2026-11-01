using System.Text.Json;

namespace ControllerBridge;

internal sealed partial class RapidFireDialog
{
    internal static void RunChecks(string directory)
    {
        Directory.CreateDirectory(directory); int checks = 0;
        void Check(bool condition, string description) { checks++; if (!condition) throw new InvalidDataException("Rapid fire: " + description); }
        static IEnumerable<Control> Children(Control parent)
        { foreach (Control child in parent.Controls) { yield return child; foreach (Control descendant in Children(child)) yield return descendant; } }
        static void Click(Control parent, string name) => Children(parent).OfType<Button>().Single(b => b.Text == name).PerformClick();
        static void Show(Form form) { form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); Application.DoEvents(); }
        DeviceProfile profile = new() { Flags = 1, Vid = 0x1234, Name = "Rapid range", ProfileId = Guid.NewGuid() };
        byte[] originalRates = [0, 10, 30, 2, 1, 60];
        for (int i = 0; i < originalRates.Length; i++) profile.Buttons[i] = new() { Bits = 1, BitOffset = (ushort)i, Mode = 1, Low = 1, High = 1, RapidHz = originalRates[i] };
        byte[] original = profile.Encode();
        using (RapidFireDialog dialog = new(profile))
        {
            Show(dialog);
            Check(dialog.rates.All(r => r.Minimum == 1 && r.Maximum == 60 && r.Increment == 1), "every output offers 1 through 60 in single steps");
            Check(dialog.rates[0].Value == 20 && !dialog.enabled[0].Checked && !dialog.rates[0].Enabled, "new rate is 20 but opening leaves rapid fire off");
            Check(Enumerable.Range(1, 5).All(i => dialog.rates[i].Value == originalRates[i] && dialog.enabled[i].Checked), "stored 10, 30, 2, 1 and 60 rates remain exact");
            Check(!dialog.enabled[6].Enabled && !dialog.rates[6].Enabled, "unassigned output stays unavailable");
            Check(profile.Encode().SequenceEqual(original), "opening does not rewrite saved values");
            dialog.enabled[0].Checked = true;
            Check(dialog.rates[0].Enabled && dialog.rates[0].Value == 20, "newly enabling an off output starts at 20");
            dialog.PerformLayout(); dialog.Refresh();
            using Bitmap bitmap = new(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new(0, 0, dialog.Width, dialog.Height)); bitmap.Save(Path.Combine(directory, "rapid-settings.png"));
            Click(dialog, L.T("適用", "Apply"));
            Check(dialog.DialogResult == DialogResult.OK && dialog.Rates[0] == 20, "Apply returns the new default");
            Check(Enumerable.Range(1, 5).All(i => dialog.Rates[i] == originalRates[i]) && dialog.Rates.Skip(6).All(r => r == 0), "Apply preserves existing rates and unassigned outputs");
            Check(profile.Encode().SequenceEqual(original), "dialog Apply remains isolated from the owning profile");
        }
        using (RapidFireDialog dialog = new(profile))
        {
            Show(dialog); Click(dialog, L.T("適用", "Apply"));
            Check(dialog.Rates[0] == 0, "unchanged off setting stays zero instead of becoming 20");
        }
        using (RapidFireDialog dialog = new(profile))
        {
            Show(dialog); dialog.rates[1].Value = 60; Click(dialog, L.T("キャンセル", "Cancel"));
            Check(dialog.DialogResult == DialogResult.Cancel && profile.Encode().SequenceEqual(original), "Cancel preserves existing data");
        }
        using (RapidFireDialog dialog = new(profile))
        {
            Show(dialog); Click(dialog, L.T("すべてOFF", "All OFF")); Click(dialog, L.T("適用", "Apply"));
            Check(dialog.Rates.All(r => r == 0), "All OFF remains zero for all outputs");
        }
        profile.Shift.Keys.Add(new() { Bits = 1, BitOffset = 7, Mode = 1, Low = 1, High = 1 });
        profile.Shift.Overrides = 1; profile.Shift.Buttons[0] = ShiftSettings.Copy(profile.Buttons[0]);
        foreach (byte rate in new byte[] { 1, 20, 60 })
        {
            profile.Buttons[0].RapidHz = rate; profile.Shift.Buttons[0].RapidHz = rate;
            byte[] bytes = profile.Encode(); DeviceProfile restored = DeviceProfile.Decode(bytes);
            Check(restored.Buttons[0].RapidHz == rate && restored.Shift.Buttons[0].RapidHz == rate && restored.Encode().SequenceEqual(bytes), "main and shifted rates round trip at " + rate);
            string path = Path.Combine(directory, "rapid-" + rate + ".json"); ProfileFile.Save(path, profile);
            Check(ProfileFile.Load(path).SameContent(profile), "profile JSON preserves rate " + rate);
            DeviceProfile[] slots = Enumerable.Range(0, 16).Select(_ => new DeviceProfile()).ToArray(); slots[0] = profile.Clone();
            string bankPath = Path.Combine(directory, "bank-" + rate + ".json"); SlotBackup.Save(bankPath, SlotBank.FromProfiles(slots));
            Check(SlotBackup.Load(bankPath).Profiles[0].SameContent(profile), "whole-bank backup preserves rate " + rate);
        }
        foreach (byte rate in new byte[] { 61, 255 })
        {
            profile.Shift.Buttons[0].RapidHz = rate; bool rejected = false;
            try { profile.Encode(); } catch (InvalidDataException) { rejected = true; }
            Check(rejected, "shifted rate above the new maximum is rejected");
        }
        File.WriteAllText(Path.Combine(directory, "rapid-checks.json"), JsonSerializer.Serialize(new { passed = true, checks, hardwareOpened = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
