namespace WinMac.Dock.Core;

public static class DockApplicationIdentity
{
    public static bool IsBrowserHostedApp(DockItem item) => item.Kind == ItemKind.Application
        && (item.AppUserModelId.Contains('!') || item.AppUserModelId.Contains("_crx_", StringComparison.OrdinalIgnoreCase));

    public static DockItem NormalizePin(DockItem item)
    {
        if (!IsBrowserHostedApp(item) || !string.IsNullOrWhiteSpace(item.Arguments)) return item;
        string target = item.Target.Trim().Trim('"');
        string executable = target.Replace('/', '\\').Split('\\').Last();
        // A running browser-hosted app supplies the browser's process path,
        // but only its registered identity can reopen that particular app.
        if (executable.Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)
            || executable.Equals("chrome.exe", StringComparison.OrdinalIgnoreCase)
            || executable.Equals("brave.exe", StringComparison.OrdinalIgnoreCase)
            || executable.Equals("vivaldi.exe", StringComparison.OrdinalIgnoreCase)
            || executable.Equals("opera.exe", StringComparison.OrdinalIgnoreCase))
            return item with { Target = "", ExecutablePath = "", WorkingDirectory = "", RunAsAdministrator = false };
        return item;
    }
}
