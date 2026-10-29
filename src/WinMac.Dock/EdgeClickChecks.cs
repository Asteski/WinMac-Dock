using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckEdgeClicks()
    {
        if (!Program.SeparatorAudit) return;
        int cases = 0, hits = 0;
        Exception? failure = null;
        string scenario = "";
        async Task Settle()
        {
            root.UpdateLayout();
            var ready = new TaskCompletionSource<bool>();
            int frames = 0;
            EventHandler<object> rendered = (_, _) => { if (++frames >= 2) ready.TrySetResult(true); };
            CompositionTarget.Rendering += rendered;
            try { if (await Task.WhenAny(ready.Task, Task.Delay(2000)) != ready.Task) throw new TimeoutException("Rendering did not settle"); await Task.Delay(16); }
            finally { CompositionTarget.Rendering -= rendered; }
        }
        try
        {
            safetyTimer.Stop(); reconcileTimer.Stop(); themeTimer.Stop(); visibilityTimer.Stop();
            Settings.ReserveSpace = false; Settings.ShowTrayIcon = false; Settings.ReduceMotion = false;
            Settings.ShowIconHighlights = true; Settings.FloatingGap = 0; Settings.AnimationMs = 0;
            Settings.IconSpacing = 0; Settings.HoverMagnificationPercent = 50;
            Settings.Items = [new() { Kind = ItemKind.StartMenu, Name = "Start fixture" },
                new() { Kind = ItemKind.Separator },
                new() { Kind = ItemKind.Application, Name = "Application fixture", Target = Environment.ProcessPath! }];
            stressTasks = []; inspection = true; DisplayMagnificationEnabled = true;
            foreach (var edge in Enum.GetValues<DockEdge>())
            foreach (int size in new[] { 16, 40, 64 })
            foreach (var margins in new[] { (4, 4, 4, 4), (0, 0, 0, 0), (3, 7, 9, 1) })
            {
                ResetMagnification(); PositionEdge = edge; PositionAlignment = DockAlignment.Center; Settings.IconSize = size;
                (Settings.DockMarginLeft, Settings.DockMarginTop, Settings.DockMarginRight, Settings.DockMarginBottom) = margins;
                ApplySettings(persist: false); Reconcile(); ShowDock(); await Settle();
                foreach (bool magnified in new[] { false, true })
                {
                    scenario = $"{edge}/size={size}/margins={margins}/magnified={magnified}";
                    if (magnified)
                    {
                        hoverTestInput = () => ((hoverWave?.Length ?? 1) / 2, true);
                        PrepareHoverSurface(); StopHoverRendering();
                        hoverWave!.RestoreGrowth(hoverSlots.Select(s => s.Icon == null ? 0d : HoverOverflow / 2).ToArray(), hoverAvailableExpansion, HoverOverflow);
                        PaintHoverFrame(); await Settle();
                    }
                    foreach (var entry in buttons)
                    {
                        var button = entry.Button;
                        var origin = button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                        double cross = (Vertical ? resting.Width : resting.Height) / scale;
                        foreach (double offset in new[] { .25 / scale, .75 / scale, cross / 2, cross - .75 / scale, cross - .25 / scale })
                        {
                            var point = new Windows.Foundation.Point(Vertical ? (resting.Left - hostBounds.Left) / scale + offset : origin.X + button.ActualWidth / 2,
                                Vertical ? origin.Y + button.ActualHeight / 2 : (resting.Top - hostBounds.Top) / scale + offset);
                            if (!VisualTreeHelper.FindElementsInHostCoordinates(point, root).Contains(button))
                                throw new Exception($"Button is not hit-testable at cross offset {offset}, {scenario}");
                            int px = (int)Math.Floor(hostBounds.Left + point.X * scale), py = (int)Math.Floor(hostBounds.Top + point.Y * scale);
                            if (!visibleBounds.Contains(px, py)) throw new Exception($"Button edge is outside native input clip: {scenario}");
                            hits++;
                        }
                    }
                    cases++;
                }
                hoverTestInput = null; ResetMagnification();
            }
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            hoverTestInput = null; ResetMagnification(); stressTasks = null;
            File.WriteAllText(Path.Combine(Log.DirectoryPath, "edge-click-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Cases = cases, Hits = hits, Scenario = scenario, Error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
}