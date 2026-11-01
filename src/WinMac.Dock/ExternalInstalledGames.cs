using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace WinMac.Dock;

internal static class ExternalInstalledGames
{
    private static readonly string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static readonly string common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    private static readonly string xboxArt = Path.Combine(local, "Packages", "Microsoft.GamingApp_8wekyb3d8bbwe", "LocalState", "ThirdPartyLibraries");
    internal static void Add(Dictionary<string, InstalledXboxGame> games)
    {
        Attempt(() => AddSteam(games));
        Attempt(() => AddBattleNet(games));
        Attempt(() => AddEa(games));
    }
    private static void Attempt(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidDataException
            or System.Xml.XmlException or RegexMatchTimeoutException or OverflowException or ArgumentException) { }
    }
    private static string ReadText(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > 4 * 1024 * 1024) throw new InvalidDataException("Launcher manifest is too large.");
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private static string? RegistryString(RegistryHive hive, RegistryView view, string path, string value)
    {
        try { using var root = RegistryKey.OpenBaseKey(hive, view); using var key = root.OpenSubKey(path); return key?.GetValue(value) as string; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { return null; }
    }
    private static string? CachedIcon(string provider, string id) => new[] { Path.Combine(xboxArt, provider, provider + "_" + id + ".png") }.FirstOrDefault(File.Exists);
    private static IEnumerable<string> Directories(string root)
    {
        try { return Directory.Exists(root) ? Directory.GetDirectories(root).Take(4096).ToArray() : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
    private static IEnumerable<string> Files(string root, string pattern)
    {
        try { return Directory.Exists(root) ? Directory.GetFiles(root, pattern).Take(4096).ToArray() : []; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return []; }
    }
    private static IEnumerable<string> SteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam") };
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        foreach (var candidate in new[] { RegistryString(RegistryHive.CurrentUser, view, @"Software\Valve\Steam", "SteamPath"), RegistryString(RegistryHive.LocalMachine, view, @"SOFTWARE\Valve\Steam", "InstallPath") })
            if (!string.IsNullOrWhiteSpace(candidate)) roots.Add(candidate);
        return roots.Where(Directory.Exists);
    }
    private static void AddSteam(Dictionary<string, InstalledXboxGame> games)
    {
        foreach (var root in SteamRoots())
        {
            var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { root };
            Attempt(() => { foreach (var path in LauncherGameMetadata.SteamLibraries(ReadText(Path.Combine(root, "steamapps", "libraryfolders.vdf")))) if (Path.IsPathFullyQualified(path) && Directory.Exists(path)) libraries.Add(path); });
            foreach (var library in libraries)
            foreach (var file in Files(Path.Combine(library, "steamapps"), "appmanifest_*.acf"))
                Attempt(() =>
                {
                    var game = LauncherGameMetadata.SteamGame(ReadText(file));
                    if (game == null || !Directory.Exists(Path.Combine(library, "steamapps", "common", game.InstallDirectory))) return;
                    games.TryAdd("steam:" + game.Id, new(game.Name, "", "steam://rungameid/" + game.Id, IconPath: CachedIcon("steam", game.Id)));
                });
        }
    }
    private static void AddBattleNet(Dictionary<string, InstalledXboxGame> games)
    {
        string? executable = null;
        string? command = RegistryString(RegistryHive.ClassesRoot, RegistryView.Registry64, @"battlenet\shell\open\command", "");
        if (command?.StartsWith('"') == true) { int end = command.IndexOf('"', 1); if (end > 1 && File.Exists(command[1..end])) executable = command[1..end]; }
        executable ??= new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Battle.net", "Battle.net.exe") }.FirstOrDefault(File.Exists);
        if (executable == null) return;
        foreach (var file in new[] { Path.Combine(common, "Battle.net", "Agent", "product.db"), Path.Combine(common, "Battle.net", "Agent", ".product.db") })
            Attempt(() =>
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length > 16 * 1024 * 1024) return;
                using var memory = new MemoryStream(); stream.CopyTo(memory);
                foreach (var game in LauncherGameMetadata.BattleNetGames(memory.ToArray()))
                {
                    string install = game.InstallDirectory.Replace('/', Path.DirectorySeparatorChar);
                    if (!Path.IsPathFullyQualified(install) || !Directory.Exists(install)) continue;
                    string code = game.Code.ToLowerInvariant();
                    // The agent uses lowercase IDs, while launch commands retain
                    // the historical case used by the Battle.net launcher.
                    string launchCode = code switch { "pro" => "Pro", "wow" => "WoW", "wow_classic" => "WoW_classic", "wow_classic_era" => "WoW_classic_era", "s2" => "S2", "s1" => "S1", "hero" => "Hero", "hsb" => "WTCG", "d3" => "D3", "fen" => "Fen", "osi" => "OSI", "w3" => "W3", "anbs" => "ANBS", _ => game.Code };
                    string name = code switch { "pro" => "Overwatch", "wow" => "World of Warcraft", "wow_classic" or "wow_classic_era" => "World of Warcraft Classic", "s2" => "StarCraft II", "s1" => "StarCraft Remastered", "hero" => "Heroes of the Storm", "hsb" => "Hearthstone", "d3" => "Diablo III", "fen" => "Diablo IV", "osi" => "Diablo II Resurrected", "w3" => "Warcraft III", "anbs" => "Diablo Immortal", _ => Path.GetFileName(install.TrimEnd(Path.DirectorySeparatorChar)) };
                    games.TryAdd("bnet:" + code, new(name, "", executable, "--exec=\"launch " + launchCode + "\"", CachedIcon("bnet", code), Path.GetDirectoryName(executable)));
                }
            });
    }
    private static void AddEa(Dictionary<string, InstalledXboxGame> games)
    {
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
        foreach (var folder in new[] { "EA Games", "Origin Games" })
            foreach (var directory in Directories(Path.Combine(drive.RootDirectory.FullName, folder))) directories.Add(directory);
        foreach (var folder in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "EA Games"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Origin Games") })
            foreach (var directory in Directories(folder)) directories.Add(directory);
        foreach (var view in new[] { RegistryView.Registry32, RegistryView.Registry64 })
        foreach (var branch in new[] { @"SOFTWARE\Origin Games", @"SOFTWARE\EA Games", @"SOFTWARE\BioWare", @"SOFTWARE\Respawn", @"SOFTWARE\Maxis", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall" })
            Attempt(() =>
            {
                using var machine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var parent = machine.OpenSubKey(branch);
                if (parent == null) return;
                foreach (var name in parent.GetSubKeyNames().Take(4096))
                    Attempt(() =>
                    {
                        using var key = parent.OpenSubKey(name);
                        if (key == null) return;
                        foreach (var value in new[] { "Install Dir", "InstallLocation", "Install Dir Local" })
                            if (key.GetValue(value) is string path && Directory.Exists(path) && File.Exists(Path.Combine(path, "__Installer", "installerdata.xml"))) directories.Add(path);
                    });
            });
        foreach (var directory in directories)
            Attempt(() =>
            {
                using var stream = File.OpenRead(Path.Combine(directory, "__Installer", "installerdata.xml"));
                var game = LauncherGameMetadata.EaGame(stream);
                if (game == null || !Directory.Exists(directory)) return;
                string? icon = Files(directory, "*.exe").FirstOrDefault();
                games.TryAdd("ea:" + game.Id, new(game.Name, "", "origin2://game/launch?offerIds=" + Uri.EscapeDataString(game.Id), IconPath: icon));
            });
    }
}
