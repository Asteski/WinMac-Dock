using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;

namespace WinMac.Dock;

internal static class FolderSettingsChecks
{
    private static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    internal static async Task Run()
    {
        DockWindow? owner = null;
        Window? window = null;
        FolderPane? pane = null;
        Exception? failure = null;
        int checks = 0;
        try
        {
            var directory = Path.Combine(AppContext.BaseDirectory, "folder-settings-fixtures");
            Directory.CreateDirectory(directory);
            for (int i = 0; i < 40; i++) File.WriteAllText(Path.Combine(directory, $"Example {i:00}.txt"), "fixture");
            Directory.CreateDirectory(Path.Combine(directory, "Folder.with.dots"));
            owner = new DockWindow(new DockSettings { Items = [], ReduceMotion = true });
            window = new Window();
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(700, 700));
            foreach (var view in new[] { FolderView.Grid, FolderView.List })
            foreach (int size in new[] { 16, 20, 24, 28, 32, 40, 48, 56, 64 })
            foreach (bool extensions in new[] { false, true })
            {
                owner.Settings.FolderFlyoutIconSize = size;
                owner.Settings.ShowFileExtensionsInTooltips = extensions;
                pane = new FolderPane(new DockItem { Kind = ItemKind.Folder, Target = directory, Name = "Fixture", Folder = new() { View = view } }, owner, () => { });
                window.Content = pane;
                window.Activate();
                ListViewBase? list = null;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    await Task.Delay(50);
                    pane.UpdateLayout();
                    list = Descendants(pane).OfType<ListViewBase>().FirstOrDefault();
                    if (list?.ContainerFromIndex(0) is Microsoft.UI.Xaml.Controls.Primitives.SelectorItem) break;
                }
                var cell = list?.ContainerFromIndex(0) as Microsoft.UI.Xaml.Controls.Primitives.SelectorItem ?? throw new Exception("Folder item was not realized.");
                var image = Descendants(cell).OfType<Image>().First();
                var label = Descendants(cell).OfType<TextBlock>().First();
                var row = (FolderEntry)list!.Items[0];
                if (image.ActualWidth != size || image.ActualHeight != size) throw new Exception($"Incorrect {view} image size at {size}: {image.ActualWidth} x {image.ActualHeight}");
                if (label.Text != row.DisplayName(extensions)) throw new Exception("Folder label ignored extension preference.");
                if (Descendants(pane).OfType<TextBlock>().Any(text => text.Visibility == Visibility.Visible && text.Text == "41 items"))
                    throw new Exception("Folder item count is still visible.");
                if (view == FolderView.Grid && cell.ActualHeight != size + 52) throw new Exception("Grid cell did not resize with artwork.");
                checks++;
                pane.Dispose(); pane = null;
                window.Content = null;
            }
            foreach (bool extensions in new[] { false, true })
            {
                owner.Settings.ShowFileExtensionsInTooltips = extensions;
                var item = new DockItem { Kind = ItemKind.Folder, Target = directory, Folder = new() { View = FolderView.Menu, Sort = FolderSort.Name } };
                var menu = owner.CreateFolderMenu(item);
                if (menu.Items.Count != 43 || menu.Items[0] is not MenuFlyoutItem { Text: "Open folder" } || menu.Items[1] is not MenuFlyoutSeparator)
                    throw new Exception("Native folder menu lost its header, separator, or entries beyond the old limit.");
                if (menu.Items[2] is not MenuFlyoutItem first || first.Text != (extensions ? "Example 00.txt" : "Example 00"))
                    throw new Exception("Native folder menu ignored sorting or extension preference.");
                checks++;
            }
            foreach (var view in new[] { FolderView.List, FolderView.Grid })
            for (int repeat = 0; repeat < 3; repeat++)
            {
                owner.Settings.FolderFlyoutIconSize = 40;
                pane = new FolderPane(new DockItem { Kind = ItemKind.Folder, Target = Native.Downloads(), Name = "Downloads", Folder = new() { View = view, Sort = FolderSort.Modified, FilesOnly = true } }, owner, () => { });
                window.Content = pane; window.Activate();
                await Task.Delay(1500);
                pane.UpdateLayout();
                pane.Dispose(); pane = null; window.Content = null;
                checks++;
            }
            window.Content = null;
            owner.Settings.ReduceMotion = false;
            owner.Settings.FollowWindowsAnimations = false;
            owner.AppWindow.Resize(new Windows.Graphics.SizeInt32(600, 100));
            owner.Activate();
            await owner.CheckDownloadsPopupLifecycle();
            checks += 6;
        }
        catch (Exception ex) { failure = ex; }
        finally { pane?.Dispose(); window?.Close(); owner?.Close(); }
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "folder-settings-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Error = failure?.ToString() }));
        Environment.ExitCode = failure == null ? 0 : 1;
        Application.Current.Exit();
    }
}

internal sealed partial class DockWindow
{
    internal async Task CheckDownloadsPopupLifecycle()
    {
        foreach (var view in new[] { FolderView.List, FolderView.Grid })
        for (int repeat = 0; repeat < 3; repeat++)
        {
            OpenFolder(new DockItem { Kind = ItemKind.Folder, Target = Native.Downloads(), Name = "Downloads", Folder = new() { View = view, Sort = FolderSort.Modified, FilesOnly = true } }, root);
            Microsoft.UI.Xaml.Controls.Primitives.FlyoutBase? popup = null;
            for (int attempt = 0; attempt < 100 && popup == null; attempt++)
            {
                await Task.Delay(50);
                popup = openingFlyouts.OfType<Flyout>().FirstOrDefault();
            }
            if (popup == null) throw new Exception("Downloads popup did not open.");
            var pane = ((Flyout)popup).Content as FolderPane ?? throw new Exception("Missing prepared folder pane.");
            pane.UpdateLayout();
            double initialHeight = pane.ActualHeight;
            var rows = DescendantsForPopup(pane).OfType<ListViewBase>().FirstOrDefault();
            if (rows == null || rows.Items.Count == 0) throw new Exception("Downloads popup opened before its listing was ready.");
            if (rows.ItemContainerTransitions.Count != 0) throw new Exception("Rows still animate separately from the popup.");
            await Task.Delay(800);
            if (Math.Abs(pane.ActualHeight - initialHeight) > 1) throw new Exception("Downloads popup resized during its entrance.");
            popup.Hide();
            await Task.Delay(500);
            if (openingFlyouts.Contains(popup)) throw new Exception("Downloads popup did not finish closing.");
        }
    }
    private static IEnumerable<FrameworkElement> DescendantsForPopup(DependencyObject parent)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var nested in DescendantsForPopup(child)) yield return nested;
        }
    }
}
