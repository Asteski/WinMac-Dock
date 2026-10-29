namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private readonly WindowsTaskbar windowsTaskbar = new();
    private readonly EdgeActivation edgeActivation = new();
    private bool edgeToggleHidden, startupWaiting;
    private HideMode previousHideMode;

    internal void SetExplicitVisibility(bool show, bool broadcast = true)
    {
        if (broadcast) App.SetDockVisibility(show, this);
        startupWaiting = false;
        edgeToggleHidden = !show;
        edgeActivation.Reset();
        if (show) ShowDock(); else { manuallyHidden = true; HideDock(); }
    }
    private PixelRect EdgeTriggerBounds => Settings.FullScreenEdgeTrigger ? DockGeometry.ExpandEdgeTrigger(resting, PositionEdge, monitor.Bounds) : resting;
    private bool ReleasedEdge(Native.POINT cursor) => PositionEdge switch
    {
        DockEdge.Top => Math.Abs(cursor.Y - (EdgeTriggerBounds.Top - Settings.FloatingGap)) > 8 || cursor.X < EdgeTriggerBounds.Left || cursor.X >= EdgeTriggerBounds.Right,
        DockEdge.Left => Math.Abs(cursor.X - (EdgeTriggerBounds.Left - Settings.FloatingGap)) > 8 || cursor.Y < EdgeTriggerBounds.Top || cursor.Y >= EdgeTriggerBounds.Bottom,
        DockEdge.Right => Math.Abs(cursor.X - (EdgeTriggerBounds.Right + Settings.FloatingGap)) > 8 || cursor.Y < EdgeTriggerBounds.Top || cursor.Y >= EdgeTriggerBounds.Bottom,
        _ => Math.Abs(cursor.Y - (EdgeTriggerBounds.Bottom + Settings.FloatingGap)) > 8 || cursor.X < EdgeTriggerBounds.Left || cursor.X >= EdgeTriggerBounds.Right
    };
    private bool EvaluateEdgeVisibility(Native.POINT cursor, bool edge, bool fullscreen, bool excluded, bool interacting)
    {
        if (DisplayHideMode == HideMode.EdgeToggle)
        {
            hideSince = revealSince = manualHideSince = null;
            if (edgeActivation.Update(edge, ReleasedEdge(cursor), popupCount > 0 || dragging, DateTime.UtcNow, Settings.RevealDelayMs))
            { edgeToggleHidden = !edgeToggleHidden; manuallyHidden = false; }
            bool shouldHide = edgeToggleHidden || (fullscreen && !excluded && !interacting && !edge);
            if (shouldHide) HideDock(immediate: fullscreen && Settings.HideImmediatelyForFullscreen);
            else if (hidden) ShowDock(restoreManualReservation: false);
            return true;
        }
        if (!startupWaiting) return false;
        if (!hidden) HideDock(immediate: true);
        if (edge)
        {
            revealSince ??= DateTime.UtcNow;
            if ((DateTime.UtcNow - revealSince.Value).TotalMilliseconds >= Settings.RevealDelayMs)
            { startupWaiting = false; ShowDock(); }
        }
        else revealSince = null;
        return true;
    }
}
