namespace ControllerBridge;

internal sealed partial class MainForm
{
    private readonly ContextMenuStrip profileMenu = new() { ShowImageMargin = false, BackColor = Ui.Surface, ForeColor = Ui.Ink };

    private void InitializeProfileMenu()
    {
        slots.MouseUp += (_, e) => { if (e.Button == MouseButtons.Right) ShowProfileMenu(e.Location); };
        slots.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Apps && !(e.Shift && e.KeyCode == Keys.F10)) return;
            if (slots.SelectedIndex >= 0)
            {
                Rectangle row = slots.GetItemRectangle(slots.SelectedIndex);
                ShowProfileMenu(new(row.Left + LogicalToDeviceUnits(12), row.Top + row.Height / 2));
            }
            e.Handled = e.SuppressKeyPress = true;
        };
        FormClosed += (_, _) => profileMenu.Dispose();
    }

    private void ShowProfileMenu(Point location)
    {
        profileMenu.Close();
        if (working || closing) return;
        int slot = slots.IndexFromPoint(location);
        if (slot < 0 || !slots.GetItemRectangle(slot).Contains(location)) return;
        AbortLearning(); slots.SelectedIndex = slot;
        foreach (ToolStripItem item in profileMenu.Items.Cast<ToolStripItem>().ToArray()) item.Dispose();
        profileMenu.Items.Clear();
        bool export = profiles[slot].Enabled;
        profileMenu.Items.Add(export ? L.T("エクスポート", "Export") : L.T("インポート", "Import"), null,
            async (_, _) => await TransferProfileFile(slot, export));
        profileMenu.Show(slots, location);
    }

    private async Task TransferProfileFile(int slot, bool export)
    {
        if (working || closing) return;
        working = true; UpdateEnabled(); AbortLearning(); await WaitForPoll();
        try
        {
            // A poll already in flight may have replaced the bank before it stopped.
            if (profiles[slot].Enabled != export) return;
            if (export)
            {
                DeviceProfile copy = profiles[slot].Clone();
                string safeName = string.Concat(copy.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).TrimEnd('.', ' ');
                using SaveFileDialog dialog = new() { Filter = "ControllerBridge profile (*.json)|*.json", DefaultExt = "json", AddExtension = true,
                    OverwritePrompt = true, FileName = "ControllerBridge-" + safeName + ".json", Title = L.T("プロファイルをエクスポート", "Export profile") };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ProfileFile.Save(dialog.FileName, copy);
                SetText(saved, L.T("選択したプロファイルをエクスポートしました。", "Exported the selected profile."));
            }
            else
            {
                using OpenFileDialog dialog = new() { Filter = "ControllerBridge profile (*.json)|*.json", CheckFileExists = true,
                    Title = L.T("プロファイルをインポート", "Import profile") };
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                ImportProfileFile(slot, dialog.FileName);
            }
        }
        catch (Exception ex) { Ui.Error(this, ex); }
        finally { working = false; UpdateEnabled(); }
    }

    private void ImportProfileFile(int slot, string path)
    {
        if (profiles[slot].Enabled) throw new InvalidDataException(L.T("未使用スロットを選んでください。", "Select an unused slot."));
        DeviceProfile imported = ProfileFile.Load(path);
        if (!imported.Enabled) throw new InvalidDataException(L.T("ファイルに登録済みプロファイルがありません。", "The file does not contain an enabled profile."));
        // A copied file may carry an identity already present in either drafts or the device bank.
        if (imported.ProfileId == Guid.Empty || Enumerable.Range(0, profiles.Length).Any(i => i != slot &&
            (profiles[i].Enabled && profiles[i].ProfileId == imported.ProfileId || persistedProfiles[i].Enabled && persistedProfiles[i].ProfileId == imported.ProfileId)))
            imported.ProfileId = Guid.NewGuid();
        if (profiles.Any(p => p.Enabled && string.Equals(p.Name, imported.Name, StringComparison.OrdinalIgnoreCase)))
            imported.Name = ProfileSlots.DuplicateName(profiles, imported.Name);
        // Validate everything before modifying any draft. Saving remains an explicit slot operation.
        _ = imported.Encode();
        profiles[slot] = imported; selected = slot; MarkDirty(); RefreshSlots(); BindSelected();
        SetText(saved, L.F("スロット {0} にインポートしました。「スロットを本体に保存」で反映します。", "Imported into slot {0}. Choose “Save slot to device” to apply it.", slot + 1));
    }
}
