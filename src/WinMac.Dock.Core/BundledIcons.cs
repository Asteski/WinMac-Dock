namespace WinMac.Dock.Core;

public static class BundledIcons
{
    public const string Start = "{Icons},-101";
    public const string Search = "{Icons},-102";
    public const string TaskViewLight = "{Icons},-103";
    public const string TaskViewDark = "{Icons},-104";
    public const string RecycleEmptyDark = "{Icons},-105";
    public const string RecycleFullDark = "{Icons},-106";
    public const string Application = "{Exe},-1";
    public const string Tray = "{Exe},-2";
    public const string MenuLight = "{Exe},-3";
    public const string MenuDark = "{Exe},-4";

    public static string Resolve(string path, string assets)
    {
        string root = Path.GetDirectoryName(Path.GetFullPath(assets))!;
        if (path.StartsWith("{Icons},", StringComparison.OrdinalIgnoreCase)) return Path.Combine(root, "WinMac.Dock.Icons.dll") + path[7..];
        if (path.StartsWith("{Exe},", StringComparison.OrdinalIgnoreCase)) return Path.Combine(root, "WinMac.Dock.exe") + path[5..];
        return path;
    }

    public static string Portable(string path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? "";
        var normalized = path.Replace('\\', '/');
        int comma = normalized.LastIndexOf(',');
        if (comma > 0 && int.TryParse(normalized[(comma + 1)..], out int resource))
        {
            string library = normalized[..comma].Split('/').Last();
            if (library.Equals("WinMac.Dock.Icons.dll", StringComparison.OrdinalIgnoreCase))
            {
                if (resource >= 0 && resource < 8) resource = -(101 + resource);
                if (resource <= -101 && resource >= -108) return "{Icons}," + resource;
            }
            if (library.Equals("WinMac.Dock.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (resource >= 0 && resource < 4) resource = -(1 + resource);
                if (resource <= -1 && resource >= -4) return "{Exe}," + resource;
            }
        }
        // Only migrate paths identifying bundled assets; leave external custom icons alone.
        bool bundled = normalized.StartsWith("{Assets}/", StringComparison.OrdinalIgnoreCase) || normalized.Contains("/assets/", StringComparison.OrdinalIgnoreCase);
        string name = normalized.Split('/').Last().ToLowerInvariant();
        bool missingLegacyAlias = !File.Exists(path) && name is ("recyclebin_black_empty.ico" or "recyclebin_black_full.ico" or "trashempty.ico" or "trashfull.ico");
        return (bundled || missingLegacyAlias) ? name switch
        {
            "start.ico" => Start, "search.ico" => Search,
            "taskview_light.ico" => TaskViewLight, "taskview_dark.ico" => TaskViewDark,
            "recyclebin-recycleemptydarkicon.ico" or "recyclebin_black_empty.ico" or "trashempty.ico" => RecycleEmptyDark,
            "recyclebin-recyclefulldarkicon.ico" or "recyclebin_black_full.ico" or "trashfull.ico" => RecycleFullDark,
            "downloads-badge.ico" => "{Icons},-107", "downloads-file-overlay.ico" => "{Icons},-108",
            "default.ico" => Application, "tray.ico" => Tray,
            "winmac_icon_menu_light.ico" => MenuLight, "winmac_icon_menu_dark.ico" => MenuDark,
            _ => path
        } : path;
    }
    public static void Upgrade(DockItem item)
    {
        item.CustomIcon = Portable(item.CustomIcon); item.DarkModeIcon = Portable(item.DarkModeIcon);
        item.RecycleEmptyIcon = Portable(item.RecycleEmptyIcon); item.RecycleFullIcon = Portable(item.RecycleFullIcon);
        item.RecycleEmptyDarkIcon = Portable(item.RecycleEmptyDarkIcon); item.RecycleFullDarkIcon = Portable(item.RecycleFullDarkIcon);
    }
}
