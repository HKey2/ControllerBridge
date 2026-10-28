namespace ControllerBridge;

// An always-open native choice list. Each row keeps its input identity and live
// meter together; selection and keyboard navigation remain standard ListBox behavior.
internal sealed class AxisSourceList : ListBox
{
    public AxisSourceList()
    {
        DrawMode = DrawMode.OwnerDrawFixed; IntegralHeight = false;
        BorderStyle = BorderStyle.FixedSingle; Dock = DockStyle.Fill;
        HorizontalScrollbar = false;
        AccessibleName = L.T("入力軸とリアルタイム値", "Input axes and live values");
        ItemHeight = 54; TabIndex = 0;
    }

    internal static int Midpoint(InputField field) => field.Minimum < 0 && field.Maximum > 0 ? 0 :
        (int)(field.Minimum + ((long)field.Maximum - field.Minimum + 1) / 2);

    internal static double Position(InputField field, int value)
    {
        long origin = Midpoint(field), delta = (long)value - origin;
        long span = delta < 0 ? origin - field.Minimum : (long)field.Maximum - origin;
        return span <= 0 ? 0 : Math.Clamp((double)delta / span, -1, 1);
    }

    internal int Fit(int rows, int width)
    {
        ItemHeight = LogicalToDeviceUnits(54);
        int columns = Math.Max(1, (Items.Count + rows - 1) / rows);
        MultiColumn = columns > 1;
        ColumnWidth = Math.Max(1, (width - 2) / columns);
        return Math.Max(1, Math.Min(rows, Items.Count)) * ItemHeight + 2 + (MultiColumn ? SystemInformation.HorizontalScrollBarHeight : 0);
    }

    internal void RefreshValue(InputField field)
    {
        int index = Items.IndexOf(field);
        if (index >= 0 && IsHandleCreated) Invalidate(GetItemRectangle(index));
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || Items[e.Index] is not InputField field) return;
        Graphics g = e.Graphics; Rectangle row = e.Bounds;
        int U(int value) => LogicalToDeviceUnits(value);
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using SolidBrush background = new(selected ? Ui.Selected : Ui.Surface);
        g.FillRectangle(background, row);
        using Pen line = new(Ui.Stroke);
        int divider = row.Left + (int)(row.Width * .38);
        g.DrawLine(line, divider, row.Top + U(5), divider, row.Bottom - U(5));
        g.DrawLine(line, row.Left, row.Bottom - 1, row.Right, row.Bottom - 1);
        if (selected)
        {
            using SolidBrush accent = new(Ui.Focus);
            g.FillRectangle(accent, row.Left, row.Top, U(3), row.Height);
        }
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(g, field.Label, Font,
            new Rectangle(row.Left + U(12), row.Top + U(2), divider - row.Left - U(20), U(24)), Ui.Ink, flags);
        using Font detailFont = new(Font.FontFamily, 8.5f);
        TextRenderer.DrawText(g, $"IF{field.Interface} / ID{field.ReportId} / bit{field.BitOffset}", detailFont,
            new Rectangle(row.Left + U(12), row.Top + U(27), divider - row.Left - U(20), U(21)), Ui.Muted, flags);

        Rectangle track = new(divider + U(14), row.Top + U(10), Math.Max(U(20), row.Right - divider - U(133)), U(16));
        using SolidBrush empty = new(Ui.Input); g.FillRectangle(empty, track);
        int middle = track.Left + track.Width / 2;
        if (field.Valid)
        {
            double position = Position(field, field.Value);
            int end = middle + (int)Math.Round(position * (position < 0 ? middle - track.Left : track.Right - middle));
            bool inRange = field.Value >= field.Minimum && field.Value <= field.Maximum;
            using SolidBrush fill = new(inRange ? Ui.Focus : Ui.Stroke);
            if (end != middle) g.FillRectangle(fill, Math.Min(end, middle), track.Top, Math.Abs(end - middle), track.Height);
        }
        g.DrawRectangle(line, track);
        using Pen origin = new(Ui.Ink);
        g.DrawLine(origin, middle, track.Top - U(3), middle, track.Bottom + U(3));
        TextRenderer.DrawText(g, "−", detailFont, new Rectangle(track.Left, row.Top + U(30), U(30), U(20)), Ui.Muted, flags);
        TextRenderer.DrawText(g, "0", detailFont, new Rectangle(middle - U(15), row.Top + U(30), U(30), U(20)), Ui.Muted, flags | TextFormatFlags.HorizontalCenter);
        TextRenderer.DrawText(g, "+", detailFont, new Rectangle(track.Right - U(30), row.Top + U(30), U(30), U(20)), Ui.Muted, flags | TextFormatFlags.Right);
        Rectangle value = new(track.Right + U(8), row.Top + U(1), row.Right - track.Right - U(16), U(25));
        TextRenderer.DrawText(g, field.Valid ? field.Value.ToString("N0") : "—", Font, value, Ui.Ink, flags | TextFormatFlags.Right);
        value.Y += U(25);
        string status = !field.Valid ? L.T("入力待ち", "Waiting") : field.Value < field.Minimum || field.Value > field.Maximum ?
            L.T("範囲外", "Out of range") : Position(field, field.Value).ToString("+0%;-0%;0%");
        TextRenderer.DrawText(g, status, detailFont, value, Ui.Muted, flags | TextFormatFlags.Right);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}
