namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private nint fullscreenUnderlay, lastFullscreenWindow;
    private uint lastFullscreenProcess;
    private long fullscreenRestoreUntil;
    private nint DockZOrderTarget => fullscreenUnderlay != 0 ? fullscreenUnderlay : (nint)(-1);

    private void UpdateFullscreenLayering(bool foregroundChanged = false)
    {
        if (!initialized || closing || monitor == null) return;
        var foreground = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(foreground, out uint process);
        var fullscreen = DescribeFullscreenForeground(foreground);
        bool known = foreground != 0 && foreground == lastFullscreenWindow && process == lastFullscreenProcess
            && Native.IsWindowVisible(foreground) && !Native.IsIconic(foreground);
        if (fullscreen != null)
        {
            lastFullscreenWindow = foreground; lastFullscreenProcess = process;
            fullscreenRestoreUntil = 0;
        }
        else if (foregroundChanged && known) fullscreenRestoreUntil = Environment.TickCount64 + 700;
        bool restoring = known && Environment.TickCount64 < fullscreenRestoreUntil;
        var candidate = fullscreen ?? (restoring ? tracker.Describe(foreground) : null);
        bool lower = candidate != null && !CanRevealAboveFullscreen(candidate) && !taskViewOpen;
        nint target = lower ? foreground : 0;
        if (fullscreenUnderlay != target)
        {
            fullscreenUnderlay = target;
            Native.SetWindowPos(Handle, DockZOrderTarget, 0, 0, 0, 0, 0x13);
            if (materialHandle != 0) Native.SetWindowPos(materialHandle, Handle, 0, 0, 0, 0, 0x13);
        }
        gapInput?.SetEnabled(!fullscreenIdleActive && !hidden && fullscreenUnderlay == 0
            && Settings.ActivateThroughScreenEdgeGap && Settings.FloatingGap > 0);
    }
}
