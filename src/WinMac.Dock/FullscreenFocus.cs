namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private TaskWindow? DescribeFullscreenForeground(nint foreground)
    {
        if (monitor == null || foreground == 0 || !Native.IsWindowVisible(foreground) || Native.IsIconic(foreground)) return null;
        Native.GetWindowThreadProcessId(foreground, out var pid);
        if (pid == Environment.ProcessId || Native.Class(foreground) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;
        var bounds = Native.Bounds(foreground);
        if (bounds.Left > monitor.Bounds.Left || bounds.Top > monitor.Bounds.Top || bounds.Right < monitor.Bounds.Right || bounds.Bottom < monitor.Bounds.Bottom) return null;
        // Games can use owned/tool windows that the ordinary task list omits.
        return new(foreground, Native.Title(foreground), Native.ProcessPath(pid), "", bounds, false, Native.IsZoomed(foreground));
    }
    private bool HasExternalFullscreenForeground() => DescribeFullscreenForeground(Native.GetForegroundWindow()) != null;
}
