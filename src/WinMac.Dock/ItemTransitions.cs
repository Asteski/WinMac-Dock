using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private sealed record RowSprite(string Key, FrameworkElement View, Windows.Foundation.Rect Bounds, double Opacity);
    private sealed record RowScene(PixelRect Panel, PixelRect Host, RowSprite[] Sprites, bool Overflow, DockEdge Edge, double Scale);
    private sealed class RowMove(RowSprite view, Windows.Foundation.Rect from, Windows.Foundation.Rect to, double fromOpacity, double toOpacity)
    {
        public RowSprite Sprite = view;
        public Windows.Foundation.Rect From = from, To = to, Current = from;
        public double FromOpacity = fromOpacity, ToOpacity = toOpacity, Opacity = fromOpacity;
        public bool ClosingIndicator;
    }
    private readonly Canvas rowArtwork = new() { IsHitTestVisible = false };
    private readonly Border rowOutline = new() { IsHitTestVisible = false, BackgroundSizing = BackgroundSizing.InnerBorderEdge };
    private readonly List<RowMove> rowMoves = [];
    private bool rowAnimating, rowFinishing, rowPainting, rowStaging;
    private bool rowPopupTransition;
    private bool rowSpanTransition;
    private PixelRect? spanTransitionHost;
    private PixelRect rowFromPanel, rowToPanel, rowDisplayPanel;
    private long rowStamp, rowAnimations, rowFrames;
    private bool rowTesting;
    private long rowClipErrors, rowIntermediateFrames;
    private long rowHandoffResizes;
    private readonly List<double> rowCompletedDurations = [];
    private PixelRect? retainedRowHost;
    private DockEdge retainedRowEdge;
    private DockAlignment retainedRowAlignment;
    private string retainedRowMonitor = "";
    private double retainedRowScale;
    private double[]? rowResumeGrowth;
    private bool rowResumeEngaged;
    private PixelRect MaterialPanel => materialVisible ? materialHostBounds : resting;

    private RowScene CaptureRowScene()
    {
        if (rowAnimating)
            return new(rowDisplayPanel, hostBounds, rowMoves.Select(m => m.Sprite with { Bounds = m.Current, Opacity = m.Opacity }).ToArray(), dockOverflow, PositionEdge, scale);
        root.UpdateLayout();
        var sprites = new List<RowSprite>();
        void Add(string key, FrameworkElement element, Windows.Foundation.Rect? overrideBounds = null)
        {
            var point = element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            var bounds = overrideBounds ?? new Windows.Foundation.Rect(point.X, point.Y, element.ActualWidth, element.ActualHeight);
            bounds = new(hostBounds.Left + bounds.X * scale, hostBounds.Top + bounds.Y * scale, bounds.Width * scale, bounds.Height * scale);
            if (bounds.Width <= 0 || bounds.Height <= 0) return;
            FrameworkElement clone;
            if (element is Border separator && separatorStrokes.TryGetValue(separator, out var stroke))
            {
                // Animate the painted pixel, rather than its wider clearance host.
                // Pixel snapping below must apply to the stroke's world origin.
                bounds = new(bounds.X + stroke.Offset.X * scale, bounds.Y + stroke.Offset.Y * scale,
                    stroke.Size.X * scale, stroke.Size.Y * scale);
                clone = new Border { Width = stroke.Size.X, Height = stroke.Size.Y, UseLayoutRounding = false,
                    Background = new SolidColorBrush(Microsoft.UI.Colors.Gray) };
            }
            else clone = CloneRowVisual(element, shellIcons.Any(item => ReferenceEquals(item.Artwork, element)));
            clone.Margin = new Thickness(0);
            // The animated Viewbox owns this element's opacity. Keeping it on
            // the clone as well squares translucent separators/indicators.
            // Nested children retain their own opacity inside the clone.
            clone.Opacity = 1;
            // Thin separators must use the same direct Border rasterization
            // as the live row, rather than being resampled by a Viewbox.
            FrameworkElement view = key.StartsWith("divider:", StringComparison.Ordinal) ? clone
                : new Viewbox { Child = clone, Stretch = Stretch.Fill, IsHitTestVisible = false };
            view.IsHitTestVisible = false;
            sprites.Add(new(key, view, bounds, element.Opacity));
        }
        foreach (var b in buttons)
        {
            var slot = hoverSlots.FirstOrDefault(s => s.Element == b.Button);
            var iconBounds = slot?.HitBounds;
            if (iconBounds is { } bouncing && slot != null)
                iconBounds = OffsetRowBounce(bouncing, b.Entry.Item.Id + ":icon", -1 / scale);
            Add(b.Entry.Item.Id + ":icon", b.Icon, iconBounds);
            if (runningIndicators.TryGetValue(b.Button, out var indicator)) Add(b.Entry.Item.Id + ":indicator", indicator);
            if (downloadIndicators.TryGetValue(b.Button, out var download)) Add(b.Entry.Item.Id + ":download", download);
            if (groupIndicators.TryGetValue(b.Button, out var badge)) Add(b.Entry.Item.Id + ":badge", badge);
        }
        int divider = 0;
        foreach (var separator in strip.Children.OfType<Border>()) Add("divider:" + divider++, separator);
        return new(MaterialPanel, hostBounds, sprites.ToArray(), dockOverflow, PositionEdge, scale);
    }
    private Windows.Foundation.Rect OffsetRowBounce(Windows.Foundation.Rect bounds, string key, double factor = 1)
    {
        if (!key.EndsWith(":icon", StringComparison.Ordinal)) return bounds;
        double bounce = LaunchOffset(key[..^5]) * scale * factor;
        return new(bounds.X + (Vertical ? PositionEdge == DockEdge.Left ? bounce : -bounce : 0),
            bounds.Y + (Vertical ? 0 : PositionEdge == DockEdge.Top ? bounce : -bounce), bounds.Width, bounds.Height);
    }
    private static FrameworkElement CloneRowVisual(FrameworkElement source, bool restoreShellArtwork = false)
    {
        FrameworkElement clone;
        switch (source)
        {
            case Image image:
                var copy = new Image { Stretch = image.Stretch };
                copy.SetBinding(Image.SourceProperty, new Binding { Source = image, Path = new PropertyPath("Source") });
                clone = copy; break;
            case FontIcon glyph: clone = new FontIcon { Glyph = glyph.Glyph, FontFamily = glyph.FontFamily, FontSize = glyph.FontSize, Foreground = glyph.Foreground }; break;
            case TextBlock text: clone = new TextBlock { Text = text.Text, FontSize = text.FontSize, Foreground = text.Foreground, FontWeight = text.FontWeight, TextAlignment = text.TextAlignment }; break;
            case Border border:
                clone = new Border { Background = border.Background, BackgroundSizing = border.BackgroundSizing, BorderBrush = border.BorderBrush, BorderThickness = border.BorderThickness, CornerRadius = border.CornerRadius,
                    Child = border.Child is FrameworkElement child ? CloneRowVisual(child, restoreShellArtwork) : null }; break;
            case Panel panel:
                var grid = new Grid(); foreach (var member in panel.Children.OfType<FrameworkElement>()) grid.Children.Add(CloneRowVisual(member, restoreShellArtwork)); clone = grid; break;
            default: clone = new Grid(); break;
        }
        clone.Width = double.IsNaN(source.Width) ? source.ActualWidth : source.Width;
        clone.Height = double.IsNaN(source.Height) ? source.ActualHeight : source.Height;
        clone.HorizontalAlignment = source.HorizontalAlignment; clone.VerticalAlignment = source.VerticalAlignment;
        clone.Margin = source.Margin; clone.Opacity = restoreShellArtwork ? 1 : source.Opacity;
        clone.SetBinding(UIElement.VisibilityProperty, new Binding { Source = source, Path = new PropertyPath("Visibility") });
        return clone;
    }
    private static PixelRect Union(PixelRect a, PixelRect b) => new(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
    private PixelRect RetainRowHost(PixelRect candidate)
    {
        if (retainedRowHost is not { } previous) return candidate;
        double AlongAnchor(PixelRect bounds) => PositionAlignment switch
        {
            DockAlignment.Start => Vertical ? bounds.Top : bounds.Left,
            DockAlignment.End => Vertical ? bounds.Bottom : bounds.Right,
            _ => Vertical ? (bounds.Top + bounds.Bottom) / 2d : (bounds.Left + bounds.Right) / 2d
        };
        int CrossAnchor(PixelRect bounds) => PositionEdge switch { DockEdge.Top => bounds.Top, DockEdge.Left => bounds.Left, DockEdge.Right => bounds.Right, _ => bounds.Bottom };
        var union = Union(previous, candidate);
        bool fits = (Vertical ? union.Height : union.Width) <= (Vertical ? monitor.Work.Height : monitor.Work.Width) - DockGeometry.Pixels(16, scale);
        if (!Motion || !HoverEnvelope || retainedRowEdge != PositionEdge || retainedRowAlignment != PositionAlignment || retainedRowMonitor != monitor.Device
            || retainedRowScale != scale || !fits || Math.Abs(AlongAnchor(previous) - AlongAnchor(candidate)) > 1 || CrossAnchor(previous) != CrossAnchor(candidate))
        { retainedRowHost = null; return candidate; }
        // The transparent native envelope may stay larger than the visible dock.
        // Keeping it stable avoids reallocating both compositor surfaces at the
        // exact frame that the transition hands back to the live row.
        return (retainedRowHost = union).Value;
    }
    private static Windows.Foundation.Rect Small(Windows.Foundation.Rect value) => new(value.X + value.Width * .08, value.Y + value.Height * .08, value.Width * .84, value.Height * .84);
    private void TransitionDockSpan()
    {
        var before = Motion && Settings.AnimationMs > 0 && !hidden && !dockOverflow && !dragging && popupCount == 0 ? CaptureRowScene() : null;
        var growth = hoverWave?.Growth.ToArray();
        bool engaged = hoverEngaged;
        if (rowAnimating) StopRowTransition();
        spanTransitionHost = before?.Host;
        rowStaging = before != null;
        hoverRebuilding = true;
        try
        {
            ResetMagnification(); Reposition(false); UpdateChrome();
            root.UpdateLayout();
            if (growth != null && Motion && !dockOverflow)
            {
                // Preserve an existing lens, but do not manufacture a zero-growth
                // hover layout for an idle span change. Its temporary rounding
                // corrections disappear on the next hover reset, causing a hop
                // after the row animation has already handed back to live items.
                // The stable native viewport keeps the idle row aligned directly.
                PrepareHoverSurface();
                if (hoverWave!.Count == growth.Length) hoverWave.RestoreGrowth(growth, hoverAvailableExpansion, HoverOverflow);
                hoverEngaged = engaged;
                PaintHoverFrame();
            }
        }
        finally { hoverRebuilding = false; rowStaging = false; spanTransitionHost = null; }
        if (before != null) { BeginRowTransition(before); rowSpanTransition = rowAnimating; }
    }
    private void BeginRowTransition(RowScene before, bool allowOpenFlyout = false)
    {
        if (!Motion || Settings.AnimationMs == 0 || hidden || visibilitySliding || dragging || (popupCount > 0 && !allowOpenFlyout) || dockOverflow || before.Overflow
            || before.Edge != PositionEdge || before.Scale != scale)
        {
            FinishRowTransition();
            if (hoverWave == null) visibleBounds = resting;
            SetHoverClip(); PaintMaterialSurface(hoverWave == null ? 0 : Math.Min(hoverWave.TotalGrowth, hoverAvailableExpansion));
            return;
        }
        var after = CaptureRowScene();
        // Rebuilds stage the resting destination without painting its native
        // material clip. The visible clip must go directly from the previous
        // scene into the tween, rather than destination -> start -> tween.
        after = after with { Panel = ExpandedMaterialPanel(hoverWave == null ? 0 : Math.Min(hoverWave.TotalGrowth, hoverAvailableExpansion)) };
        StopRowTransition();
        rowPopupTransition = allowOpenFlyout;
        rowResumeGrowth = hoverWave?.Growth.ToArray(); rowResumeEngaged = hoverEngaged;
        hoverRebuilding = true; ResetMagnification(); hoverRebuilding = false;
        var from = before.Sprites.ToDictionary(s => s.Key);
        var to = after.Sprites.ToDictionary(s => s.Key);
        foreach (var old in before.Sprites.Where(s => !to.ContainsKey(s.Key)))
        {
            bool indicator = old.Key.EndsWith(":indicator", StringComparison.Ordinal) || old.Key.EndsWith(":badge", StringComparison.Ordinal)
                || old.Key.EndsWith(":download", StringComparison.Ordinal);
            bool badge = old.Key.EndsWith(":badge", StringComparison.Ordinal);
            var end = badge ? new Windows.Foundation.Rect(old.Bounds.X + old.Bounds.Width / 2, old.Bounds.Y + old.Bounds.Height / 2, 0, 0) : Small(old.Bounds);
            if (indicator && !badge)
            {
                // Reverse the reveal by collapsing symmetrically to the centre.
                bool rotated = !old.Key.EndsWith(":download", StringComparison.Ordinal) && Vertical && !Settings.ForceIndicatorsToItemBase;
                end = rotated ? new(old.Bounds.X, old.Bounds.Y + old.Bounds.Height / 2, old.Bounds.Width, 0)
                    : new(old.Bounds.X + old.Bounds.Width / 2, old.Bounds.Y, 0, old.Bounds.Height);
            }
            rowMoves.Add(new(old, old.Bounds, end, old.Opacity, 0) { ClosingIndicator = indicator });
        }
        foreach (var next in after.Sprites)
            rowMoves.Add(from.TryGetValue(next.Key, out var old) ? new(next, old.Bounds, next.Bounds, old.Opacity, next.Opacity) : new(next, Small(next.Bounds), next.Bounds, 0, next.Opacity));
        // Keep the old and new artwork inside a stable transparent envelope.
        // Native input/material clips define the visible dock size; the host
        // itself need not shrink when handing the animation to the live row.
        var bounds = Union(before.Host, after.Host);
        retainedRowHost = bounds; retainedRowEdge = PositionEdge; retainedRowAlignment = PositionAlignment;
        retainedRowScale = scale; retainedRowMonitor = monitor.Device;
        hostBounds = bounds; root.Width = bounds.Width / scale; root.Height = bounds.Height / scale;
        if (Native.Bounds(Handle) != bounds)
            Native.SetWindowPos(Handle, -1, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x10 | 0x40);
        hoverClip = null;
        strip.Opacity = 0; hoverArtwork.Opacity = 0; dockBorder.Opacity = 0;
        rowOutline.BorderBrush = dockBorder.BorderBrush; rowOutline.BorderThickness = dockBorder.BorderThickness; rowOutline.CornerRadius = dockBorder.CornerRadius;
        rowArtwork.Children.Add(rowOutline);
        foreach (var move in rowMoves) rowArtwork.Children.Add(move.Sprite.View);
        if (!root.Children.Contains(rowArtwork)) root.Children.Add(rowArtwork);
        rowFromPanel = before.Panel; rowToPanel = after.Panel; rowDisplayPanel = rowFromPanel;
        rowStamp = Stopwatch.GetTimestamp(); rowAnimating = true; rowAnimations++;
        PaintRowTransition(0);
        CompositionTarget.Rendering += RowRendering;
    }
    private void RowRendering(object? sender, object args)
    {
        if (!rowAnimating || reconciling || positioning || rowPainting) return;
        if (!Motion || hidden || visibilitySliding || dragging || (popupCount > 0 && !rowPopupTransition) || Settings.AnimationMs == 0) { FinishRowTransition(); return; }
        double elapsed = Stopwatch.GetElapsedTime(rowStamp).TotalMilliseconds;
        double t = Math.Clamp(elapsed / DockMotion.RearrangeMs, 0, 1);
        PaintRowTransition(DockMotion.RearrangeProgress(elapsed));
        if (t >= 1) FinishRowTransition();
    }
    private void PaintRowTransition(double progress)
    {
        rowPainting = true;
        try
        {
            double Lerp(double a, double b) => a + (b - a) * progress;
            rowDisplayPanel = new((int)Math.Round(Lerp(rowFromPanel.Left, rowToPanel.Left)), (int)Math.Round(Lerp(rowFromPanel.Top, rowToPanel.Top)),
                (int)Math.Round(Lerp(rowFromPanel.Right, rowToPanel.Right)), (int)Math.Round(Lerp(rowFromPanel.Bottom, rowToPanel.Bottom)));
            var input = rowDisplayPanel;
            Canvas.SetLeft(rowOutline, (rowDisplayPanel.Left - hostBounds.Left) / scale); Canvas.SetTop(rowOutline, (rowDisplayPanel.Top - hostBounds.Top) / scale);
            rowOutline.Width = rowDisplayPanel.Width / scale; rowOutline.Height = rowDisplayPanel.Height / scale;
            foreach (var move in rowMoves)
            {
                var value = new Windows.Foundation.Rect(Lerp(move.From.X, move.To.X), Lerp(move.From.Y, move.To.Y), Lerp(move.From.Width, move.To.Width), Lerp(move.From.Height, move.To.Height));
                if (move.ClosingIndicator)
                {
                    double elapsed = Stopwatch.GetElapsedTime(rowStamp).TotalMilliseconds;
                    double close = move.Sprite.Key.EndsWith(":badge", StringComparison.Ordinal) ? DockMotion.BadgeCloseProgress(elapsed) : DockMotion.IndicatorCloseProgress(elapsed);
                    double Blend(double a, double b) => a + (b - a) * close;
                    value = new(Blend(move.From.X, move.To.X), Blend(move.From.Y, move.To.Y), Blend(move.From.Width, move.To.Width), Blend(move.From.Height, move.To.Height));
                }
                if (move.Sprite.Key.StartsWith("divider:", StringComparison.Ordinal))
                {
                    // Fractional coverage makes a moving one-pixel line appear
                    // to fade even when its brush and opacity remain unchanged.
                    value = new(Math.Round(value.X), Math.Round(value.Y), Math.Max(1, Math.Round(value.Width)), Math.Max(1, Math.Round(value.Height)));
                }
                move.Current = value;
                value = OffsetRowBounce(value, move.Sprite.Key);
                move.Opacity = move.ClosingIndicator ? (Stopwatch.GetElapsedTime(rowStamp).TotalMilliseconds < DockMotion.IndicatorCloseMs ? move.FromOpacity : 0) : Lerp(move.FromOpacity, move.ToOpacity);
                Canvas.SetLeft(move.Sprite.View, (value.X - hostBounds.Left) / scale); Canvas.SetTop(move.Sprite.View, (value.Y - hostBounds.Top) / scale);
                move.Sprite.View.Width = value.Width / scale; move.Sprite.View.Height = value.Height / scale; move.Sprite.View.Opacity = move.Opacity;
                // Keep the native shell visual instead of swapping it for a
                // bitmap while windows are inserted or removed from the row.
                if (shellIcons.Any(icon => move.Sprite.Key == icon.Entry.Item.Id + ":icon")) move.Sprite.View.Opacity = 0;
                if (move.Opacity > .001 && !move.ClosingIndicator) input = Union(input, new((int)Math.Floor(value.Left), (int)Math.Floor(value.Top), (int)Math.Ceiling(value.Right), (int)Math.Ceiling(value.Bottom)));
            }
            visibleBounds = input; SetHoverClip(); PaintMaterialSurface(0); rowFrames++;
            if (rowTesting || hoverTestInput != null)
            {
                if (progress > 0 && progress < 1) rowIntermediateFrames++;
                foreach (var move in rowMoves.Where(m => m.Opacity > .001 && !m.ClosingIndicator))
                    if (move.Current.Left < hostBounds.Left - 1.1 || move.Current.Top < hostBounds.Top - 1.1
                        || move.Current.Right > hostBounds.Right + 1.1 || move.Current.Bottom > hostBounds.Bottom + 1.1) rowClipErrors++;
            }
        }
        finally { rowPainting = false; }
    }
    private void ClearRowArtwork()
    {
        rowArtwork.Children.Clear(); root.Children.Remove(rowArtwork); rowMoves.Clear();
    }
    private void StopRowTransition(bool keepArtwork = false)
    {
        CompositionTarget.Rendering -= RowRendering;
        rowAnimating = false;
        rowPopupTransition = false;
        rowSpanTransition = false;
        strip.Opacity = 1; hoverArtwork.Opacity = 1; dockBorder.Opacity = 1;
        if (!keepArtwork) ClearRowArtwork();
    }
    private void FinishRowTransition()
    {
        if (!rowAnimating || rowFinishing) return;
        rowFinishing = true;
        var previousHost = hostBounds;
        var previousMaterialHost = materialHostBounds;
        double elapsed = Stopwatch.GetElapsedTime(rowStamp).TotalMilliseconds;
        bool natural = !reconciling && !positioning && rowDisplayPanel == rowToPanel && elapsed >= DockMotion.RearrangeMs;
        bool spanLayoutSettled = rowSpanTransition && natural && resting == rowToPanel && materialHostBounds == rowToPanel
            && Native.Bounds(Handle) == hostBounds;
        if ((rowTesting || hoverTestInput != null) && natural) rowCompletedDurations.Add(elapsed);
        try
        {
            StopRowTransition(true); hoverRebuilding = true;
            // Span staging already positioned the live row and both HWNDs.
            // Reapplying layout at a natural handoff temporarily resets the
            // outline to stretch/automatic sizing before painting it again.
            // Keep the completed geometry; interrupted/layout-changing tweens
            // still need the normal reposition and reconciliation path.
            if (!spanLayoutSettled) Reposition(false);
            hoverRebuilding = false; root.UpdateLayout();
            // An edge/work-area/settings change can end a tween before its old
            // row has been refitted. Reconcile that row before painting hover.
            bool rebuilding = reconciling;
            if (!rebuilding && !closing)
            {
                rowFinishing = false; Reconcile(); rowFinishing = true;
                if (rowAnimating) return;
            }
            if (!rebuilding && !closing && !dockOverflow && Motion && (rowResumeGrowth != null || CanResumeHoverHere()))
            {
                PrepareHoverSurface();
                if (rowResumeGrowth?.Length == hoverWave!.Count) hoverWave.RestoreGrowth(rowResumeGrowth, hoverAvailableExpansion, HoverOverflow);
                hoverEngaged = rowResumeEngaged || CanResumeHoverHere(); PaintHoverFrame(); StartHoverRendering();
            }
            else { visibleBounds = resting; SetHoverClip(); PaintMaterialSurface(0); }
            // Reveal and lay out the live row before retiring its transition
            // cover, so there is no blank compositor frame between the two.
            root.UpdateLayout(); ClearRowArtwork();
            rowResumeGrowth = null;
            if ((rowTesting || hoverTestInput != null) && natural && Motion && !rowAnimating
                && (hostBounds != previousHost || materialHostBounds != previousMaterialHost)) rowHandoffResizes++;
        }
        finally { hoverRebuilding = false; rowFinishing = false; }
    }
}
