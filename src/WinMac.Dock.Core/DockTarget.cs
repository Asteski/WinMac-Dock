namespace WinMac.Dock.Core;

public static class DockTarget
{
    public static bool IsMissing(DockItem item)
    {
        if (item.Kind is ItemKind.StartMenu or ItemKind.RecycleBin or ItemKind.Separator or ItemKind.TaskView or ItemKind.Search) return false;
        // Packaged applications activate by identity rather than their often
        // inaccessible or versioned WindowsApps executable path.
        if (item.Kind == ItemKind.Application && (item.AppUserModelId.Contains('!') || !string.IsNullOrWhiteSpace(item.AppUserModelId) && string.IsNullOrWhiteSpace(item.Target))) return false;
        string target = Environment.ExpandEnvironmentVariables(item.Target.Trim().Trim('"'));
        if (target.StartsWith("::", StringComparison.Ordinal)) return false;
        if (Uri.TryCreate(target, UriKind.Absolute, out var uri))
        {
            if (!uri.IsFile) return false;
            target = uri.LocalPath;
        }
        if (string.IsNullOrWhiteSpace(target)) return true;
        bool Exists(string path) => File.Exists(path) || Directory.Exists(path);
        bool exists = Exists(target);
        if (!exists && !Path.IsPathRooted(target) && !string.IsNullOrWhiteSpace(item.WorkingDirectory))
            exists = Exists(Path.Combine(Environment.ExpandEnvironmentVariables(item.WorkingDirectory), target));
        if (!exists && item.Kind == ItemKind.Application && !target.Contains('/') && !target.Contains('\\'))
        {
            var extensions = Path.HasExtension(target) ? new[] { "" } : new[] { "" }.Concat((Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD").Split(';')).ToArray();
            exists = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                .Any(directory => extensions.Any(extension => Exists(Path.Combine(directory.Trim('"'), target + extension))));
        }
        if (!exists) return true;
        if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.ExecutablePath))
        {
            var executable = Environment.ExpandEnvironmentVariables(item.ExecutablePath.Trim().Trim('"'));
            if (Path.IsPathFullyQualified(executable)) return !Exists(executable);
        }
        return false;
    }
}
