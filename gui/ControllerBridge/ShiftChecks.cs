using System.Security.Cryptography;
using System.Text.Json;

namespace ControllerBridge;

internal sealed partial class ShiftDialog
{
    internal static void RunChecks(string directory)
    {
        Directory.CreateDirectory(directory); int checks = 0;
        void Check(bool condition, string description) { checks++; if (!condition) throw new InvalidDataException("Button shift: " + description); }
        void Reject(Action action, string description) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Check(rejected, description); }
        static IEnumerable<Control> Children(Control parent)
        { foreach (Control child in parent.Controls) { yield return child; foreach (Control descendant in Children(child)) yield return descendant; } }
        static void Click(Control parent, string name) => Children(parent).OfType<Button>().Single(b => b.Text == name).PerformClick();
        static void Input(ShiftDialog dialog, InputField field, int value) { field.Value = value; dialog.OnInput(field, value); }
        static void Capture(Form dialog, string path)
        {
            dialog.PerformLayout(); Application.DoEvents(); dialog.Refresh();
            using Bitmap bitmap = new(dialog.Width, dialog.Height); dialog.DrawToBitmap(bitmap, new(0, 0, dialog.Width, dialog.Height)); bitmap.Save(path);
        }
        List<InputField> fields = Enumerable.Range(0, 8).Select(i => new InputField { Index = (ushort)i, Interface = 0, BitOffset = (ushort)i, Bits = 1, UsagePage = 9, Usage = (ushort)(i + 11), Minimum = 0, Maximum = 1, Value = 0, Valid = true }).ToList();
        DeviceProfile profile = new() { Flags = 1, Vid = 0x1234, Pid = 0x5678, Name = "Shift demo", ProfileId = Guid.NewGuid() };
        profile.Buttons[0] = fields[0].Digital(1, 1, 1); profile.Buttons[3] = fields[3].Digital(1, 1, 1);
        profile.Axes[0] = new() { Mode = 2, Negative = fields[4].Digital(1, 1, 1), Positive = fields[5].Digital(1, 1, 1) };
        using (ShiftDialog dialog = new(profile, fields, true, true, () => Task.FromResult(true)))
        {
            dialog.ShowInTaskbar = false; dialog.Opacity = 0; dialog.Show(); Application.DoEvents();
            dialog.Learn().GetAwaiter().GetResult(); Input(dialog, fields[1], 1); Input(dialog, fields[2], 1);
            Check(dialog.Result.Keys.Count == 0 && dialog.status.Text.Contains(fields[1].Label) && dialog.status.Text.Contains(fields[2].Label), "all held buttons are shown by their log/status numbers before registration");
            Click(dialog, L.T("編集に反映", "Apply to draft"));
            Check(dialog.Visible && dialog.DialogResult != DialogResult.OK && dialog.Result.Keys.Count == 0, "Apply waits for the pending chord to be released");
            Capture(dialog, Path.Combine(directory, "shift-learning.png"));
            Input(dialog, fields[1], 0); Check(dialog.Result.Keys.Count == 0, "releasing one member keeps chord learning active");
            Input(dialog, fields[2], 0);
            Check(dialog.Result.Keys.Count == 2 && dialog.Result.Keys[0].BitOffset == 1 && dialog.Result.Keys[1].BitOffset == 2, "unmapped physical keys learned as an AND chord");
            InputLogBuffer log = new(); log.Field(fields[1], 1, true, 1, false);
            Check(dialog.keys.Items[0]!.ToString()!.StartsWith(log[0][4] + "  [") && dialog.keys.Items[0]!.ToString()!.Contains("bit1"), "list uses HID button number, not field index or bit offset, and retains source details");
            dialog.Learn().GetAwaiter().GetResult(); Input(dialog, fields[1], 1); Input(dialog, fields[1], 0);
            Check(dialog.Result.Keys.Count == 2, "duplicate modifier rejected");
            dialog.editor.SelectButton(2); Input(dialog, fields[0], 1); Input(dialog, fields[0], 0);
            Check(dialog.Result.Overrides == 4 && dialog.Result.Buttons[2].BitOffset == 0, "back-page button learned without altering main mapping");
            Check(profile.Shift.IsEmpty && profile.Buttons[0].BitOffset == 0, "dialog changes stay isolated in a draft");
            dialog.editor.SelectButton(4); Click(dialog.editor, L.T("出力なし", "No output"));
            Check((dialog.Result.Overrides & 16) != 0 && dialog.Result.Buttons[4].Mode == 0, "explicit no-output differs from inheritance");
            Click(dialog.editor, L.T("表から継承", "Inherit from main"));
            Check((dialog.Result.Overrides & 16) == 0, "restore inheritance clears override");
            foreach (bool inherit in new[] { false, true })
            {
                TaskCompletionSource<bool> preparation = new(); dialog.editor.PrepareLearningAsync = () => preparation.Task;
                dialog.editor.SelectButton(4); Click(dialog.editor, inherit ? L.T("表から継承", "Inherit from main") : L.T("出力なし", "No output"));
                preparation.SetResult(true); Application.DoEvents(); Input(dialog, fields[7], 1); Input(dialog, fields[7], 0);
                Check(dialog.Result.Buttons[4].Mode == 0 && ((dialog.Result.Overrides & 16) == 0) == inherit, "cancelled asynchronous learning cannot overwrite " + (inherit ? "inheritance" : "no output"));
            }
            dialog.editor.PrepareLearningAsync = () => Task.FromResult(true);
            Check(!Children(dialog.editor).OfType<Button>().Any(b => b.Visible && (b.Text.StartsWith(L.T("左 X", "Left X"), StringComparison.Ordinal) || b.Text.StartsWith(L.T("右 X", "Right X"), StringComparison.Ordinal))), "shift editor has no independent axes");
            dialog.draft.Shift.Buttons[2].RapidHz = 7; profile.Shift = dialog.draft.Clone().Shift;
            dialog.RefreshKeys(); Capture(dialog, Path.Combine(directory, "shift-settings.png"));
            dialog.ConnectionLost(); Check(!dialog.add.Enabled, "connection loss disables new learning");
            int keyCount = dialog.Result.Keys.Count; Input(dialog, fields[6], 1); Check(dialog.Result.Keys.Count == keyCount, "late input after disconnect does not change modifiers");
            dialog.Close();
        }
        byte[] encoded = profile.Encode();
        Check(encoded.Length == 848 && Wire.U16(encoded, 0) == 6 && Wire.U32(encoded, 488) == 4 && encoded[492] == 2, "v6 header, override mask and modifier count");
        Check(encoded[496 + 2] == 1 && encoded[512 + 2] == 2 && encoded[560 + 2 * 16 + 7] == 7, "modifier and shifted rapid bindings use agreed wire offsets");
        Check(profile.Clone().SameContent(profile), "shift, axes and identity round trip");
        string json = Path.Combine(directory, "shift-profile.json"); ProfileFile.Save(json, profile);
        Check(ProfileFile.Load(json).SameContent(profile), "JSON export preserves all shift settings");
        File.WriteAllBytes(Path.Combine(directory, "gui-shift-profile.bin"), encoded);
        DeviceProfile malformed = profile.Clone(); malformed.Shift.Keys.Add(ShiftSettings.Copy(malformed.Shift.Keys[0])); Reject(() => malformed.Encode(), "duplicate physical modifier rejected by codec");
        malformed = profile.Clone(); malformed.Shift.Keys = fields.Take(5).Select(f => f.Digital(1, 1, 1)).ToList(); Reject(() => malformed.Encode(), "more than four keys rejected");
        byte[] corrupt = (byte[])encoded.Clone(); corrupt[493] = 1; Reject(() => DeviceProfile.Decode(corrupt), "unknown shift flags rejected");
        malformed = profile.Clone(); malformed.Buttons[1] = fields[1].Digital(1, 1, 1); Reject(() => malformed.Shift.ValidateSwitchKeys(malformed, 3), "overlapping profile shortcut rejected");
        profile.Shift.ValidateSwitchKeys(profile, 9); Check(true, "unrelated profile shortcut permitted");
        // A v5 record and JSON file upgrade without changing the original settings or identity.
        DeviceProfile legacy = profile.Clone(); legacy.Shift = new(); byte[] old = legacy.Encode()[..488]; Wire.W16(old, 0, 5);
        DeviceProfile converted = DeviceProfile.Decode(old); Check(converted.Version == 6 && converted.Shift.IsEmpty && converted.Encode().AsSpan(2, 486).SequenceEqual(old.AsSpan(2)), "legacy binary prefix preserved");
        legacy.Version = 5; string oldJson = Path.Combine(directory, "legacy-profile.json");
        File.WriteAllText(oldJson, JsonSerializer.Serialize(new ProfileFile { Version = 2, Profile = legacy }));
        converted = ProfileFile.Load(oldJson); Check(converted.Version == 6 && converted.ProfileId == legacy.ProfileId && converted.Shift.IsEmpty, "v2/v5 JSON imports with shift disabled");
        // Sixteen distinct slots, common keys, preferred selection and GUID bytes survive bank migration.
        DeviceProfile[] profiles = Enumerable.Range(0, 16).Select(i => new DeviceProfile { Flags = 1, Vid = 0x1234, Pid = (ushort)(0x5000 + i), Name = $"Legacy {i + 1}", ProfileId = Guid.NewGuid() }).ToArray();
        profiles[5].Buttons[0] = fields[0].Digital(1, 1, 1); profiles[5].Buttons[0].RapidHz = 11; profiles[5].SetLedColor(true, 100, 50, 25);
        uint[] keys = new uint[16]; keys[5] = 9; SlotBank current = SlotBank.FromProfiles(profiles, 1u << 5, keys);
        byte[] oldBank = new byte[8148], currentBytes = current.Encode(); currentBytes.AsSpan(0, 16).CopyTo(oldBank); Wire.W32(oldBank, 4, 5); Wire.W32(oldBank, 12, 8148);
        for (int i = 0; i < 16; i++) { currentBytes.AsSpan(16 + i * 848, 488).CopyTo(oldBank.AsSpan(16 + i * 488)); Wire.W16(oldBank, 16 + i * 488, 5); }
        currentBytes.AsSpan(SlotBank.IdsAt, 324).CopyTo(oldBank.AsSpan(7824));
        SlotBank migrated = new(oldBank); Check(migrated.Encode().SequenceEqual(currentBytes), "v5 bank migration preserves all slots and metadata");
        string backup = Path.Combine(directory, "legacy-slots.json"); File.WriteAllText(backup, JsonSerializer.Serialize(new SlotBackup { Version = 2, Bank = oldBank, Sha256 = Convert.ToHexString(SHA256.HashData(oldBank)) }));
        Check(SlotBackup.Load(backup).Encode().SequenceEqual(currentBytes), "legacy backup checksum verified before migration");
        oldBank[16 + 15 * 488] = 4; Reject(() => new SlotBank(oldBank), "mixed legacy profile versions rejected");
        profiles[5] = profile.Clone(); current = SlotBank.FromProfiles(profiles); SlotBackup.Save(Path.Combine(directory, "shift-slots.json"), current);
        Check(SlotBackup.Load(Path.Combine(directory, "shift-slots.json")).Profiles[5].SameContent(profile), "new bank backup preserves shifted mappings");
        Check(current.Move(5, 2).Profiles[2].SameContent(profile), "slot reorder retains shifted mappings");
        MainForm.CheckShiftHost(directory, Check);
        CheckLearningGestures(directory, Check);
        File.WriteAllText(Path.Combine(directory, "shift-checks.json"), JsonSerializer.Serialize(new { passed = true, checks, hardwareOpened = false }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void CheckLearningGestures(string directory, Action<bool, string> check)
    {
        static void Input(ShiftDialog dialog, InputField field, int value) { field.Value = value; dialog.OnInput(field, value); }
        static List<InputField> Fields() => Enumerable.Range(0, 6).Select(i => new InputField { Index = (ushort)i,
            Interface = (byte)(i / 2), ReportId = (byte)(i % 2 + 1), Flags = 2, Bits = 1, BitOffset = 9,
            UsagePage = 9, Usage = (ushort)(i + 21), Maximum = 1, Valid = true }).ToList();
        static ShiftDialog Open(DeviceProfile profile, List<InputField> fields, bool online = true, Func<Task<bool>>? prepare = null)
        {
            ShiftDialog dialog = new(profile, fields, online, true, prepare ?? (() => Task.FromResult(true))) { ShowInTaskbar = false, Opacity = 0 };
            dialog.Show(); Application.DoEvents(); return dialog;
        }
        DeviceProfile profile = new() { Flags = 1, Vid = 0x1234, Name = "Chord checks" };
        List<InputField> fields = Fields();
        using (ShiftDialog dialog = Open(profile, fields))
        {
            dialog.Learn().GetAwaiter().GetResult();
            foreach (InputField field in fields.Take(4)) Input(dialog, field, 1);
            Input(dialog, fields[0], 1);
            foreach (InputField field in fields.Take(4).Reverse()) Input(dialog, field, 0);
            check(dialog.Result.Keys.Count == 4 && !dialog.add.Enabled, "four distinct sources across interfaces/report IDs register together without repeats");
            check(dialog.Result.Keys.Select(k => (k.Interface, k.ReportId)).Distinct().Count() == 4, "same bit positions on separate reports stay distinct");
            check(dialog.keys.Items[3]!.ToString()!.StartsWith(fields[3].Label), "last key uses its own report's label");
            dialog.keys.SelectedIndex = 3; dialog.remove.PerformClick();
            check(dialog.Result.Keys.Count == 3 && dialog.add.Enabled, "removing a key frees capacity");
            dialog.Learn().GetAwaiter().GetResult(); Input(dialog, fields[0], 1); Input(dialog, fields[3], 1);
            Input(dialog, fields[3], 0); Input(dialog, fields[0], 0);
            check(dialog.Result.Keys.Count == 4, "an existing key plus a new key only consumes one remaining slot");
            profile = dialog.draft.Clone(); dialog.Close();
        }
        using (ShiftDialog dialog = Open(profile, fields))
            check(dialog.keys.Items[2]!.ToString()!.StartsWith(fields[2].Label), "saved bindings regain their log labels when reopened");
        using (ShiftDialog dialog = Open(profile, fields, false))
            check(dialog.keys.Items.Cast<string>().All(s => s.StartsWith(L.T("入力名未確認", "Input name unavailable"))), "unrelated/disconnected descriptor is never used to guess button numbers");
        profile.Shift.Keys.RemoveRange(2, 2);
        using (ShiftDialog dialog = Open(profile, fields))
        {
            dialog.Learn().GetAwaiter().GetResult();
            foreach (InputField field in fields.Skip(2).Take(3)) Input(dialog, field, 1);
            foreach (InputField field in fields.Skip(2).Take(3)) Input(dialog, field, 0);
            check(dialog.Result.Keys.Count == 2 && dialog.status.Text.Contains(L.T("登録していません", "not added")), "over-capacity gesture is rejected atomically without changing existing keys");
            dialog.Learn().GetAwaiter().GetResult(); Input(dialog, fields[4], 1); Input(dialog, fields[4], 0);
            check(dialog.Result.Keys.Count == 3, "single-key addition still works after overflow");
            Input(dialog, fields[5], 1); Input(dialog, fields[5], 0);
            check(dialog.Result.Keys.Count == 3, "later separate gestures are not registered without another Learn action");
        }
        fields = Fields(); fields[0].Value = 1;
        using (ShiftDialog dialog = Open(new() { Flags = 1, Vid = 0x1234, Name = "Chord checks" }, fields))
        {
            dialog.Learn().GetAwaiter().GetResult(); Input(dialog, fields[0], 1); Input(dialog, fields[1], 1); Input(dialog, fields[1], 0);
            check(dialog.Result.Keys.Count == 1 && dialog.Result.Keys[0].ReportId == 2, "an input held before learning is excluded from a new gesture");
            dialog.Learn().GetAwaiter().GetResult(); Input(dialog, fields[0], 0); Input(dialog, fields[0], 1); Input(dialog, fields[0], 0);
            check(dialog.Result.Keys.Count == 2, "previously held input becomes learnable after release and repress");
            dialog.Learn().GetAwaiter().GetResult(); Input(dialog, fields[2], 1);
            dialog.editor.SelectButton(0); Input(dialog, fields[2], 0);
            check(dialog.Result.Keys.Count == 2, "choosing an output cancels the pending modifier gesture");
            dialog.Learn().GetAwaiter().GetResult(); Input(dialog, fields[2], 1); dialog.ConnectionLost(); Input(dialog, fields[2], 0);
            check(dialog.Result.Keys.Count == 2 && !dialog.add.Enabled && dialog.keys.Items[0]!.ToString()!.StartsWith(fields[1].Label), "disconnect discards pending inputs and retains known labels");
        }
        fields = Fields();
        TaskCompletionSource<bool> preparation = new();
        using (ShiftDialog dialog = Open(new() { Flags = 1, Vid = 0x1234, Name = "Chord checks" }, fields, prepare: () => preparation.Task))
        {
            Task learning = dialog.Learn(); dialog.ConnectionLost(); preparation.SetResult(true); Application.DoEvents(); learning.GetAwaiter().GetResult();
            Input(dialog, fields[0], 1); Input(dialog, fields[0], 0);
            check(dialog.Result.Keys.Count == 0 && dialog.learner is null, "late asynchronous preparation cannot rearm a disconnected dialog");
        }
        preparation = new();
        using (ShiftDialog dialog = Open(new() { Flags = 1, Vid = 0x1234, Name = "Chord checks" }, fields, prepare: () => preparation.Task))
        {
            Task learning = dialog.Learn(); dialog.Close(); preparation.SetResult(true); Application.DoEvents(); learning.GetAwaiter().GetResult();
            check(dialog.learner is null, "closing cancels pending asynchronous preparation");
        }
        InputField hat = new() { Index = 10, Bits = 4, UsagePage = 1, Usage = 0x39, Maximum = 7, Value = 8, Valid = true };
        InputField axis = new() { Index = 11, Bits = 8, BitOffset = 8, UsagePage = 1, Usage = 0x32, Maximum = 255, Valid = true };
        fields = [hat, axis];
        using (ShiftDialog dialog = Open(new() { Flags = 1, Vid = 0x1234, Name = "Chord checks" }, fields))
        {
            dialog.Learn().GetAwaiter().GetResult(); Input(dialog, hat, 0); Input(dialog, hat, 2);
            check(dialog.Result.Keys.Count == 0, "hat direction changes do not end the held gesture");
            Input(dialog, axis, 255); Input(dialog, hat, 8); Input(dialog, axis, 0);
            check(dialog.Result.Keys.Count == 2 && dialog.Result.Keys[0].Low == 0 && dialog.Result.Keys[1].Low > 0, "hat and absolute trigger learning retain their original ranges");
        }
        CheckWizardGesture(directory, check);
    }
    private static void CheckWizardGesture(string directory, Action<bool, string> check)
    {
        static IEnumerable<Control> Children(Control parent)
        { foreach (Control child in parent.Controls) { yield return child; foreach (Control descendant in Children(child)) yield return descendant; } }
        List<InputField> fields = Enumerable.Range(0, 2).Select(i => new InputField { Index = (ushort)i, BitOffset = (ushort)i,
            Bits = 1, UsagePage = 9, Usage = (ushort)(31 + i), Maximum = 1, Valid = true }).ToList();
        Capabilities caps = new(255, 2, 0x1234, 0x5678, 0, 1, 1, 1, 1, 0, 0, 0);
        using RegistrationWizard wizard = new(0, caps, fields, () => Task.FromResult(true), _ => throw new InvalidOperationException("No hardware save expected")) { ShowInTaskbar = false, Opacity = 0 };
        wizard.Show(); wizard.PreviewStep(1); Application.DoEvents();
        Exception? failure = null;
        using System.Windows.Forms.Timer drive = new() { Interval = 100 };
        drive.Tick += (_, _) =>
        {
            drive.Stop(); ShiftDialog? dialog = wizard.OwnedForms.OfType<ShiftDialog>().SingleOrDefault();
            try
            {
                check(dialog is { Modal: true }, "registration wizard opens the real modal shift dialog");
                dialog!.add.PerformClick();
                foreach (InputField field in fields) { field.Value = 1; wizard.OnInput(field, 1); }
                foreach (InputField field in fields) { field.Value = 0; wizard.OnInput(field, 0); }
                check(dialog.Result.Keys.Count == 2 && wizard.Registered.Shift.Keys.Count == 0, "wizard input delivery captures both keys in an isolated draft");
                Children(dialog).OfType<Button>().Single(b => b.Text == L.T("編集に反映", "Apply to draft")).PerformClick();
            }
            catch (Exception ex) { failure = ex; dialog?.Close(); }
        };
        drive.Start(); Children(wizard).OfType<Button>().Single(b => b.Text == L.T("シフト設定…", "Button shift…")).PerformClick();
        if (failure is not null) throw failure;
        wizard.PreviewStep(2); Application.DoEvents();
        ListView review = Children(wizard).OfType<ListView>().Single();
        ListViewItem row = review.Items.Cast<ListViewItem>().Single(i => i.Text == L.T("シフトキー（AND）", "Shift keys (AND)"));
        check(fields.All(f => row.SubItems[1].Text.Contains(f.Label)), "registration review uses the same button numbers for all modifiers");
        review.EnsureVisible(row.Index); Application.DoEvents();
        using Bitmap bitmap = new(wizard.Width, wizard.Height); wizard.DrawToBitmap(bitmap, new(0, 0, wizard.Width, wizard.Height)); bitmap.Save(Path.Combine(directory, "shift-registration-review.png"));
        wizard.Close();
    }
}

internal sealed partial class MainForm
{
    internal static void CheckShiftHost(string directory, Action<bool, string> check)
    {
        static IEnumerable<Control> Children(Control parent)
        { foreach (Control child in parent.Controls) { yield return child; foreach (Control descendant in Children(child)) yield return descendant; } }
        using MainForm form = new(preview: true, autoConnect: false) { ShowInTaskbar = false, Opacity = 0 };
        form.Show(); Application.DoEvents(); form.timer.Stop();
        try
        {
            form.profiles[0] = new() { Flags = 1, Vid = 0x1234, Pid = 0x5678, Name = "Shift input delivery" };
            form.fields.Clear();
            form.fields.Add(new() { Index = 0, Bits = 1, UsagePage = 9, Usage = 15, Minimum = 0, Maximum = 1, Valid = true });
            form.fields.Add(new() { Index = 1, BitOffset = 1, Bits = 1, UsagePage = 9, Usage = 27, Minimum = 0, Maximum = 1, Valid = true });
            form.selected = 0; form.RefreshSlots(); form.BindSelected();
            Exception? failure = null;
            using System.Windows.Forms.Timer drive = new() { Interval = 100 };
            drive.Tick += (_, _) =>
            {
                drive.Stop(); ShiftDialog? dialog = form.OwnedForms.OfType<ShiftDialog>().SingleOrDefault();
                try
                {
                    check(dialog is not null && dialog.Modal && IsWindowEnabled(dialog.Handle) && !IsWindowEnabled(form.Handle), "actual main-window button opens a modal shift editor");
                    Children(dialog!).OfType<Button>().Single(b => b.Text == L.T("シフトキーを追加", "Learn shift keys")).PerformClick();
                    form.ApplyChanges([new(0, 1, 1, false), new(1, 1, 1, false)], true);
                    form.ApplyChanges([new(0, 0, 2, false)], true); form.ApplyChanges([new(1, 0, 3, false)], true);
                    check(dialog!.Result.Keys.Count == 2 && !form.dirty[0], "normal input delivery learns a multi-field chord across polls without mutating the main draft");
                    Children(dialog).OfType<Button>().Single(b => b.Text == L.T("編集に反映", "Apply to draft")).PerformClick();
                }
                catch (Exception ex) { failure = ex; dialog?.Close(); }
            };
            drive.Start();
            Children(form.editor).OfType<Button>().Single(b => b.Text == L.T("シフト設定…", "Button shift…")).PerformClick();
            if (failure is not null) throw failure;
            check(form.dirty[0] && form.profiles[0].Shift.Keys.Count == 2 && form.bridge is null, "Apply updates the unsaved draft without opening a USB handle");
            form.CapturePreview(Path.Combine(directory, "main-shift.png"));
        }
        finally { Array.Clear(form.dirty); form.working = false; form.Close(); }
    }
}
