using System.Globalization;
using System.Drawing.Drawing2D;

namespace ControllerBridge;

internal sealed partial class LedRgbDialog : Form
{
    private readonly RadioButton standard = new() { Text = L.T("標準の暗い青（R: 0 / G: 0 / B: 1）", "Default dim blue (R: 0 / G: 0 / B: 1)"), AutoSize = true, Margin = new(4, 8, 4, 8) };
    private readonly RadioButton custom = new() { Text = L.T("RGB値を指定", "Custom RGB values"), AutoSize = true, Margin = new(4, 8, 4, 8) };
    private readonly TrackBar[] values = Enumerable.Range(0, 3).Select(_ => new TrackBar {
        Minimum = 0, Maximum = 255, SmallChange = 1, LargeChange = 16, TickStyle = TickStyle.None,
        AutoSize = false, Height = 36, Dock = DockStyle.Fill, BackColor = Ui.Background, Margin = new(4, 4, 8, 4)
    }).ToArray();
    private readonly Label[] valueLabels = Enumerable.Range(0, 3).Select(_ => Ui.Label("0")).ToArray();
    private readonly TextBox hex = new() { Width = 110, CharacterCasing = CharacterCasing.Upper, MaxLength = 0, Margin = new(0, 6, 12, 6), AccessibleName = L.T("RGBカラーコード（6桁）", "RGB color code (6 digits)") };
    private readonly Label hexError = Ui.Label("", 9);
    private readonly Panel swatch = new() { Width = 230, Height = 48, BorderStyle = BorderStyle.FixedSingle, Margin = new(4, 4, 16, 8) };
    private readonly Button accept, cancel, colorPicker;
    private readonly ToolTip pickerTip = new();
    private bool updating;

    public LedRgbDialog(DeviceProfile profile)
    {
        using IDisposable formLayout = Ui.Form(this, L.T("LED RGB設定", "LED RGB settings"), new(740, 480));
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = MinimizeBox = false; DoubleBuffered = true;
        BufferedTableLayoutPanel root = new() { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 1, RowCount = 7 };
        root.ColumnStyles.Add(new(SizeType.Percent, 100));
        for (int i = 0; i < 6; i++) root.RowStyles.Add(new(SizeType.AutoSize));
        root.RowStyles.Add(new(SizeType.Percent, 100));
        root.Controls.Add(standard, 0, 0); root.Controls.Add(custom, 0, 1);
        BufferedTableLayoutPanel colorControls = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        colorControls.ColumnStyles.Add(new(SizeType.Percent, 100)); colorControls.ColumnStyles.Add(new(SizeType.AutoSize));
        BufferedTableLayoutPanel channels = new() { AutoSize = true, Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, Margin = new(22, 0, 12, 0) };
        channels.ColumnStyles.Add(new(SizeType.AutoSize)); channels.ColumnStyles.Add(new(SizeType.Percent, 100)); channels.ColumnStyles.Add(new(SizeType.Absolute, 48));
        string[] names = [L.T("R（赤）", "R (red)"), L.T("G（緑）", "G (green)"), L.T("B（青）", "B (blue)")];
        for (int i = 0; i < 3; i++)
        {
            channels.RowStyles.Add(new(SizeType.AutoSize));
            Label name = Ui.Label(names[i]); name.Anchor = AnchorStyles.Left; channels.Controls.Add(name, 0, i);
            values[i].AccessibleName = names[i]; channels.Controls.Add(values[i], 1, i); values[i].ValueChanged += (_, _) => UpdatePreview();
            valueLabels[i].AutoSize = false; valueLabels[i].Size = new(40, 26); valueLabels[i].Anchor = AnchorStyles.Right;
            valueLabels[i].TextAlign = ContentAlignment.MiddleRight;
            channels.Controls.Add(valueLabels[i], 2, i);
        }
        colorPicker = new EyedropperButton { Anchor = AnchorStyles.Right };
        colorPicker.Click += (_, _) => ChooseColor();
        pickerTip.SetToolTip(colorPicker, colorPicker.AccessibleName);
        Disposed += (_, _) => pickerTip.Dispose();
        colorControls.Controls.Add(channels, 0, 0); colorControls.Controls.Add(colorPicker, 1, 0); root.Controls.Add(colorControls, 0, 2);
        FlowLayoutPanel code = new() { AutoSize = true, WrapContents = false, Margin = new(22, 0, 0, 0) };
        Label prefix = Ui.Label("#", 10); prefix.Anchor = AnchorStyles.Left;
        hex.Anchor = AnchorStyles.Left; hexError.Anchor = AnchorStyles.Left; hexError.ForeColor = Color.FromArgb(255, 180, 180);
        code.Controls.Add(prefix); code.Controls.Add(hex); code.Controls.Add(hexError); root.Controls.Add(code, 0, 3);
        hex.TextChanged += (_, _) => ReadHex();
        FlowLayoutPanel preview = new() { AutoSize = true, WrapContents = false, Margin = new(22, 0, 0, 0) };
        Label previewLabel = Ui.Label(L.T("プレビュー", "Preview"), 9); previewLabel.Anchor = AnchorStyles.Left;
        preview.Controls.Add(swatch); preview.Controls.Add(previewLabel); root.Controls.Add(preview, 0, 4);
        Label hint = Ui.Label(L.T("デバイスには入力したRGB値（0～255）を送信します。0 / 0 / 0 は消灯です。\n状態通知の色・点滅は優先されます。\n",
            "The entered RGB values (0–255) are sent to the LED. 0 / 0 / 0 is off.\nStatus colors and flashes take priority.\n") + L.ProfileSaveNotice, 9);
        hint.Dock = DockStyle.Fill; hint.ForeColor = Ui.Muted; hint.Margin = new(4, 8, 4, 8); root.Controls.Add(hint, 0, 5);
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
    private void ChooseColor()
    {
        using ColorDialog picker = new() { FullOpen = true, AnyColor = true, Color = Color.FromArgb(values[0].Value, values[1].Value, values[2].Value) };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        updating = true;
        try { values[0].Value = picker.Color.R; values[1].Value = picker.Color.G; values[2].Value = picker.Color.B; }
        finally { updating = false; }
        UpdatePreview();
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
            for (int i = 0; i < values.Length; i++)
            {
                values[i].Enabled = custom.Checked;
                valueLabels[i].ForeColor = custom.Checked ? Ui.Ink : Ui.Muted;
                valueLabels[i].Text = values[i].Value.ToString(CultureInfo.InvariantCulture);
                valueLabels[i].AccessibleName = values[i].AccessibleName + L.T(" の値: ", " value: ") + valueLabels[i].Text;
            }
            hex.Enabled = colorPicker.Enabled = custom.Checked;
            int red = custom.Checked ? (int)values[0].Value : 0, green = custom.Checked ? (int)values[1].Value : 0, blue = custom.Checked ? (int)values[2].Value : 1;
            if (syncHex) hex.Text = $"{red:X2}{green:X2}{blue:X2}";
            swatch.BackColor = LedPreview.Color((byte)red, (byte)green, (byte)blue);
            hexError.Text = ""; accept.Enabled = true;
        }
        finally { updating = false; }
    }
}


