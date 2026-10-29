using Microsoft.UI.Xaml;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckDockOutline()
    {
        var savedEdge = PositionEdge;
        bool savedSpan = Settings.SpanFullWidth, savedMotion = Settings.ReduceMotion, savedInspection = inspection, savedBorder = Settings.ShowDockBorder;
        Exception? failure = null;
        int checks = 0;
        try
        {
            inspection = true; Settings.SpanFullWidth = false; Settings.ReduceMotion = false; Settings.ShowDockBorder = true;
            foreach (var edge in new[] { DockEdge.Bottom, DockEdge.Left, DockEdge.Right })
            {
                PositionEdge = edge; ApplySettings(); ShowDock();
                await Task.Delay(180);
                StopHoverRendering(); PrepareHoverSurface();
                foreach (double growth in new[] { 0d, 3d, 6d, 2d, 0d })
                {
                    hoverWave!.RestoreGrowth(hoverSlots.Select(slot => slot.Icon == null ? 0d : growth).ToArray(), hoverAvailableExpansion, HoverOverflow);
                    PaintHoverFrame(); root.UpdateLayout();
                    var point = dockBorder.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    var expected = MaterialPanel;
                    expected = new(expected.Left - hostBounds.Left, expected.Top - hostBounds.Top, expected.Right - hostBounds.Left, expected.Bottom - hostBounds.Top);
                    if (Math.Abs(point.X * scale - expected.Left) > 1 || Math.Abs(point.Y * scale - expected.Top) > 1
                        || Math.Abs(dockBorder.ActualWidth * scale - expected.Width) > 1 || Math.Abs(dockBorder.ActualHeight * scale - expected.Height) > 1)
                        throw new InvalidOperationException($"Dock outline differs from material bounds on {edge} at growth {growth}.");
                    checks++;
                }
                ResetMagnification();
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            ResetMagnification(); PositionEdge = savedEdge; Settings.SpanFullWidth = savedSpan; Settings.ReduceMotion = savedMotion;
            Settings.ShowDockBorder = savedBorder; inspection = savedInspection; ApplySettings();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "dock-outline-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Error = failure?.ToString() }));
    }
}
