using System.Drawing.Drawing2D;

namespace ControllerBridge;

internal sealed class StickRangeView : Control
{
    private StickCalibration.StickView state;
    private string title = "";
    internal static Color InputColor(int index) => index switch
    {
        // Red, green, yellow and magenta from Paul Tol's bright palette.
        // https://sronpersonalpages.nl/~pault/#sec:qualitative
        0 => Color.FromArgb(0xEE, 0x66, 0x77), 1 => Color.FromArgb(0x22, 0x88, 0x33),
        2 => Color.FromArgb(0xCC, 0xBB, 0x44), 3 => Color.FromArgb(0xAA, 0x33, 0x77), _ => Ui.Muted
    };
    internal StickRangeView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Surface; ForeColor = Ui.Ink; Dock = DockStyle.Fill; Margin = new(6);
        MinimumSize = new(220, 190); TabStop = false; AccessibleRole = AccessibleRole.Graphic;
    }
    internal void Display(string caption, StickCalibration.StickView value)
    {
        title = caption; state = value; AccessibleName = caption;
        AccessibleDescription = L.T($"X入力 {value.LiveX:P0}、Y入力 {value.LiveY:P0}。X{(value.ReadyX ? "確定済み" : "未確定")}、Y{(value.ReadyY ? "確定済み" : "未確定")}。",
            $"X input {value.LiveX:P0}, Y input {value.LiveY:P0}. X {(value.ReadyX ? "confirmed" : "unconfirmed")}, Y {(value.ReadyY ? "confirmed" : "unconfirmed")}.");
        Invalidate();
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        int U(int value) => LogicalToDeviceUnits(value);
        TextRenderer.DrawText(g, title, Font, new Rectangle(U(8), U(3), Width - U(16), U(27)), Ui.Ink,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        float radius = Math.Max(1, Math.Min((Width - U(100)) / 2f, (Height - U(90)) / 2f));
        float cx = Width / 2f, cy = (Height + U(30)) / 2f;
        RectangleF circle = new(cx - radius, cy - radius, radius * 2, radius * 2);
        using SolidBrush surface = new(Ui.Input); g.FillEllipse(surface, circle);
        using Pen grid = new(Ui.Stroke, Math.Max(1, U(1))) { DashStyle = DashStyle.Dot };
        g.DrawLine(grid, cx - radius, cy, cx + radius, cy); g.DrawLine(grid, cx, cy - radius, cx, cy + radius);
        using Pen reference = new(Ui.Muted, Math.Max(1, U(1))); g.DrawEllipse(reference, circle);
        void Band(StickCalibration.AxisRange range, bool vertical, bool ready, int colorIndex, bool retained)
        {
            if (range.Size <= 0) return;
            using Pen band = new(Color.FromArgb(retained ? 112 : ready ? 225 : 105, InputColor(colorIndex)), Math.Max(2, U(retained ? 6 : 20)))
                { StartCap = LineCap.Flat, EndCap = LineCap.Flat };
            float minus = (float)range.Negative * radius, plus = (float)range.Positive * radius;
            if (vertical) g.DrawLine(band, cx, cy + minus, cx, cy - plus);
            else g.DrawLine(band, cx - minus, cy, cx + plus, cy);
        }
        GraphicsState clipped = g.Save();
        using (GraphicsPath boundary = new()) { boundary.AddEllipse(circle); g.SetClip(boundary); }
        void Bands(bool retained)
        {
            if (state.RetainedX == retained) Band(state.X, false, state.ReadyX, state.XColor, retained);
            if (state.RetainedY == retained) Band(state.Y, true, state.ReadyY, state.YColor, retained);
        }
        Bands(true); Bands(false);
        g.Restore(clipped);
        if (state.ShowPointer)
        {
            float dot = U(4);
            using SolidBrush live = new(Ui.Ink);
            g.FillEllipse(live, cx + (float)state.LiveX * radius - dot, cy - (float)state.LiveY * radius - dot, dot * 2, dot * 2);
        }
        void Caption(string text, float x, float y, bool active)
        {
            Rectangle bounds = new((int)x - U(22), (int)y - U(12), U(44), U(24));
            TextRenderer.DrawText(g, text, Font, bounds, active ? Ui.Focus : Ui.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
        Caption("X−", cx - radius - U(23), cy, state.ActiveAxis == 0);
        Caption("X+", cx + radius + U(23), cy, state.ActiveAxis == 0);
        Caption("Y+", cx, cy - radius - U(15), state.ActiveAxis == 1);
        Caption("Y−", cx, cy + radius + U(15), state.ActiveAxis == 1);
    }
}
