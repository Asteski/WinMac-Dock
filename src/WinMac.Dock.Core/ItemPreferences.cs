namespace WinMac.Dock.Core;

public static class ItemPreferences
{
    private static string Key(DockItem item, string? module) => module != null ? "module:" + module
        : !string.IsNullOrWhiteSpace(item.AppUserModelId) ? "app:" + item.AppUserModelId
        : $"{item.Kind}:" + Environment.ExpandEnvironmentVariables(item.Target).Trim().Trim('"').Replace('/', '\\');
    private static DockItem Copy(DockItem item) => item with { Folder = item.Folder with { }, Snipping = item.Snipping with { }, ClickActions = item.ClickActions is { } actions ? actions with { } : null };
    public static void Remember(DockSettings settings, DockItem item, string? module = null)
    {
        if (item.IsAutoUsbPin || item.IsDownloadedPin || item.Kind == ItemKind.Separator) return;
        settings.RememberedItemProperties[Key(item, module)] = Copy(item);
    }
    public static DockItem? Restore(DockSettings settings, DockItem item, string? module = null) =>
        settings.RememberedItemProperties.TryGetValue(Key(item, module), out var saved)
            ? Copy(saved) with { Id = item.Id, IsDownloadedPin = item.IsDownloadedPin } : null;
}
