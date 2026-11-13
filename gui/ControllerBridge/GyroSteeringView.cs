using System.Drawing.Drawing2D;

namespace ControllerBridge;

internal sealed class GyroSteeringView : Control
{
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    internal double Steering { get; set; }
    internal GyroSteeringView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Ui.Surface; Dock = DockStyle.Fill; MinimumSize = new(260, 180); TabStop = false;
        AccessibleRole = AccessibleRole.Graphic; AccessibleName = L.T("ジャイロのハンドル", "Gyro steering wheel");
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); Graphics g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        float r = Math.Max(1, Math.Min(Width * .35f, Height * .44f));
        g.TranslateTransform(Width / 2f, Height / 2f);
        using Pen ticks = new(Ui.Stroke, Math.Max(1, r * .015f));
        g.DrawLine(ticks, 0, -r * 1.04f, 0, -r * 1.14f); g.DrawLine(ticks, 0, r * 1.04f, 0, r * 1.14f);
        g.RotateTransform((float)Math.Clamp(Steering, -1, 1) * 80);
        using SolidBrush body = new(Ui.Ink), mark = new(Ui.Focus);
        g.FillRectangle(body, -r * .89f, -r * .09f, r * 1.78f, r * .18f);
        using Pen arc = new(Ui.Ink, r * .18f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        RectangleF circle = new(-r * .89f, -r * .89f, r * 1.78f, r * 1.78f);
        const float sweep = .65f * 180 / MathF.PI;
        g.DrawArc(arc, circle, -sweep, 2 * sweep); g.DrawArc(arc, circle, 180 - sweep, 2 * sweep);
        g.FillRectangle(body, -r * .14f, -r * .18f, r * .28f, r * .36f);
        g.FillRectangle(mark, -r * .035f, -r * .13f, r * .07f, r * .26f);
        AccessibleDescription = L.T($"操舵量 {Steering:+0%;-0%;0%}", $"Steering {Steering:+0%;-0%;0%}");
    }
}
