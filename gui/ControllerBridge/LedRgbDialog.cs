using System.Globalization;

namespace ControllerBridge;

internal sealed partial class LedRgbDialog : Form
{
    private readonly RadioButton standard = new() { Text = L.T("標準の暗い青（R: 0 / G: 0 / B: 1）", "Default dim blue (R: 0 / G: 0 / B: 1)"), AutoSize = true, Margin = new(4, 8, 4, 8) };
    private readonly RadioButton custom = new() { Text = L.T("RGB値を指定", "Custom RGB values"), AutoSize = true, Margin = new(4, 8, 4, 8) };
    private readonly NumericUpDown[] values = Enumerable.Range(0, 3).Select(_ => new NumericUpDown { Minimum = 0, Maximum = 255, Width = 110, Anchor = AnchorStyles.Left, Margin = new(4, 6, 16, 6) }).ToArray();
    private readonly TextBox hex = new() { Width = 110, CharacterCasing = CharacterCasing.Upper, MaxLength = 0, Margin = new(0, 6, 12, 6), AccessibleName = L.T("RGBカラーコード（6桁）", "RGB color code (6 digits)") };
    private readonly Label hexError = Ui.Label("", 9);
    private readonly Panel swatch = new() { Width = 230, Height = 48, BorderStyle = BorderStyle.FixedSingle, Margin = new(4, 4, 16, 8) };
    private readonly Button accept, cancel;
    private bool updating;

    public LedRgbDialog(DeviceProfile profile)
    {
        Ui.Form(this, L.T("LED RGB設定", "LED RGB settings"), new(610, 425));
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; DoubleBuffered = true;
        BufferedTableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 7 };
        for (int i = 0; i < 6; i++) root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.Controls.Add(standard, 0, 0); root.Controls.Add(custom, 0, 1);
        BufferedTableLayoutPanel channels = new() { AutoSize = true, ColumnCount = 3, RowCount = 2, Margin = new(22, 0, 0, 0) };
        string[] names = [L.T("R（赤）", "R (red)"), L.T("G（緑）", "G (green)"), L.T("B（青）", "B (blue)")];
        for (int i = 0; i < 3; i++)
        {
            channels.ColumnStyles.Add(new(SizeType.AutoSize)); channels.Controls.Add(Ui.Label(names[i], 10), i, 0);
            values[i].AccessibleName = names[i]; channels.Controls.Add(values[i], i, 1); values[i].ValueChanged += (_, _) => UpdatePreview();
        }
        root.Controls.Add(channels, 0, 2);
        FlowLayoutPanel code = new() { AutoSize = true, WrapContents = false, Margin = new(22, 0, 0, 0) };
        Label prefix = Ui.Label("#", 10); prefix.Anchor = AnchorStyles.Left;
        hex.Anchor = AnchorStyles.Left; hexError.Anchor = AnchorStyles.Left; hexError.ForeColor = Color.FromArgb(255, 180, 180);
        code.Controls.Add(prefix); code.Controls.Add(hex); code.Controls.Add(hexError); root.Controls.Add(code, 0, 3);
        hex.TextChanged += (_, _) => ReadHex();
        FlowLayoutPanel preview = new() { AutoSize = true, WrapContents = false, Margin = new(22, 0, 0, 0) };
        Label previewLabel = Ui.Label(L.T("プレビュー", "Preview"), 9); previewLabel.Anchor = AnchorStyles.Left;
        preview.Controls.Add(swatch); preview.Controls.Add(previewLabel); root.Controls.Add(preview, 0, 4);
        Label hint = Ui.Label(L.T("本体には入力したRGB値（0～255）を送信します。0 / 0 / 0 は消灯です。\n状態通知の色・点滅は優先されます。\n変更後は「スロットを本体に保存」で反映してください。",
            "The entered RGB values (0–255) are sent to the LED. 0 / 0 / 0 is off.\nStatus colors and flashes take priority.\nAfter editing, choose “Save slot to device” to apply the color."), 9);
        hint.ForeColor = Ui.Muted; hint.Margin = new(4, 8, 4, 8); root.Controls.Add(hint, 0, 5);
        FlowLayoutPanel actions = new() { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Dock = DockStyle.Bottom, Margin = Padding.Empty };
        accept = Ui.Button("OK", (_, _) => { if (ValidateChildren() && (!custom.Checked || TryReadHex(out _))) DialogResult = DialogResult.OK; }, true);
        cancel = Ui.Button(L.T("キャンセル", "Cancel")); cancel.DialogResult = DialogResult.Cancel; cancel.CausesValidation = false;
        actions.Controls.Add(cancel); actions.Controls.Add(accept); root.Controls.Add(actions, 0, 6);
        Controls.Add(root); AcceptButton = accept; CancelButton = cancel; Ui.ThemeInputs(this);
        int rgb = profile.LedRgb;
        values[0].Value = (rgb >> 16) & 255; values[1].Value = (rgb >> 8) & 255; values[2].Value = rgb & 255;
        standard.Checked = !profile.CustomLed; custom.Checked = profile.CustomLed;
        standard.CheckedChanged += (_, _) => UpdatePreview(); custom.CheckedChanged += (_, _) => UpdatePreview();
        UpdatePreview();
    }

    internal void ApplyTo(DeviceProfile profile)
    {
        if (custom.Checked && !TryReadHex(out _)) throw new InvalidDataException(L.T("RGBコードは6桁の16進数で入力してください。", "Enter the RGB code as 6 hexadecimal digits."));
        profile.SetLedColor(custom.Checked, (byte)values[0].Value, (byte)values[1].Value, (byte)values[2].Value);
    }
    private bool TryReadHex(out int rgb)
    {
        rgb = 0;
        return hex.Text.Length == 6 && int.TryParse(hex.Text, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb);
    }
    private void ReadHex()
    {
        if (updating || !custom.Checked) return;
        if (hex.Text.StartsWith('#'))
        {
            int start = Math.Max(0, hex.SelectionStart - 1);
            int end = Math.Max(0, hex.SelectionStart + hex.SelectionLength - 1);
            updating = true;
            try { hex.Text = hex.Text[1..]; hex.Select(start, end - start); }
            finally { updating = false; }
        }
        bool valid = TryReadHex(out int rgb);
        accept.Enabled = valid;
        hexError.Text = valid ? "" : L.T("6桁の16進数で入力してください。", "Enter 6 hex digits (0–9, A–F).");
        if (!valid) return;
        updating = true;
        try { values[0].Value = (rgb >> 16) & 255; values[1].Value = (rgb >> 8) & 255; values[2].Value = rgb & 255; }
        finally { updating = false; }
        // Leave the caret and selection under the user's control while typing.
        UpdatePreview(syncHex: false);
    }
    private void UpdatePreview(bool syncHex = true)
    {
        if (updating) return;
        updating = true;
        try
        {
            foreach (NumericUpDown value in values) value.Enabled = custom.Checked;
            hex.Enabled = custom.Checked;
            int red = custom.Checked ? (int)values[0].Value : 0, green = custom.Checked ? (int)values[1].Value : 0, blue = custom.Checked ? (int)values[2].Value : 1;
            if (syncHex) hex.Text = $"{red:X2}{green:X2}{blue:X2}";
            swatch.BackColor = LedPreview.Color((byte)red, (byte)green, (byte)blue);
            hexError.Text = ""; accept.Enabled = true;
        }
        finally { updating = false; }
    }
}
