using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckTrayDismiss()
    {
        Exception? failure = null;
        int cycles = 0;
        try
        {
            if (popupCount != 0) throw new Exception("Close existing dock menus before this check");
            for (int i = 0; i < 3; i++)
            {
                ShowTrayMenu(); await Task.Delay(250);
                if (trayMenu == null || popupCount == 0) throw new Exception("Tray menu did not remain open");
                // Exercise the real window-procedure deactivation path and the
                // native flyout close lifecycle, including repeated reopening.
                Native.PostMessage(Handle, 0x001C, 0, 0);
                await Task.Delay(250);
                if (trayMenu != null || popupCount != 0) throw new Exception("Deactivation left a tray popup or interaction hold open");
                cycles++;
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { trayMenu?.Hide(); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "tray-dismiss-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Cycles = cycles }));
    }
    private async Task CheckSpacing()
    {
        int originalSpacing = Settings.IconSpacing;
        var originalEdge = PositionEdge;
        var originalItems = Settings.Items.ToList();
        bool originalInspection = inspection;
        Exception? failure = null;
        var results = new List<object>();
        try
        {
            inspection = true;
            var sample = originalItems.First(i => i.Kind != ItemKind.Separator);
            Settings.Items = [sample with { Id = "spacing-a" }, sample with { Id = "spacing-b" }, new() { Id = "spacing-divider", Kind = ItemKind.Separator }, sample with { Id = "spacing-c" }, sample with { Id = "spacing-d" }];
            foreach (var edge in Enum.GetValues<DockEdge>())
            foreach (int spacing in new[] { 8, 0, -1, -8, -16 })
            {
                ResetMagnification(); PositionEdge = edge; Settings.IconSpacing = spacing;
                lastSignature = ""; Reposition(true); Reconcile(); root.UpdateLayout();
                await Task.Delay(60);
                var children = strip.Children.OfType<FrameworkElement>().ToArray();
                var gaps = new List<double>();
                for (int i = 1; i < children.Length; i++)
                {
                    var before = children[i - 1]; var after = children[i];
                    var p = before.TransformToVisual(strip).TransformPoint(new Windows.Foundation.Point());
                    var q = after.TransformToVisual(strip).TransformPoint(new Windows.Foundation.Point());
                    double gap = Vertical ? q.Y - p.Y - before.ActualHeight : q.X - p.X - before.ActualWidth;
                    double expected = DockSpacing.Gap(before is Button, after is Button, spacing);
                    if (Math.Abs(gap - expected) > 1 / scale + .01) throw new Exception($"{edge}/{spacing}: gap {i} was {gap}, expected {expected}");
                    gaps.Add(gap);
                }
                PrepareHoverSurface();
                if (Math.Abs(hoverWave!.Length - DockSpacing.Length(children.Select(e => e is Button ? (double)ButtonSize : SeparatorWidth).ToArray(), children.Select(e => e is Button).ToArray(), spacing)) > .01) throw new Exception("Hover wave and resting spacing disagree");
                results.Add(new { Edge = edge.ToString(), Spacing = spacing, Gaps = gaps, HoverLength = hoverWave.Length });
                ResetMagnification();
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            ResetMagnification(); Settings.Items = originalItems; PositionEdge = originalEdge; Settings.IconSpacing = originalSpacing;
            inspection = originalInspection; lastSignature = ""; Reposition(true); Reconcile();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "spacing-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Cases = results.Count, Results = results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
