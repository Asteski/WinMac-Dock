namespace WinMac.Dock;

internal static class ModuleIconPresentation
{
    private static readonly RetryingTaskCache<(bool Dark, bool Coloured), Microsoft.UI.Xaml.Media.ImageSource> modernArtwork = new(TimeSpan.FromSeconds(5));
    internal static Task<Microsoft.UI.Xaml.Media.ImageSource?> ModernArtworkAsync(nint parent, bool dark, bool coloured = false)
    {
        var key = (dark, coloured);
        return modernArtwork.GetOrCreate(key, Capture);
        async Task<Microsoft.UI.Xaml.Media.ImageSource?> Capture()
        {
            try
            {
                if (coloured)
                {
                    var original = await ModernArtworkAsync(parent, dark);
                    return original == null ? null : await IconService.ColourSearchArtwork(original);
                }
                var bitmap = await ShellAnimatedIcon.CaptureArtworkAsync(parent, ItemKind.Search, dark, true, 256);
                return bitmap == null ? null : await IconService.FromSoftwareBitmap(bitmap, 256);
            }
            catch (Exception ex) { Log.Write(ex); return null; }
        }
    }
    internal static bool IsModern(DockItem item) => item.Kind == ItemKind.Search && item.ModuleIconStyle == "modern";
    internal static string[] Styles(ItemKind kind) => kind == ItemKind.StartMenu
        ? ["default", "mono-rounded", "mono-squared", "colour-rounded", "colour-squared", "custom"]
        : kind == ItemKind.Search ? ["default", "modern", "custom"] : ["default", "custom"];
    internal static string[] Labels(ItemKind kind) => kind == ItemKind.StartMenu
        ? ["Default", "Monochrome rounded", "Monochrome squared", "Colourful rounded", "Colourful squared", "Custom"]
        : kind == ItemKind.Search ? ["Default", "Modern Search", "Custom"] : ["Default", "Custom"];
    internal static string SelectedStyle(DockItem item) => string.IsNullOrEmpty(item.ModuleIconStyle)
        ? UsesPreset(item) ? "default" : "custom" : item.ModuleIconStyle;
    internal static bool Animated(string style) => style is "default" or "modern";
    internal static bool IsModule(DockItem item) => item.Kind is ItemKind.StartMenu or ItemKind.Search or ItemKind.TaskView;
    internal static bool UsesPreset(DockItem item) => IsModule(item) && item.ModuleIconStyle != "custom"
        && (!string.IsNullOrEmpty(item.ModuleIconStyle) || (Bundled(item.CustomIcon) && Bundled(item.DarkModeIcon)));
    private static bool Bundled(string path)
    {
        string portable = BundledIcons.Portable(path);
        return string.IsNullOrEmpty(portable) || portable == BundledIcons.Start || portable == BundledIcons.Search
            || portable == BundledIcons.TaskViewLight || portable == BundledIcons.TaskViewDark;
    }
    internal static string PathFor(DockItem item, bool dark)
    {
        if (item.Kind == ItemKind.StartMenu && item.ModuleIconStyle is "mono-rounded" or "mono-squared" or "colour-rounded" or "colour-squared")
            return System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "start-button",
                (item.ModuleIconStyle.StartsWith("mono-") ? dark ? "white" : "black" : "color") +
                (item.ModuleIconStyle.EndsWith("rounded") ? "-rounded.svg" : "-squared.svg"));
        return IconResources.PathFor(item.Kind switch
        {
            ItemKind.StartMenu => BundledIcons.Start,
            ItemKind.Search => BundledIcons.Search,
            _ => dark ? BundledIcons.TaskViewDark : BundledIcons.TaskViewLight
        });
    }
    internal static string Name(DockItem item) => item.ModuleIconStyle switch
    {
        "mono-rounded" => "Monochrome rounded", "mono-squared" => "Monochrome squared",
        "colour-rounded" => "Colourful rounded", "colour-squared" => "Colourful squared",
        "modern" => "Modern Search",
        _ => item.Kind switch { ItemKind.StartMenu => "Windows Start", ItemKind.Search => "Default Search", _ => "Task view" }
    };
}
