using System.Drawing.Drawing2D;
using System.Globalization;

namespace ControllerBridge;

// Compact graph button with a single horizontal row of graph-only choices.
internal sealed class AnalogCurvePicker : Button
{
    internal const byte LowSpeedCurve = 40;
    internal static readonly byte[] Codes = [4, 6, 8, 0, 14, 20, 30, LowSpeedCurve];
    internal static readonly double[] Powers = [.4, .6, .8, 1, 1.4, 2, 3];
    internal static bool ValidCode(byte code) => code is 0 or 1 or 2 or 4 or 6 or 8 or 10 or 12 or 14 or 16 or 20 or 30 or LowSpeedCurve;
    // Earlier prototypes retain their stored value until edited.
    internal static byte NormalizeCode(byte code) => code switch { 1 => 16, 2 => 8, 10 => 0, _ => code };
    internal static double Response(double speed, byte code)
    {
        code = NormalizeCode(code);
        if (speed <= 0) return 0;
        if (code == LowSpeedCurve)
        {
            // Preserve the existing continuation above normalized speed 1.
            if (speed > 1)
            {
                double inverse = 1 / speed;
                double above = Math.Max(0, inverse - .25);
                return 1 / (inverse * inverse - 2.4 * above * above * (1 - inverse));
            }
            const double initial = 1.0 / 128;
            if (speed <= initial) return .75 * Math.Sqrt(initial * speed);
            if (speed < 5 * initial)
            {
                double d = speed - initial;
                return .75 * initial + .375 * d + d * d / (64 * initial);
            }
            if (speed <= .5) return .5 * speed;
            double t = 2 * speed - 1;
            return (10 + 10 * t + 3 * t * t + 17 * t * t * t) / 40;
        }
        double power = code == 0 ? 1 : code / 10.0;
        double value = Math.Pow(speed, power);
        return speed <= 1 ? Math.Min(value, speed * 4) : value;
    }
    private static string CurveName(byte code) => code == LowSpeedCurve
        ? L.T("低速保持", "Low-speed preserve")
        : (code == 0 ? 1 : code / 10.0).ToString("0.0", CultureInfo.InvariantCulture);
    private readonly ToolStripDropDown popup = new()
    {
        LayoutStyle = ToolStripLayoutStyle.HorizontalStackWithOverflow,
        Padding = new(4), BackColor = Ui.Input, AutoClose = true,
        AccessibleName = L.T("反応曲線", "Response curve")
    };
    private readonly CurveItem[] choices;
    private byte curveCode;
    private int SelectedIndex => Array.IndexOf(Codes, curveCode);
    private double SelectedPower => curveCode == 0 ? 1 : curveCode / 10.0;
    private bool hovered;
    internal event EventHandler? SelectedIndexChanged;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal byte CurveCode
    {
        get => curveCode;
        set
        {
            if (!ValidCode(value)) throw new ArgumentOutOfRangeException(nameof(value));
            byte next = NormalizeCode(value);
            if (next == curveCode) return;
            curveCode = next; UpdateSelection(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    internal AnalogCurvePicker()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AutoSize = false; Size = new(78, 32); Anchor = AnchorStyles.Left;
        FlatStyle = FlatStyle.Flat; BackColor = Ui.Input; ForeColor = Ui.Ink; Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.ButtonDropDown; TabStop = true;
        choices = Codes.Select((code, index) => new CurveItem(code, () =>
        {
            CurveCode = Codes[index]; popup.Close(ToolStripDropDownCloseReason.ItemClicked); Focus();
        })).ToArray();
        popup.Items.AddRange(choices);
        popup.Closed += (_, _) => Invalidate();
        UpdateSelection();
    }
    private void UpdateSelection()
    {
        for (int i = 0; i < choices.Length; i++) choices[i].Checked = i == SelectedIndex;
        // Values remain available to assistive tools without visible captions.
        AccessibleDescription = CurveName(curveCode);
        Invalidate();
    }
    private void OpenChoices()
    {
        if (!Enabled) return;
        if (popup.Visible) { popup.Close(); return; }
        popup.Padding = new(LogicalToDeviceUnits(4));
        foreach (CurveItem choice in choices)
        {
            choice.Size = LogicalToDeviceUnits(new Size(58, 54));
            choice.Margin = new(LogicalToDeviceUnits(2));
        }
        int width = popup.GetPreferredSize(Size.Empty).Width;
        popup.Show(this, new Point(Width - width, Height + LogicalToDeviceUnits(2)));
        // A saved curve removed from the presets keeps its actual preview and
        // response until the user explicitly chooses a replacement.
        int focus = SelectedIndex;
        if (focus < 0) focus = Enumerable.Range(0, Powers.Length).MinBy(i => Math.Abs(Powers[i] - SelectedPower));
        choices[focus].Select(); Invalidate();
    }
    protected override void OnClick(EventArgs e) { base.OnClick(e); OpenChoices(); }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down)) OpenChoices();
        else if (e.KeyCode is Keys.Left or Keys.Up)
            CurveCode = Codes[SelectedIndex >= 0 ? Math.Max(0, SelectedIndex - 1) :
                Math.Max(0, Array.FindLastIndex(Powers, p => p < SelectedPower))];
        else if (e.KeyCode is Keys.Right or Keys.Down)
        {
            int next = SelectedIndex >= 0 ? SelectedIndex + 1 : Array.FindIndex(Powers, p => p > SelectedPower);
            CurveCode = Codes[next < 0 ? Codes.Length - 1 : Math.Min(Codes.Length - 1, next)];
        }
        else { base.OnKeyDown(e); return; }
        e.Handled = e.SuppressKeyPress = true;
    }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); if (!Enabled) popup.Close(); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        Color face = Enabled && (hovered || popup.Visible) ? Ui.Selected : Ui.Input;
        e.Graphics.Clear(face);
        using Pen border = new(Focused && Enabled ? Ui.Focus : Ui.Stroke);
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
        int inset = LogicalToDeviceUnits(5), arrow = LogicalToDeviceUnits(16);
        DrawCurve(e.Graphics, new RectangleF(inset, inset, Math.Max(1, Width - arrow - 2 * inset), Math.Max(1, Height - 2 * inset)),
            curveCode, Enabled, DeviceDpi / 96f);
        float x = Width - arrow / 2f, y = Height / 2f, half = LogicalToDeviceUnits(3);
        using Pen mark = new(Enabled ? Ui.Muted : Ui.Stroke);
        e.Graphics.DrawLines(mark, [new PointF(x - half, y - 1), new PointF(x, y + half - 1), new PointF(x + half, y - 1)]);
        if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(ClientRectangle, -3, -3), Ui.Ink, face);
    }
    private static void DrawCurve(Graphics graphics, RectangleF graph, byte code, bool enabled, float scale)
    {
        SmoothingMode previous = graphics.SmoothingMode; graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using Pen axes = new(Ui.Stroke, scale), reference = new(Ui.Stroke, scale) { DashStyle = DashStyle.Dot },
            curve = new(enabled ? Ui.Focus : Ui.Stroke, 2 * scale);
        graphics.DrawLine(axes, graph.Left, graph.Top, graph.Left, graph.Bottom);
        graphics.DrawLine(axes, graph.Left, graph.Bottom, graph.Right, graph.Bottom);
        graphics.DrawLine(reference, graph.Left, graph.Bottom, graph.Right, graph.Top);
        PointF[] points = new PointF[41];
        for (int i = 0; i < points.Length; i++)
        {
            double speed = i / 40.0;
            points[i] = new(graph.Left + (float)speed * graph.Width, graph.Bottom - (float)Response(speed, code) * graph.Height);
        }
        graphics.DrawLines(curve, points); graphics.SmoothingMode = previous;
    }
    protected override void Dispose(bool disposing) { if (disposing) popup.Dispose(); base.Dispose(disposing); }

    private sealed class CurveItem : ToolStripButton
    {
        private readonly byte code;
        internal CurveItem(byte code, Action choose)
        {
            this.code = code; AutoSize = false; Size = new(58, 54); Margin = new(2);
            DisplayStyle = ToolStripItemDisplayStyle.None; AutoToolTip = false;
            AccessibleName = L.T("反応曲線 ", "Response curve ") + CurveName(code);
            ToolTipText = AccessibleName;
            Click += (_, _) => choose();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            Color face = Checked || Selected ? Ui.Selected : Ui.Input;
            e.Graphics.Clear(face);
            using Pen border = new(Checked ? Ui.Focus : Selected ? Ui.Muted : Ui.Stroke);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            float scale = Height / 54f, inset = 6 * scale;
            DrawCurve(e.Graphics, new RectangleF(inset, inset, Width - 2 * inset, Height - 2 * inset), code, Enabled, scale);
            if (Selected) ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(2, 2, Width - 4, Height - 4), Ui.Ink, face);
        }
    }
}
