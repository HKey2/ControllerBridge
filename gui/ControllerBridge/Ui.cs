namespace ControllerBridge;

internal static class Ui
{
    private static readonly Icon AppIcon = LoadAppIcon();
    private static Icon LoadAppIcon()
    {
        using Stream stream = typeof(Ui).Assembly.GetManifestResourceStream("ControllerBridge.App.ico")
            ?? throw new InvalidDataException("Missing application icon");
        using Icon icon = new(stream);
        return (Icon)icon.Clone();
    }
    // Fluent 2 webDarkTheme neutral aliases and the brandWeb ramp:
    // https://fluent2.microsoft.design/color-tokens
    // https://github.com/microsoft/fluentui/tree/master/packages/tokens/src
    public static readonly Color Background = Color.FromArgb(0x1f, 0x1f, 0x1f); // neutralBackground2
    public static readonly Color Surface = Color.FromArgb(0x29, 0x29, 0x29); // neutralBackground1
    public static readonly Color Input = Color.FromArgb(0x14, 0x14, 0x14); // neutralBackground3
    public static readonly Color Hover = Color.FromArgb(0x3d, 0x3d, 0x3d); // neutralBackground1Hover
    public static readonly Color Ink = Color.White; // neutralForeground1
    public static readonly Color Muted = Color.FromArgb(0xd6, 0xd6, 0xd6); // neutralForeground2
    public static readonly Color Stroke = Color.FromArgb(0x66, 0x66, 0x66); // neutralStroke1
    public static readonly Color Accent = Color.FromArgb(0x11, 0x5e, 0xa3); // brandBackground = brand[70]
    public static readonly Color AccentHover = Color.FromArgb(0x0f, 0x6c, 0xbd); // brand[80]
    public static readonly Color AccentPressed = Color.FromArgb(0x0c, 0x3b, 0x5e); // brand[40]
    public static readonly Color Selected = Color.FromArgb(0x08, 0x23, 0x38); // brandBackground2 = brand[20]
    public static readonly Color Focus = Color.FromArgb(0x47, 0x9e, 0xf5); // brandStroke1 = brand[100]

