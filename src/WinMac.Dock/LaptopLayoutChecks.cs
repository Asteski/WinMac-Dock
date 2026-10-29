using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private async Task CheckLaptopLayout()
    {
        var report = new List<object>();
        var savedEdge = PositionEdge;
        int savedSpacing = Settings.IconSpacing;
        string scenario = "";
        Exception? failure = null;
        var baseline = new Dictionary<FrameworkElement, Windows.Foundation.Point>();

        object Read(string stage)
        {
            var icons = buttons.Select(b =>
            {
                var p = b.Icon.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                if (stage == "rest") baseline[b.Icon] = p;
                else if (stage != "magnified" && baseline.TryGetValue(b.Icon, out var expected)
                    && (Math.Abs(p.X - expected.X) > .01 || Math.Abs(p.Y - expected.Y) > .01))
                    throw new InvalidOperationException($"Icon {b.Entry.Item.Name} moved at {stage} on {scenario}: {p} versus {expected}.");
                return new { b.Entry.Item.Name, p.X, p.Y };
            }).ToArray();
            var separators = strip.Children.OfType<Border>().Select(s =>
            {
                var p = s.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                separatorStrokes.TryGetValue(s, out var v);
                return new { p.X, p.Y, s.ActualWidth, s.ActualHeight, OffsetX = v?.Offset.X, OffsetY = v?.Offset.Y,
                    PaintedY = p.Y + (v?.Offset.Y ?? 0), PaintedX = p.X + (v?.Offset.X ?? 0) };
            }).ToArray();
            return new { stage, scenario, scale, icons, separators };
        }

        try
        {
            foreach (var edge in new[] { DockEdge.Bottom, DockEdge.Top, DockEdge.Left, DockEdge.Right })
            foreach (int spacing in new[] { 0, 6 })
            {
                scenario = $"{edge}, spacing={spacing}";
                PositionEdge = edge;
                Settings.IconSpacing = spacing;
                ApplySettings(persist: false);
                ShowDock();
                await Task.Delay(500);
                root.UpdateLayout(); baseline.Clear(); report.Add(Read("rest"));
                hoverTestInput = () => (ButtonSize * 2, true);
                PrepareHoverSurface(); StopHoverRendering();
                root.UpdateLayout(); report.Add(Read("prepared"));
                hoverWave!.RestoreGrowth(hoverSlots.Select(s => s.Icon == null ? 0d : 4d).ToArray(), hoverAvailableExpansion, HoverOverflow);
                PaintHoverFrame(); root.UpdateLayout(); report.Add(Read("magnified"));
                hoverWave.RestoreGrowth(hoverSlots.Select(_ => 0d).ToArray(), hoverAvailableExpansion, HoverOverflow);
                PaintHoverFrame(); root.UpdateLayout(); report.Add(Read("contracted"));
                hoverTestInput = null;
                ResetMagnification(); root.UpdateLayout(); report.Add(Read("reset"));
                await Task.Delay(300);
                root.UpdateLayout(); report.Add(Read("settled"));

                // Also exercise the live rendering loop and its natural exit.
                hoverTestInput = () => (ButtonSize * 2, true);
                PrepareHoverSurface(); hoverEngaged = true; StartHoverRendering();
                await Task.Delay(700);
                hoverTestInput = () => (ButtonSize * 2, false);
                hoverEngaged = false;
                await Task.Delay(1200);
                if (hoverWave != null) throw new InvalidOperationException($"Animated exit did not settle on {scenario}.");
                root.UpdateLayout(); report.Add(Read("animatedSettled"));
                hoverTestInput = null;
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            hoverTestInput = null; ResetMagnification();
            PositionEdge = savedEdge; Settings.IconSpacing = savedSpacing;
            ApplySettings(persist: false);
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "laptop-layout.json"),
            System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Frames = report }));
    }
}
