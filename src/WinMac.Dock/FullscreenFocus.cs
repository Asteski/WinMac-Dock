namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private static bool IsWindowsSearchWindow(nint window)
    {
        if (window == 0) return false;
        Native.GetWindowThreadProcessId(window, out var pid);
        string path = Native.ProcessPath(pid);
        string name = System.IO.Path.GetFileName(path);
        // The modern Search surface is hosted by the system ShellHost. Its
        // fullscreen-sized shell window must not count as a fullscreen app.
        if (name.Equals("ShellHost.exe", StringComparison.OrdinalIgnoreCase)
            && path.Equals(System.IO.Path.Combine(Environment.SystemDirectory, "ShellHost.exe"), StringComparison.OrdinalIgnoreCase)) return true;
        return (name.Equals("SearchHost.exe", StringComparison.OrdinalIgnoreCase) || name.Equals("SearchApp.exe", StringComparison.OrdinalIgnoreCase))
            && path.StartsWith(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SystemApps") + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
    private TaskWindow? DescribeFullscreenForeground(nint foreground)
    {
        if (monitor == null || foreground == 0 || !Native.IsWindowVisible(foreground) || Native.IsIconic(foreground)) return null;
        if (IsWindowsSearchWindow(foreground)) return null;
        Native.GetWindowThreadProcessId(foreground, out var pid);
        if (pid == Environment.ProcessId || Native.Class(foreground) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;
        var bounds = Native.Bounds(foreground);
        if (bounds.Left > monitor.Bounds.Left || bounds.Top > monitor.Bounds.Top || bounds.Right < monitor.Bounds.Right || bounds.Bottom < monitor.Bounds.Bottom) return null;
        // Games can use owned/tool windows that the ordinary task list omits.
        return new(foreground, Native.Title(foreground), Native.ProcessPath(pid), "", bounds, false, Native.IsZoomed(foreground));
    }
    private bool CanRevealAboveFullscreen(TaskWindow? window) => Settings.AllowDockAboveFullscreen || FullscreenExclusions.Contains(Settings, window);
    private bool HasExternalFullscreenForeground() => DescribeFullscreenForeground(Native.GetForegroundWindow()) != null;
}
