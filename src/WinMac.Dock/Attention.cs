using Microsoft.UI.Xaml.Hosting;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private readonly HashSet<nint> attentionWindows = [];
    private readonly Dictionary<string, long> attentionBounces = [];
    private readonly Dictionary<string, double> attentionBounceEnds = [];
    private bool HasIconBounces => launchBounces.Count > 0 || attentionBounces.Count > 0;
    private double BounceEnvelopeHeight => Settings.AttentionAnimation == AttentionAnimation.HighBounce ? LaunchHeight * 3 : LaunchHeight;

    private void AttentionRequested(nint window)
    {
        if (window == 0 || !Native.IsWindow(window)) return;
        attentionWindows.Add(window);
        if (Settings.PreventAttentionBlinking) StopAttentionAnimation(window);
        else StartAttentionAnimation(window);
        if (Settings.ShowDockForAttention && hidden) ShowDock(restoreManualReservation: false);
    }

    private bool AttentionRequiresDock(nint foreground)
    {
        foreach (var window in attentionWindows.Where(w => !Native.IsWindow(w) || w == foreground).ToArray())
        {
            attentionWindows.Remove(window);
            StopAttentionAnimation(window);
        }
        foreach (var id in attentionBounces.Keys.ToArray())
        {
            if (!buttons.Any(b => b.Entry.Item.Id == id)) { attentionBounces.Remove(id); attentionBounceEnds.Remove(id); }
            else if (!buttons.Any(b => b.Entry.Item.Id == id && b.Entry.Windows.Any(w => attentionWindows.Contains(w.Handle)))) FinishAttentionBounce(id);
        }
        if (Settings.AttentionAnimation == AttentionAnimation.HighBounce && !Settings.PreventAttentionBlinking && Motion)
            foreach (var window in attentionWindows)
                if (buttons.FirstOrDefault(b => b.Entry.Windows.Any(w => w.Handle == window)) is var item
                    && item.Button != null && !attentionBounces.ContainsKey(item.Entry.Item.Id)) StartAttentionAnimation(window);
        if (attentionBounces.Count > 0 && Motion && !hidden && !dockOverflow && !dragging && popupCount == 0 && !rowAnimating && !reconciling)
        {
            if (hoverWave == null) PrepareHoverSurface();
            StartHoverRendering();
        }
        return Settings.ShowDockForAttention && (attentionWindows.Count > 0 || attentionBounceEnds.Count > 0);
    }

    private void StartAttentionAnimation(nint window)
    {
        if (!Motion || Settings.PreventAttentionBlinking) return;
        var entry = buttons.FirstOrDefault(x => x.Entry.Windows.Any(w => w.Handle == window));
        if (entry.Button == null) return;
        if (Settings.AttentionAnimation == AttentionAnimation.HighBounce)
        {
            attentionBounceEnds.Remove(entry.Entry.Item.Id);
            if (!attentionBounces.TryAdd(entry.Entry.Item.Id, System.Diagnostics.Stopwatch.GetTimestamp())) return;
            var opacity = ElementCompositionPreview.GetElementVisual(entry.Icon);
            opacity.StopAnimation("Opacity"); opacity.Opacity = 1;
            if (!hidden && !dockOverflow && popupCount == 0)
            {
                if (hoverWave == null) PrepareHoverSurface();
                StartHoverRendering();
            }
            return;
        }
        attentionBounces.Remove(entry.Entry.Item.Id); attentionBounceEnds.Remove(entry.Entry.Item.Id);
        var visual = ElementCompositionPreview.GetElementVisual(entry.Icon);
        visual.StopAnimation("Opacity");
        var animation = visual.Compositor.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, 1); animation.InsertKeyFrame(.5f, .25f); animation.InsertKeyFrame(1, 1);
        animation.Duration = TimeSpan.FromMilliseconds(800); animation.IterationCount = 3;
        visual.StartAnimation("Opacity", animation);
    }

    private void StopAttentionAnimation(nint window)
    {
        foreach (var entry in buttons.Where(x => x.Entry.Windows.Any(w => w.Handle == window)))
        {
            if (!Settings.PreventAttentionBlinking && entry.Entry.Windows.Any(w => w.Handle != window && attentionWindows.Contains(w.Handle))) continue;
            FinishAttentionBounce(entry.Entry.Item.Id);
            var visual = ElementCompositionPreview.GetElementVisual(entry.Icon);
            visual.StopAnimation("Opacity"); visual.Opacity = 1;
        }
    }

    private void RefreshAttentionAnimations()
    {
        attentionBounces.Clear(); attentionBounceEnds.Clear();
        foreach (var window in attentionWindows)
            if (Settings.PreventAttentionBlinking) StopAttentionAnimation(window); else StartAttentionAnimation(window);
    }

    private void FinishAttentionBounce(string id)
    {
        if (!attentionBounces.TryGetValue(id, out long stamp)) return;
        if (!Motion || Settings.PreventAttentionBlinking) { attentionBounces.Remove(id); attentionBounceEnds.Remove(id); return; }
        attentionBounceEnds.TryAdd(id, DockMotion.AttentionFinishTime(System.Diagnostics.Stopwatch.GetElapsedTime(stamp).TotalSeconds, LaunchBounceDuration));
    }

    private void ClearClickedAttention(DockEntry entry)
    {
        foreach (var window in entry.Windows.Select(w => w.Handle).Where(attentionWindows.Contains).ToArray())
        { attentionWindows.Remove(window); StopAttentionAnimation(window); }
    }
}
