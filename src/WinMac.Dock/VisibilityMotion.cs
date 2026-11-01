using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private bool visibilitySliding;
    private double visibilityFraction, visibilityFrom, visibilityTo;
    private long visibilityStamp;
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
        if (immediate || !Motion || Settings.AnimationMs == 0) { FinishVisibilityAnimation(); return; }
        PaintVisibilityFrame();
        CompositionTarget.Rendering += VisibilityRendering;
    }
    private void VisibilityRendering(object? sender, object args)
    {
        double duration = Settings.AnimationMs * Math.Abs(visibilityTo - visibilityFrom);
        double t = Math.Clamp(Stopwatch.GetElapsedTime(visibilityStamp).TotalMilliseconds / Math.Max(1, duration), 0, 1);
        visibilityFraction = visibilityFrom + (visibilityTo - visibilityFrom) * (1 - Math.Pow(1 - t, 3));
        PaintVisibilityFrame();
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
}