    public static Button Button(string text, EventHandler? click = null, bool primary = false)
    {
        Button b = new DarkButton() { Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, MinimumSize = new(90, 40), Padding = new(10, 6, 10, 6),
            FlatStyle = FlatStyle.Flat, Cursor = Cursors.Hand, Margin = new(4), UseVisualStyleBackColor = false };
        ButtonColors(b, primary);
        if (click is not null) b.Click += click;
        return b;
    }
    public static void ButtonColors(Button b, bool primary, bool assigned = false)
    {
        if (b is DarkButton raised) raised.Assigned = assigned;
        b.BackColor = primary ? Accent : assigned ? Selected : Surface; b.ForeColor = Ink;
        b.FlatAppearance.BorderColor = primary || assigned ? Focus : Stroke;
        b.FlatAppearance.MouseOverBackColor = primary ? AccentHover : Hover;
        b.FlatAppearance.MouseDownBackColor = primary ? AccentPressed : Background;
    }
    public static Label Label(string text, float size = 10, bool bold = false) => new() { Text = text, AutoSize = true,
        ForeColor = Ink, TextAlign = ContentAlignment.MiddleLeft, UseMnemonic = false,
        Font = new Font(L.FontFamily, size, bold ? FontStyle.Bold : FontStyle.Regular), Margin = new(4, 3, 4, 3) };
    public static IDisposable Form(Form f, string title, Size size, Size? minimum = null)
    {
        // Keep the 96-DPI design baseline until the complete control tree exists.
        // Otherwise an early layout consumes it before later controls are added.
        f.SuspendLayout();
        f.AutoScaleDimensions = new(96, 96); f.AutoScaleMode = AutoScaleMode.Dpi;
        f.Text = title; f.Icon = AppIcon; f.Font = new Font(L.FontFamily, 10); f.ClientSize = size; f.BackColor = Background;
        f.ForeColor = Ink; f.StartPosition = FormStartPosition.CenterParent;
        Size contentMinimum = minimum ?? size;
        f.MinimumSize = new(contentMinimum.Width + 16, contentMinimum.Height + 39);
        f.AutoScroll = true;
        f.Shown += (_, _) => FitToWorkingArea(f, contentMinimum);
        f.DpiChanged += (_, _) => f.BeginInvoke(() => { if (!f.IsDisposed) FitToWorkingArea(f, contentMinimum); });
        return new FormLayout(f);
    }
    private sealed class FormLayout(Form form) : IDisposable
    {
        public void Dispose() => form.ResumeLayout(true);
    }
    internal static void FitToWorkingArea(Form form, Size logicalMinimum, Rectangle? workingArea = null)
    {
        Rectangle work = workingArea ?? Screen.FromControl(form).WorkingArea;
        Size minimum = form.LogicalToDeviceUnits(logicalMinimum);
        Size frame = form.Size - form.ClientSize;
        form.SuspendLayout();
        try
        {
            // Docked content keeps a scrollable minimum when the monitor is
            // smaller than the dialog. The window itself always fits on screen.
            form.MinimumSize = Size.Empty;
            form.AutoScrollMinSize = minimum;
            form.MinimumSize = new(Math.Min(minimum.Width + frame.Width, work.Width), Math.Min(minimum.Height + frame.Height, work.Height));
            if (form.WindowState == FormWindowState.Normal)
            {
                form.Size = new(Math.Min(form.Width, work.Width), Math.Min(form.Height, work.Height));
                form.Location = new(Math.Clamp(form.Left, work.Left, Math.Max(work.Left, work.Right - form.Width)),
                    Math.Clamp(form.Top, work.Top, Math.Max(work.Top, work.Bottom - form.Height)));
            }
        }
        finally { form.ResumeLayout(true); }
    }
    public static void ThemeInputs(Control root)
    {
        switch (root)
        {
            case TextBox box: box.BackColor = Input; box.ForeColor = Ink; box.BorderStyle = BorderStyle.Fixed3D; break;
            case NumericUpDown number: number.BackColor = Input; number.ForeColor = Ink; number.BorderStyle = BorderStyle.Fixed3D; break;
            case ComboBox combo: combo.BackColor = Input; combo.ForeColor = Ink; combo.FlatStyle = FlatStyle.Standard; break;
            case ListBox list: list.BackColor = Surface; list.ForeColor = Ink; break;
            case ListView list: list.BackColor = Surface; list.ForeColor = Ink; list.BorderStyle = BorderStyle.None; break;
        }
        foreach (Control child in root.Controls) ThemeInputs(child);
    }
    public static void Error(IWin32Window owner, Exception ex) => MessageBox.Show(owner, ex.Message, "ControllerBridge", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}

// Native flat-button disabled text can remain black even with .NET 9 dark mode.
// Keep the standard enabled painting/input/accessibility; paint only disabled state.
internal class DarkButton : Button
{
    public DarkButton() => SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Assigned { get; set; }
    private bool hovered, pressed;
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = pressed = false; Invalidate(); }
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); pressed = e.Button == MouseButtons.Left; Invalidate(); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); pressed = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { base.OnKeyDown(e); if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } }
    protected override void OnKeyUp(KeyEventArgs e) { base.OnKeyUp(e); pressed = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Color face = !Enabled ? Ui.Input : pressed ? FlatAppearance.MouseDownBackColor : hovered ? FlatAppearance.MouseOverBackColor : BackColor;
        Rectangle bounds = new(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using System.Drawing.Drawing2D.LinearGradientBrush background = new(bounds, ControlPaint.Light(face, .14f), ControlPaint.Dark(face, .1f), 90);
        e.Graphics.FillRectangle(background, bounds);
        using Pen border = new(Enabled ? FlatAppearance.BorderColor : Color.FromArgb(66, 66, 66)); e.Graphics.DrawRectangle(border, bounds);
        using Pen light = new(pressed ? ControlPaint.Dark(face) : ControlPaint.Light(face, .35f));
        using Pen shadow = new(pressed ? ControlPaint.Light(face, .2f) : Color.FromArgb(18, 18, 18));
        e.Graphics.DrawLine(light, 1, 1, Width - 2, 1); e.Graphics.DrawLine(light, 1, 1, 1, Height - 2);
        e.Graphics.DrawLine(shadow, 1, Height - 2, Width - 2, Height - 2); e.Graphics.DrawLine(shadow, Width - 2, 1, Width - 2, Height - 2);
        Rectangle text = Rectangle.FromLTRB(Padding.Left, Padding.Top, Width - Padding.Right, Height - Padding.Bottom);
        if (pressed) text.Offset(1, 1);
        TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? ForeColor : Color.FromArgb(0xad, 0xad, 0xad),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -5, -5), Ui.Ink, face);
        if (Assigned)
        {
            int size = LogicalToDeviceUnits(4), inset = LogicalToDeviceUnits(5);
            using SolidBrush brush = new(Ui.Ink); e.Graphics.FillEllipse(brush, Width - inset - size, inset, size, size);
        }
    }
}

// Preserve the established disabled checkbox painter across profile and mode controls.
internal class DarkCheckBox : CheckBox
{
    private static readonly Color DisabledText = Color.FromArgb(0xad, 0xad, 0xad);
    private const TextFormatFlags DisabledGraphicsFlags = TextFormatFlags.PreserveGraphicsClipping | TextFormatFlags.PreserveGraphicsTranslateTransform;
    protected override void OnPaint(PaintEventArgs e)
    {
        if (Enabled) { base.OnPaint(e); return; }
        // ButtonBase is Opaque: WinForms skips OnPaintBackground. Unlike
        // the normal adapter, this disabled path must clear its own face
        // before drawing into a reused buffer (including the first tab show).
        using SolidBrush background = new(BackColor);
        e.Graphics.FillRectangle(background, ClientRectangle);
        var state = Checked ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedDisabled : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedDisabled;
        Size glyph = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
        Point location = new(Padding.Left, (Height - glyph.Height) / 2);
        CheckBoxRenderer.DrawCheckBox(e.Graphics, location, state);
        Rectangle text = Rectangle.FromLTRB(location.X + glyph.Width + LogicalToDeviceUnits(3), Padding.Top, Width - Padding.Right, Height - Padding.Bottom);
        TextRenderer.DrawText(e.Graphics, Text, Font, text, DisabledText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | DisabledGraphicsFlags);
    }
}

