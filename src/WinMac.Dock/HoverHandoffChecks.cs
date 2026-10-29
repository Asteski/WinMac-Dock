using Microsoft.UI.Xaml;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckHoverHandoff()
    {
        var edge = PositionEdge; var alignment = PositionAlignment;
        bool span = Settings.SpanFullWidth, conditional = Settings.SpanFullWidthOnlyWhenMaximized;
        bool inspected = inspection, wasHidden = hidden, manual = manuallyHidden;
        var results = new List<object>(); Exception? failure = null;
        try
        {
            inspection = true; ShowDock();
            hoverTestInput = () => (0, false);
            foreach (bool spanned in new[] { false, true })
            foreach (var testEdge in PositionChoices)
            foreach (var align in Enum.GetValues<DockAlignment>())
            {
                ResetMagnification(); PositionEdge = testEdge; PositionAlignment = align;
                Settings.SpanFullWidth = spanned; Settings.SpanFullWidthOnlyWhenMaximized = false;
                Reposition(true); await Task.Delay(100); root.UpdateLayout();
                PrepareHoverSurface(); StopHoverRendering();
                hoverWave!.RestoreGrowth(hoverSlots.Select(s => s.Icon == null ? 0d : 4d).ToArray(), hoverAvailableExpansion, HoverOverflow);
                PaintHoverFrame();
                hoverWave.ClearTarget(); hoverWave.Advance(1, 0); PaintHoverFrame(); root.UpdateLayout();
                var before = buttons.ToDictionary(b => b.Button, b => b.Icon.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()));
                ResetMagnification(); root.UpdateLayout();
                double maximum = 0;
                foreach (var button in buttons)
                {
                    var after = button.Icon.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    var previous = before[button.Button];
                    maximum = Math.Max(maximum, Math.Max(Math.Abs(after.X - previous.X), Math.Abs(after.Y - previous.Y)) * scale);
                }
                results.Add(new { Edge = testEdge.ToString(), Alignment = align.ToString(), Spanned = spanned, MaxShiftPixels = maximum });
                if (maximum > .1) throw new InvalidOperationException($"Hover handoff moved {maximum:F3} pixels on {testEdge}/{align}, spanning={spanned}");
            }
            // A centered row has the same screen position in both span modes.
            // Catch a stale ScrollPresenter viewport becoming a false animation
            // destination, including the no-hover path that used to skip anchoring.
            foreach (var testEdge in PositionChoices)
            {
                FinishRowTransition(); ResetMagnification();
                PositionEdge = testEdge; PositionAlignment = DockAlignment.Center;
                Settings.SpanFullWidth = false; Settings.SpanFullWidthOnlyWhenMaximized = false;
                Reposition(false); await Task.Delay(120); root.UpdateLayout();
                foreach (bool spanned in new[] { true, false })
                {
                    ResetMagnification(); root.UpdateLayout();
                    var previousHost = hostBounds;
                    var before = CaptureRowScene().Sprites.Where(s => s.Key.EndsWith(":icon")).ToDictionary(s => s.Key, s => s.Bounds);
                    Settings.SpanFullWidth = spanned;
                    TransitionDockSpan();
                    if (HoverEnvelope && hostBounds != previousHost) throw new InvalidOperationException($"Span transition moved the native icon viewport on {testEdge}");
                    var targets = rowMoves.Where(m => m.Sprite.Key.EndsWith(":icon")).ToDictionary(m => m.Sprite.Key, m => m.To);
                    double maximum = 0;
                    foreach (var (key, target) in targets)
                        if (before.TryGetValue(key, out var previous))
                            maximum = Math.Max(maximum, Math.Max(Math.Abs(target.X - previous.X), Math.Abs(target.Y - previous.Y)));
                    if (maximum > 1.1) throw new InvalidOperationException($"Span transition targets moved centered icons {maximum:F3} pixels on {testEdge}, spanning={spanned}");
                    for (int i = 0; i < 100 && rowAnimating; i++) await Task.Delay(16);
                    if (rowAnimating) throw new InvalidOperationException("Span transition did not finish");
                    if (hoverWave != null) throw new InvalidOperationException("Idle span handoff retained a temporary hover layout");
                    var handoffHost = Native.Bounds(Handle);
                    var handoffMaterial = Native.Bounds(materialHandle);
                    if (handoffMaterial != resting) throw new InvalidOperationException("Span handoff left the material window outside its resting bounds");
                    var handoff = CaptureRowScene().Sprites.ToDictionary(s => s.Key, s => s.Bounds);
                    for (int frame = 0; frame < 5; frame++)
                    {
                        await Task.Delay(16); root.UpdateLayout();
                        if (Native.Bounds(Handle) != handoffHost || Native.Bounds(materialHandle) != handoffMaterial)
                            throw new InvalidOperationException($"Span handoff moved a dock window on frame {frame}, {testEdge}, spanning={spanned}");
                        foreach (var sprite in CaptureRowScene().Sprites)
                            if (handoff.TryGetValue(sprite.Key, out var previous)
                                && Math.Max(Math.Abs(sprite.Bounds.X - previous.X), Math.Abs(sprite.Bounds.Y - previous.Y)) > .1)
                                throw new InvalidOperationException($"Span handoff shifted {sprite.Key} on frame {frame}, {testEdge}, spanning={spanned}");
                    }
                    await Task.Delay(80); root.UpdateLayout();
                    var settled = CaptureRowScene().Sprites.ToDictionary(s => s.Key, s => s.Bounds);
                    foreach (var (key, target) in targets)
                        if (settled.TryGetValue(key, out var actual))
                            maximum = Math.Max(maximum, Math.Max(Math.Abs(target.X - actual.X), Math.Abs(target.Y - actual.Y)));
                    results.Add(new { Edge = testEdge.ToString(), SpanTransition = spanned, MaxShiftPixels = maximum });
                    if (maximum > 1.1) throw new InvalidOperationException($"Span handoff moved icons {maximum:F3} pixels on {testEdge}, spanning={spanned}");
                }
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            hoverTestInput = null; ResetMagnification(); PositionEdge = edge; PositionAlignment = alignment;
            Settings.SpanFullWidth = span; Settings.SpanFullWidthOnlyWhenMaximized = conditional;
            inspection = inspected; Reposition(true); manuallyHidden = manual;
            if (wasHidden) HideDock(true); else ShowDock(!manual);
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "hover-handoff-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Results = results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
