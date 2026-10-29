using System.Text.Json;
using Microsoft.UI.Xaml;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckClickStability()
    {
        if (rowTesting || hoverTestInput != null) return;
        var edge = PositionEdge; bool inspected = inspection;
        var checks = new List<string>(); Exception? failure = null;
        try
        {
            inspection = true;
            foreach (var testEdge in Enum.GetValues<DockEdge>())
            {
                ResetMagnification(); PositionEdge = testEdge; Reposition(true); Reconcile();
                hoverTestInput = () => ((hoverWave?.Length ?? 1) * .5, true);
                UpdateMagnification(0); await Task.Delay(45); root.UpdateLayout();
                hoverTestInput = null;
                var point = new Windows.Foundation.Point(root.ActualWidth / 2, root.ActualHeight / 2);
                HoldHoverForClick(point);
                var positions = buttons.Select(b => b.Button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point())).ToArray();
                StartHoverRendering(); await Task.Delay(180); root.UpdateLayout();
                if (!hoverClickHeld || hoverRendering) throw new Exception($"{testEdge}: click did not hold in-flight hover");
                for (int i = 0; i < positions.Length; i++)
                {
                    var current = buttons[i].Button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    if (Math.Abs(current.X - positions[i].X) > .01 || Math.Abs(current.Y - positions[i].Y) > .01)
                        throw new Exception($"{testEdge}: item moved away from stationary repeated click");
                }
                checks.Add($"{testEdge}: all button positions remain fixed during repeated-click hold");
                if (!KeepHoverClickHold(new(point.X + 1, point.Y + 1))) throw new Exception($"{testEdge}: small input jitter released click target");
                checks.Add($"{testEdge}: input jitter preserves target");
                if (KeepHoverClickHold(new(point.X + 5, point.Y)) || hoverClickHeld) throw new Exception($"{testEdge}: pointer movement did not release hold");
                StartHoverRendering();
                if (!hoverRendering) throw new Exception($"{testEdge}: magnification did not resume");
                checks.Add($"{testEdge}: pointer movement resumes magnification");
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { hoverTestInput = null; ResetMagnification(); PositionEdge = edge; Reposition(true); Reconcile(); inspection = inspected; }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "click-stability-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
