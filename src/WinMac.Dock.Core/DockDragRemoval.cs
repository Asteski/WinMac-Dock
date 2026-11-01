namespace WinMac.Dock.Core;

public static class DockDragRemoval
{
    public static bool IsOutside(PixelRect dock, int x, int y, int clearance) =>
        x < dock.Left - clearance || x >= dock.Right + clearance || y < dock.Top - clearance || y >= dock.Bottom + clearance;

    public static bool CanDragOutside(bool locked, bool alt, bool requireShift, bool shift) =>
        (!locked || alt) && (!requireShift || shift);

    public static bool CanRemove(bool outside, bool locked, bool alt, bool requireShift, bool shift) =>
        outside && CanDragOutside(locked, alt, requireShift, shift);
}
