using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckQuotedMenuStates()
    {
        Exception? failure = null; int transitions = 0;
        bool inspected = inspection;
        var menu = new MenuFlyout();
        static IEnumerable<TextBlock> Labels(DependencyObject parent)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock text) yield return text;
                foreach (var nested in Labels(child)) yield return nested;
            }
        }
        try
        {
            inspection = true; ShowDock(); TrackFlyout(menu);
            AddQuotedWindowAction(menu, new(Handle, "Crash regression fixture", Environment.ProcessPath!, "", default, false, false));
            AddQuotedWindowAction(menu, new(Handle, string.Join(" ", Enumerable.Repeat("Long quoted title", 30)), Environment.ProcessPath!, "", default, false, false));
            for (int opening = 0; opening < 5; opening++)
            {
                ShowDockFlyout(menu, dockBorder); await Task.Delay(150);
                foreach (var row in menu.Items.OfType<MenuFlyoutItem>())
                {
                    var part = Labels(row).SingleOrDefault(text => text.Name == "TextBlock");
                    if (part == null || part.Visibility != Visibility.Collapsed)
                        throw new InvalidOperationException("Quoted label removed or exposed its named template part: " + string.Join("; ", Labels(row).Select(text => $"{text.Name} [{text.Visibility}] {text.Text}")));
                    foreach (var state in new[] { "PointerOver", "Pressed", "Disabled", "Normal", "PointerOver", "Normal" })
                    {
                        if (!VisualStateManager.GoToState(row, state, false)) throw new InvalidOperationException("Missing menu state: " + state);
                        row.UpdateLayout(); await Task.Delay(20); transitions++;
                    }
                    row.IsEnabled = false; await Task.Delay(20); row.IsEnabled = true;
                }
                menu.Hide(); await Task.Delay(100);
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { menu.Hide(); inspection = inspected; }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "quoted-menu-states-test.json"), System.Text.Json.JsonSerializer.Serialize(
            new { Passed = failure == null, Error = failure?.ToString(), Openings = 5, Transitions = transitions }));
    }
    private async void CheckWindowTitleMenu()
    {
        Exception? failure = null;
        var measurements = new List<object>();
        var menu = new MenuFlyout();
        bool inspected = inspection;
        try
        {
            inspection = true; ShowDock(); TrackFlyout(menu);
            foreach (var title in new[] { "Picture in picture", "A moderately long browser window title that should widen this menu", string.Join(" ", Enumerable.Repeat("Very long browser window title", 12)) })
                MenuWindow(menu, new(Handle, title, Environment.ProcessPath!, "", default, false, false), () => { });
            ShowDockFlyout(menu, dockBorder);
            await Task.Delay(600);
            static IEnumerable<TextBlock> Labels(DependencyObject parent)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = VisualTreeHelper.GetChild(parent, i);
                    if (child is TextBlock text) yield return text;
                    foreach (var nested in Labels(child)) yield return nested;
                }
            }
            var rows = menu.Items.OfType<MenuFlyoutItem>().ToArray();
            foreach (var row in rows)
            {
                var label = Labels(row).Single(t => t.Text == row.Text);
                bool tooltip = ToolTipService.GetToolTip(row)?.ToString() == row.Text;
                measurements.Add(new { row.ActualWidth, TextWidth = label.ActualWidth, label.IsTextTrimmed, Tooltip = tooltip });
                if (label.TextTrimming != TextTrimming.CharacterEllipsis || label.IsTextTrimmed != tooltip)
                    throw new InvalidOperationException("Window title ellipsis or full-title tooltip is incorrect.");
            }
            if (rows[0].ActualWidth <= 294) throw new InvalidOperationException("Window menu did not expand beyond its old width.");
            if (!Labels(rows[^1]).Single(t => t.Text == rows[^1].Text).IsTextTrimmed)
                throw new InvalidOperationException("Long window title did not trim at the menu width limit.");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { menu.Hide(); inspection = inspected; }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "window-title-menu-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Measurements = measurements }));
    }
}
