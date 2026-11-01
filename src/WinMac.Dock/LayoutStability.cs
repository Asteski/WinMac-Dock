using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private readonly record struct HoverLayout(PixelRect Host, PixelRect Resting, DockEdge Edge, DockAlignment Alignment, double Scale,
        long X, long Y, long Width, long Height, long ScrollX, long ScrollY, long Icon, long Button, long Spacing, int Count);
    private HoverLayout? hoverLayoutStamp;
    private bool hoverGeometryRepairing, hoverGeometryQueued, hoverPainting;
    private bool deferredReposition, deferredReservation, deferredRepositionQueued;
    private HoverLayout CaptureHoverLayout()
    {
        var point = strip.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        static long Quantize(double value) => (long)Math.Round(value * 100);
        return new(hostBounds, resting, PositionEdge, PositionAlignment, scale, Quantize(point.X), Quantize(point.Y),
            Quantize(strip.ActualWidth), Quantize(strip.ActualHeight), Quantize(scroll.HorizontalOffset), Quantize(scroll.VerticalOffset),
            Quantize(DisplayIconSize), Quantize(ButtonSize), Quantize(DisplaySpacing), strip.Children.Count);
    }
    private bool CanResumeHoverHere()
    {
        if (!StartupComplete || closing || hidden || dragging || popupCount > 0 || dockOverflow || !Motion || !DisplayMagnificationEnabled || Settings.HoverMagnificationPercent == 0) return false;
        Native.GetCursorPos(out var cursor);
        return HoverContains(cursor);
    }
    private void QueueHoverGeometryCheck()
    {
        if (closing || reconciling || positioning || hoverRebuilding || hoverGeometryRepairing || hoverGeometryQueued || hoverWave == null
            || hoverLayoutStamp == CaptureHoverLayout()) return;
        hoverGeometryQueued = true;
        DispatcherQueue.TryEnqueue(() => { hoverGeometryQueued = false; if (!closing) RepairHoverGeometry(); });
    }
    private bool RepairHoverGeometry()
    {
        if (closing || reconciling || positioning || hoverRebuilding || hoverGeometryRepairing || hoverPainting || hoverWave == null || hoverLayoutStamp == CaptureHoverLayout()) return false;
        hoverGeometryRepairing = true;
        bool engaged = hoverEngaged;
        var growth = hoverSlots.Select((slot, i) => (slot.Element, Growth: hoverWave.Growth[i])).ToDictionary(x => x.Element, x => x.Growth);
        try
        {
            hoverRebuilding = true;
            ResetMagnification();
            if (dockOverflow || !Motion) return true;
            PrepareHoverSurface();
            hoverWave!.RestoreGrowth(hoverSlots.Select(slot => growth.GetValueOrDefault(slot.Element)).ToArray(), hoverAvailableExpansion, HoverOverflow);
            hoverEngaged = engaged;
            PaintHoverFrame();
            if (!hoverMenuHeld && popupCount == 0) StartHoverRendering();
            
            return true;
        }
        finally { hoverRebuilding = false; hoverGeometryRepairing = false; FlushDeferredReposition(); }
    }
    private void FlushDeferredReposition()
    {
        if (!deferredReposition || deferredRepositionQueued || closing) return;
        deferredRepositionQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            deferredRepositionQueued = false;
            if (closing || hoverPainting || hoverGeometryRepairing) return;
            bool reserve = deferredReservation;
            deferredReposition = deferredReservation = false;
            Reposition(reserve);
        });
    }
    private bool FitsNativeHoverClip(HoverSlot slot)
    {
        var clip = hoverClip ?? new PixelRect(0, 0, hostBounds.Width, hostBounds.Height);
        double tolerance = 1.1 / scale + .01;
        return slot.HitBounds.Left >= clip.Left / scale - tolerance && slot.HitBounds.Top >= clip.Top / scale - tolerance
            && slot.HitBounds.Right <= clip.Right / scale + tolerance && slot.HitBounds.Bottom <= clip.Bottom / scale + tolerance;
    }
    private double ValidateHoverGeometry()
    {
        if (dockOverflow)
        {
            if (hoverWave != null || hoverArtwork.Children.Count != 0) throw new Exception("Overflow retained detached hover artwork");
            return 0;
        }
        if (hoverWave == null) return 0;
        RepairHoverGeometry(); root.UpdateLayout();
        if (hoverWave == null) return 0;
        if (hoverWave.TotalGrowth > hoverAvailableExpansion + .001) throw new Exception("Hover growth exceeds the viewport expansion budget");
        double drift = 0, tolerance = 1.1 / scale + .01;
        foreach (var slot in hoverSlots.Where(s => s.Icon != null))
        {
            var parent = slot.OriginalParent!;
            var point = parent.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            double x = point.X - (slot.Shift.X - slot.LayoutShiftX) + (parent.ActualWidth - slot.Icon!.Width + slot.OriginalMargin.Left - slot.OriginalMargin.Right) / 2;
            double y = point.Y - (slot.Shift.Y - slot.LayoutShiftY) + (parent.ActualHeight - slot.Icon.Height + slot.OriginalMargin.Top - slot.OriginalMargin.Bottom) / 2;
            drift = Math.Max(drift, Math.Max(Math.Abs(x - slot.IconOrigin.X), Math.Abs(y - slot.IconOrigin.Y)));
            if (drift > tolerance) throw new Exception($"Detached artwork retained stale layout coordinates: {drift} DIP");
            var clip = hoverClip ?? new PixelRect(0, 0, hostBounds.Width, hostBounds.Height);
            if (slot.HitBounds.Left < clip.Left / scale - tolerance || slot.HitBounds.Top < clip.Top / scale - tolerance
                || slot.HitBounds.Right > clip.Right / scale + tolerance || slot.HitBounds.Bottom > clip.Bottom / scale + tolerance)
                throw new Exception($"Artwork escaped native clip: {slot.HitBounds}, clip {clip}, size {DisplayIconSize}, growth {hoverWave.TotalGrowth}/{hoverAvailableExpansion}");
        }
        return drift;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SetWindowTextW", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool SetStressWindowTitle(nint window, string title);
}
