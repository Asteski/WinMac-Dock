using Windows.Foundation;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private async Task CheckDragBounds()
    {
        bool inspected = inspection;
        var start = dragStartRoot;
        string order = string.Join(",", Settings.Items.Select(i => i.Id));
        int checks = 0; Exception? failure = null;
        try
        {
            if (dragging) throw new InvalidOperationException("An item is already being dragged.");
            inspection = true; FinishRowTransition(); root.UpdateLayout();
            var pins = buttons.Where(b => b.Entry.IsPinned).ToArray();
            if (pins.Length == 0) throw new InvalidOperationException("No pinned items to check.");
            foreach (var pin in new[] { pins[0], pins[pins.Length / 2], pins[^1] }.Distinct())
            {
                dragStartRoot = pin.Button.TransformToVisual(root).TransformPoint(new Point(pin.Button.ActualWidth / 2, pin.Button.ActualHeight / 2));
                BeginItemDrag(pin.Button, pin.Entry); root.UpdateLayout();
                foreach (double offset in new[] { -10000d, 10000d, 0d })
                {
                    UpdateItemDragPosition(new Point(dragStartRoot.X + (Vertical ? 0 : offset), dragStartRoot.Y + (Vertical ? offset : 0)));
                    root.UpdateLayout();
                    var origin = draggedVisual!.TransformToVisual(root).TransformPoint(new Point());
                    double length = Vertical ? draggedVisual.ActualHeight : draggedVisual.ActualWidth;
                    var panel = MaterialPanel;
                    double low = (Vertical ? panel.Top - hostBounds.Top : panel.Left - hostBounds.Left) / scale;
                    double high = (Vertical ? panel.Bottom - hostBounds.Top : panel.Right - hostBounds.Left) / scale;
                    if (Along(origin) < low - .5 || Along(origin) + length > high + .5)
                        throw new InvalidOperationException("Dragged artwork left the visible dock at offset " + offset);
                    checks++;
                }
                FinishItemDrag(); await Task.Delay(30);
            }
            if (string.Join(",", Settings.Items.Select(i => i.Id)) != order) throw new InvalidOperationException("Boundary checks changed the saved item order.");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { FinishItemDrag(); dragStartRoot = start; inspection = inspected; }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "drag-bounds-test.json"), System.Text.Json.JsonSerializer.Serialize(
            new { Passed = failure == null, Checks = checks, Error = failure?.ToString() }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
