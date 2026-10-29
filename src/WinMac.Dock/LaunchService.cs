using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace WinMac.Dock;
internal static partial class ShellService
{
    public static void LaunchWithoutElevation(DockItem item)
    {
        if (IsSystemSettings(item)) { Launch(item); return; }
        if (item.Kind == ItemKind.StartMenu) { Native.OpenStartMenu(); return; }
        if (item.Kind is ItemKind.TaskView or ItemKind.Search) { Native.OpenModule(item.Kind); return; }
        var ordinary = item with { RunAsAdministrator = false };
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        if (!new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
        { Launch(ordinary); return; }
        // Use the desktop's existing Shell dispatcher when the dock itself is
        // elevated, so the one-time override does not inherit the dock's token.
        var start = LaunchInfo(ordinary);
        object? windows = null, desktop = null, document = null, application = null;
        try
        {
            windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"))!);
            object location = 0, root = 0;
            desktop = ((dynamic)windows!).FindWindowSW(ref location, ref root, 8, out int handle, 1);
            if (desktop == null) throw new InvalidOperationException("Windows Explorer is not available to open this application without elevation.");
            document = ((dynamic)desktop).Document;
            application = ((dynamic)document!).Application;
            ((dynamic)application!).ShellExecute(start.FileName, start.Arguments, start.WorkingDirectory, "open", 1);
        }
        finally { Release(application); Release(document); Release(desktop); Release(windows); }
    }
    internal static ProcessStartInfo LaunchInfo(DockItem item, bool forceAdministrator = false)
    {
        item = DockApplicationIdentity.NormalizePin(item);
        if (IsSystemSettings(item)) return new ProcessStartInfo("ms-settings:") { UseShellExecute = true };
        if (IsMicrosoftStore(item)) return new ProcessStartInfo("ms-windows-store://home") { UseShellExecute = true };
        bool elevated = ShellService.SupportsAdministratorLaunch(item) && (forceAdministrator || item.RunAsAdministrator);
        string target = Environment.ExpandEnvironmentVariables(item.Target.Trim().Trim('"'));
        string arguments = item.Arguments, directory = item.WorkingDirectory;
        string appId = item.AppUserModelId;
        if (elevated && target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var shortcut = FromPath(target);
            target = shortcut.ExecutablePath;
            if (string.IsNullOrWhiteSpace(arguments)) arguments = shortcut.Arguments;
            if (string.IsNullOrWhiteSpace(directory)) directory = shortcut.WorkingDirectory;
            if (string.IsNullOrEmpty(appId)) appId = shortcut.AppUserModelId;
        }
        bool packaged = appId.Contains('!') && (!File.Exists(target) || target.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(target), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase));
        if (elevated && packaged)
        {
            target = ElevationAlias(appId) ?? throw new InvalidOperationException($"{item.Name} does not expose an enabled application alias for administrator launch. Windows may not support running this application as administrator.");
            if (appId.StartsWith("Microsoft.WindowsTerminal", StringComparison.OrdinalIgnoreCase)) arguments = "--window new " + arguments;
        }
        else if (!elevated && !string.IsNullOrEmpty(appId) && (!File.Exists(target)
            || target.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(target), "ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase)))
        { target = "shell:AppsFolder\\" + appId; }
        else if (!elevated && target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) arguments = "";
        if (item.Kind == ItemKind.RecycleBin) target = "shell:RecycleBinFolder";
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("This item has no launch target. Choose a target in Properties.");
        return new ProcessStartInfo(target, arguments) { UseShellExecute = true, WorkingDirectory = directory, Verb = elevated ? "runas" : "" };
    }
    private static string? ElevationAlias(string appId)
    {
        var parts = appId.Split('!', 2);
        var package = new Windows.Management.Deployment.PackageManager().FindPackagesForUser("")
            .FirstOrDefault(p => p.Id.FamilyName.Equals(parts[0], StringComparison.OrdinalIgnoreCase));
        if (package == null) return null;
        var manifest = XDocument.Load(Path.Combine(package.InstalledLocation.Path, "AppxManifest.xml"));
        var app = manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "Application" && (string?)e.Attribute("Id") == parts[1]);
        if (app == null) return null;
        foreach (var alias in app.Descendants().Where(e => e.Name.LocalName == "ExecutionAlias").Select(e => (string?)e.Attribute("Alias")))
        {
            if (string.IsNullOrEmpty(alias) || Path.GetFileName(alias) != alias) continue;
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", parts[0], alias);
            if (File.Exists(path)) return path;
        }
        return null;
    }
    internal static bool? WindowIsElevated(nint window)
    {
        Native.GetWindowThreadProcessId(window, out uint pid);
        nint process = OpenProcess(0x1000, false, pid);
        if (process == 0) return null;
        try
        {
            if (!OpenProcessToken(process, 8, out var token)) return null;
            try { return GetTokenInformation(token, 20, out int elevation, sizeof(int), out _) ? elevation != 0 : null; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(nint token, int information, out int elevation, int length, out int returned);
}
