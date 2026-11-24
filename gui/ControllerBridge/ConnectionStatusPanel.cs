namespace ControllerBridge;

// Keep status groups inside one header row during table measurement.
internal sealed class ConnectionStatusPanel : Label
{
    public ConnectionStatusPanel()
    {
        AutoSize = false; Margin = new(4, 3, 4, 3); UseMnemonic = false;
        Font = new Font(L.FontFamily, 10); ForeColor = Ui.Muted;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    public override Size GetPreferredSize(Size proposedSize) => new(0, LogicalToDeviceUnits(34));
    protected override void OnPaint(PaintEventArgs e)
    {
        string[] sections = Text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (sections.Length == 0 || ClientSize.Width <= 0) return;
        int padding = LogicalToDeviceUnits(6), gap = LogicalToDeviceUnits(4);
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        int[] widths = sections.Select(section => TextRenderer.MeasureText(e.Graphics, section, Font, Size.Empty, flags).Width + padding * 2).ToArray();
        int available = Math.Max(sections.Length, ClientSize.Width - gap * (sections.Length - 1));
        // Long messages yield space first. The complete text remains accessible.
        while (widths.Sum() > available)
        {
            int widest = Array.IndexOf(widths, widths.Max());
            if (widths[widest] <= 1) break;
            widths[widest]--;
        }
        int height = Math.Min(ClientSize.Height, Font.Height + LogicalToDeviceUnits(10));
        int top = Math.Max(0, (ClientSize.Height - height) / 2), x = 0;
        using SolidBrush face = new(Ui.Surface);
        using Pen light = new(Color.FromArgb(77, 77, 77)), shadow = new(Color.FromArgb(12, 12, 12));
        for (int i = 0; i < sections.Length; i++)
        {
            Rectangle box = new(x, top, widths[i], height);
            e.Graphics.FillRectangle(face, box);
            e.Graphics.DrawLine(light, box.Left, box.Top, box.Right - 2, box.Top);
            e.Graphics.DrawLine(light, box.Left, box.Top, box.Left, box.Bottom - 2);
            e.Graphics.DrawLine(shadow, box.Left + 1, box.Bottom - 1, box.Right - 1, box.Bottom - 1);
            e.Graphics.DrawLine(shadow, box.Right - 1, box.Top + 1, box.Right - 1, box.Bottom - 1);
            Rectangle text = Rectangle.FromLTRB(box.Left + padding, box.Top, Math.Max(box.Left + padding, box.Right - padding), box.Bottom);
            TextRenderer.DrawText(e.Graphics, sections[i], Font, text, ForeColor, flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            x += widths[i] + gap;
        }
    }
}
