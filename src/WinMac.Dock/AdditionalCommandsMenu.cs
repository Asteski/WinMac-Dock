using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private void AddAdditionalCommands(MenuFlyout menu, DockItem item)
    {
        if (ShellService.IsMicrosoftEdge(item))
        {
            int commandsStart = menu.Items.Count;
            var sections = new (AdditionalCommandsVisibility Mode, Action Add)[]
            {
                (item.EdgeFavoritesCommands, () => AddEdgeFavorites(menu, item)),
                (item.EdgeWorkspacesCommands, () => AddEdgeFavorites(menu, item, workspacesOnly: true)),
                (item.EdgeProfilesCommands, () => AddEdgeProfiles(menu, item))
            };
            // Stable ordering keeps Favorites / Workspaces / Profiles together
            // within each mode, with expanded sections before submenu groups.
            bool expandedCommands = false, submenuBoundaryAdded = false;
            foreach (var section in sections.Where(section => section.Mode != AdditionalCommandsVisibility.Hidden).OrderBy(section => section.Mode))
            {
                if (section.Mode == AdditionalCommandsVisibility.Submenu && expandedCommands && !submenuBoundaryAdded)
                {
                    menu.Items.Add(new MenuFlyoutSeparator());
                    submenuBoundaryAdded = true;
                }
                int beforeSection = menu.Items.Count;
                section.Add();
                if (section.Mode == AdditionalCommandsVisibility.MainMenu && menu.Items.Count > beforeSection) expandedCommands = true;
                while (menu.Items.Count > commandsStart && menu.Items.LastOrDefault() is MenuFlyoutSeparator) menu.Items.RemoveAt(menu.Items.Count - 1);
            }
            if (menu.Items.Count > commandsStart) menu.Items.Add(new MenuFlyoutSeparator());
            return;
        }
        if (IsFileExplorer(item)) { AddExplorerLocations(menu, item); return; }
        if (ShellService.IsTerminal(item))
        {
            if (item.AdditionalCommands != AdditionalCommandsVisibility.Hidden && AddTerminalProfiles(menu, item)) menu.Items.Add(new MenuFlyoutSeparator());
            return;
        }
        if (ShellService.IsXbox(item)) { AddXboxSections(menu, item); return; }
        if (ShellService.IsSnippingTool(item))
        {
            foreach (var section in new[] { (Mode: item.Snipping.FoldersCommands, Label: "Folders", Folders: true), (Mode: item.AdditionalCommands, Label: "Tasks", Folders: false) }.OrderBy(s => s.Mode))
            {
                if (section.Mode == AdditionalCommandsVisibility.Hidden) continue;
                var content = new MenuFlyout(); AddSnippingToolCommands(content, section.Folders);
                var items = content.Items.ToArray(); content.Items.Clear();
                if (section.Mode == AdditionalCommandsVisibility.Submenu)
                {
                    var submenu = new MenuFlyoutSubItem { Text = section.Label };
                    foreach (var entry in items) submenu.Items.Add(entry);
                    menu.Items.Add(submenu);
                }
                else foreach (var entry in items) menu.Items.Add(entry);
                menu.Items.Add(new MenuFlyoutSeparator());
            }
            if (Settings.Items.Any(i => i.IsDownloadedPin && i.CapturePin != CapturePinKind.None))
            {
                Menu(menu, "Unpin captures", () =>
                {
                    Settings.Items.RemoveAll(i => i.IsDownloadedPin && i.CapturePin != CapturePinKind.None);
                    Save(); Reconcile();
                }, "\uE77A");
                menu.Items.Add(new MenuFlyoutSeparator());
            }
            return;
        }
        if (item.AdditionalCommands == AdditionalCommandsVisibility.Hidden) return;
        var commands = new MenuFlyout();
        if (ShellService.IsTerminal(item)) AddTerminalProfiles(commands, item);
        else if (ShellService.IsSystemSettings(item)) AddSystemSettingsCategories(commands, item);
        else if (ShellService.IsMicrosoftStore(item)) AddStoreCategories(commands);
        else if (ShellService.IsSnippingTool(item)) AddSnippingToolCommands(commands);
        else if (ShellService.IsXbox(item)) AddXboxCommands(commands);
        else if (IsFileExplorer(item)) AddExplorerLocations(commands);
        while (commands.Items.LastOrDefault() is MenuFlyoutSeparator) commands.Items.RemoveAt(commands.Items.Count - 1);
        if (commands.Items.Count == 0) return;
        var entries = commands.Items.ToArray();
        commands.Items.Clear();
        if (item.AdditionalCommands == AdditionalCommandsVisibility.Submenu)
        {
            var more = new MenuFlyoutSubItem { Text = ShellService.IsSnippingTool(item) ? "Tasks" : "Navigate to" };
            foreach (var entry in entries) more.Items.Add(entry);
            menu.Items.Add(more);
        }
        else foreach (var entry in entries) menu.Items.Add(entry);
        menu.Items.Add(new MenuFlyoutSeparator());
    }
}
