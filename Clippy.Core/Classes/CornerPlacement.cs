using System;

namespace Clippy.Core.Classes
{
    public static class CornerPlacement
    {
        // All bounds are physical pixels; requested dimensions are XAML units.
        public static (int X, int Y, int Width, int Height) Calculate(
            int x, int y, int width, int height, double scale, double requestedWidth, double requestedHeight)
        {
            var w = Math.Clamp((int)Math.Round(requestedWidth * scale), 1, Math.Max(1, width));
            var h = Math.Clamp((int)Math.Round(requestedHeight * scale), 1, Math.Max(1, height));
            return (x + width - w, y + height - h, w, h);
        }
    }
}
