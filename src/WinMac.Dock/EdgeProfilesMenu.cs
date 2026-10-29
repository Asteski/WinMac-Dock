using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private void AddEdgeProfiles(MenuFlyout menu, DockItem item)
    {
        if (item.EdgeProfilesCommands == AdditionalCommandsVisibility.Hidden) return;
        var entries = new List<MenuFlyoutItemBase>();
        try
        {
            foreach (var profile in ShellService.ReadEdgeFavorites(item, profilesOnly: true))
            {
                var entry = new MenuFlyoutItem { Text = profile.Name, Icon = Glyph("\uE77B") };
                entry.Click += (_, _) => TryAction(() => ShellService.LaunchWithoutElevation(
                    ShellService.EdgeFavoriteLaunchItem(item, profile, "edge://newtab/")));
                entries.Add(entry);
            }
        }
        catch (Exception ex) { Log.Write(ex); }
        if (entries.Count == 0) entries.Add(new MenuFlyoutItem { Text = "No profiles found", IsEnabled = false });
        if (item.EdgeProfilesCommands == AdditionalCommandsVisibility.Submenu)
        {
            var profiles = new MenuFlyoutSubItem { Text = "Profiles", Icon = Glyph("\uE716") };
            foreach (var entry in entries) profiles.Items.Add(entry);
            menu.Items.Add(profiles);
        }
        else
        {
            menu.Items.Add(new MenuFlyoutItem { Text = "Profiles", IsEnabled = false });
            foreach (var entry in entries) menu.Items.Add(entry);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
    }
}
