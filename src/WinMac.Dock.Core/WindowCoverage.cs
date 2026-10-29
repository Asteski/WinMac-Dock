namespace WinMac.Dock.Core;
public static class WindowCoverage
{
    public static bool TouchesDockAcrossScreen(PixelRect screen, PixelRect dock, IEnumerable<PixelRect> windows, DockEdge edge, int tolerance = 2)
    {
        var touching = windows.Where(window => edge switch
        {
            DockEdge.Top => window.Top <= dock.Bottom + tolerance && window.Bottom >= dock.Bottom,
            DockEdge.Left => window.Left <= dock.Right + tolerance && window.Right >= dock.Right,
            DockEdge.Right => window.Right >= dock.Left - tolerance && window.Left <= dock.Left,
            _ => window.Bottom >= dock.Top - tolerance && window.Top <= dock.Top
        });
        return SpansAxis(screen, touching, edge is DockEdge.Left or DockEdge.Right, tolerance);
    }
    public static bool SpansAxis(PixelRect screen, IEnumerable<PixelRect> windows, bool vertical, int tolerance = 2)
    {
        int start = vertical ? screen.Top : screen.Left;
        int end = vertical ? screen.Bottom : screen.Right;
        var ranges = windows.Where(window => window.Right > screen.Left && window.Left < screen.Right && window.Bottom > screen.Top && window.Top < screen.Bottom)
            .Select(window => (Start: vertical ? window.Top : window.Left, End: vertical ? window.Bottom : window.Right)).OrderBy(range => range.Start);
        int covered = start;
        foreach (var range in ranges)
        {
            if (range.Start > covered + tolerance) return false;
            covered = Math.Max(covered, range.End);
            if (covered >= end - tolerance) return true;
        }
        return false;
    }
}
