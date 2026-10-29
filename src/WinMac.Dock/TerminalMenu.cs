using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal static partial class ShellService
{
    internal sealed record TerminalProfile(string Name, string Selector, string? Icon = null);
    internal static IReadOnlyList<TerminalProfile> TerminalProfiles(DockItem item)
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var family = item.AppUserModelId.Split('!')[0];
            if (!family.StartsWith("Microsoft.WindowsTerminal", StringComparison.OrdinalIgnoreCase))
                family = item.Target.Contains("WindowsTerminalPreview", StringComparison.OrdinalIgnoreCase) ? "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe" : "Microsoft.WindowsTerminal_8wekyb3d8bbwe";
            var paths = new[] { Path.Combine(local, "Packages", family, "LocalState", "settings.json"), Path.Combine(local, "Microsoft", "Windows Terminal", "settings.json") };
            string? path = paths.FirstOrDefault(File.Exists);
            if (path == null) return [];
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (!document.RootElement.TryGetProperty("profiles", out var profiles)) return [];
            string? defaultIcon = profiles.ValueKind == JsonValueKind.Object && profiles.TryGetProperty("defaults", out var defaults) && defaults.TryGetProperty("icon", out var inheritedIcon) && inheritedIcon.ValueKind == JsonValueKind.String ? inheritedIcon.GetString() : null;
            var packageRoot = new Windows.Management.Deployment.PackageManager().FindPackagesForUser("").FirstOrDefault(p => p.Id.FamilyName.Equals(family, StringComparison.OrdinalIgnoreCase))?.InstalledLocation.Path;
            if (profiles.ValueKind == JsonValueKind.Object && !profiles.TryGetProperty("list", out profiles)) return [];
            if (profiles.ValueKind != JsonValueKind.Array) return [];
            var result = new List<TerminalProfile>();
            foreach (var profile in profiles.EnumerateArray())
            {
                if (profile.ValueKind != JsonValueKind.Object || profile.TryGetProperty("hidden", out var hidden) && hidden.ValueKind == JsonValueKind.True) continue;
                if (!profile.TryGetProperty("name", out var nameValue) || nameValue.ValueKind != JsonValueKind.String) continue;
                string name = nameValue.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(name)) continue;
                string selector = profile.TryGetProperty("guid", out var id) && id.ValueKind == JsonValueKind.String && Guid.TryParse(id.GetString(), out var guid) ? guid.ToString("B") : name;
                string? icon = profile.TryGetProperty("icon", out var iconValue) && iconValue.ValueKind == JsonValueKind.String ? iconValue.GetString() : defaultIcon;
                if (icon == null)
                {
                    string stock = profile.TryGetProperty("source", out var source) && source.GetString() is "Windows.Terminal.Wsl" or "Microsoft.WSL" ? "{9acb9455-ca41-5af7-950f-6bca1bc9722f}" : selector;
                    icon = "ms-appx:///ProfileIcons/" + stock + ".png";
                }
                result.Add(new(name, selector, ResolveTerminalIcon(icon, path, packageRoot)));
            }
            return result.DistinctBy(p => p.Selector, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { Log.Write(ex); return []; }
    }
    private static string? ResolveTerminalIcon(string icon, string settingsPath, string? packageRoot)
    {
        if (string.IsNullOrWhiteSpace(icon) || icon.Equals("none", StringComparison.OrdinalIgnoreCase)) return "";
        icon = Environment.ExpandEnvironmentVariables(icon);
        if (icon.StartsWith("ms-appx:///", StringComparison.OrdinalIgnoreCase))
        {
            if (packageRoot == null) return null;
            icon = Path.Combine(packageRoot, Uri.UnescapeDataString(icon[11..]).Replace('/', Path.DirectorySeparatorChar));
        }
        else if (icon.StartsWith("ms-appdata:///", StringComparison.OrdinalIgnoreCase))
        {
            var relative = icon[14..];
            int slash = relative.IndexOf('/');
            if (slash < 0) return null;
            string folder = relative[..slash].ToLowerInvariant() switch { "local" => "LocalState", "roaming" => "RoamingState", "temp" => "TempState", _ => "" };
            if (folder.Length == 0) return null;
            icon = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(settingsPath))!, folder, Uri.UnescapeDataString(relative[(slash + 1)..]));
        }
        else if (Uri.TryCreate(icon, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http" or "file") return icon;
        else if (!Path.IsPathRooted(icon))
        {
            // Terminal also accepts a short glyph string instead of an image.
            if (!icon.Contains('.') && !icon.Contains('/') && !icon.Contains('\\')) return icon;
            icon = Path.Combine(Path.GetDirectoryName(settingsPath)!, icon);
        }
        if (File.Exists(icon)) return icon;
        string directory = Path.GetDirectoryName(icon) ?? "";
        return Directory.Exists(directory) ? Directory.EnumerateFiles(directory, Path.GetFileNameWithoutExtension(icon) + ".scale-*" + Path.GetExtension(icon)).OrderByDescending(p => p.Contains("scale-200")).FirstOrDefault() : null;
    }
    internal static ProcessStartInfo TerminalLaunchInfo(DockItem item, bool newWindow, string? profile = null)
    {
        string executable = new[] { item.ExecutablePath, item.Target }.FirstOrDefault(p => IsTerminalExecutable(p) && File.Exists(p)) ?? "";
        // Packaged Terminal must be activated through its execution alias;
        // directly starting WindowsApps\...\WindowsTerminal.exe can be denied.
        string windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        if (item.AppUserModelId.StartsWith("Microsoft.WindowsTerminal", StringComparison.OrdinalIgnoreCase))
        {
            string family = item.AppUserModelId.Split('!')[0];
            string alias = Path.Combine(windowsApps, family, "wt.exe");
            executable = File.Exists(alias) ? alias : Path.Combine(windowsApps, "wt.exe");
        }
        else if (executable.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) && !executable.StartsWith(windowsApps + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) executable = "";
        if (executable.Length == 0) executable = "wt.exe";
        // Do not override startingDirectory: Terminal resolves it from the profile.
        // Home is also a sensible fallback for profiles that inherit the launcher's CWD.
        var start = new ProcessStartInfo(executable) { UseShellExecute = true, Verb = item.RunAsAdministrator ? "runas" : "", WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        start.ArgumentList.Add("--window"); start.ArgumentList.Add(newWindow ? "new" : "0"); start.ArgumentList.Add("new-tab");
        if (profile != null) { start.ArgumentList.Add("--profile"); start.ArgumentList.Add(profile.Replace(";", "\\;")); }
        return start;
    }
}
internal sealed partial class DockWindow
{
    private void OpenTerminal(DockItem item, bool newWindow, string? profile = null) => TryAction(() =>
    {
        using var process = Process.Start(ShellService.TerminalLaunchInfo(item, newWindow, profile));
        var current = buttons.FirstOrDefault(b => b.Entry.Item.Id == item.Id);
        if (current.Button != null) Bounce(current.Icon);
    });
    private bool AddTerminalProfiles(MenuFlyout menu, DockItem item)
    {
        var profiles = ShellService.TerminalProfiles(item);
        if (profiles.Count == 0) return false;
        var submenu = new MenuFlyoutSubItem { Text = "Profiles", Icon = Glyph("\uEC6C") };
        foreach (var profile in profiles)
        {
            IconElement? icon = profile.Icon == "" ? null : Glyph("\uE756");
            if (!string.IsNullOrEmpty(profile.Icon))
            {
                if (Uri.TryCreate(profile.Icon, UriKind.Absolute, out var uri))
                    icon = new ImageIcon { Source = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(uri) };
                else icon = Glyph(profile.Icon);
            }
            var entry = new MenuFlyoutItem { Text = profile.Name, Icon = icon };
            entry.Click += (_, _) => OpenTerminal(item, !tracker.Read().Any(w => TaskReconciler.Matches(item, w)), profile.Selector);
            submenu.Items.Add(entry);
        }
        if (item.AdditionalCommands == AdditionalCommandsVisibility.MainMenu)
        {
            menu.Items.Add(new MenuFlyoutItem { Text = "Profiles", IsEnabled = false });
            var commands = submenu.Items.ToArray(); submenu.Items.Clear();
            foreach (var command in commands) menu.Items.Add(command);
        }
        else menu.Items.Add(submenu);
        return true;
    }
}
