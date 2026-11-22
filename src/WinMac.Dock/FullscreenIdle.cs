using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private volatile bool fullscreenIdleActive;
    private readonly DispatcherTimer fullscreenIdleResumeTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private bool fullscreenIdleResumeHooked;

    private void UpdateFullscreenIdle()
    {
        var fullscreen = DescribeFullscreenForeground(Native.GetForegroundWindow());
        bool active = hidden && !visibilitySliding && !taskViewOpen
            && settingsWindow == null && popupCount == 0 && !dragging
            && fullscreen != null && !FullscreenExclusions.Contains(Settings, fullscreen);
        SetFullscreenIdleActive(active);
    }

    private void SetFullscreenIdleActive(bool active)
    {
        if (fullscreenIdleActive == active) return;
        fullscreenIdleActive = active;
        fullscreenIdleResumeTimer.Stop();
        visibilityTimer.Interval = TimeSpan.FromMilliseconds(100);
        if (active)
        {
            gapInput?.SetEnabled(false);
            reconcileTimer.Stop();
            StopRowTransition();
            StopIndicatorAnimations();
            launchBounces.Clear(); attentionBounces.Clear(); attentionBounceEnds.Clear();
            ResetMagnification();
            StopHoverRendering();
            foreach (var entry in buttons)
            {
                var visual = ElementCompositionPreview.GetElementVisual(entry.Icon);
                visual.StopAnimation("Opacity"); visual.Opacity = 1;
            }
        }
        else if (!closing)
        {
            // Watchers remain subscribed. Rescan after returning so deferred
            // file and drive changes are applied without replaying stale work.
            if (!fullscreenIdleResumeHooked)
            {
                fullscreenIdleResumeTimer.Tick += (_, _) => ResumeFullscreenIdleWork();
                fullscreenIdleResumeHooked = true;
            }
            fullscreenIdleResumeTimer.Start();
        }
    }
    private void ResumeFullscreenIdleWork()
    {
        fullscreenIdleResumeTimer.Stop();
        if (closing || fullscreenIdleActive) return;
        if (ReadThemeIdentity() != themeIdentity) UpdateTheme(reconcile: false);
        Reconcile();
        UpdateRecycle();
        _ = ScanDownloadPins();
        _ = ScanUsbPins();
        _ = RefreshPinArtwork();
        RefreshAttentionAnimations();
    }
}
