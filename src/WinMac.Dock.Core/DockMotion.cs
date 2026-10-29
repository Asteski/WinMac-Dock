namespace WinMac.Dock.Core;

public static class DockMotion
{
    public const int RearrangeMs = 320;
    public const double IndicatorCloseMs = 167;
    public static double IndicatorCloseProgress(double elapsedMs)
    {
        return CubicProgress(elapsedMs / IndicatorCloseMs, 1, 0, .83, .83);
    }
    public static double BadgeCloseProgress(double elapsedMs) => CubicProgress(elapsedMs / IndicatorCloseMs, 0, 0, 0, 1);
    private static double CubicProgress(double fraction, double x1, double y1, double x2, double y2)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction is 0 or 1) return fraction;
        static double Curve(double t, double a, double b) => 3 * (1 - t) * (1 - t) * t * a + 3 * (1 - t) * t * t * b + t * t * t;
        double low = 0, high = 1;
        for (int i = 0; i < 24; i++) { double t = (low + high) / 2; if (Curve(t, x1, x2) < fraction) low = t; else high = t; }
        return Curve((low + high) / 2, y1, y2);
    }
    public const float RearrangeX1 = .2f, RearrangeY1 = .7f, RearrangeX2 = .2f, RearrangeY2 = 1;
    public static double RearrangeProgress(double elapsedMs)
    {
        double x = Math.Clamp(elapsedMs / RearrangeMs, 0, 1);
        if (x is 0 or 1) return x;
        static double Curve(double t, double p1, double p2) => 3 * (1 - t) * (1 - t) * t * p1 + 3 * (1 - t) * t * t * p2 + t * t * t;
        double low = 0, high = 1;
        for (int i = 0; i < 24; i++)
        {
            double t = (low + high) / 2;
            if (Curve(t, RearrangeX1, RearrangeX2) < x) low = t; else high = t;
        }
        return Curve((low + high) / 2, RearrangeY1, RearrangeY2);
    }
    public const double LaunchDuration = 1.18;
    public static double AttentionOffset(double seconds, double normalHeight, double normalDuration = LaunchDuration)
    {
        if (seconds < 0) return 0;
        if (seconds < .55)
        {
            double t = seconds < .25 ? seconds / .25 : (seconds - .25) / .30;
            return normalHeight * 3 * (seconds < .25 ? 1 - (1 - t) * (1 - t) : 1 - t * t);
        }
        normalDuration = Math.Max(.001, normalDuration);
        return LaunchOffset((seconds - .55) % normalDuration / normalDuration * LaunchDuration, normalHeight);
    }
    public static double AttentionFinishTime(double seconds, double normalDuration = LaunchDuration)
        => .55 + Math.Max(1, Math.Ceiling((seconds - .55) / Math.Max(.001, normalDuration))) * Math.Max(.001, normalDuration);
    public static double LaunchOffset(double seconds, double height)
    {
        if (seconds < 0 || seconds >= LaunchDuration) return 0;
        double start, duration, amplitude;
        if (seconds < .42) { start = 0; duration = .42; amplitude = 1; }
        else if (seconds < .72) { start = .42; duration = .30; amplitude = .52; }
        else if (seconds < .94) { start = .72; duration = .22; amplitude = .22; }
        else if (seconds < 1.08) { start = .94; duration = .14; amplitude = .085; }
        else { start = 1.08; duration = .10; amplitude = .035; }
        double t = (seconds - start) / duration;
        return height * amplitude * 4 * t * (1 - t);
    }
}
