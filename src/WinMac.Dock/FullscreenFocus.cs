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
    private async void CheckPassiveFocusRecovery()
    {
        Exception? failure = null;
        nint foreground = Native.GetForegroundWindow();
        bool fullscreen = HasExternalFullscreenForeground(), deferred = false;
        try
        {
            // Temporarily bypass the dock's minimize blocker to exercise the
            // recovery path that otherwise only runs after shell interference.
            Native.RemoveWindowSubclass(Handle, subclass, 1);
            try { Native.ShowWindow(Handle, 7); }
            finally { Native.SetWindowSubclass(Handle, subclass, 1, 0); }
            if (!Native.IsIconic(Handle)) throw new InvalidOperationException("Could not prepare a minimized dock for the recovery test.");
            EnsureDockIsNotMinimized();
            deferred = Native.IsIconic(Handle);
            if (fullscreen && !deferred) throw new InvalidOperationException("Recovery was not deferred during fullscreen.");
            if (!fullscreen)
            {
                Native.ShowWindow(Handle, 4);
                Reposition(false);
                if (Native.IsIconic(Handle)) throw new InvalidOperationException("Non-activating recovery did not restore the dock.");
            }
            await Task.Delay(700);
            if (Native.GetForegroundWindow() != foreground) throw new InvalidOperationException("Dock recovery changed foreground focus.");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            if (!HasExternalFullscreenForeground()) { Native.ShowWindow(Handle, 4); Reposition(false); }
            EvaluateVisibility();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "passive-focus-recovery-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Fullscreen = fullscreen, RecoveryDeferred = deferred, ForegroundUnchanged = Native.GetForegroundWindow() == foreground }));
    }
}
