using System;
using System.Drawing;
using System.Globalization;

namespace Cs2Roulette
{
    internal static class ColorUtil
    {
        public static Color ParseHex(string hex, Color fallback)
        {
            if (string.IsNullOrEmpty(hex)) return fallback;
            hex = hex.TrimStart('#');
            try
            {
                if (hex.Length == 6)
                {
                    int r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    return Color.FromArgb(r, g, b);
                }
            }
            catch { }
            return fallback;
        }

        public static Color Blend(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(
                (int)Math.Round(a.R + (b.R - a.R) * t),
                (int)Math.Round(a.G + (b.G - a.G) * t),
                (int)Math.Round(a.B + (b.B - a.B) * t));
        }

        public static Color Darken(Color c, double amount) { return Blend(c, Color.Black, amount); }
        public static Color Lighten(Color c, double amount) { return Blend(c, Color.White, amount); }

        public static Color ReadableOn(Color bg)
        {
            double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
            return lum > 0.6 ? Color.FromArgb(24, 24, 32) : Color.White;
        }
    }
}
