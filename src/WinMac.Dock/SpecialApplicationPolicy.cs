namespace WinMac.Dock;
internal static partial class ShellService
{
    internal static bool IsTaskManager(DockItem item) =>
        Path.GetFileName(item.Target).Equals("Taskmgr.exe", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(item.ExecutablePath).Equals("Taskmgr.exe", StringComparison.OrdinalIgnoreCase)
        || item.AppUserModelId.Equals("Microsoft.Windows.TaskManager", StringComparison.OrdinalIgnoreCase);

    internal static bool IsTerminal(DockItem item) =>
        item.AppUserModelId.StartsWith("Microsoft.WindowsTerminal_", StringComparison.OrdinalIgnoreCase)
        || item.AppUserModelId.StartsWith("Microsoft.WindowsTerminalPreview_", StringComparison.OrdinalIgnoreCase)
        || IsTerminalExecutable(item.Target) || IsTerminalExecutable(item.ExecutablePath);

    private static bool IsTerminalExecutable(string path) => Path.GetFileName(path).Equals("WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(path).Equals("wt.exe", StringComparison.OrdinalIgnoreCase);

    internal static bool IsMicrosoftStore(DockItem item) => item.AppUserModelId.StartsWith("Microsoft.WindowsStore_", StringComparison.OrdinalIgnoreCase)
        || item.Target.StartsWith("ms-windows-store:", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(item.ExecutablePath).Equals("WinStore.App.exe", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(item.Target).Equals("WinStore.App.exe", StringComparison.OrdinalIgnoreCase);
    internal static bool SupportsAdministratorLaunch(DockItem item) => item.Kind is not (ItemKind.TaskView or ItemKind.Search)
        && !IsSystemSettings(item) && !IsTaskManager(item) && !IsMicrosoftStore(item) && !IsMicrosoftEdge(item) && !IsXbox(item) && !DockWindow.IsFileExplorer(item);
    internal static bool OnlyAlwaysOnTopOptions(DockItem item) => IsSystemSettings(item) || IsTaskManager(item) || IsMicrosoftStore(item) || IsXbox(item);
}
