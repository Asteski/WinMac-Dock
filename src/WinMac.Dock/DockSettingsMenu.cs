using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private void AddDockSettingsMenu(MenuFlyout menu)
    {
        var icon = new ImageIcon();
        var item = new MenuFlyoutItem { Text = "Dock settings", Icon = icon };
        item.Click += (_, _) => ShowSettings();
        menu.Items.Add(item);
        int request = 0;
        async void UpdateIcon()
        {
            int current = ++request;
            try
            {
                string reference = root.ActualTheme == ElementTheme.Dark ? BundledIcons.MenuDark : BundledIcons.MenuLight;
                var source = await IconService.Load(IconResources.PathFor(reference), imageFile: true,
                    requestedPixels: IconRaster.Pixels(16, root.XamlRoot?.RasterizationScale ?? scale));
                if (current == request && source != null) icon.Source = source;
            }
            catch (Exception ex) { Log.Write(ex); }
        }
        menu.Opening += (_, _) => UpdateIcon();
        UpdateIcon();
    }
}
