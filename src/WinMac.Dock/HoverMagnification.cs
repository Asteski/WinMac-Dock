using System.Diagnostics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private sealed record HoverSlot(FrameworkElement Element, TranslateTransform Shift, FrameworkElement? Icon, Visual? Visual, Windows.Foundation.Point IconOrigin)
    {
        public double LayoutShiftX { get; set; }
        public double LayoutShiftY { get; set; }
        public double ElementOriginX { get; init; }
        public double ElementOriginY { get; init; }
        public Windows.Foundation.Rect HitBounds { get; set; }
        public Grid? OriginalParent { get; set; }
        public int OriginalIndex { get; set; }
        public Thickness OriginalMargin { get; set; }
        public Windows.Foundation.Point OriginalIconOffset { get; set; }
        public TranslateTransform ArtworkShift { get; } = new();
        public Grid? ArtworkCarrier { get; set; }
        public Border? Indicator { get; set; }
        public Windows.Foundation.Point IndicatorOrigin { get; set; }
        public double IndicatorWidth { get; set; }
        public double IndicatorHeight { get; set; }
        public Grid? IndicatorCarrier { get; set; }
        public Grid? IndicatorParent { get; set; }
        public int IndicatorIndex { get; set; }
        public Thickness IndicatorMargin { get; set; }
        public HorizontalAlignment IndicatorHorizontal { get; set; }
        public VerticalAlignment IndicatorVertical { get; set; }
        public TranslateTransform IndicatorShift { get; } = new();
        public Border? GroupIndicator { get; set; }
        public Grid? GroupParent { get; set; }
        public Thickness GroupMargin { get; set; }
        public Thickness GroupInsets { get; set; }
        public Windows.Foundation.Point GroupOrigin { get; set; }
        public Windows.Foundation.Point GroupTextOrigin { get; set; }
        public Grid? GroupCarrier { get; set; }
        public TranslateTransform GroupShift { get; } = new();
    }
    private HoverSlot[] hoverSlots = [];
    private MagnificationWave? hoverWave;
    private PixelRect hostBounds;
    private PixelRect? hoverClip;
    private long hoverWindowMoves, hoverFrames, hoverFrameStamp;
    private bool hoverRendering, hoverEngaged, hoverMenuHeld, hoverRebuilding;
    private double hoverAvailableExpansion;
    private double hoverRowOffset;
    private readonly ScaleTransform hoverPanelScale = new();
    private readonly List<double> hoverFrameIntervals = [], hoverFrameCosts = [];
    private readonly List<string> hoverUnexpectedMoves = [];
    private int hoverArtworkClips;
    private double hoverIndicatorDrift, hoverEndGapDrift, hoverGroupTextDrift;
    private long hoverValidationTicks;
    private Func<(double Pointer, bool Active)>? hoverTestInput;
    private readonly Dictionary<string, long> launchBounces = [];
    private double LaunchHeight => Math.Min(24, DisplayIconSize * .5);
    private double LaunchBounceDuration => Settings.LaunchBounceMs / 1000d;
    private double LaunchOffset(HoverSlot slot)
    {
        if (slot.Element is not Button button || button.Tag is not DockEntry entry) return 0;
        return LaunchOffset(entry.Item.Id);
    }
    private double LaunchOffset(string itemId)
    {
        if (attentionBounces.TryGetValue(itemId, out var attentionStamp))
        {
            double seconds = Stopwatch.GetElapsedTime(attentionStamp).TotalSeconds;
            if (!attentionBounceEnds.TryGetValue(itemId, out double end) || seconds < end)
                return DockMotion.AttentionOffset(seconds, LaunchHeight, LaunchBounceDuration);
            attentionBounces.Remove(itemId); attentionBounceEnds.Remove(itemId);
        }
        if (!launchBounces.TryGetValue(itemId, out var stamp)) return 0;
        double elapsed = Stopwatch.GetElapsedTime(stamp).TotalSeconds;
        if (elapsed >= LaunchBounceDuration) { launchBounces.Remove(itemId); return 0; }
        return DockMotion.LaunchOffset(elapsed * DockMotion.LaunchDuration / LaunchBounceDuration, LaunchHeight);
    }
    private double HoverAnchor => PositionAlignment switch { DockAlignment.Start => 0, DockAlignment.End => 1, _ => .5 };
    private static void SyncHoverIndicator(HoverSlot slot)
    {
        if (slot.IndicatorCarrier == null || slot.Indicator == null) return;
        slot.IndicatorCarrier.Width = slot.Indicator.Width;
        slot.IndicatorCarrier.Height = slot.Indicator.Height;
        Canvas.SetLeft(slot.IndicatorCarrier, slot.IndicatorOrigin.X + (slot.IndicatorWidth - slot.Indicator.Width) / 2);
        Canvas.SetTop(slot.IndicatorCarrier, slot.IndicatorOrigin.Y + (slot.IndicatorHeight - slot.Indicator.Height) / 2);
    }

    private void StartHoverRendering()
    {
        if (hoverRendering || (hoverClickHeld && !HasIconBounces)) return;
        hoverFrameStamp = Stopwatch.GetTimestamp();
        hoverRendering = true;
        CompositionTarget.Rendering += HoverRendering;
    }
    private void StopHoverRendering()
    {
        if (!hoverRendering) return;
        CompositionTarget.Rendering -= HoverRendering;
        hoverRendering = false;
    }
    private bool hoverClickHeld;
    private Windows.Foundation.Point hoverClickPoint;
    private void HoldHoverForClick(Windows.Foundation.Point point)
    {
        if (hoverWave == null || hoverTestInput != null) return;
        hoverClickHeld = true; hoverClickPoint = point;
        // Hold the magnification lens, but keep the bounce clock painting.
        // Pausing frames here makes the fast high jump skip on activation.
        if (HasIconBounces) StartHoverRendering();
        else StopHoverRendering();
    }
    private bool KeepHoverClickHold(Windows.Foundation.Point point)
    {
        if (!hoverClickHeld) return false;
        if (Math.Abs(point.X - hoverClickPoint.X) <= 2 && Math.Abs(point.Y - hoverClickPoint.Y) <= 2) return true;
        hoverClickHeld = false;
        return false;
    }
    private async Task CheckPinMagnificationContinuity()
    {
        int running = buttons.Count(b => !b.Entry.IsPinned);
        if (!Settings.CombinePinnedIconsWithRunningApplications || running == 0
            || (running == 1 && Settings.Items.LastOrDefault()?.Kind != ItemKind.Separator)) return;
        var savedItems = Settings.Items.ToArray();
        try
        {
            var candidate = buttons.First(b => !b.Entry.IsPinned);
            ResetMagnification(); PrepareHoverSurface();
            int index = Array.FindIndex(hoverSlots, s => s.Element == candidate.Button);
            double pointer = DockSpacing.Starts(hoverSlots.Select(s => s.Element is Button ? ButtonSize : SeparatorWidth).ToArray(), hoverSlots.Select(s => s.Element is Button).ToArray(), DisplaySpacing)[index] + ButtonSize / 2d;
            hoverTestInput = () => (pointer, true); hoverEngaged = true; StartHoverRendering();
            await Task.Delay(300); StopHoverRendering(); root.UpdateLayout();
            var savedGrowth = buttons.ToDictionary(b => b.Entry.Item.Id, b => hoverWave!.Growth[Array.FindIndex(hoverSlots, s => s.Element == b.Button)]);
            var savedPositions = buttons.ToDictionary(b => b.Entry.Item.Id, b => b.Icon.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()));
            void CheckPositions(bool includeCandidate)
            {
                root.UpdateLayout();
                foreach (var b in buttons)
                    if ((includeCandidate || b.Entry.Item.Id != candidate.Entry.Item.Id) && savedPositions.TryGetValue(b.Entry.Item.Id, out var before))
                    {
                        var after = b.Icon.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                        if (Math.Abs(before.X - after.X) > 1 || Math.Abs(before.Y - after.Y) > 1) throw new InvalidOperationException($"Pin/unpin shifted {b.Entry.Item.Name}: {before.X},{before.Y} -> {after.X},{after.Y}; growth {savedGrowth[b.Entry.Item.Id]} -> {hoverWave!.Growth[Array.FindIndex(hoverSlots, s => s.Element == b.Button)]}.");
                    }
            }
            double candidateGrowth = savedGrowth[candidate.Entry.Item.Id];
            var originalBounds = resting;
            var pin = candidate.Entry.Item with { Id = Guid.NewGuid().ToString("N"), ClickActions = null };
            Settings.Items.Insert(PinInsertionIndex(), pin); lastSignature = ""; Reconcile(); StopHoverRendering(); await Task.Delay(30);
            if (resting != originalBounds) throw new InvalidOperationException("Pinning an existing running item changed dock width/placement.");
            CheckPositions(false);
            var pinnedButton = buttons.First(b => b.Entry.Item.Id == pin.Id).Button;
            if (Math.Abs(hoverWave!.Growth[Array.FindIndex(hoverSlots, s => s.Element == pinnedButton)] - candidateGrowth) > .001)
                throw new InvalidOperationException("Pinning discarded the running item's magnification.");
            Settings.Items.Remove(pin); lastSignature = ""; Reconcile(); StopHoverRendering(); await Task.Delay(30);
            if (resting != originalBounds) throw new InvalidOperationException("Unpinning an existing running item changed dock width/placement.");
            CheckPositions(true);
            foreach (var b in buttons)
                if (savedGrowth.TryGetValue(b.Entry.Item.Id, out double growth) && Math.Abs(hoverWave!.Growth[Array.FindIndex(hoverSlots, s => s.Element == b.Button)] - growth) > .001)
                    throw new InvalidOperationException("Pin/unpin cycle changed unchanged items' magnification.");
        }
        finally
        {
            hoverTestInput = null; ResetMagnification(); Settings.Items.Clear(); Settings.Items.AddRange(savedItems); lastSignature = ""; Reconcile();
        }
    }
    private void UpdateMagnification(double _)
    {
        if (!StartupComplete) return;
        if (rowAnimating && rowSpanTransition) return;
        if (rowAnimating) FinishRowTransition();
        if (dockOverflow || dragging || hidden || popupCount > 0 || !Motion || !DisplayMagnificationEnabled || Settings.HoverMagnificationPercent == 0) return;
        if (hoverTestInput == null)
        {
            Native.GetCursorPos(out var cursor);
            // Native clip updates can deliver routed pointer events after the
            // cursor has already left. Do not restart the lens from those.
            if (!HoverContains(cursor)) return;
        }
        hoverEngaged = true;
        if (hoverWave == null) PrepareHoverSurface();
        StartHoverRendering();
    }
    private void PrepareHoverSurface()
    {
        if (hoverWave != null) return;
        // Capture resting placement once. Translation uses separate carriers,
        // so artwork scaling never scales row displacement or indicator size.
        hoverLayoutExpanded = true;
        hoverClip = null;
        root.UpdateLayout();
        var children = strip.Children.OfType<FrameworkElement>().ToArray();
        var starts = DockSpacing.Starts(children.Select(e => e is Button ? ButtonSize : SeparatorWidth).ToArray(), children.Select(e => e is Button).ToArray(), DisplaySpacing);
        double rowStart = Vertical ? MarginDip(Settings.DockMarginTop) : MarginDip(Settings.DockMarginLeft);
        double rowEnd = Vertical ? MarginDip(Settings.DockMarginBottom) : MarginDip(Settings.DockMarginRight);
        double rowContent = DockSpacing.Length(children.Select(e => e is Button ? ButtonSize : SeparatorWidth).ToArray(), children.Select(e => e is Button).ToArray(), DisplaySpacing);
        double rowExtent = (Vertical ? resting.Height : resting.Width) / scale;
        double rowOffset = PositionAlignment switch
        {
            DockAlignment.Start => rowStart,
            DockAlignment.End => rowExtent - rowEnd - rowContent,
            _ => rowStart + Math.Max(0, (rowExtent - rowStart - rowEnd - rowContent) / 2)
        };
        hoverRowOffset = rowOffset;
        double rowX = (resting.Left - hostBounds.Left) / scale + (Vertical ? MarginDip(Settings.DockMarginLeft) : rowOffset);
        double rowY = (resting.Top - hostBounds.Top) / scale + (Vertical ? rowOffset : MarginDip(Settings.DockMarginTop));
        hoverSlots = children.Select((element, index) =>
        {
            var shift = new TranslateTransform(); element.RenderTransform = shift;
            // Scroll alignment/offset commits can lag a content rebuild by a
            // frame. Anchor the live buttons to the intended resting row now;
            // the correction disappears when WinUI catches up.
            var originBefore = element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            double intendedX = Vertical && element is Button ? (resting.Left - hostBounds.Left) / scale
                : rowX + (Vertical ? (ButtonSize - element.ActualWidth) / 2 : starts[index]);
            double intendedY = !Vertical && element is Button ? (resting.Top - hostBounds.Top) / scale
                : rowY + (Vertical ? starts[index] : (ButtonSize - element.ActualHeight) / 2);
            // Keep WinUI's settled pixel rounding. A one-pixel correction here
            // disappears at reset, producing a visible hop after hover-out.
            double correctionX = DockGeometry.Pixels(intendedX, scale) / scale - originBefore.X;
            double correctionY = DockGeometry.Pixels(intendedY, scale) / scale - originBefore.Y;
            shift.X = Math.Abs(correctionX * scale) <= 1.01 ? 0 : correctionX;
            shift.Y = Math.Abs(correctionY * scale) <= 1.01 ? 0 : correctionY;
            var icon = element is Button button ? buttons.First(b => b.Button == button).Icon : null;
            var visual = icon == null ? null : ElementCompositionPreview.GetElementVisual(icon);
            float iconSide = (float)DisplayIconSize;
            if (visual != null)
                visual.CenterPoint = PositionEdge switch { DockEdge.Top => new(iconSide / 2f, 0, 0), DockEdge.Left => new(0, iconSide / 2f, 0), DockEdge.Right => new(iconSide, iconSide / 2f, 0), _ => new(iconSide / 2f, iconSide, 0) };
            var iconOrigin = new Windows.Foundation.Point();
            if (icon?.Parent is FrameworkElement layoutParent)
            {
                // Composition can still present a scale from the previous
                // frame. Use settled layout coordinates, then preserve them
                // without a second rounding pass in the detached carrier.
                var origin = layoutParent.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                iconOrigin = new(origin.X + icon.ActualOffset.X, origin.Y + icon.ActualOffset.Y);
            }
            var slot = new HoverSlot(element, shift, icon, visual, iconOrigin) {
                LayoutShiftX = shift.X, LayoutShiftY = shift.Y,
                OriginalIconOffset = icon == null ? default : new(icon.ActualOffset.X, icon.ActualOffset.Y),
                ElementOriginX = originBefore.X + shift.X, ElementOriginY = originBefore.Y + shift.Y
            };
            if (icon?.Parent is Grid parent)
            {
                slot.OriginalParent = parent; slot.OriginalIndex = parent.Children.IndexOf(icon); slot.OriginalMargin = icon.Margin;
                parent.Children.Remove(icon);
                icon.Margin = new Thickness(0); icon.RenderTransform = null;
                // Translation belongs to the carrier; scaling belongs to its
                // child. Mixing both transforms on the artwork can scale its
                // displacement too, pulling end icons into the panel border.
                slot.ArtworkCarrier = new Grid { Width = DisplayIconSize, Height = DisplayIconSize, RenderTransform = slot.ArtworkShift, UseLayoutRounding = false };
                slot.ArtworkCarrier.Children.Add(icon); hoverArtwork.Children.Add(slot.ArtworkCarrier);
                Canvas.SetLeft(slot.ArtworkCarrier, slot.IconOrigin.X); Canvas.SetTop(slot.ArtworkCarrier, slot.IconOrigin.Y);
            }
            if (element is Button indicatorButton && (downloadIndicators.TryGetValue(indicatorButton, out var indicator) || runningIndicators.TryGetValue(indicatorButton, out indicator)))
            {
                slot.Indicator = indicator;
                slot.IndicatorOrigin = indicator.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                slot.IndicatorWidth = indicator.Width; slot.IndicatorHeight = indicator.Height;
                if (indicator.Parent is Grid indicatorParent)
                {
                    slot.IndicatorParent = indicatorParent;
                    slot.IndicatorIndex = indicatorParent.Children.IndexOf(indicator);
                    if (indicatorParent == slot.OriginalParent && slot.IndicatorIndex >= slot.OriginalIndex) slot.IndicatorIndex++;
                    slot.IndicatorMargin = indicator.Margin; slot.IndicatorHorizontal = indicator.HorizontalAlignment; slot.IndicatorVertical = indicator.VerticalAlignment;
                    indicatorParent.Children.Remove(indicator);
                    indicator.Margin = new Thickness(0); indicator.HorizontalAlignment = HorizontalAlignment.Left; indicator.VerticalAlignment = VerticalAlignment.Top;
                    slot.IndicatorCarrier = new Grid { Width = indicator.Width, Height = indicator.Height, RenderTransform = slot.IndicatorShift };
                    slot.IndicatorCarrier.Children.Add(indicator); hoverArtwork.Children.Add(slot.IndicatorCarrier);
                    Canvas.SetLeft(slot.IndicatorCarrier, slot.IndicatorOrigin.X); Canvas.SetTop(slot.IndicatorCarrier, slot.IndicatorOrigin.Y);
                }
            }
            if (element is Button groupButton && groupIndicators.TryGetValue(groupButton, out var badge) && badge.Parent is Grid badgeParent)
            {
                slot.GroupIndicator = badge; slot.GroupParent = badgeParent; slot.GroupMargin = badge.Margin;
                var badgeOrigin = badge.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                var itemOrigin = badgeParent.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                slot.GroupOrigin = badgeOrigin;
                slot.GroupTextOrigin = ((TextBlock)badge.Child).TransformToVisual(badge).TransformPoint(new Windows.Foundation.Point());
                double inset = Math.Max(0, -DisplaySpacing) / 2d;
                double insetX = Vertical ? 0 : inset, insetY = Vertical ? inset : 0;
                slot.GroupInsets = new Thickness(slot.IconOrigin.X - itemOrigin.X - insetX, slot.IconOrigin.Y - itemOrigin.Y - insetY,
                    itemOrigin.X + badgeParent.ActualWidth - insetX - slot.IconOrigin.X - DisplayIconSize,
                    itemOrigin.Y + badgeParent.ActualHeight - insetY - slot.IconOrigin.Y - DisplayIconSize);
                badgeParent.Children.Remove(badge); badge.Margin = new Thickness(0);
                slot.GroupCarrier = new Grid { Width = badge.Width, Height = badge.Height, RenderTransform = slot.GroupShift };
                slot.GroupCarrier.Children.Add(badge); hoverArtwork.Children.Add(slot.GroupCarrier);
                Canvas.SetLeft(slot.GroupCarrier, badgeOrigin.X); Canvas.SetTop(slot.GroupCarrier, badgeOrigin.Y);
            }
            return slot;
        }).ToArray();
        // Badges occupy the top layer even where neighboring artwork overlaps.
        foreach (var slot in hoverSlots.Where(s => s.GroupIndicator != null))
        {
            hoverArtwork.Children.Remove(slot.GroupCarrier!); hoverArtwork.Children.Add(slot.GroupCarrier!);
        }
        root.UpdateLayout();
        for (int index = 0; index < hoverSlots.Length; index++)
        {
            var slot = hoverSlots[index];
            var actual = slot.Element.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            if (slot.Element is Border)
            {
                // Separators keep their XAML layout host. Anchor both axes to
                // its settled position, rather than retaining the ideal row
                // correction that is removed on magnification reset.
                slot = slot with { ElementOriginX = actual.X - slot.Shift.X, ElementOriginY = actual.Y - slot.Shift.Y };
                slot.LayoutShiftX = slot.LayoutShiftY = 0;
                slot.Shift.X = slot.Shift.Y = 0;
                hoverSlots[index] = slot;
                continue;
            }
            slot.LayoutShiftX += slot.ElementOriginX - actual.X;
            slot.LayoutShiftY += slot.ElementOriginY - actual.Y;
            slot.Shift.X = slot.LayoutShiftX; slot.Shift.Y = slot.LayoutShiftY;
            if (slot.OriginalParent != null && slot.ArtworkCarrier != null)
            {
                // Detaching the artwork triggers one final layout pass. Keep
                // both baselines at the parent's settled, unshifted
                // position, which is where reset will put the live icon back.
                // The lens adds its along-axis displacement during painting.
                var parent = slot.OriginalParent.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                double dx = parent.X + slot.OriginalIconOffset.X - slot.LayoutShiftX - slot.IconOrigin.X;
                double dy = parent.Y + slot.OriginalIconOffset.Y - slot.LayoutShiftY - slot.IconOrigin.Y;
                slot = slot with { IconOrigin = new(slot.IconOrigin.X + dx, slot.IconOrigin.Y + dy) };
                hoverSlots[index] = slot;
                Canvas.SetLeft(slot.ArtworkCarrier, slot.IconOrigin.X); Canvas.SetTop(slot.ArtworkCarrier, slot.IconOrigin.Y);
                slot.IndicatorOrigin = new(slot.IndicatorOrigin.X + dx, slot.IndicatorOrigin.Y + dy);
                slot.GroupOrigin = new(slot.GroupOrigin.X + dx, slot.GroupOrigin.Y + dy);
                if (slot.IndicatorCarrier != null)
                {
                    Canvas.SetLeft(slot.IndicatorCarrier, slot.IndicatorOrigin.X); Canvas.SetTop(slot.IndicatorCarrier, slot.IndicatorOrigin.Y);
                }
                if (slot.GroupCarrier != null)
                {
                    Canvas.SetLeft(slot.GroupCarrier, slot.GroupOrigin.X); Canvas.SetTop(slot.GroupCarrier, slot.GroupOrigin.Y);
                }
            }
        }
        hoverWave = new MagnificationWave(children.Select(e => e is Button ? ButtonSize : SeparatorWidth).ToArray(),
            children.Select(e => e is Button).ToArray(), DisplaySpacing);
        hoverAvailableExpansion = Math.Max(0, (Vertical ? hostBounds.Height : hostBounds.Width) / scale - rowContent - rowStart - rowEnd);
        hoverLayoutStamp = CaptureHoverLayout();
    }
    private void HoverRendering(object? sender, object args)
    {
        if (reconciling || positioning || hoverRebuilding || hoverGeometryRepairing || hoverPainting) return;
        long started = Stopwatch.GetTimestamp();
        double dt = Stopwatch.GetElapsedTime(hoverFrameStamp, started).TotalMilliseconds;
        hoverFrameStamp = started;
        if (hoverMenuHeld) { StopHoverRendering(); return; }
        if (closing || hidden || dragging || visibilitySliding || popupCount > 0 || !Motion) { ResetMagnification(); return; }
        if (hoverWave == null) { StopHoverRendering(); return; }
        if (hoverClickHeld)
        {
            // Launch feedback may continue without advancing the hover lens.
            PaintHoverFrame();
            if (!HasIconBounces) StopHoverRendering();
            return;
        }
        RepairHoverGeometry();
        if (hoverWave == null) return;
        Native.GetCursorPos(out var cursor);
        double pointer;
        if (hoverTestInput != null)
        {
            (pointer, hoverEngaged) = hoverTestInput();
        }
        else
        {
            // Derive both entry and exit from the same screen coordinates.
            // Re-entry during contraction must reverse the existing animation
            // even when WinUI misses PointerMoved at the changing native edge.
            hoverEngaged = HoverContains(cursor);
            pointer = (Vertical ? cursor.Y - resting.Top : cursor.X - resting.Left) / scale
                - hoverRowOffset;
        }
        if (hoverEngaged)
            hoverWave.Target(pointer, ButtonSize + DisplaySpacing, HoverOverflow, Settings.MagnificationSpan, HoverAnchor, hoverAvailableExpansion, DisplayIconSize < Settings.IconSize && !laidOutSpanning);
        else hoverWave.ClearTarget();
        bool moving = hoverWave.Advance(dt, Settings.MagnificationMs);
        hoverValidationTicks = 0;
        PaintHoverFrame();
        hoverFrames++;
        if (hoverTestInput != null)
        {
            hoverFrameIntervals.Add(dt);
            // Exclude test-only tree traversal from production frame work;
            // full frame intervals still include every assertion's overhead.
            hoverFrameCosts.Add(Math.Max(0, Stopwatch.GetElapsedTime(started).TotalMilliseconds - hoverValidationTicks * 1000.0 / Stopwatch.Frequency));
        }
        if (!hoverEngaged && !moving && !HasIconBounces) ResetMagnification();
        else if (!moving && hoverTestInput == null && !HasIconBounces) StopHoverRendering();
    }
    private PixelRect HoverInteractionBounds
    {
        get
        {
            if (!HasIconBounces || hoverWave == null) return visibleBounds;
            var panel = ExpandedMaterialPanel(Math.Min(hoverWave.TotalGrowth, hoverAvailableExpansion));
            int overflow = DockGeometry.Pixels(hoverWave.Growth.DefaultIfEmpty().Max(), scale);
            return PositionEdge switch
            {
                DockEdge.Top => new(panel.Left, panel.Top, panel.Right, panel.Bottom + overflow),
                DockEdge.Left => new(panel.Left, panel.Top, panel.Right + overflow, panel.Bottom),
                DockEdge.Right => new(panel.Left - overflow, panel.Top, panel.Right, panel.Bottom),
                _ => new(panel.Left, panel.Top - overflow, panel.Right, panel.Bottom)
            };
        }
    }
    private bool BouncingArtworkContains(Native.POINT cursor)
    {
        if (!HasIconBounces || hoverWave == null) return false;
        var point = new Windows.Foundation.Point((cursor.X - hostBounds.Left) / scale, (cursor.Y - hostBounds.Top) / scale);
        return hoverSlots.Any(slot => LaunchOffset(slot) > 0 && slot.HitBounds.Contains(point));
    }
    private bool HoverContains(Native.POINT cursor) => HoverInteractionBounds.Contains(cursor.X, cursor.Y)
        || resting.Contains(cursor.X, cursor.Y) || HoverGapContains(cursor);
    private bool HoverGapContains(Native.POINT cursor) => Settings.ActivateThroughScreenEdgeGap && Settings.FloatingGap > 0 && (PositionEdge switch
    {
        DockEdge.Top => cursor.Y >= visibleBounds.Top - Settings.FloatingGap && cursor.Y < visibleBounds.Top && cursor.X >= visibleBounds.Left && cursor.X < visibleBounds.Right,
        DockEdge.Left => cursor.X >= visibleBounds.Left - Settings.FloatingGap && cursor.X < visibleBounds.Left && cursor.Y >= visibleBounds.Top && cursor.Y < visibleBounds.Bottom,
        DockEdge.Right => cursor.X >= visibleBounds.Right && cursor.X < visibleBounds.Right + Settings.FloatingGap && cursor.Y >= visibleBounds.Top && cursor.Y < visibleBounds.Bottom,
        _ => cursor.Y >= visibleBounds.Bottom && cursor.Y < visibleBounds.Bottom + Settings.FloatingGap && cursor.X >= visibleBounds.Left && cursor.X < visibleBounds.Right
    });
    private void PaintHoverFrame()
    {
        if (hoverWave == null || positioning || hoverPainting) return;
        if (!hoverGeometryRepairing && RepairHoverGeometry()) return;
        hoverPainting = true;
        try
        {
            hoverWave.ConstrainGrowth(hoverAvailableExpansion, HoverOverflow);
            double expansion = Math.Min(hoverWave.TotalGrowth, hoverAvailableExpansion);
            var previousBounds = visibleBounds;
            int before = laidOutSpanning ? 0 : DockGeometry.Pixels(expansion * HoverAnchor, scale), after = laidOutSpanning ? 0 : DockGeometry.Pixels(expansion * (1 - HoverAnchor), scale);
            int overflow = DockGeometry.Pixels(hoverWave.Growth.DefaultIfEmpty().Max() + (HasIconBounces ? BounceEnvelopeHeight : 0), scale);
            var panel = ExpandedMaterialPanel(expansion);
            visibleBounds = PositionEdge switch
            {
                DockEdge.Top => new(panel.Left, panel.Top, panel.Right, panel.Bottom + overflow),
                DockEdge.Left => new(panel.Left, panel.Top, panel.Right + overflow, panel.Bottom),
                DockEdge.Right => new(panel.Left - overflow, panel.Top, panel.Right, panel.Bottom),
                _ => new(panel.Left, panel.Top - overflow, panel.Right, panel.Bottom)
            };
            // Expand the native clip and lay out the panel before submitting
            // new artwork transforms. During contraction the compositor may
            // still display the previous frame, which must remain unclipped.
            SetHoverClip(previousBounds);
            PaintMaterialSurface(expansion);
            double growthBefore = 0;
            for (int i = 0; i < hoverSlots.Length; i++)
            {
                var slot = hoverSlots[i];
                double offset = growthBefore + hoverWave.Growth[i] / 2 - HoverAnchor * expansion;
                growthBefore += hoverWave.Growth[i];
                slot.Shift.X = slot.LayoutShiftX + (Vertical ? 0 : offset);
                slot.Shift.Y = slot.LayoutShiftY + (Vertical ? offset : 0);
                if (slot.Element is Border separator)
                    SnapSeparator(separator, slot.ElementOriginX + (Vertical ? 0 : offset), slot.ElementOriginY + (Vertical ? offset : 0));
                if (slot.Visual != null)
                {
                    double bounce = LaunchOffset(slot);
                    double bounceX = Vertical ? (PositionEdge == DockEdge.Left ? bounce : -bounce) : 0;
                    double bounceY = Vertical ? 0 : PositionEdge == DockEdge.Top ? bounce : -bounce;
                    slot.ArtworkShift.X = (Vertical ? 0 : offset) + bounceX;
                    slot.ArtworkShift.Y = (Vertical ? offset : 0) + bounceY;
                    float factor = (float)(1 + hoverWave.Growth[i] / DisplayIconSize);
                    slot.Visual.Scale = new(factor, factor, 1);
                    var pivot = slot.Visual.CenterPoint;
                    slot.HitBounds = new(slot.IconOrigin.X + (Vertical ? 0 : offset) + bounceX - (factor - 1) * pivot.X,
                        slot.IconOrigin.Y + (Vertical ? offset : 0) + bounceY - (factor - 1) * pivot.Y,
                        DisplayIconSize * factor, DisplayIconSize * factor);
                    if (slot.GroupIndicator is { } badge)
                    {
                        bool right = Settings.GroupIndicatorCorner is GroupIndicatorCorner.TopRight or GroupIndicatorCorner.BottomRight;
                        bool bottom = Settings.GroupIndicatorCorner is GroupIndicatorCorner.BottomLeft or GroupIndicatorCorner.BottomRight;
                        // Preserve the resting highlight-to-icon padding. Move the
                        // whole badge with one transform so text and fill never
                        // undergo separate layout rounding during the animation.
                        slot.GroupShift.X = (right ? slot.HitBounds.Right + slot.GroupInsets.Right - badge.Width : slot.HitBounds.Left - slot.GroupInsets.Left) - slot.GroupOrigin.X;
                        double topInset = slot.GroupInsets.Top * (1 - .4 * Math.Clamp(hoverWave.Growth[i] / Math.Max(1, HoverOverflow), 0, 1));
                        slot.GroupShift.Y = (bottom ? slot.HitBounds.Bottom + slot.GroupInsets.Bottom - badge.Height : slot.HitBounds.Top - topInset) - slot.GroupOrigin.Y;
                        if (hoverTestInput != null)
                        {
                            long validationStarted = Stopwatch.GetTimestamp();
                            var badgePoint = badge.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                            var textPoint = ((TextBlock)badge.Child).TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                            hoverGroupTextDrift = Math.Max(hoverGroupTextDrift, Math.Max(Math.Abs(textPoint.X - badgePoint.X - slot.GroupTextOrigin.X),
                                Math.Abs(textPoint.Y - badgePoint.Y - slot.GroupTextOrigin.Y)));
                            hoverValidationTicks += Stopwatch.GetTimestamp() - validationStarted;
                        }
                    }
                    bool downloadedIndicator = slot.Element is Button downloadButton && downloadIndicators.ContainsKey(downloadButton);
                    if (downloadedIndicator && slot.Indicator != null) slot.Indicator.Width = Math.Max(16, (DisplayIconSize + hoverWave.Growth[i]) * .6);
                    double indicatorX = Vertical && (downloadedIndicator || Settings.ForceIndicatorsToItemBase) ? (PositionEdge == DockEdge.Left ? 1 : -1) * hoverWave.Growth[i] / 2 : 0;
                    double indicatorY = downloadedIndicator || Settings.ForceIndicatorsToItemBase ? (Vertical ? hoverWave.Growth[i] / 2 : PositionEdge == DockEdge.Top ? hoverWave.Growth[i] : 0) : 0;
                    if (slot.IndicatorCarrier != null && slot.Indicator != null)
                    {
                        slot.IndicatorShift.X = (Vertical ? 0 : offset) + indicatorX;
                        slot.IndicatorShift.Y = (Vertical ? offset : 0) + indicatorY;
                        SyncHoverIndicator(slot);
                    }
                    if (hoverTestInput != null && (slot.ArtworkCarrier?.Parent != hoverArtwork || slot.HitBounds.Left < -.5 || slot.HitBounds.Top < -.5
                        || slot.HitBounds.Right > hostBounds.Width / scale + .5 || slot.HitBounds.Bottom > hostBounds.Height / scale + .5)) hoverArtworkClips++;
                    if (hoverTestInput != null && slot.Indicator != null)
                    {
                        long validationStarted = Stopwatch.GetTimestamp();
                        var point = slot.Indicator.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                        hoverIndicatorDrift = Math.Max(hoverIndicatorDrift, Math.Max(Math.Abs(point.X - slot.IndicatorOrigin.X - (slot.IndicatorWidth - slot.Indicator.Width) / 2 - (Vertical ? 0 : offset) - indicatorX),
                            Math.Abs(point.Y - slot.IndicatorOrigin.Y - (slot.IndicatorHeight - slot.Indicator.Height) / 2 - (Vertical ? offset : 0) - indicatorY)));
                        if (slot.IndicatorCarrier?.Width != slot.Indicator.Width || slot.IndicatorCarrier?.Height != slot.Indicator.Height) hoverIndicatorDrift = double.PositiveInfinity;
                        hoverValidationTicks += Stopwatch.GetTimestamp() - validationStarted;
                    }
                }
            }
            double baseLength = (Vertical ? resting.Height : resting.Width) / scale;
            if (Vertical) hoverPanelScale.ScaleY = laidOutSpanning ? 1 : 1 + expansion / Math.Max(1, baseLength);
            else hoverPanelScale.ScaleX = laidOutSpanning ? 1 : 1 + expansion / Math.Max(1, baseLength);
            if (hoverTestInput != null && hoverSlots.Length > 0)
            {
                long validationStarted = Stopwatch.GetTimestamp();
                var first = hoverSlots.FirstOrDefault(s => s.Icon != null);
                var last = hoverSlots.LastOrDefault(s => s.Icon != null);
                if (first != null && last != null)
                {
                    double panelStart = (Vertical ? resting.Top - hostBounds.Top : resting.Left - hostBounds.Left) / scale;
                    double panelEnd = (Vertical ? resting.Bottom - hostBounds.Top : resting.Right - hostBounds.Left) / scale;
                    double firstGap = (Vertical ? first.HitBounds.Top : first.HitBounds.Left) - panelStart + before / scale;
                    double lastGap = panelEnd + after / scale - (Vertical ? last.HitBounds.Bottom : last.HitBounds.Right);
                    double firstRestGap = Along(first.IconOrigin) - panelStart;
                    double lastRestGap = panelEnd - Along(last.IconOrigin) - DisplayIconSize;
                    hoverEndGapDrift = Math.Max(hoverEndGapDrift, Math.Max(Math.Abs(firstGap - firstRestGap), Math.Abs(lastGap - lastRestGap)));
                }
                hoverValidationTicks += Stopwatch.GetTimestamp() - validationStarted;
            }
            if (hoverTestInput != null)
            {
                long checkStarted = Stopwatch.GetTimestamp();
                foreach (var slot in hoverSlots.Where(slot => slot.Icon != null))
                    if (!FitsNativeHoverClip(slot)) { hoverNativeClipViolations++; firstHoverClipViolation ??= $"{slot.HitBounds}, clip {hoverClip}, edge {PositionEdge}, align {PositionAlignment}, icon {DisplayIconSize}/{slot.Icon!.Width}, count {hoverSlots.Length}, growth {hoverWave.TotalGrowth}/{hoverAvailableExpansion}, host {hostBounds}, rest {resting}"; }
                hoverValidationTicks += Stopwatch.GetTimestamp() - checkStarted;
            }
        }
        finally { hoverPainting = false; QueueHoverGeometryCheck(); FlushDeferredReposition(); }
    }
    private void SetHoverClip(PixelRect? previousBounds = null)
    {
        if (rowStaging) return;
        var bounds = previousBounds is { } previous ? Union(previous, visibleBounds) : visibleBounds;
        var clip = new PixelRect(Math.Max(0, bounds.Left - hostBounds.Left), Math.Max(0, bounds.Top - hostBounds.Top),
            Math.Min(hostBounds.Width, bounds.Right - hostBounds.Left), Math.Min(hostBounds.Height, bounds.Bottom - hostBounds.Top));
        if (hidden && !visibilitySliding)
        {
            var trigger = DockGeometry.Hidden(resting, PositionEdge, Settings.FloatingGap);
            clip = Settings.KeepHiddenEdgeTrigger ? new(trigger.Left - hostBounds.Left, trigger.Top - hostBounds.Top, trigger.Right - hostBounds.Left, trigger.Bottom - hostBounds.Top) : new(0, 0, 0, 0);
        }
        if (hoverClip != clip) { Native.SetDockRegion(Handle, clip); hoverClip = clip; }
    }
    private void ResetMagnification(bool immediate = true)
    {
        if (!immediate && hoverMenuHeld) return;
        hoverClickHeld = false;
        hoverEngaged = false;
        if (!immediate && Motion && hoverWave != null) { StartHoverRendering(); return; }
        StopHoverRendering();
        if (!hoverRebuilding) launchBounces.Clear();
        foreach (var slot in hoverSlots)
        {
            slot.Shift.X = slot.Shift.Y = 0;
            if (slot.Visual != null) slot.Visual.Scale = new(1, 1, 1);
            if (slot.Icon != null && slot.OriginalParent != null)
            {
                if (slot.ArtworkCarrier != null) { slot.ArtworkCarrier.Children.Remove(slot.Icon); hoverArtwork.Children.Remove(slot.ArtworkCarrier); }
                slot.Icon.RenderTransform = null; slot.Icon.Margin = slot.OriginalMargin;
                slot.OriginalParent.Children.Insert(Math.Min(slot.OriginalIndex, slot.OriginalParent.Children.Count), slot.Icon);
            }
            if (slot.Indicator != null && slot.IndicatorCarrier != null && slot.IndicatorParent != null)
            {
                slot.IndicatorCarrier.Children.Remove(slot.Indicator); hoverArtwork.Children.Remove(slot.IndicatorCarrier);
                if (slot.Element is Button downloadButton && downloadIndicators.ContainsKey(downloadButton)) slot.Indicator.Width = Math.Max(16, DisplayIconSize * .6);
                slot.Indicator.Margin = slot.IndicatorMargin; slot.Indicator.HorizontalAlignment = slot.IndicatorHorizontal; slot.Indicator.VerticalAlignment = slot.IndicatorVertical;
                slot.IndicatorParent.Children.Insert(Math.Min(slot.IndicatorIndex, slot.IndicatorParent.Children.Count), slot.Indicator);
            }
            if (slot.GroupIndicator is { } badge && slot.GroupParent != null)
            {
                slot.GroupCarrier!.Children.Remove(badge); hoverArtwork.Children.Remove(slot.GroupCarrier); badge.Margin = slot.GroupMargin;
                slot.GroupParent.Children.Add(badge);
            }
        }
        hoverSlots = []; hoverWave = null; hoverLayoutStamp = null;
        SnapRestingSeparators();
        hoverClip = null;
        hoverPanelScale.ScaleX = hoverPanelScale.ScaleY = 1;
        if (hoverLayoutExpanded)
        {
            hoverLayoutExpanded = false;
            if (!hoverRebuilding) { visibleBounds = resting; SetHoverClip(); PaintMaterialSurface(0); }
        }
    }

    private void EnlargedIconPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (hoverWave == null || dragging || popupCount > 0) return;
        // Normal button clicks keep their existing capture/drag/click behavior.
        // This only covers artwork enlarged outside its original button slot.
        for (var element = e.OriginalSource as DependencyObject; element != null; element = VisualTreeHelper.GetParent(element))
            if (element is Button) return;
        var point = e.GetCurrentPoint(root);
        var slot = hoverSlots.FirstOrDefault(s => s.Icon != null && s.HitBounds.Contains(point.Position));
        if (slot?.Element is not Button button) return;
        var item = buttons.First(b => b.Button == button);
        var properties = point.Properties;
        DockMouseButton? mouse = properties.IsLeftButtonPressed ? DockMouseButton.Left : properties.IsRightButtonPressed ? DockMouseButton.Right : properties.IsMiddleButtonPressed ? DockMouseButton.Middle : null;
        if (mouse == null) return;
        e.Handled = true; CaptureClickForeground();
        InvokeClick(item.Entry, mouse.Value, item.Button, item.Icon);
    }

    private async Task CheckMagnification()
    {
        if (hoverTestInput != null) return;
        var originalEdge = PositionEdge;
        int originalPercent = Settings.HoverMagnificationPercent, originalSpan = Settings.MagnificationSpan;
        var originalMaterial = Settings.Material; int originalBackground = Settings.BackgroundStrength;
        bool originalReduceMotion = Settings.ReduceMotion, originalInspection = inspection, originalRounded = Settings.RoundedCorners;
        var originalGroupCorner = Settings.GroupIndicatorCorner; var originalGroupShape = Settings.GroupIndicatorShape;
        bool originalGroupSquare = Settings.SquareGroupIndicatorsWhenUnrounded;
        var reports = new List<object>();
        Exception? failure = null;
        try
        {
            inspection = true;
            foreach (var scenario in new[] { (DockEdge.Bottom, originalPercent, originalSpan, originalMaterial), (DockEdge.Left, originalPercent, originalSpan, originalMaterial), (DockEdge.Right, originalPercent, originalSpan, originalMaterial), (DockEdge.Bottom, 100, 9, originalMaterial), (DockEdge.Bottom, originalPercent, originalSpan, DockMaterial.None) })
            {
                var (edge, percent, span, material) = scenario;
                Settings.Material = material;
                if (material == DockMaterial.None) Settings.BackgroundStrength = 100;
                Settings.HoverMagnificationPercent = percent; Settings.MagnificationSpan = span;
                PositionEdge = edge; Settings.ReduceMotion = false; ApplySettings(); ShowDock();
                if (!Motion) throw new InvalidOperationException("Windows animations must be enabled for the animated hover test.");
                // Drain shell appbar notifications from changing the tested edge.
                await Task.Delay(200);
                PrepareHoverSurface();
                await Task.Delay(70);
                root.UpdateLayout();
                var materialClient = Native.ClientBounds(materialHandle);
                if (materialClient.Width != materialHostBounds.Width || materialClient.Height != materialHostBounds.Height)
                    throw new InvalidOperationException("Material window native frame leaves uncovered dock pixels.");
                // Auto-fit can shrink the row while the retained canvas keeps
                // its preferred-size hover envelope. Anchor to that actual
                // viewport, including its unused expansion space.
                double expectedCross = PositionEdge switch { DockEdge.Right => root.ActualWidth - ButtonCrossSize, DockEdge.Left => 0, DockEdge.Top => 0, _ => root.ActualHeight - ButtonCrossSize };
                foreach (var item in buttons)
                {
                    var point = item.Button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    double actualCross = Vertical ? point.X : point.Y;
                    if (Math.Abs(actualCross - expectedCross) > 1.5)
                        throw new InvalidOperationException($"Hover envelope alignment mismatch on {edge}: {actualCross}, expected {expectedCross}.");
                }
                var watch = Stopwatch.StartNew();
                double length = hoverWave!.Length;
                double[] min = Enumerable.Repeat(double.PositiveInfinity, hoverWave.Count).ToArray();
                double[] max = Enumerable.Repeat(double.NegativeInfinity, hoverWave.Count).ToArray();
                hoverFrameIntervals.Clear(); hoverFrameCosts.Clear(); hoverUnexpectedMoves.Clear();
                long windowMoves = hoverWindowMoves, materialMoves = materialWindowMoves, frames = hoverFrames;
                hoverArtworkClips = 0;
                hoverIndicatorDrift = hoverEndGapDrift = hoverGroupTextDrift = 0;
                var backdropBefore = System.Text.Json.JsonSerializer.Serialize(materialBackdrop!.Diagnostics);
                hoverTestInput = () =>
                {
                    double seconds = watch.Elapsed.TotalSeconds;
                    if (seconds > 1 && seconds < 1.5)
                        for (int i = 0; i < hoverWave!.Count; i++) { min[i] = Math.Min(min[i], hoverWave.Growth[i]); max[i] = Math.Max(max[i], hoverWave.Growth[i]); }
                    // Dwell, slow sweep, fast reversal, and leaving the row.
                    double pointer = seconds < 1.5 ? length / 2 : seconds < 3 ? (seconds - 1.5) / 1.5 * length
                        : seconds < 4 ? (1 + Math.Sin((seconds - 3) * Math.PI * 6)) * length / 2 : length / 2;
                    return (pointer, seconds < 4.1);
                };
                hoverEngaged = true; StartHoverRendering();
                await Task.Delay(4100);
                // Measure the active sweep before the normal leave path retires its surface.
                var intervals = hoverFrameIntervals.Skip(5).Order().ToArray();
                var costs = hoverFrameCosts.Skip(5).Order().ToArray();
                double Percentile(double[] values, double q) => values.Length == 0 ? double.PositiveInfinity : values[(int)Math.Min(values.Length - 1, Math.Floor(values.Length * q))];
                double drift = min.Zip(max, (low, high) => double.IsFinite(low) && double.IsFinite(high) ? high - low : double.PositiveInfinity).Max();
                double median = Percentile(intervals, .5);
                long movesDuringSweep = hoverWindowMoves - windowMoves;
                long materialMovesDuringSweep = materialWindowMoves - materialMoves;
                var moveSources = hoverUnexpectedMoves.ToArray();
                await Task.Delay(350);
                bool exitSettled = hoverWave == null && !hoverLayoutExpanded && !hoverRendering;
                bool materialUnchanged = backdropBefore == System.Text.Json.JsonSerializer.Serialize(materialBackdrop!.Diagnostics);
                var report = new {
                    Edge = edge.ToString(), Percent = percent, Span = span, Material = material.ToString(), Frames = hoverFrames - frames,
                    MedianIntervalMs = median, P95IntervalMs = Percentile(intervals, .95), P99IntervalMs = Percentile(intervals, .99),
                    P99WorkMs = Percentile(costs, .99), MaxWorkMs = costs.DefaultIfEmpty(double.PositiveInfinity).Max(),
                    FrameWorkBudgetMs = Math.Min(4, median * .6),
                    LongFrames = intervals.Count(ms => ms > median * 1.9), MeasuredFrames = intervals.Length,
                    StationaryDriftDip = drift, NativeWindowMovesDuringSweep = movesDuringSweep, ExitSettled = exitSettled,
                    MaterialWindowMovesDuringSweep = materialMovesDuringSweep,
                    ArtworkClipViolations = hoverArtworkClips, MaterialUnchanged = materialUnchanged,
                    IndicatorTrackingErrorDip = hoverIndicatorDrift, EndGapErrorDip = hoverEndGapDrift,
                    GroupDigitDriftDip = hoverGroupTextDrift,
                    UnexpectedMoveSources = moveSources,
                    Passed = costs.Length > 60 && Percentile(costs, .99) < Math.Min(4, median * .6) && drift < .05 && movesDuringSweep == 0
                        && Percentile(intervals, .95) < median * 1.3 && intervals.Count(ms => ms > median * 1.9) < intervals.Length * .02 && exitSettled
                        && hoverArtworkClips == 0 && materialUnchanged && hoverIndicatorDrift < 1 && hoverEndGapDrift < 1 && hoverGroupTextDrift < .05
                };
                reports.Add(report);
                hoverTestInput = null; ResetMagnification();
            }
            // A stationary hover must survive task-title/launch reconciliation
            // and a real context-menu popup without a collapsed frame.
            hoverTestInput = () => (ButtonSize * 1.5, true);
            UpdateMagnification(0);
            await Task.Delay(500);
            var savedGrowth = buttons.ToDictionary(b => b.Entry.Item.Id, b => hoverWave!.Growth[Array.FindIndex(hoverSlots, s => s.Element == b.Button)]);
            lastSignature = ""; Reconcile();
            foreach (var b in buttons)
                if (savedGrowth.TryGetValue(b.Entry.Item.Id, out double value) && Math.Abs(value - hoverWave!.Growth[Array.FindIndex(hoverSlots, s => s.Element == b.Button)]) > .001)
                    throw new InvalidOperationException("Task reconciliation collapsed the magnified pose.");
            var menu = BackgroundMenu();
            menu.ShowAt(dockBorder);
            await Task.Delay(150);
            var heldGrowth = hoverWave!.Growth.ToArray();
            ResetMagnification(false);
            await Task.Delay(150);
            if (!hoverMenuHeld || !heldGrowth.SequenceEqual(hoverWave!.Growth)) throw new InvalidOperationException("Context menu failed to hold magnification.");
            menu.Hide();
            await Task.Delay(100);
            hoverTestInput = null; ResetMagnification();
            bool rounded = Settings.RoundedCorners;
            foreach (bool enabled in new[] { false, true })
            {
                Settings.RoundedCorners = enabled; ApplySettings();
                if (Native.DwmGetInt(materialHandle, 33, out int preference, 4) != 0 || preference != (enabled ? 2 : 1)) throw new InvalidOperationException("Corner preference was not applied immediately.");
            }
            Settings.RoundedCorners = rounded; ApplySettings();
            hoverTestInput = () => (ButtonSize * 1.5, true);
            Settings.RoundedCorners = true;
            foreach (var corner in Enum.GetValues<GroupIndicatorCorner>())
            foreach (var shape in Enum.GetValues<GroupIndicatorShape>())
            {
                Settings.GroupIndicatorCorner = corner; Settings.GroupIndicatorShape = shape; ApplySettings();
                PrepareHoverSurface();
                hoverWave!.Target(ButtonSize * 1.5, ButtonSize + DisplaySpacing, HoverOverflow, Settings.MagnificationSpan, HoverAnchor, hoverAvailableExpansion);
                hoverWave.Advance(16, 0); PaintHoverFrame(); root.UpdateLayout();
                foreach (var slot in hoverSlots.Where(s => s.GroupIndicator != null))
                {
                    var badge = slot.GroupIndicator!;
                    var point = badge.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    bool right = corner is GroupIndicatorCorner.TopRight or GroupIndicatorCorner.BottomRight;
                    bool bottom = corner is GroupIndicatorCorner.BottomLeft or GroupIndicatorCorner.BottomRight;
                    double expectedX = right ? slot.HitBounds.Right + slot.GroupInsets.Right - badge.Width : slot.HitBounds.Left - slot.GroupInsets.Left;
                    int slotIndex = Array.IndexOf(hoverSlots, slot);
                    double topInset = slot.GroupInsets.Top * (1 - .4 * Math.Clamp(hoverWave.Growth[slotIndex] / Math.Max(1, HoverOverflow), 0, 1));
                    double expectedY = bottom ? slot.HitBounds.Bottom + slot.GroupInsets.Bottom - badge.Height : slot.HitBounds.Top - topInset;
                    double radius = shape switch { GroupIndicatorShape.Circle => badge.Width / 2, GroupIndicatorShape.Squircle => 4, _ => 0 };
                    if (Math.Abs(point.X - expectedX) > 1 || Math.Abs(point.Y - expectedY) > 1 || badge.ActualWidth != badge.ActualHeight
                        || Math.Abs(badge.ActualWidth - badge.Width) > .1 || badge.CornerRadius.TopLeft != radius
                        || hoverArtwork.Children.IndexOf(slot.GroupCarrier!) < hoverSlots.Max(s => s.ArtworkCarrier == null ? -1 : hoverArtwork.Children.IndexOf(s.ArtworkCarrier)))
                        throw new InvalidOperationException($"Group badge failed fixed-size corner/layer check: {corner}, {shape}.");
                }
                ResetMagnification();
            }
            Settings.RoundedCorners = false; Settings.SquareGroupIndicatorsWhenUnrounded = true; ApplySettings();
            if (groupIndicators.Values.Any(b => b.CornerRadius.TopLeft != 0)) throw new InvalidOperationException("Square group badge override was not applied.");
            hoverTestInput = null;
            // Exercise the exact setting callback path that previously crashed.
            for (int i = 0; i < 10; i++) { Settings.ReduceMotion = i % 2 == 0; ApplySettings(); await Task.Delay(30); }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            hoverTestInput = null; ResetMagnification();
            PositionEdge = originalEdge; Settings.ReduceMotion = originalReduceMotion;
            Settings.HoverMagnificationPercent = originalPercent; Settings.MagnificationSpan = originalSpan;
            Settings.Material = originalMaterial; Settings.BackgroundStrength = originalBackground;
            Settings.RoundedCorners = originalRounded;
            Settings.GroupIndicatorCorner = originalGroupCorner; Settings.GroupIndicatorShape = originalGroupShape;
            Settings.SquareGroupIndicatorsWhenUnrounded = originalGroupSquare;
            inspection = originalInspection; ApplySettings();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "magnification-test.json"), System.Text.Json.JsonSerializer.Serialize(
            new { Scenarios = reports, ReduceMotionToggles = failure == null ? 10 : 0, Error = failure?.ToString() }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
