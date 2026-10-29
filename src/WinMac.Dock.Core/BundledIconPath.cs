namespace WinMac.Dock.Core;
public static class BundledIconPath
{
    public static string Resolve(string path, string assets)
    {
        string resource = BundledIcons.Resolve(BundledIcons.Portable(path), assets);
        if (resource != path) return resource;
        if (path.StartsWith("{Assets}/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("{Assets}\\", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(assets, path[9..].Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(path)) return path;
        var normalized = path.Replace('\\', '/');
        int marker = normalized.LastIndexOf("/assets/", StringComparison.OrdinalIgnoreCase);
        if (marker >= 0)
        {
            var candidate = Path.GetFullPath(Path.Combine(assets, normalized[(marker + 8)..].Replace('/', Path.DirectorySeparatorChar)));
            if (candidate.StartsWith(Path.GetFullPath(assets) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate)) return candidate;
        }
        string? replacement = Path.GetFileName(normalized).ToLowerInvariant() switch
        {
            "recyclebin_black_empty.ico" or "trashempty.ico" => "recyclebin-recycleemptydarkicon.ico",
            "recyclebin_black_full.ico" or "trashfull.ico" => "recyclebin-recyclefulldarkicon.ico",
            _ => null
        };
        return replacement == null ? path : Path.Combine(assets, "defaults", replacement);
    }
}
