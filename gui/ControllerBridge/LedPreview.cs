namespace ControllerBridge;

// Display-only approximation, fixed at 4.0 following the user's LED comparison.
// Profile values remain the original PWM bytes. No per-PC preference is read.
internal static partial class LedPreview
{
    private const double Gamma = 4.0;
    internal static Color Color(byte red, byte green, byte blue) => System.Drawing.Color.FromArgb(Channel(red), Channel(green), Channel(blue));
    internal static byte Channel(byte value) => (byte)Math.Clamp((int)Math.Round(255 * Math.Pow(value / 255.0, 1 / Gamma)), 0, 255);
}
