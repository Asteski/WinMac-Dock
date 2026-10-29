using Windows.UI;

namespace WinMac.Dock;

internal static class AppearanceColor
{
    internal static Color DefaultBorder(bool dark) => dark ? Color.FromArgb(255, 0x3C, 0x3C, 0x3C) : Color.FromArgb(255, 0xAF, 0xAF, 0xAF);
    internal static Color Resolve(string hex, Color fallback, int opacity)
    {
        var color = fallback;
        if (hex.Length == 7 && hex[0] == '#' && uint.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            color = Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        color.A = (byte)Math.Round(color.A * Math.Clamp(opacity, 0, 100) / 100d);
        return color;
    }
}
