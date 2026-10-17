using System;
using System.Windows;

#pragma warning disable 1591
namespace WinMemoryCleaner
{
    public static class OverlayPlacement
    {
        // Coordinates are in one consistent space chosen by the caller (physical
        // desktop pixels for the native overlay); negative monitor origins are valid.
        public static Point Place(Rect area, Size size, int corner, double customX, double customY)
        {
            double width = Math.Min(size.Width, area.Width);
            double height = Math.Min(size.Height, area.Height);
            double maxX = area.Right - width;
            double maxY = area.Bottom - height;
            double marginX = Math.Min(16, Math.Max(0, (area.Width - width) / 2));
            double marginY = Math.Min(16, Math.Max(0, (area.Height - height) / 2));
            if (corner == 4 && IsFinite(customX) && IsFinite(customY))
                return new Point(Clamp(customX, area.Left, maxX), Clamp(customY, area.Top, maxY));
            bool right = corner == 1 || corner == 3;
            bool bottom = corner == 2 || corner == 3;
            return new Point(right ? maxX - marginX : area.Left + marginX,
                bottom ? maxY - marginY : area.Top + marginY);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return Math.Max(minimum, Math.Min(maximum, value));
        }
    }
}
#pragma warning restore 1591
