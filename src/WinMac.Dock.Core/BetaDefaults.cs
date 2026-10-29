using System.Text.Json;

namespace WinMac.Dock.Core;
public static class BetaDefaults
{
    private sealed class Snapshot
    {
        public DockSettings Settings { get; set; } = new();
        public Dictionary<string, DockItem> SpecialItems { get; set; } = [];
    }
    private static Snapshot Read()
    {
        using var stream = typeof(BetaDefaults).Assembly.GetManifestResourceStream("WinMac.Dock.BetaDefaults.json")!;
        return JsonSerializer.Deserialize<Snapshot>(stream)!;
    }
    public static DockItem CreateItem(string key, string downloads, string windows, string assets, string localAppData)
    {
        var item = Read().SpecialItems[key];
        string Resolve(string value) => value.Replace("{Downloads}", downloads).Replace("{Windows}", windows).Replace("{Assets}", assets).Replace("{LocalAppData}", localAppData).Replace('/', Path.DirectorySeparatorChar);
        item.Id = Guid.NewGuid().ToString("N");
        // Preserve URI scheme separators while resolving only tokenized paths.
        if (item.Target.Contains('{')) item.Target = Resolve(item.Target);
        item.ExecutablePath = Resolve(item.ExecutablePath);
        // Keep asset references portable when settings are saved or the app is moved.
        return item;
    }
    public static DockSettings CreateSettings(string downloads, string windows, string assets, string localAppData)
    {
        var settings = Read().Settings;
        foreach (var key in new[] { "start", "|", "settings", "taskmanager", "|", "fileexplorer", "store", "|", "downloads", "recyclebin" })
            settings.Items.Add(key == "|" ? new DockItem { Kind = ItemKind.Separator, Name = "Separator" } : CreateItem(key, downloads, windows, assets, localAppData));
        settings.Validate();
        return settings;
    }
    public static void ApplyProperties(DockItem item, DockItem template)
    {
        item.AdditionalCommands = template.AdditionalCommands;
        item.ExplorerRecentCommands = template.ExplorerRecentCommands; item.ExplorerPinnedCommands = template.ExplorerPinnedCommands;
        item.CustomIcon = template.CustomIcon; item.DarkModeIcon = template.DarkModeIcon;
        item.RecycleEmptyIcon = template.RecycleEmptyIcon; item.RecycleFullIcon = template.RecycleFullIcon;
        item.RecycleEmptyDarkIcon = template.RecycleEmptyDarkIcon; item.RecycleFullDarkIcon = template.RecycleFullDarkIcon;
        item.RunAsAdministrator = template.RunAsAdministrator; item.ConfirmRecycleBinEmpty = template.ConfirmRecycleBinEmpty;
        item.PlayRecycleBinSounds = template.PlayRecycleBinSounds;
        item.RecycleBinSoundVolumePercent = template.RecycleBinSoundVolumePercent;
        item.ShowThumbnail = template.ShowThumbnail; item.Folder = template.Folder; item.ClickActions = template.ClickActions;
    }
}
