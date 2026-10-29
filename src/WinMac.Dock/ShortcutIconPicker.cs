using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;

internal static class ShortcutIconPicker
{
    internal static async Task<string?> Choose(string path, ElementTheme theme)
    {
        if (!new[] { ".dll", ".exe" }.Contains(Path.GetExtension(path).ToLowerInvariant())) return path;
        int count = IconService.ResourceIconCount(path);
        if (count == 0) throw new InvalidOperationException("This file contains no selectable icons.");
        var result = new TaskCompletionSource<string?>();
        var window = new Window { Title = "Choose an icon", SystemBackdrop = new MicaBackdrop() };
        bool closed = false;
        window.Closed += (_, _) => { closed = true; result.TrySetResult(null); };
        IconResources.SetWindowIcon(window);
        var layout = new Grid { Padding = new Thickness(16), RowSpacing = 12 };
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var icons = new GridView { SelectionMode = ListViewSelectionMode.Single,
            ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                "<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal' ItemWidth='88' ItemHeight='96'/></ItemsPanelTemplate>") };
        ScrollViewer.SetHorizontalScrollBarVisibility(icons, ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollMode(icons, ScrollMode.Disabled);
        layout.Children.Add(icons);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        var cancel = new Button { Content = "Cancel" };
        var choose = new Button { Content = "Choose", IsEnabled = false };
        icons.SelectionChanged += (_, _) => choose.IsEnabled = icons.SelectedItem != null;
        cancel.Click += (_, _) => window.Close();
        choose.Click += (_, _) => { if (icons.SelectedItem is FrameworkElement selected) { result.TrySetResult(path + "," + selected.Tag); window.Close(); } };
        buttons.Children.Add(cancel); buttons.Children.Add(choose); Grid.SetRow(buttons, 1); layout.Children.Add(buttons);
        _ = new WindowChrome(window, layout, theme);
        window.Activate();
        double dpi = Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(window)) / 96d;
        window.AppWindow.Resize(new Windows.Graphics.SizeInt32((int)(600 * dpi), (int)(440 * dpi)));
        async Task Populate()
        {
            try
            {
                for (int index = 0; index < count && !closed; index++)
                {
                    var source = await IconService.Load(path + "," + index, imageFile: true, requestedPixels: 48);
                    if (closed) return;
                    if (source == null) continue;
                    var tile = new StackPanel { Width = 64, Padding = new Thickness(8), Spacing = 4, Tag = index };
                    tile.Children.Add(new Image { Width = 48, Height = 48, Source = source, Stretch = Stretch.Uniform });
                    tile.Children.Add(new TextBlock { Text = index.ToString(), HorizontalAlignment = HorizontalAlignment.Center });
                    icons.Items.Add(tile);
                }
            }
            catch (Exception ex) { Log.Write(ex); if (!closed) window.Close(); }
        }
        _ = Populate();
        return await result.Task;
    }
}
