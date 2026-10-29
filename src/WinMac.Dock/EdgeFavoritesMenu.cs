using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinMac.Dock;
internal static partial class ShellService
{
    internal sealed record EdgeFavoriteProfile(string Directory, string Name, string UserData, IReadOnlyList<EdgeFavorite> Favorites, string? PicturePath = null);
    private static string ReadSharedText(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length > 16 * 1024 * 1024) throw new InvalidDataException("Favorites file is too large.");
        using var reader = new StreamReader(input); return reader.ReadToEnd();
    }
    internal static IReadOnlyList<EdgeFavoriteProfile> ReadEdgeFavorites(DockItem item, bool profilesOnly = false)
    {
        string? Option(string name)
        {
            var match = Regex.Match(item.Arguments, "--" + name + """=(?:"([^"]*)"|(\S+))""", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value : null;
        }
        string channel = item.ExecutablePath.Contains("Edge Beta", StringComparison.OrdinalIgnoreCase) ? "Edge Beta"
            : item.ExecutablePath.Contains("Edge Dev", StringComparison.OrdinalIgnoreCase) ? "Edge Dev"
            : item.ExecutablePath.Contains("Edge SxS", StringComparison.OrdinalIgnoreCase) ? "Edge SxS" : "Edge";
        string userData = Environment.ExpandEnvironmentVariables(Option("user-data-dir") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", channel, "User Data"));
        userData = Path.GetFullPath(userData);
        var profiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var identities = new Dictionary<string, EdgeProfileIdentity>(StringComparer.OrdinalIgnoreCase);
        string? selected = profilesOnly ? null : Option("profile-directory");
        try
        {
            var stateFile = Path.Combine(userData, "Local State");
            if (File.Exists(stateFile))
            {
                using var state = JsonDocument.Parse(ReadSharedText(stateFile));
                if (state.RootElement.TryGetProperty("profile", out var profile) && profile.TryGetProperty("info_cache", out var cache))
                    foreach (var entry in cache.EnumerateObject())
                    {
                        var identity = EdgeProfileIdentity.Parse(entry.Value, entry.Name);
                        identities[entry.Name] = identity;
                        profiles[entry.Name] = identity.Name;
                    }
            }
        }
        catch (Exception ex) { Log.Write(ex); }
        if (selected != null) profiles = new(StringComparer.OrdinalIgnoreCase) { [selected] = profiles.GetValueOrDefault(selected, selected) };
        else
        {
            if (!profiles.ContainsKey("Default")) profiles["Default"] = "Default";
            if (System.IO.Directory.Exists(userData))
                foreach (string directory in System.IO.Directory.EnumerateDirectories(userData, "Profile *"))
                    profiles.TryAdd(Path.GetFileName(directory), Path.GetFileName(directory));
        }
        var result = new List<EdgeFavoriteProfile>();
        foreach (var (directory, name) in profiles)
        {
            try
            {
                // Only profile directories within this Edge data directory qualify.
                string profilePath = Path.GetFullPath(Path.Combine(userData, directory));
                if (!profilePath.StartsWith(userData.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
                string? picturePath = null;
                var pictureFile = identities.GetValueOrDefault(directory)?.PictureFile;
                if (!string.IsNullOrEmpty(pictureFile) && File.Exists(Path.Combine(profilePath, pictureFile))) picturePath = Path.Combine(profilePath, pictureFile);
                else if (File.Exists(Path.Combine(profilePath, "Edge Profile.ico"))) picturePath = Path.Combine(profilePath, "Edge Profile.ico");
                if (profilesOnly)
                {
                    // Profiles without bookmarks still belong in the launcher.
                    // Ignore stale profile entries and never inspect their browsing data.
                    if (System.IO.Directory.Exists(profilePath)) result.Add(new(directory, name, userData, [], picturePath));
                    continue;
                }
                string file = Path.Combine(profilePath, "Bookmarks");
                var favorites = (File.Exists(file) ? EdgeFavorites.Parse(ReadSharedText(file)) : []).ToList();
                // Workspace bookmark folders are not launchable workspace IDs.
                string workspaceCache = Path.Combine(profilePath, "Workspaces", "WorkspacesCache");
                try
                {
                    if (File.Exists(workspaceCache))
                    {
                        var workspaces = EdgeFavorites.ParseWorkspaces(ReadSharedText(workspaceCache));
                        if (workspaces.Count > 0) favorites.Add(new("Workspaces", null, workspaces) { IsWorkspaceGroup = true });
                    }
                }
                catch (Exception ex) { Log.Write(ex); }
                if (favorites.Count > 0) result.Add(new(directory, name, userData, favorites, picturePath));
            }
            catch (Exception ex) { Log.Write(ex); }
        }
        return result;
    }
    internal static DockItem EdgeWorkspaceLaunchItem(DockItem item, EdgeFavoriteProfile profile, string workspaceId)
    {
        if (!Guid.TryParse(workspaceId, out var id)) throw new InvalidOperationException("This workspace has an invalid identity.");
        string executable = new[] { item.ExecutablePath, item.Target }.FirstOrDefault(path => Path.GetFileName(path).Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The Edge executable was not found.");
        return new DockItem { Kind = ItemKind.Application, Target = executable, ExecutablePath = executable,
            Arguments = string.Join(" ", new[] { "--user-data-dir=" + profile.UserData, "--profile-directory=" + profile.Directory, "--launch-workspace=" + id }.Select(EdgeFavorites.QuoteArgument)) };
    }
    internal static DockItem EdgeFavoriteLaunchItem(DockItem item, EdgeFavoriteProfile profile, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "file" or "edge")) throw new InvalidOperationException("This favorite has an unsupported address.");
        string executable = new[] { item.ExecutablePath, item.Target }.FirstOrDefault(path => Path.GetFileName(path).Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The Edge executable was not found.");
        return new DockItem { Kind = ItemKind.Application, Target = executable, ExecutablePath = executable,
            Arguments = string.Join(" ", new[] { "--user-data-dir=" + profile.UserData, "--profile-directory=" + profile.Directory, uri.AbsoluteUri }.Select(EdgeFavorites.QuoteArgument)) };
    }
}
internal sealed partial class DockWindow
{
    private void AddEdgeFavorites(MenuFlyout menu, DockItem item, bool workspacesOnly = false)
    {
        var visibility = workspacesOnly ? item.EdgeWorkspacesCommands : item.EdgeFavoritesCommands;
        if (visibility == AdditionalCommandsVisibility.Hidden) return;
        var entries = new List<MenuFlyoutItemBase>();
        MenuFlyoutItemBase Entry(EdgeFavorite favorite, ShellService.EdgeFavoriteProfile profile, bool isWorkspace = false)
        {
            if (favorite.WorkspaceId is { } workspaceId)
            {
                var workspace = new MenuFlyoutItem { Text = favorite.Name, Icon = Glyph("\uE81E") };
                workspace.Click += (_, _) => TryAction(() => ShellService.LaunchWithoutElevation(ShellService.EdgeWorkspaceLaunchItem(item, profile, workspaceId)));
                return workspace;
            }
            if (favorite.Url == null)
            {
                var folder = new MenuFlyoutSubItem { Text = favorite.Name, Icon = Glyph(visibility == AdditionalCommandsVisibility.MainMenu && favorite.IsFavoritesBar ? "\uE728" : isWorkspace || favorite.Name.Equals("Workspaces", StringComparison.OrdinalIgnoreCase) ? "\uE81E" : "\uE8B7") };
                foreach (var child in favorite.Children) folder.Items.Add(Entry(child, profile, favorite.Name.Equals("Workspaces", StringComparison.OrdinalIgnoreCase)));
                return folder;
            }
            var link = new MenuFlyoutItem { Text = favorite.Name, Icon = Glyph(isWorkspace ? "\uE81E" : "\uE734") };
            ToolTipService.SetToolTip(link, favorite.Name + "\n" + favorite.Url);
            link.Click += (_, _) => TryAction(() => ShellService.LaunchWithoutElevation(ShellService.EdgeFavoriteLaunchItem(item, profile, favorite.Url)));
            return link;
        }
        try
        {
            var profiles = ShellService.ReadEdgeFavorites(item).Select(profile => profile with
            {
                Favorites = EdgeFavorites.Section(profile.Favorites, workspacesOnly)
            }).Where(profile => profile.Favorites.Count > 0).ToArray();
            foreach (var profile in profiles)
            {
                var favorites = profile.Favorites.Select(favorite => Entry(favorite, profile)).ToArray();
                if (profiles.Length == 1) entries.AddRange(favorites);
                else
                {
                    var group = new MenuFlyoutSubItem { Text = profile.Name, Icon = Glyph("\uE77B") };
                    foreach (var favorite in favorites) group.Items.Add(favorite);
                    entries.Add(group);
                }
            }
        }
        catch (Exception ex) { Log.Write(ex); }
        if (entries.Count == 0) entries.Add(new MenuFlyoutItem { Text = workspacesOnly ? "No workspaces found" : "No favorites found", IsEnabled = false });
        if (visibility == AdditionalCommandsVisibility.Submenu)
        {
            var favorites = new MenuFlyoutSubItem { Text = workspacesOnly ? "Workspaces" : "Favorites", Icon = Glyph(workspacesOnly ? "\uE81E" : "\uE728") };
            foreach (var entry in entries) favorites.Items.Add(entry);
            menu.Items.Add(favorites);
        }
        else
        {
            menu.Items.Add(new MenuFlyoutItem { Text = workspacesOnly ? "Workspaces" : "Favorites", IsEnabled = false });
            foreach (var entry in entries) menu.Items.Add(entry);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
    }
    private void CheckEdgeFavorites()
    {
        Exception? failure = null; int profiles = 0, links = 0;
        try
        {
            var item = Settings.Items.FirstOrDefault(ShellService.IsMicrosoftEdge) ?? new DockItem { Kind = ItemKind.Application, Target = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe" };
            var data = ShellService.ReadEdgeFavorites(item); profiles = data.Count;
            int Count(EdgeFavorite entry) => entry.WorkspaceId != null || entry.Url != null ? 1 : entry.Children.Sum(Count);
            links = data.Sum(profile => EdgeFavorites.Section(profile.Favorites, false).Sum(Count));
            foreach (var mode in Enum.GetValues<AdditionalCommandsVisibility>())
            {
                var menu = new MenuFlyout(); AddEdgeFavorites(menu, item with { EdgeFavoritesCommands = mode });
                bool submenu = menu.Items.OfType<MenuFlyoutSubItem>().Any(entry => entry.Text == "Favorites");
                if (mode == AdditionalCommandsVisibility.Hidden ? menu.Items.Count != 0 : mode == AdditionalCommandsVisibility.Submenu ? !submenu : submenu)
                    throw new InvalidOperationException("Favorites visibility mode was not applied.");
                if (mode != AdditionalCommandsVisibility.Hidden && links > 0 && MenuItems(menu.Items).Count(entry => entry.IsEnabled) != links)
                    throw new InvalidOperationException("Favorites menu omitted a saved link.");
            }
            IEnumerable<EdgeFavorite> Urls(IEnumerable<EdgeFavorite> entries) => entries.SelectMany(entry => entry.Url != null ? new[] { entry } : Urls(entry.Children));
            var urlProfile = data.FirstOrDefault(profile => Urls(profile.Favorites).Any());
            if (urlProfile != null)
            {
                var profile = urlProfile; var favorite = Urls(profile.Favorites).First();
                var plan = ShellService.LaunchInfo(ShellService.EdgeFavoriteLaunchItem(item, profile, favorite.Url!));
                if (plan.Verb == "runas" || !plan.Arguments.Contains(profile.Directory) || !plan.Arguments.Contains(favorite.Url!)) throw new InvalidOperationException("Favorite launch lost its profile or URL.");
            }
            var ordinary = item with { EdgeFavoritesCommands = AdditionalCommandsVisibility.Hidden };
            var labels = ItemMenu(new(ordinary, null, true)).Items.OfType<MenuFlyoutItem>().Select(entry => entry.Text);
            if (!labels.Contains("Open Edge") || labels.Contains("Open")) throw new InvalidOperationException("Edge Open label was not updated.");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "edge-favorites-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Profiles = profiles, Links = links }));
    }
}