internal class BufferedTableLayoutPanel : TableLayoutPanel
{
    public BufferedTableLayoutPanel() => SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
}

internal sealed class RaisedTableLayoutPanel : BufferedTableLayoutPanel
{
    public RaisedTableLayoutPanel()
    {
        BackColor = Ui.Surface; Padding = new(12); Margin = new(0, 0, 0, 10);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using Pen light = new(Color.FromArgb(77, 77, 77)), shadow = new(Color.FromArgb(12, 12, 12));
        e.Graphics.DrawLine(light, 0, 0, Width - 2, 0); e.Graphics.DrawLine(light, 0, 0, 0, Height - 2);
        e.Graphics.DrawLine(shadow, 1, Height - 1, Width - 1, Height - 1); e.Graphics.DrawLine(shadow, Width - 1, 1, Width - 1, Height - 1);
    }
}

// Retain native tab selection, keyboard navigation and accessibility while
// painting the entire strip, including the unused area, in the app palette.
internal sealed class DarkTabControl : TabControl
{
    private readonly HashSet<int> mutedTabs = [];
    // Selection policy belongs to the owning form; this only changes the tab
    // header so an unavailable output does not disable the current editor.
    public void SetTabMuted(int index, bool muted)
    {
        bool changed = muted ? mutedTabs.Add(index) : mutedTabs.Remove(index);
        if (changed && IsHandleCreated && index >= 0 && index < TabCount) Invalidate(GetTabRect(index));
    }
    public DarkTabControl() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (mutedTabs.Count != 0 && TabCount > 0)
        {
            int direction = keyData == (Keys.Control | Keys.Tab) || keyData == (Keys.Control | Keys.PageDown) || Focused && keyData == Keys.Right ? 1 :
                keyData == (Keys.Control | Keys.Shift | Keys.Tab) || keyData == (Keys.Control | Keys.PageUp) || Focused && keyData == Keys.Left ? -1 : 0;
            if (direction != 0)
            {
                for (int step = 1; step <= TabCount; step++)
                {
                    int next = (Math.Max(0, SelectedIndex) + direction * step + TabCount) % TabCount;
                    if (!mutedTabs.Contains(next)) { SelectedIndex = next; break; }
                }
                return true;
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Ui.Surface);
        Rectangle page = Rectangle.Inflate(DisplayRectangle, 2, 2);
        using Pen border = new(Ui.Stroke); e.Graphics.DrawRectangle(border, page);
        for (int i = 0; i < TabCount; i++)
        {
            Rectangle bounds = GetTabRect(i); bool active = i == SelectedIndex;
            if (!active) { bounds.Y += LogicalToDeviceUnits(4); bounds.Height -= LogicalToDeviceUnits(4); }
            Color face = active ? Ui.Surface : Ui.Background;
            using System.Drawing.Drawing2D.LinearGradientBrush background = new(bounds, active ? Color.FromArgb(70, 70, 70) : Color.FromArgb(48, 48, 48), face, 90);
            e.Graphics.FillRectangle(background, bounds);
            using Pen light = new(active ? Color.FromArgb(133, 133, 133) : Ui.Stroke), shadow = new(Color.FromArgb(12, 12, 12));
            e.Graphics.DrawLine(light, bounds.Left, bounds.Bottom - 1, bounds.Left, bounds.Top);
            e.Graphics.DrawLine(light, bounds.Left, bounds.Top, bounds.Right - 2, bounds.Top);
            e.Graphics.DrawLine(shadow, bounds.Right - 1, bounds.Top + 1, bounds.Right - 1, bounds.Bottom - 1);
            TextRenderer.DrawText(e.Graphics, TabPages[i].Text, Font, bounds, mutedTabs.Contains(i) ? Color.FromArgb(146, 146, 146) : Ui.Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (active)
            {
                using Pen line = new(Focused && ShowFocusCues ? Color.FromArgb(150, 210, 255) : Ui.Focus, LogicalToDeviceUnits(3));
                e.Graphics.DrawLine(line, bounds.Left + 3, bounds.Top + 2, bounds.Right - 3, bounds.Top + 2);
                // Open the selected tab into the page, including the native gap.
                using SolidBrush join = new(Ui.Surface);
                e.Graphics.FillRectangle(join, bounds.Left + 1, bounds.Bottom - 2,
                    bounds.Width - 3, Math.Max(4, DisplayRectangle.Top - bounds.Bottom + 3));
            }
        }
    }
    protected override void OnSelectedIndexChanged(EventArgs e) { base.OnSelectedIndexChanged(e); Invalidate(); }
    protected override void OnEnter(EventArgs e) { base.OnEnter(e); Invalidate(); }
    protected override void OnLeave(EventArgs e) { base.OnLeave(e); Invalidate(); }
}
