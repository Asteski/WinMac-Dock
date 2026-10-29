namespace WinMac.Dock.Core;

public readonly record struct DockFit(double IconSize, double Padding, bool Overflow);
public static class DockAutoFit
{
    public static DockFit PixelAligned(DockFit fit, double scale)
    {
        double tile = Math.Floor((fit.IconSize + fit.Padding) * scale + .000001) / scale;
        return fit with { Padding = tile - fit.IconSize };
    }
    public static double PixelSpacing(double spacing, double scale) => Math.Floor(spacing * scale + .000001) / scale;
    public static double ExpansionBudget(double iconSize, int iconCount, int percent, int span)
        => iconSize * (Math.Clamp(percent, 0, 100) / 100d) * Math.Min(iconCount, (Math.Clamp(span, 1, 9) + 1) / 2d);
    public static double Spacing(int value, double effective, int requested) => value * effective / requested;
    public static DockFit Calculate(int requested, int normalPadding, IReadOnlyList<bool> icons, int spacing, double available, int magnificationPercent = 0, int magnificationSpan = 5, double separatorWidth = 1)
    {
        requested = Math.Max(16, requested);
        double Padding(double size) => Math.Max(4, normalPadding * size / requested);
        double Length(double size) => DockSpacing.Length(icons.Select(icon => icon ? size + Padding(size) : separatorWidth).ToArray(), icons, Spacing(spacing, size, requested));
        double Expanded(double size) => Length(size) + ExpansionBudget(size, icons.Count(icon => icon), magnificationPercent, magnificationSpan);
        if (Expanded(requested) <= available) return new(requested, normalPadding, false);
        if (Expanded(16) >= available) return new(16, Padding(16), Length(16) > available);
        double low = 16, high = requested;
        for (int i = 0; i < 48; i++)
        {
            double mid = (low + high) / 2;
            if (Expanded(mid) <= available) low = mid; else high = mid;
        }
        return new(low, Padding(low), false);
    }
}