// Paint a vector glyph at the current button size so it stays sharp at any DPI.
internal sealed class EyedropperButton : DarkButton
{
    internal EyedropperButton()
    {
        AutoSize = false; MinimumSize = Size = new(40, 40); Padding = new(8); Margin = new(4);
        FlatStyle = FlatStyle.Flat; UseVisualStyleBackColor = false; Cursor = Cursors.Hand;
        AccessibleName = L.T("カラーピッカー", "Color picker");
        AccessibleDescription = L.T("色の選択画面を開きます。", "Open the color chooser.");
        Ui.ButtonColors(this, false);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        GraphicsState state = e.Graphics.Save();
        try
        {
            float scale = Math.Min(ClientSize.Width, ClientSize.Height) / 40f;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.TranslateTransform(ClientSize.Width / 2f, ClientSize.Height / 2f);
            e.Graphics.ScaleTransform(scale, scale);
            e.Graphics.RotateTransform(45);
            Color ink = Enabled ? ForeColor : Color.FromArgb(0xad, 0xad, 0xad);
            using Pen pen = new(ink, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            using SolidBrush brush = new(ink);
            using GraphicsPath bulb = new();
            bulb.AddArc(-3, -11, 6, 6, 180, 180);
            bulb.AddLine(3, -8, 3, -3); bulb.AddLine(3, -3, -3, -3); bulb.CloseFigure();
            e.Graphics.FillPath(brush, bulb);
            e.Graphics.DrawLine(pen, -5, -3, 5, -3);
            e.Graphics.DrawLines(pen, new PointF[] { new(-2, -2), new(-2, 6), new(0, 10), new(2, 6), new(2, -2) });
        }
        finally { e.Graphics.Restore(state); }
    }
}
