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
    private long hoverGeometryRepairs, hoverNativeClipViolations;
    private string? firstHoverClipViolation;
    private IReadOnlyList<TaskWindow>? stressTasks;
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
        if (hoverTestInput != null) return hoverTestInput().Active;
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
            hoverGeometryRepairs++;
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
    private async Task CheckLayoutStress()
    {
        if (hoverTestInput != null) return;
        var items = Settings.Items.ToList(); var edge = PositionEdge; var alignment = PositionAlignment;
        int spacing = Settings.IconSpacing, percent = Settings.HoverMagnificationPercent, span = Settings.MagnificationSpan, gap = Settings.FloatingGap;
        bool grouping = Settings.GroupWindows, reduce = Settings.ReduceMotion, inspect = inspection;
        Exception? failure = null; int cases = 0, transitions = 0; double maxDrift = 0; long repairStart = hoverGeometryRepairs, clipStart = hoverNativeClipViolations;
        long rowClipStart = rowClipErrors;
        firstHoverClipViolation = null;
        bool originalHitTest = root.IsHitTestVisible;
        bool reserve = Settings.ReserveSpace;
        int realEvents = 0;
        var details = new List<object>();
        double focus = .5;
        try
        {
            root.IsHitTestVisible = false;
            inspection = true; Settings.ReserveSpace = false; Settings.ReduceMotion = false; Settings.HoverMagnificationPercent = 100; Settings.MagnificationSpan = 9;
            var pin = new DockItem { Kind = ItemKind.Application, Name = "Layout stress", Target = Environment.ProcessPath!, AppUserModelId = "layout.pin" };
            Settings.Items = [pin with { Id = "stress-pin-a" }, new() { Id = "stress-separator", Kind = ItemKind.Separator }, pin with { Id = "stress-pin-b" }];
            hoverTestInput = () => ((hoverWave?.Length ?? 1) * focus, true);
            foreach (var testEdge in Enum.GetValues<DockEdge>())
            foreach (var testAlignment in Enum.GetValues<DockAlignment>())
            foreach (int testSpacing in new[] { -16, 0, 8 })
            foreach (bool group in new[] { false, true })
            {
                ResetMagnification(); PositionEdge = testEdge; PositionAlignment = testAlignment; Settings.IconSpacing = testSpacing; Settings.GroupWindows = group;
                foreach (int applications in new[] { 4, 35, 160, 35, 4 })
                {
                    focus = cases % 3 == 0 ? .01 : cases % 3 == 1 ? .99 : .5;
                    int count = applications * (group ? 3 : 1);
                    stressTasks = Enumerable.Range(0, count).Select(i => new TaskWindow((nint)(0x7fff0000 + i * 4), "Layout task " + i,
                        Environment.ProcessPath!, "layout.app." + (group ? i / 3 : i), new(100, 100, 500, 400), false, false)).ToArray();
                    Settings.FloatingGap = cases % 2 == 0 ? 0 : 8;
                    lastSignature = ""; Reposition(true); Reconcile();
                    if (CanResumeHoverHere()) UpdateMagnification(0);
                    await Task.Delay(40);
                    maxDrift = Math.Max(maxDrift, ValidateHoverGeometry());
                    if (dockOverflow)
                    {
                        scroll.ChangeView(Vertical ? null : scroll.ScrollableWidth, Vertical ? scroll.ScrollableHeight : null, null, true);
                        await Task.Delay(25);
                        ValidateHoverGeometry();
                    }
                    else
                    {
                        // Rebuild while the pointer stays over the row, then
                        // shift the host without rebuilding its application list.
                        stressTasks = stressTasks.Select(w => w with { Title = w.Title + " changed" }).ToArray();
                        Reconcile(); await Task.Delay(25);
                        maxDrift = Math.Max(maxDrift, ValidateHoverGeometry()); transitions++;
                        Settings.FloatingGap = Settings.FloatingGap == 0 ? 8 : 0; Reposition(true);
                        await Task.Delay(25);
                        maxDrift = Math.Max(maxDrift, ValidateHoverGeometry()); transitions++;
                        if (applications == 4 && group && testEdge == DockEdge.Bottom)
                        {
                            var menu = BackgroundMenu(); menu.ShowAt(root); await Task.Delay(40);
                            try { Settings.FloatingGap = Settings.FloatingGap == 0 ? 8 : 0; Reposition(true); await Task.Delay(25); maxDrift = Math.Max(maxDrift, ValidateHoverGeometry()); }
                            finally { menu.Hide(); }
                            await Task.Delay(40); Reconcile(); transitions++;
                        }
                    }
                    cases++;
                    details.Add(new { Edge = testEdge.ToString(), Alignment = testAlignment.ToString(), Spacing = testSpacing, Grouped = group,
                        Applications = applications, ActualSize = DisplayIconSize, Overflow = dockOverflow });
                }
            }
            stressTasks = null; Settings.Items = items;
            foreach (var realEdge in Enum.GetValues<DockEdge>())
            foreach (bool realGroup in new[] { false, true })
            {
                PositionEdge = realEdge; Settings.GroupWindows = realGroup;
                lastSignature = ""; Reposition(true); Reconcile(); UpdateMagnification(0);
                var fixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tests", "WinMac.Dock.Fixture", "bin", "Release", "net10.0-windows", "WinMac.Dock.Fixture.exe"));
                using var fixture = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(fixturePath) { UseShellExecute = true });
                try
                {
                    TaskWindow[] fixtureWindows = [];
                    for (int attempt = 0; attempt < 50 && fixtureWindows.Length != 2; attempt++)
                    {
                        await Task.Delay(100);
                        fixtureWindows = tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out var pid); return pid == fixture!.Id; }).ToArray();
                    }
                    Reconcile(); maxDrift = Math.Max(maxDrift, ValidateHoverGeometry()); realEvents++;
                    if (fixtureWindows.Length != 2) throw new Exception("Real fixture windows did not appear");
                    SetStressWindowTitle(fixtureWindows[0].Handle, "Dock test window changed during hover");
                    await Task.Delay(250); Reconcile(); maxDrift = Math.Max(maxDrift, ValidateHoverGeometry()); realEvents++;
                    ShellService.Activate(fixtureWindows[0], true); await Task.Delay(100);
                    ShellService.Activate(fixtureWindows[0], false); await Task.Delay(250); Reconcile(); maxDrift = Math.Max(maxDrift, ValidateHoverGeometry()); realEvents++;
                    foreach (var task in fixtureWindows)
                    {
                        ShellService.Close(task); await Task.Delay(250); Reconcile(); maxDrift = Math.Max(maxDrift, ValidateHoverGeometry()); realEvents++;
                    }
                }
                finally
                {
                    foreach (var task in tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out var pid); return pid == fixture!.Id; })) ShellService.Close(task);
                }
            }
            // Check zero-task, separator-only and one-item layouts as well.
            stressTasks = [];
            foreach (var pins in new[] { Array.Empty<DockItem>(), new[] { new DockItem { Kind = ItemKind.Separator } }, new[] { pin } })
            {
                Settings.Items = pins.ToList(); lastSignature = ""; Reposition(true); Reconcile();
                await Task.Delay(40); maxDrift = Math.Max(maxDrift, ValidateHoverGeometry()); cases++;
            }
            if (hoverNativeClipViolations != clipStart) throw new Exception($"Transient native clipping on {hoverNativeClipViolations - clipStart} sampled icons: {firstHoverClipViolation}");
            if (rowClipErrors != rowClipStart) throw new Exception($"Row transition clipping on {rowClipErrors - rowClipStart} sampled icons");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            root.IsHitTestVisible = originalHitTest;
            Settings.ReserveSpace = reserve;
            hoverTestInput = null; ResetMagnification(); stressTasks = null; Settings.Items = items;
            PositionEdge = edge; PositionAlignment = alignment; Settings.IconSpacing = spacing; Settings.GroupWindows = grouping;
            Settings.HoverMagnificationPercent = percent; Settings.MagnificationSpan = span; Settings.FloatingGap = gap;
            Settings.ReduceMotion = reduce; inspection = inspect; lastSignature = ""; Reposition(true); Reconcile();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "layout-stress-test.json"), System.Text.Json.JsonSerializer.Serialize(new {
            Passed = failure == null, Error = failure?.ToString(), Cases = cases, Transitions = transitions, MaxOriginDriftDip = maxDrift,
            GeometryRepairs = hoverGeometryRepairs - repairStart, NativeClipViolations = hoverNativeClipViolations - clipStart, FirstClipViolation = firstHoverClipViolation, RowClipViolations = rowClipErrors - rowClipStart, RealWindowEvents = realEvents, Results = details
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
