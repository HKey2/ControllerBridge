using System.Text;
using System.Text.Json;

namespace ControllerBridge;

internal sealed partial class MainForm
{
    internal async Task CheckProfileMenu(string directory)
    {
        Directory.CreateDirectory(directory);
        if (!preview) throw new InvalidOperationException("Profile checks use preview data only.");
        List<string> checks = [];
        void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); checks.Add(message); }
        void Rejected(Action action, string message)
        {
            bool rejected = false;
            try { action(); } catch (Exception ex) when (ex is InvalidDataException or JsonException) { rejected = true; }
            Check(rejected, message);
        }
        void Menu(int slot, string label, string capture)
        {
            Rectangle row = slots.GetItemRectangle(slot);
            ShowProfileMenu(new(row.Left + 12, row.Top + row.Height / 2));
            Check(selected == slot && profileMenu.Visible && profileMenu.Items.Count == 1 && profileMenu.Items[0].Text == label, label + " is the only action for the clicked row");
            using Bitmap bitmap = new(profileMenu.Width, profileMenu.Height);
            profileMenu.DrawToBitmap(bitmap, new(Point.Empty, bitmap.Size)); bitmap.Save(Path.Combine(directory, capture)); profileMenu.Close();
        }
        try
        {
            DeviceProfile original = profiles[4].Clone();
            DuplicateProfile();
            Check(selected == 6 && profiles[6].Name == original.Name + " [2]" && name.Text == profiles[6].Name && dirty[6], "duplicate stores [2] in the name and selects the new draft");
            Check(profiles[6].ProfileId != original.ProfileId && profiles[6].OutputMode == original.OutputMode, "duplicate has a new identity and retains output mode");
            await SaveSelected();
            Check(persistedProfiles[6].Name.EndsWith(" [2]") && !dirty[6], "slot save preserves the numbered name");
            DuplicateProfile();
            Check(selected == 7 && profiles[7].Name == original.Name + " [3]", "copying a copy increments without nested suffixes");
            await SaveSelected();
            SlotBank reordered = SlotBank.FromProfiles(persistedProfiles).Move(7, 6);
            Check(reordered.Profiles[6].Name.EndsWith(" [3]"), "reorder keeps the stored suffix");
            string longName = new string('あ', 14) + "😀";
            DeviceProfile[] occupied = Enumerable.Range(2, 8).Select(n => new DeviceProfile { Flags = 1, Name = new string('あ', 14) + $" [{n}]" }).ToArray();
            string shortened = ProfileSlots.DuplicateName(occupied, longName);
            Check(shortened.EndsWith(" [10]") && Encoding.UTF8.GetByteCount(shortened) <= 47 && !shortened.Contains('�'), "UTF-8 truncation retains complete characters and a two-digit suffix");
            int width = slots.GetItemRectangle(4).Width - LogicalToDeviceUnits(20);
            int needed = TextRenderer.MeasureText("16  " + original.Name + " [16] *", Font, Size.Empty, TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width;
            Check(needed <= width, $"paddle name including suffix fits: {needed} <= {width} pixels at {DeviceDpi} DPI");
            Menu(6, L.T("エクスポート", "Export"), "export-menu.png");
            Menu(8, L.T("インポート", "Import"), "import-menu.png");
            ShowProfileMenu(new(-1, -1)); Check(!profileMenu.Visible, "outside a row no menu opens");
            working = true; ShowProfileMenu(new(12, 12)); working = false;
            Check(!profileMenu.Visible, "busy state suppresses the menu");
            string path = Path.Combine(directory, "individual.json"); ProfileFile.Save(path, original);
            Check(ProfileFile.Load(path).SameContent(original), "individual JSON preserves all profile bytes and identity");
            DeviceProfile revised = original.Clone(); revised.OutputMode = 1; ProfileFile.Save(path, revised);
            Check(ProfileFile.Load(path).SameContent(revised), "existing export is atomically replaced and verified");
            DeviceProfile[] before = profiles.Select(p => p.Clone()).ToArray();
            ImportProfileFile(8, path);
            Check(profiles[8].Name == original.Name + " [4]" && profiles[8].ProfileId != original.ProfileId, "import resolves existing name and identity collisions");
            Check(dirty[8] && !persistedProfiles[8].Enabled && profiles[8].OutputMode == 1 && selected == 8, "import stages an unsaved profile and binds its output controls");
            Check(Enumerable.Range(0, 16).Where(i => i != 8).All(i => profiles[i].SameContent(before[i])), "import leaves every other draft unchanged");
            Rejected(() => ImportProfileFile(8, path), "occupied slots reject import");
            string invalid = Path.Combine(directory, "invalid.json");
            File.WriteAllText(invalid, "{"); Rejected(() => ImportProfileFile(9, invalid), "malformed JSON is rejected");
            SlotBackup.Save(invalid, SlotBank.FromProfiles(persistedProfiles)); Rejected(() => ImportProfileFile(9, invalid), "whole-bank JSON is not an individual profile");
            ProfileFile.Save(invalid, new()); Rejected(() => ImportProfileFile(9, invalid), "empty profile is rejected");
            File.WriteAllText(invalid, new string(' ', 256 * 1024 + 1)); Rejected(() => ImportProfileFile(9, invalid), "oversized JSON is rejected before loading");
            Check(!profiles[9].Enabled && !dirty[9] && dirty[8], "failed imports preserve empty target and existing draft");
            revised.Name = "Unique imported profile"; revised.ProfileId = Guid.NewGuid(); ProfileFile.Save(path, revised); ImportProfileFile(9, path);
            Check(profiles[9].SameContent(revised), "unique imported name and identity are retained");
            selected = 6; RefreshSlots(); BindSelected(); CapturePreview(Path.Combine(directory, "profile-list.png"));
            File.WriteAllText(Path.Combine(directory, "profile-check.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally { working = false; profileMenu.Close(); LoadPreview(); }
    }
}
