using System.Text.Json;

namespace ControllerBridge;

internal sealed partial class AxisDialog
{
    // Deterministic UI regression check. Uses the same input notification path as
    // MainForm, with synthetic fields; never opens or writes a USB device.
    internal static void RunChecks(string directory)
    {
        Directory.CreateDirectory(directory); int checks = 0;
        void Check(bool condition, string description)
        { checks++; if (!condition) throw new InvalidDataException("Axis dialog: " + description); }
        static void Show(AxisDialog form)
        { form.ShowInTaskbar = false; form.Opacity = 0; form.Show(); form.PerformLayout(); Application.DoEvents(); }
        static void Capture(AxisDialog form, string path)
        {
            form.Refresh(); Application.DoEvents();
            using Bitmap bitmap = new(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new(0, 0, form.Width, form.Height)); bitmap.Save(path);
        }
        static IEnumerable<Control> Descendants(Control root)
        {
            foreach (Control child in root.Controls)
            { yield return child; foreach (Control inner in Descendants(child)) yield return inner; }
        }
        static void Click(AxisDialog form, string text) => Descendants(form).OfType<Button>().Single(b => b.Text == text).PerformClick();
        static void Input(AxisDialog form, InputField field, int value)
        { field.Value = value; field.Valid = true; form.OnInput(field, value); }
        List<InputField> fields = Enumerable.Range(0, 6).Select(i => new InputField {
            Index = (ushort)i, Interface = (byte)(i / 4), ReportId = 1, BitOffset = (ushort)(i * 16),
            Bits = (byte)(i < 4 ? 16 : 8), Flags = (byte)(i < 4 ? 3 : 2), UsagePage = 1, Usage = (ushort)(0x30 + i),
            Minimum = i < 4 ? -32768 : 0, Maximum = i < 4 ? 32767 : 255,
            Value = i < 4 ? 0 : 128, Valid = true }).ToList();
        InputField signed = fields[0], unsigned = fields[4];
        foreach ((InputField field, int value, double expected) in new[] {
            (signed, -32768, -1d), (signed, 0, 0d), (signed, 32767, 1d),
            (unsigned, 0, -1d), (unsigned, 128, 0d), (unsigned, 255, 1d),
            (unsigned, -1, -1d), (unsigned, 300, 1d) })
            Check(AxisSourceList.Position(field, value) == expected, "meter direction and endpoints");
        InputField wide = new() { Minimum = int.MinValue, Maximum = int.MaxValue };
        Check(AxisSourceList.Position(wide, int.MinValue) == -1 && AxisSourceList.Position(wide, int.MaxValue) == 1, "32-bit range does not overflow");
        InputField button = new() { Bits = 1, Minimum = 0, Maximum = 1 }, hat = new() { Bits = 4, UsagePage = 1, Usage = 0x39, Minimum = 0, Maximum = 7 },
            relative = new() { Bits = 8, Minimum = -127, Maximum = 127, HidFlags = 4 };
        AxisBinding binding = new() { Mode = 1, Analog = signed.Analog(-30000, 12, 30000, 7, true) };
        using AxisDialog dialog = new(0, binding, [.. fields, button, hat, relative], true);
        Show(dialog);
        Check(dialog.source.Items.Count == 6, "only absolute analog candidates appear");
        Check(dialog.minimum.Value == -30000 && dialog.center.Value == 12 && dialog.maximum.Value == 30000 && dialog.deadzone.Value == 7 && dialog.invert.Checked,
            "saved calibration and inversion survive opening");
        void CheckLayout(AxisDialog form)
        {
            File.WriteAllText(Path.Combine(directory, $"layout-{form.source.Items.Count}.json"), JsonSerializer.Serialize(new {
                form.Width, form.Height, form.DeviceDpi, panel = form.analogPanel.ClientRectangle.ToString(), layout = form.analogLayout.Bounds.ToString(),
                list = form.source.ClientRectangle.ToString(), first = form.source.GetItemRectangle(0).ToString(), last = form.source.GetItemRectangle(form.source.Items.Count - 1).ToString(),
                vertical = form.analogPanel.VerticalScroll.Visible, horizontal = form.analogPanel.HorizontalScroll.Visible
            }, new JsonSerializerOptions { WriteIndented = true }));
            Capture(form, Path.Combine(directory, $"layout-{form.source.Items.Count}.png"));
            Check(!form.analogPanel.VerticalScroll.Visible && !form.analogPanel.HorizontalScroll.Visible, "analog settings fit without scrolling");
            for (int i = 0; i < form.source.Items.Count; i++)
                Check(form.source.ClientRectangle.Contains(form.source.GetItemRectangle(i)), "every candidate is fully visible");
            Check(form.analogPanel.ClientRectangle.Contains(form.analogLayout.Bounds), "calibration controls remain visible");
        }
        CheckLayout(dialog);
        int[] first = [-32768, -16384, 0, 16384, 255, 0];
        for (int i = 0; i < fields.Count; i++) Input(dialog, fields[i], first[i]);
        Check(dialog.source.SelectedItem == signed && dialog.live.Text.Contains("-32768"), "all axes update while selected input remains selected");
        Capture(dialog, Path.Combine(directory, "axes-live.png"));
        int[] second = [32767, 16384, 0, -16384, 0, 255];
        for (int i = 0; i < fields.Count; i++) Input(dialog, fields[i], second[i]);
        Check(fields.Select((f, i) => f.Value == second[i]).All(x => x), "non-selected axes receive opposite input values");
        Capture(dialog, Path.Combine(directory, "axes-reversed.png"));
        dialog.source.SelectedIndex = 4; unsigned.Value = 128;
        Click(dialog, L.T("可動範囲を計測", "Measure range"));
        Input(dialog, unsigned, 0); Input(dialog, unsigned, 255); Input(dialog, unsigned, 127);
        Click(dialog, L.T("中心を記録", "Record center"));
        Check(dialog.minimum.Value == 0 && dialog.center.Value == 127 && dialog.maximum.Value == 255 && !dialog.measuring, "range and center calibration use selected axis");
        Click(dialog, L.T("操作して入力軸を検出", "Move to detect an input axis"));
        Input(dialog, fields[1], -20000);
        Check(dialog.source.SelectedItem == fields[1] && dialog.measuring, "motion detection selects the moved row");
        Input(dialog, fields[1], -32768); Input(dialog, fields[1], 32767); Input(dialog, fields[1], 0);
        Click(dialog, L.T("中心を記録", "Record center"));
        dialog.Apply();
        Check(dialog.DialogResult == DialogResult.OK && dialog.Result.Analog.BitOffset == fields[1].BitOffset && dialog.Result.Analog.Center == 0 &&
            dialog.Result.Analog.Minimum == -32768 && dialog.Result.Analog.Maximum == 32767, "selected source and calibration apply to binding");
        Check(binding.Analog.Center == 12, "editing did not change original binding");

        using AxisDialog pending = new(1, new(), [new() { Index = 0, Bits = 8, Minimum = 0, Maximum = 255 }], true);
        Show(pending); pending.mode.SelectedIndex = 1; pending.PerformLayout(); Application.DoEvents(); pending.source.SelectedIndex = 0;
        Check(pending.source.Visible && pending.source.ClientRectangle.Contains(pending.source.GetItemRectangle(0)), "switching from unused to analog expands the list");
        Check(pending.live.Text.EndsWith("—"), "unreceived input is not shown as zero");
        Capture(pending, Path.Combine(directory, "axes-waiting.png"));
        Click(pending, L.T("操作して入力軸を検出", "Move to detect an input axis"));
        InputField pendingField = (InputField)pending.source.Items[0];
        Input(pending, pendingField, 128); Input(pending, pendingField, 255);
        Check(!pending.detect && pending.measuring, "first input establishes a baseline for motion detection");
        pending.Close();

        using AxisDialog offline = new(0, binding, [], false);
        Show(offline); Check(offline.source.Items.Count == 0 && offline.center.Value == 12, "offline calibration is retained");
        Capture(offline, Path.Combine(directory, "axes-offline.png"));
        offline.Apply(); Check(offline.DialogResult == DialogResult.OK && offline.Result.Analog.Center == 12, "offline calibration can be applied");

        List<InputField> many = Enumerable.Range(0, 32).Select(i => new InputField { Index = (ushort)i, Interface = (byte)(i / 8), ReportId = 1,
            BitOffset = (ushort)((i % 8) * 16), Bits = 16, Flags = 3, UsagePage = 1, Usage = (ushort)(0x30 + i % 6),
            Minimum = -32768, Maximum = 32767, Value = (i % 3 - 1) * 16384, Valid = true }).ToList();
        using AxisDialog expanded = new(0, new() { Mode = 1, Analog = many[0].Analog(-32768, 0, 32767, 5, false) }, many, true);
        Show(expanded); CheckLayout(expanded); Capture(expanded, Path.Combine(directory, "axes-many.png")); expanded.Close();
        File.WriteAllText(Path.Combine(directory, "axis-check.json"), JsonSerializer.Serialize(new {
            checks, passed = true, syntheticInput = true, dpi = dialog.DeviceDpi,
            normalWindow = new { dialog.Width, dialog.Height }, axes = fields.Count,
            expandedAxes = many.Count, expandedWindow = new { expanded.Width, expanded.Height }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
