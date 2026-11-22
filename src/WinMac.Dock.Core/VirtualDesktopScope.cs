namespace WinMac.Dock.Core;

public static class VirtualDesktopScope
{
    public static bool Includes(bool allDesktops, bool reportedCurrent, Guid? assignedDesktop, Guid? currentDesktop)
    {
        if (allDesktops) return true;
        if (assignedDesktop is { } assigned && assigned != Guid.Empty && currentDesktop is { } current && current != Guid.Empty)
            return assigned == current;
        return reportedCurrent;
    }
}
