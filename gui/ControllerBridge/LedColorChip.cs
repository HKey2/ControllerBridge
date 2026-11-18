namespace ControllerBridge;

internal sealed class LedColorChip : IDisposable
{
    private const double InnerGlowOpacity = .5, OuterGlowOpacity = .3;
    private Bitmap? image;
    private (Size Size, int Color, int Background, int InnerSigma, int OuterSigma) cached;

    internal void Draw(Graphics graphics, Rectangle chip, Color color, Color background, int innerSigma, int outerSigma, Rectangle clip)
    {
        if (chip.Width <= 0 || chip.Height <= 0) return;
        var key = (chip.Size, color.ToArgb(), background.ToArgb(), innerSigma, outerSigma);
        if (image is null || cached != key)
        {
            Dispose();
            image = Render(chip.Size, color, background, innerSigma, outerSigma);
            cached = key;
        }
        var state = graphics.Save();
        try
        {
            graphics.SetClip(clip, System.Drawing.Drawing2D.CombineMode.Intersect);
            int padding = 3 * outerSigma;
            graphics.DrawImageUnscaled(image, chip.X - padding, chip.Y - padding);
        }
        finally { graphics.Restore(state); }
    }

    private static Bitmap Render(Size size, Color color, Color background, int innerSigma, int outerSigma)
    {
        // A solid chip's Gaussian blur separates into horizontal and vertical coverage.
        double[] innerKernel = Kernel(innerSigma), outerKernel = Kernel(outerSigma);
        int padding = outerKernel.Length / 2;
        double[] innerX = Coverage(size.Width, padding, innerKernel), innerY = Coverage(size.Height, padding, innerKernel);
        double[] outerX = Coverage(size.Width, padding, outerKernel), outerY = Coverage(size.Height, padding, outerKernel);
        Bitmap result = new(outerX.Length, outerY.Length);
        for (int y = 0; y < result.Height; y++)
        {
            for (int x = 0; x < result.Width; x++)
            {
                Color original = x >= padding && x < padding + size.Width && y >= padding && y < padding + size.Height
                    ? color : background;
                double glow = innerX[x] * innerY[y] * InnerGlowOpacity + outerX[x] * outerY[y] * OuterGlowOpacity;
                result.SetPixel(x, y, Color.FromArgb(Add(original.R, color.R, glow),
                    Add(original.G, color.G, glow), Add(original.B, color.B, glow)));
            }
        }
        return result;
    }

    private static double[] Kernel(int sigma)
    {
        // Keep the faint tails beyond the visible glow instead of truncating at its spread.
        int radius = 3 * sigma;
        double[] kernel = Enumerable.Range(-radius, radius * 2 + 1)
            .Select(offset => Math.Exp(-offset * offset / (2.0 * sigma * sigma))).ToArray();
        double sum = kernel.Sum();
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= sum;
        return kernel;
    }

    private static double[] Coverage(int length, int padding, double[] kernel)
    {
        int radius = kernel.Length / 2;
        double[] coverage = new double[length + 2 * padding];
        for (int position = 0; position < coverage.Length; position++)
            for (int offset = -radius; offset <= radius; offset++)
                if (position - offset >= padding && position - offset < padding + length)
                    coverage[position] += kernel[offset + radius];
        return coverage;
    }

    private static int Add(byte original, byte glow, double amount) => Math.Min(255, original + (int)Math.Round(glow * amount));

    public void Dispose()
    {
        image?.Dispose();
        image = null;
    }
}
