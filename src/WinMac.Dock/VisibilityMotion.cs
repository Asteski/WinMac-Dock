using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private bool visibilitySliding;
    private double visibilityFraction, visibilityFrom, visibilityTo;
    private long visibilityStamp;
    private bool motionTesting;
    private int motionLayerErrors;
    private readonly List<double> motionFrameCosts = [];
    private readonly List<double> motionFrameIntervals = [];
    private long visibilityFrameStamp;
    private readonly TranslateTransform visibilityShift = new();

    private void StopVisibilityAnimation()
    {
        CompositionTarget.Rendering -= VisibilityRendering;
        visibilitySliding = false;
    }
    private void AnimateVisibility(bool show, bool immediate = false)
    {
        FinishRowTransition();
        bool reversing = visibilitySliding;
        StopVisibilityAnimation();
        if (!reversing) visibilityFraction = show ? 1 : 0;
        visibilityFrom = visibilityFraction; visibilityTo = show ? 0 : 1;
        visibilitySliding = true; root.Opacity = 1;
        root.RenderTransform = visibilityShift;
        Reposition(false);
        if (Native.Bounds(Handle) != hostBounds) Native.SetWindowPos(Handle, -1, hostBounds.Left, hostBounds.Top, hostBounds.Width, hostBounds.Height, 0x10 | 0x40);
        visibilityStamp = Stopwatch.GetTimestamp();
        visibilityFrameStamp = visibilityStamp;
        if (immediate || !Motion || Settings.AnimationMs == 0) { FinishVisibilityAnimation(); return; }
        PaintVisibilityFrame();
        CompositionTarget.Rendering += VisibilityRendering;
    }
    private void VisibilityRendering(object? sender, object args)
    {
        long started = Stopwatch.GetTimestamp();
        if (motionTesting) motionFrameIntervals.Add(Stopwatch.GetElapsedTime(visibilityFrameStamp, started).TotalMilliseconds);
        visibilityFrameStamp = started;
        double duration = Settings.AnimationMs * Math.Abs(visibilityTo - visibilityFrom);
        double t = Math.Clamp(Stopwatch.GetElapsedTime(visibilityStamp).TotalMilliseconds / Math.Max(1, duration), 0, 1);
        visibilityFraction = visibilityFrom + (visibilityTo - visibilityFrom) * (1 - Math.Pow(1 - t, 3));
        PaintVisibilityFrame();
        if (motionTesting)
        {
            if (Native.Bounds(Handle) != hostBounds || Native.Bounds(materialHandle) != DockGeometry.Slide(resting, PositionEdge, visibilityFraction, Settings.FloatingGap, Settings.KeepHiddenEdgeTrigger)) motionLayerErrors++;
            motionFrameCosts.Add(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        }
        if (t == 1 || !Motion) FinishVisibilityAnimation();
    }
    private void PaintVisibilityFrame()
    {
        // WinUI/shell position notifications can arrive after changing edge
        // or appbar reservation. Repair that one-time move before drawing.
        if (Native.Bounds(Handle) != hostBounds)
            Native.SetWindowPos(Handle, -1, hostBounds.Left, hostBounds.Top, hostBounds.Width, hostBounds.Height, 0x10 | 0x40);
        var panel = DockGeometry.Slide(resting, PositionEdge, visibilityFraction, Settings.FloatingGap, Settings.KeepHiddenEdgeTrigger);
        int dx = panel.Left - resting.Left, dy = panel.Top - resting.Top;
        // The input envelope stays fixed; the native material frame follows
        // the panel so its system shadow moves with the visible dock.
        visibilityShift.X = dx / scale; visibilityShift.Y = dy / scale;
        PaintMaterialSurface(0);
    }
    private void FinishVisibilityAnimation()
    {
        visibilityFraction = visibilityTo;
        StopVisibilityAnimation();
        visibilityShift.X = visibilityShift.Y = 0;
        materialHostBounds = default;
        root.Opacity = hidden ? 0 : 1;
        Reposition(true);
    }
    private async Task CheckDockMotion()
    {
        var originalEdge = PositionEdge; var originalMode = DisplayHideMode;
        bool originalInspection = inspection, originalReduce = Settings.ReduceMotion;
        Exception? failure = null; int checks = 0;
        try
        {
            inspection = true; motionTesting = true; motionLayerErrors = 0; motionFrameCosts.Clear(); motionFrameIntervals.Clear(); DisplayHideMode = HideMode.AlwaysVisible; Settings.ReduceMotion = false;
            foreach (var edge in Enum.GetValues<DockEdge>())
            {
                PositionEdge = edge; ApplySettings(); ShowDock();
                await Task.Delay(100);
                for (int cycle = 0; cycle < 4; cycle++)
                {
                    HandleCommand("--toggle"); await Task.Delay(cycle % 2 == 0 ? 45 : Settings.AnimationMs + 60);
                    if (visibilitySliding && Native.Bounds(materialHandle) != DockGeometry.Slide(resting, PositionEdge, visibilityFraction, Settings.FloatingGap, Settings.KeepHiddenEdgeTrigger)) throw new InvalidOperationException($"Material frame separated from dock during slide: {edge}, cycle {cycle}.");
                    HandleCommand("--toggle"); await Task.Delay(Settings.AnimationMs + 80);
                    if (hidden || visibilitySliding || root.Opacity != 1 || Native.Bounds(Handle) != hostBounds || Native.Bounds(materialHandle) != resting)
                        throw new InvalidOperationException("Hotkey toggle left the dock displaced or partially hidden.");
                    root.UpdateLayout();
                    if (Native.ClientBounds(Handle).Width != hostBounds.Width || Native.ClientBounds(materialHandle).Height != resting.Height)
                        throw new InvalidOperationException("Slide changed the client dimensions.");
                    checks++;
                }
                hoverTestInput = () => (ButtonSize * 1.5, true); UpdateMagnification(0);
                await Task.Delay(250);
                var icon = buttons.First().Icon; Bounce(icon);
                await Task.Delay(180); lastSignature = ""; Reconcile();
                if (launchBounces.Count == 0) throw new InvalidOperationException("Task refresh discarded launch bounce.");
                await Task.Delay(Math.Max(0, Settings.LaunchBounceMs - 180) + 100);
                if (launchBounces.Count != 0) throw new InvalidOperationException("Launch bounce did not settle.");
                checks++;
                StopHoverRendering();
                StopIndicatorAnimations();
                foreach (var slot in hoverSlots.Where(s => s.Indicator != null))
                {
                    bool rotated = Vertical && !Settings.ForceIndicatorsToItemBase;
                    foreach (int length in new[] { 14, 6, 14 })
                    {
                        slot.Indicator!.Width = rotated ? 3 : length; slot.Indicator.Height = rotated ? length : 3;
                        PositionIndicator(slot.Indicator); root.UpdateLayout();
                        if (slot.Indicator.ActualWidth != slot.Indicator.Width || slot.Indicator.ActualHeight != slot.Indicator.Height
                            || slot.IndicatorCarrier!.ActualWidth != slot.Indicator.Width || slot.IndicatorCarrier.ActualHeight != slot.Indicator.Height)
                            throw new InvalidOperationException("Stationary magnified indicator did not resize on focus change.");
                        var point = slot.Indicator.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                        double expectedX = slot.IndicatorOrigin.X + (slot.IndicatorWidth - slot.Indicator.Width) / 2 + slot.IndicatorShift.X;
                        double expectedY = slot.IndicatorOrigin.Y + (slot.IndicatorHeight - slot.Indicator.Height) / 2 + slot.IndicatorShift.Y;
                        if (Math.Abs(point.X - expectedX) > 1 || Math.Abs(point.Y - expectedY) > 1)
                            throw new InvalidOperationException("Resized magnified indicator lost its center.");
                    }
                }
                if (hoverSlots.FirstOrDefault(s => s.Indicator != null)?.Indicator is { } animatedIndicator)
                    await CheckIndicatorSizeTransition(animatedIndicator);
                hoverTestInput = null; ResetMagnification();
            }
            await CheckPinMagnificationContinuity();
            HandleCommand("--toggle"); Settings.ReduceMotion = true; ApplySettings(); HandleCommand("--show");
            if (visibilitySliding || hidden || Native.Bounds(Handle) != hostBounds) throw new InvalidOperationException("Reduce motion left an animation running.");
            checks++;
            if (motionLayerErrors > 0) throw new InvalidOperationException("Background and artwork diverged during a display frame.");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            motionTesting = false; hoverTestInput = null; ResetMagnification(); StopVisibilityAnimation(); hidden = manuallyHidden = false;
            PositionEdge = originalEdge; DisplayHideMode = originalMode; Settings.ReduceMotion = originalReduce;
            inspection = originalInspection; ApplySettings(); ShowDock();
        }
        var costs = motionFrameCosts.Order().ToArray();
        var intervals = motionFrameIntervals.Order().ToArray();
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "motion-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Frames = costs.Length, LayerErrors = motionLayerErrors,
            P99WorkMs = costs.Length == 0 ? 0 : costs[Math.Min(costs.Length - 1, (int)(costs.Length * .99))],
            MedianIntervalMs = intervals.Length == 0 ? 0 : intervals[intervals.Length / 2], P95IntervalMs = intervals.Length == 0 ? 0 : intervals[Math.Min(intervals.Length - 1, (int)(intervals.Length * .95))], Error = failure?.ToString() }));
    }
}
