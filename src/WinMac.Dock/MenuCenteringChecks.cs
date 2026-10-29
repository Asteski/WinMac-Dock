using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckMenuCentering()
    {
        var edge = PositionEdge;
        bool inspected = inspection, wasHidden = hidden, manual = manuallyHidden;
        var results = new List<object>();
        Exception? failure = null;
        MenuFlyout? menu = null;
        try
        {
            inspection = true; ShowDock();
            foreach (var testEdge in PositionChoices)
            {
                PositionEdge = testEdge; ResetMagnification(); Reposition(true);
                await Task.Delay(200);
                var item = buttons.First(b => b.Entry.IsPinned);
                menu = ItemMenu(item.Entry); ShowDockFlyout(menu, item.Button);
                await Task.Delay(200);
                var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).First(p => p.Child is MenuFlyoutPresenter && p.IsOpen);
                var presenter = (FrameworkElement)popup.Child;
                var origin = presenter.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                var target = item.Button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                double min = Vertical ? (monitor.Work.Top - hostBounds.Top) / scale : (monitor.Work.Left - hostBounds.Left) / scale;
                double extent = Vertical ? presenter.ActualHeight : presenter.ActualWidth;
                double max = (Vertical ? monitor.Work.Bottom - hostBounds.Top : monitor.Work.Right - hostBounds.Left) / scale - extent;
                double desired = Math.Clamp((Vertical ? target.Y + item.Button.ActualHeight / 2 : target.X + item.Button.ActualWidth / 2) - extent / 2, min, Math.Max(min, max));
                double error = Math.Abs((Vertical ? origin.Y : origin.X) - desired);
                results.Add(new { Edge = testEdge.ToString(), ErrorDips = error, presenter.ActualWidth, presenter.ActualHeight });
                if (error > 2) throw new InvalidOperationException($"{testEdge} menu centering error: {error:F2} DIPs");
                menu.Hide(); menu = null; await Task.Delay(100);
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            menu?.Hide(); PositionEdge = edge; inspection = inspected;
            Reposition(true); manuallyHidden = manual;
            if (wasHidden) HideDock(true); else ShowDock(!manual);
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "menu-centering-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Results = results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
