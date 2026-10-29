namespace WinMac.Dock.Core;

public static class ItemTooltip
{
    public static string Label(DockItem item, bool showFileExtensions)
    {
        if (item.Kind != ItemKind.File) return item.Name;
        var extension = Path.GetExtension(item.Target);
        if (string.IsNullOrEmpty(extension)) return item.Name;
        bool hasExtension = item.Name.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
        return showFileExtensions ? hasExtension ? item.Name : item.Name + extension
            : hasExtension ? item.Name[..^extension.Length] : item.Name;
    }
}
