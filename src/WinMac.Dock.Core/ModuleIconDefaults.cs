namespace WinMac.Dock.Core;

public static class ModuleIconDefaults
{
    public static void Upgrade(DockItem item)
    {
        BundledIcons.Upgrade(item);
        if (item.Kind is not (ItemKind.Search or ItemKind.TaskView)) return;
        // Older module creation stored the shell fallback as a custom icon,
        // preventing the bundled module icon from being used in that theme.
        bool Legacy(string path) => !string.IsNullOrWhiteSpace(path) && path.Replace('\\', '/').Split('/').Last()
            .Equals(item.Kind == ItemKind.Search ? "imageres.dll,168" : "imageres.dll,0", StringComparison.OrdinalIgnoreCase);
        if (Legacy(item.CustomIcon)) item.CustomIcon = "";
        if (Legacy(item.DarkModeIcon)) item.DarkModeIcon = "";
    }
}
