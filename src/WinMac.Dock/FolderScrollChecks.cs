using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async void CheckDownloadsSliderSpacing()
    {
        ItemPropertiesWindow? editor = null;
        Exception? failure = null;
        try
        {
            editor = new ItemPropertiesWindow(this, new DockItem { Kind = ItemKind.Folder, Name = "Downloads", Target = Native.Downloads() });
            editor.Activate();
            await editor.CheckDownloadsSliders();
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { editor?.Close(); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "downloads-slider-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString() }));
    }
    private async void CheckFolderScrolling()
    {
        Window? window = null;
        FolderPane? pane = null;
        Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase? folderFlyout = null;
        Exception? failure = null;
        double extent = 0, offset = 0;
        var layouts = new List<object>();
        var directory = Path.Combine(Path.GetTempPath(), "WinMac-folder-grid-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            for (int i = 0; i < 56; i++) File.WriteAllText(Path.Combine(directory, $"Item {i:00}.txt"), "Grid fixture");
            Directory.CreateDirectory(Path.Combine(directory, "Subfolder"));
            var item = new DockItem { Kind = ItemKind.Folder, Name = "Folder fixture", Target = directory, Folder = new() { View = FolderView.Grid } };
            pane = new FolderPane(item, this, () => { });
            var viewer = pane.Scroller;
            pane.MaxHeight = viewer.MaxHeight = 300;
            window = new Window { Content = pane };
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(600, 400));
            window.Activate();
            for (int i = 0; i < 50 && viewer.ScrollableHeight <= 0; i++) await Task.Delay(450);
            pane.UpdateLayout();
            extent = viewer.ScrollableHeight;
            if (extent <= 0) throw new InvalidOperationException("Grid did not expose its full scroll extent.");
            // Loading the grid and fitting its heading can queue another
            // layout pass after the first nonzero scroll extent appears.
            for (int attempt = 0; attempt < 20; attempt++)
            {
                await Task.Delay(450); pane.UpdateLayout();
                extent = viewer.ScrollableHeight;
                viewer.ChangeView(null, extent, null, true);
                await Task.Delay(450);
                offset = viewer.VerticalOffset;
                if (Math.Abs(offset - extent) <= 1) break;
            }
            if (Math.Abs(offset - extent) > 1) throw new InvalidOperationException("The outer scrollbar could not reach the end of the grid.");
            window.Close(); window = null;
            ShowDock();
            hoverTestInput = () => (ButtonSize * 1.5, true);
            PrepareHoverSurface(); StopHoverRendering();
            hoverWave!.RestoreGrowth(hoverSlots.Select(s => s.Icon == null ? 0d : 4d).ToArray(), hoverAvailableExpansion, HoverOverflow);
            PaintHoverFrame();
            var growth = hoverWave.Growth.ToArray();
            var button = buttons.First(b => IsDownloads(b.Entry.Item));
            OpenFolder(item, button.Button);
            await Task.Delay(450);
            folderFlyout = openingFlyouts.OfType<Flyout>().First();
            await Task.Delay(200);
            ResetMagnification(false);
            if (!hoverMenuHeld || hoverWave == null || !growth.SequenceEqual(hoverWave.Growth))
                throw new InvalidOperationException("Opening the folder grid collapsed magnification.");
            folderFlyout.Hide();
            await Task.Delay(450);
            static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
            {
                for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
                {
                    var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
                    if (child is FrameworkElement element) yield return element;
                    foreach (var nested in Descendants(child)) yield return nested;
                }
            }
            foreach (bool hideHeading in new[] { false, true })
            for (int columns = 1; columns <= 7; columns++)
            {
                item.Folder.HideFolderNameInGrid = hideHeading;
                item.Folder.GridColumns = columns;
                OpenFolder(item, button.Button);
            await Task.Delay(450);
                folderFlyout = openingFlyouts.OfType<Flyout>().First();
                var gridPane = (FolderPane)((Flyout)folderFlyout).Content;
                GridView? grid = null;
                for (int attempt = 0; attempt < 50; attempt++)
                {
                    await Task.Delay(50);
                    grid = Descendants(gridPane).OfType<GridView>().FirstOrDefault();
                    gridPane.UpdateLayout();
                    if (grid?.ContainerFromIndex(columns - 1) is GridViewItem) break;
                }
                if (grid == null) throw new InvalidOperationException("Grid was not rendered.");
                var controls = Descendants(gridPane).ToArray();
                if (controls.OfType<ComboBox>().Any() || controls.OfType<Button>().Any(b => b.Content is SymbolIcon { Symbol: Symbol.Back or Symbol.OpenFile }))
                    throw new InvalidOperationException("Folder flyout contains navigation or view/sort controls.");
                var openFolder = controls.OfType<Button>().Single(b => b.Content is FontIcon { Glyph: "\uED25" });
                if (!hideHeading && (openFolder.ActualWidth != 32 || openFolder.ActualHeight != 32)) throw new InvalidOperationException("Open-folder highlight is not 32 by 32.");
                if (openFolder.Background is not Microsoft.UI.Xaml.Media.SolidColorBrush { Color.A: 0 } || openFolder.BorderThickness != new Thickness(0))
                    throw new InvalidOperationException("Open-folder button is not transparent at rest.");
                var title = controls.OfType<TextBlock>().Single(t => t.Text == item.Name);
                if ((title.Visibility == Visibility.Collapsed) != hideHeading) throw new InvalidOperationException("Grid heading preference was not applied.");
                if ((openFolder.Parent as Grid)?.Visibility != (hideHeading ? Visibility.Collapsed : Visibility.Visible))
                    throw new InvalidOperationException("Grid header preference did not hide the entire header.");
                var first = (GridViewItem)grid.ContainerFromIndex(0);
                var last = (GridViewItem)grid.ContainerFromIndex(columns - 1);
                var start = first.TransformToVisual(gridPane).TransformPoint(new Windows.Foundation.Point());
                var end = last.TransformToVisual(gridPane).TransformPoint(new Windows.Foundation.Point());
                double right = end.X + last.ActualWidth;
                layouts.Add(new { Columns = columns, HideHeading = hideHeading, PaneWidth = gridPane.ActualWidth, GridWidth = grid.ActualWidth, PanelWidth = grid.ItemsPanelRoot?.ActualWidth, RasterScale = grid.XamlRoot.RasterizationScale, Panel = grid.ItemsPanelRoot is ItemsWrapGrid wrap ? new { wrap.ItemWidth, wrap.MaximumRowsOrColumns, Margin = wrap.Margin.ToString() } : null, CellWidth = first.ActualWidth, CellHeight = first.ActualHeight, GridBorder = grid.BorderThickness.ToString(), LastColumnRight = right, LastColumnY = end.Y, FirstColumnY = start.Y });
                if (Math.Abs(end.Y - start.Y) > 1 || right > gridPane.ActualWidth - 16 + 1 || last.ActualWidth < 93)
                    throw new InvalidOperationException($"Grid with {columns} columns clipped or wrapped its last column.");
                var presenter = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).Select(p => p.Child).OfType<FlyoutPresenter>().First();
                if (presenter.ActualWidth + 1 < gridPane.ActualWidth)
                    throw new InvalidOperationException($"Flyout clipped the {columns}-column pane.");
                if (!hideHeading)
                {
                    await Task.Delay(400); gridPane.UpdateLayout();
                    var titleBefore = title.TransformToVisual(gridPane).TransformPoint(new Windows.Foundation.Point()).Y;
                    gridPane.Scroller.ChangeView(null, gridPane.Scroller.ScrollableHeight, null, true);
                    await Task.Delay(450);
                    var titleAfter = title.TransformToVisual(gridPane).TransformPoint(new Windows.Foundation.Point()).Y;
                    if (Math.Abs(titleBefore - titleAfter) > 1) throw new InvalidOperationException("Folder heading moved while scrolling.");
                }
                folderFlyout.Hide(); await Task.Delay(450);
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            folderFlyout?.Hide(); hoverTestInput = null; ResetMagnification(); pane?.Dispose(); window?.Close();
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory)) File.Delete(file);
                Directory.Delete(Path.Combine(directory, "Subfolder")); Directory.Delete(directory);
            }
            catch (Exception ex) { Log.Write(ex); }
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "folder-scroll-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), ScrollableHeight = extent, FinalOffset = offset, Layouts = layouts }));
    }
}
