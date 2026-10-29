namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckBetaDefaultIcons(bool correctOrder, string[] icons)
    {
        Exception? failure = null;
        int decoded = 0;
        try
        {
            if (!correctOrder) throw new InvalidOperationException("Incorrect default pin order.");
            foreach (var path in icons)
            {
                if (await IconService.Load(path, imageFile: true, requestedPixels: IconPixels) == null)
                    throw new InvalidOperationException("Bundled icon could not be decoded: " + path);
                decoded++;
            }
            Settings.Theme = DockTheme.Dark;
            ApplySettings();
            await Task.Delay(600);
            foreach (var entry in buttons.Where(button => button.Entry.Item.Kind is ItemKind.StartMenu or ItemKind.RecycleBin))
            {
                var grid = (Microsoft.UI.Xaml.Controls.Grid)entry.Icon;
                if (grid.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().First().Source == null
                    || grid.Children.OfType<Microsoft.UI.Xaml.Controls.FontIcon>().Any(icon => icon.Visibility == Microsoft.UI.Xaml.Visibility.Visible))
                    throw new InvalidOperationException("Dock item is still showing a fallback glyph: " + entry.Entry.Item.Name);
                var expected = await IconService.Load(IconService.PathFor(entry.Entry.Item), imageFile: true, requestedPixels: IconPixels);
                if (!ReferenceEquals(grid.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().First().Source, expected))
                    throw new InvalidOperationException("Dock did not display the selected bundled icon: " + entry.Entry.Item.Name);
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "beta-defaults-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, DecodedIcons = decoded, Error = failure?.ToString(), IsolatedSettings = store.Path }));
    }
}
