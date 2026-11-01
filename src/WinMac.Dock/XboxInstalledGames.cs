using Microsoft.Win32;
using Windows.Management.Deployment;

namespace WinMac.Dock;

internal sealed record InstalledXboxGame(string Name, string AppId, string? Target = null, string Arguments = "", string? IconPath = null, string? WorkingDirectory = null)
{
    public string LaunchTarget => string.IsNullOrEmpty(AppId) ? Target ?? "" : "shell:AppsFolder\\" + AppId;
}
internal sealed record XboxGameScan(IReadOnlyList<InstalledXboxGame> Games, string? Error = null);

internal static class XboxInstalledGames
{
    private static readonly object gate = new();
    private static Task<XboxGameScan>? pending;
    private static DateTime refreshed;

    internal static Task<XboxGameScan> ReadAsync()
    {
        lock (gate)
        {
            if (pending == null || pending.IsCompleted && DateTime.UtcNow - refreshed > TimeSpan.FromSeconds(10))
            {
                refreshed = DateTime.UtcNow;
                pending = Task.Run(ScanAsync);
            }
            return pending;
        }
    }

    private static async Task<XboxGameScan> ScanAsync()
    {
        var games = new Dictionary<string, InstalledXboxGame>(StringComparer.OrdinalIgnoreCase);
        var repository = RepositoryPackages();
        AddRegisteredRepositoryGames(repository, games);
        ExternalInstalledGames.Add(games);
        try
        {
            foreach (var package in new PackageManager().FindPackagesForUser(""))
            {
                try
                {
                    if (package.IsFramework || package.IsResourcePackage || package.Id.Name.StartsWith("Microsoft.Xbox", StringComparison.OrdinalIgnoreCase)
                        || package.Id.Name.Equals("Microsoft.GamingApp", StringComparison.OrdinalIgnoreCase)) continue;
                    string root = package.InstalledLocation.Path;
                    if (!Directory.Exists(root)) continue;
                    XboxGameMetadata? metadata = null;
                    foreach (var config in new[] { Path.Combine(root, "MicrosoftGame.config"), Path.Combine(root, "Content", "MicrosoftGame.config") })
                    {
                        if (!File.Exists(config)) continue;
                        try { using var stream = File.OpenRead(config); metadata = XboxGameMetadata.Read(stream); }
                        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException) { }
                        if (metadata != null) break;
                    }
                    // Gaming Services records identify registered games even when
                    // their protected manifests cannot be read. Legacy UWP Xbox
                    // games can also advertise the Xbox services configuration.
                    bool game = repository.ContainsKey(package.Id.FullName) || metadata != null
                        || File.Exists(Path.Combine(root, "xboxservices.config")) || File.Exists(Path.Combine(root, "Assets", "xboxservices.config"));
                    if (!game) continue;
                    var apps = await package.GetAppListEntriesAsync();
                    var app = apps.FirstOrDefault(a => metadata != null && a.AppUserModelId.EndsWith("!" + metadata.ApplicationId, StringComparison.OrdinalIgnoreCase))
                        ?? apps.FirstOrDefault();
                    if (app == null || string.IsNullOrWhiteSpace(app.AppUserModelId)) continue; // DLC / hidden support packages have no launchable app.
                    string title = app.DisplayInfo.DisplayName;
                    if (string.IsNullOrWhiteSpace(title) || title.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) title = metadata?.DisplayName ?? "";
                    if (string.IsNullOrWhiteSpace(title)) title = package.DisplayName;
                    if (string.IsNullOrWhiteSpace(title)) continue;
                    games.TryAdd(package.Id.FamilyName, new(title, app.AppUserModelId));
                }
                catch (Exception ex) when (ex is not OutOfMemoryException) { Log.Write(ex); }
            }
            return new(games.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
        }
        catch (Exception ex) { Log.Write(ex); return new(games.Values.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).ToArray(), games.Count == 0 ? "Installed games could not be read" : null); }
    }

    private static void AddRegisteredRepositoryGames(IReadOnlyDictionary<string, string> repository, Dictionary<string, InstalledXboxGame> games)
    {
        // Read-only fallback also works when WinRT package enumeration is not
        // available. Require registration for this user as well as actual files.
        try
        {
            using var user = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (user == null) return;
            foreach (var (fullName, root) in repository)
            {
                try
                {
                    using var registration = user.OpenSubKey(fullName);
                    if (registration == null || !Directory.Exists(root)) continue;
                    foreach (var config in new[] { Path.Combine(root, "MicrosoftGame.config"), Path.Combine(root, "Content", "MicrosoftGame.config") })
                    {
                        if (!File.Exists(config)) continue;
                        using var stream = File.OpenRead(config);
                        var metadata = XboxGameMetadata.Read(stream);
                        if (metadata == null || !fullName.StartsWith(metadata.Identity + "_", StringComparison.OrdinalIgnoreCase)) continue;
                        int suffix = fullName.LastIndexOf('_');
                        if (suffix <= 0 || suffix == fullName.Length - 1) continue;
                        string family = metadata.Identity + fullName[suffix..];
                        string title = registration.GetValue("DisplayName") as string ?? "";
                        if (string.IsNullOrWhiteSpace(title) || title.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase) || title.StartsWith('@')) title = metadata.DisplayName;
                        if (string.IsNullOrWhiteSpace(title)) continue;
                        games.TryAdd(family, new(title, family + "!" + metadata.ApplicationId));
                        break;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or System.Xml.XmlException) { }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
    }

    private static Dictionary<string, string> RepositoryPackages()
    {
        var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var root = machine.OpenSubKey(@"SOFTWARE\Microsoft\GamingServices\PackageRepository\Root");
            if (root == null) return packages;
            foreach (var volume in root.GetSubKeyNames())
            {
                using var volumeKey = root.OpenSubKey(volume);
                if (volumeKey == null) continue;
                foreach (var name in volumeKey.GetSubKeyNames())
                {
                    using var entry = volumeKey.OpenSubKey(name);
                    if (entry?.GetValue("Package") is string package && entry.GetValue("Root") is string path && Directory.Exists(path)) packages.TryAdd(package, path);
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        return packages;
    }
}
