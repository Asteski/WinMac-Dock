namespace WinMac.Dock.Core;

// Controls the persisted shape only. Missing fields still receive model defaults
// when loading old settings or changing an item's kind/launch target.
internal static class ItemSettingsSerialization
{
    internal static bool Includes(DockItem item, string property, object? value)
    {
        bool application = item.Kind == ItemKind.Application;
        bool folder = item.Kind == ItemKind.Folder;
        bool file = item.Kind == ItemKind.File;
        bool recycle = item.Kind == ItemKind.RecycleBin;
        bool explorer = application && (Executable(item, "explorer.exe") || Equal(item.AppUserModelId, "Microsoft.Windows.Explorer"));
        bool edge = application && Executable(item, "msedge.exe") && !DockApplicationIdentity.IsBrowserHostedApp(item)
            && !item.Arguments.Contains("--app", StringComparison.OrdinalIgnoreCase);
        bool relevant = property switch
        {
            nameof(DockItem.EdgeFavoritesCommands) or nameof(DockItem.EdgeWorkspacesCommands) or nameof(DockItem.EdgeProfilesCommands) => edge,
            nameof(DockItem.ExplorerRecentCommands) or nameof(DockItem.ExplorerPinnedCommands) or nameof(DockItem.PinUsbDrives) => explorer,
            nameof(DockItem.AdditionalCommands) => application && HasAdditionalCommands(item),
            nameof(DockItem.Folder) => folder,
            nameof(DockItem.ShowThumbnail) => file || folder,
            nameof(DockItem.RecycleEmptyIcon) or nameof(DockItem.RecycleFullIcon)
                or nameof(DockItem.RecycleEmptyDarkIcon) or nameof(DockItem.RecycleFullDarkIcon)
                or nameof(DockItem.ConfirmRecycleBinEmpty) or nameof(DockItem.PlayRecycleBinSounds)
                or nameof(DockItem.RecycleBinSoundVolumePercent) => recycle,
            nameof(DockItem.Target) or nameof(DockItem.ExecutablePath) => application || file || folder,
            nameof(DockItem.Arguments) or nameof(DockItem.WorkingDirectory) or nameof(DockItem.RunAsAdministrator) => application || file,
            nameof(DockItem.AppUserModelId) => application,
            nameof(DockItem.IsAutoUsbPin) => folder && item.IsAutoUsbPin,
            nameof(DockItem.AttachedDriveKind) => folder && item.AttachedDriveKind != default,
            nameof(DockItem.IsDownloadedPin) => file && item.IsDownloadedPin,
            nameof(DockItem.CapturePin) => file && item.CapturePin != CapturePinKind.None,
            nameof(DockItem.Snipping) => application && (Prefix(item.AppUserModelId, "Microsoft.ScreenSketch_")
                || Prefix(item.Target, @"shell:AppsFolder\Microsoft.ScreenSketch_") || Prefix(item.Target, "ms-screensketch:") || Executable(item, "SnippingTool.exe")),
            nameof(DockItem.Id) or nameof(DockItem.Kind) or nameof(DockItem.Name) => true,
            _ => item.Kind != ItemKind.Separator
        };
        return relevant && value is not null && (value is not string text || text.Length > 0 || property == nameof(DockItem.Name));
    }

    private static bool HasAdditionalCommands(DockItem item) =>
        Prefix(item.AppUserModelId, "windows.immersivecontrolpanel") || Prefix(item.Target, "ms-settings:") || Executable(item, "SystemSettings.exe")
        || Prefix(item.AppUserModelId, "Microsoft.WindowsStore_") || Prefix(item.Target, "ms-windows-store:") || Executable(item, "WinStore.App.exe")
        || Prefix(item.AppUserModelId, "Microsoft.WindowsTerminal_") || Prefix(item.AppUserModelId, "Microsoft.WindowsTerminalPreview_")
        || Executable(item, "WindowsTerminal.exe") || Executable(item, "wt.exe")
        || Prefix(item.AppUserModelId, "Microsoft.GamingApp_") || Prefix(item.Target, @"shell:AppsFolder\Microsoft.GamingApp_")
        || Executable(item, "XboxPcApp.exe") || Executable(item, "XboxPcAppFT.exe")
        || Prefix(item.AppUserModelId, "Microsoft.ScreenSketch_") || Prefix(item.Target, @"shell:AppsFolder\Microsoft.ScreenSketch_")
        || Prefix(item.Target, "ms-screensketch:") || Executable(item, "SnippingTool.exe");

    private static bool Executable(DockItem item, string name) => Equal(FileName(item.Target), name) || Equal(FileName(item.ExecutablePath), name);
    private static string FileName(string path) => path.Replace('/', '\\').Split('\\').Last();
    private static bool Prefix(string text, string prefix) => text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    private static bool Equal(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
