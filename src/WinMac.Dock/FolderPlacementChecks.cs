using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckFolderPlacement()
    {
        var savedEdge = PositionEdge;
        var savedAlignment = PositionAlignment;
        bool savedReduceMotion = Settings.ReduceMotion, savedInspection = inspection;
        bool savedHidden = hidden, savedManual = manuallyHidden;
        var results = new List<object>();
        var failures = new List<string>();
        Flyout? flyout = null;
        try
        {
            foreach (var open in openingFlyouts.ToArray()) open.Hide();
            await Task.Delay(400);
            inspection = true; ShowDock();
            var drives = Settings.Items.Where(i => i.IsAutoUsbPin).ToArray();
            if (drives.Length < 2) throw new InvalidOperationException("Connect two drives for the placement check.");
            foreach (bool reduceMotion in new[] { true, false })
            foreach (var edge in PositionChoices)
            foreach (var alignment in Enum.GetValues<DockAlignment>())
            {
                Settings.ReduceMotion = reduceMotion;
                PositionEdge = edge; PositionAlignment = alignment;
                ResetMagnification(); ApplySettings(persist: false);
                await Task.Delay(350); FinishRowTransition(); root.UpdateLayout();
                // Real attached drives plus a folder at the first slot exercise
                // both ends of the screen without changing the user's pin order.
                foreach (var item in drives.Cast<DockItem>().Append(drives[0]))
                {
                    bool firstSlot = results.Count % (drives.Length + 1) == drives.Length;
                    var anchor = firstSlot ? buttons.First().Button : buttons.First(b => b.Entry.Item.Id == item.Id).Button;
                    OpenFolder(item, anchor);
                    flyout = openingFlyouts.OfType<Flyout>().Last();
                    await Task.Delay(650); root.UpdateLayout();
                    var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
                        .First(p => p.IsOpen && p.Child is FlyoutPresenter fp && ReferenceEquals(fp.Content, flyout.Content));
                    var presenter = (FrameworkElement)popup.Child;
                    var origin = presenter.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    var target = anchor.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    double minimum = Vertical ? monitor.Work.Top + 12 : monitor.Work.Left + 12;
                    double maximum = Vertical ? monitor.Work.Bottom - 12 : monitor.Work.Right - 12;
                    double extent = (Vertical ? presenter.ActualHeight : presenter.ActualWidth) * scale;
                    double center = (Vertical ? hostBounds.Top + (target.Y + anchor.ActualHeight / 2) * scale
                        : hostBounds.Left + (target.X + anchor.ActualWidth / 2) * scale);
                    double expected = Math.Clamp(center - extent / 2, minimum, Math.Max(minimum, maximum - extent));
                    double actual = Vertical ? hostBounds.Top + origin.Y * scale : hostBounds.Left + origin.X * scale;
                    double error = Math.Abs(actual - expected);
                    var label = $"{edge}/{alignment}/{item.Target}/first={firstSlot}/motion={Motion}";
                    results.Add(new { Scenario = label, Actual = actual, Expected = expected, ErrorPixels = error,
                        LeadingGap = actual - minimum + 12, TrailingGap = maximum - actual - extent + 12, Extent = extent, Scale = scale });
                    if (error > 1.1) failures.Add(label + $": {error:F2}px");
                    flyout.Hide(); await Task.Delay(380); flyout = null;
                }
            }
        }
        catch (Exception ex) { failures.Add(ex.ToString()); }
        finally
        {
            flyout?.Hide(); await Task.Delay(380);
            PositionEdge = savedEdge; PositionAlignment = savedAlignment; Settings.ReduceMotion = savedReduceMotion;
            inspection = savedInspection; ResetMagnification(); ApplySettings(persist: false);
            manuallyHidden = savedManual;
            if (savedHidden) HideDock(true); else ShowDock(!savedManual);
            Save();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "folder-placement-test.json"), System.Text.Json.JsonSerializer.Serialize(
            new { Passed = failures.Count == 0, Failures = failures, Results = results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
