namespace WinMac.Dock.Core;

public static class CapturePins
{
    public static CapturePinKind KindFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif" => CapturePinKind.Screenshot,
        ".mp4" or ".webm" or ".mov" or ".mkv" => CapturePinKind.Recording,
        _ => CapturePinKind.None
    };

    public static bool Trim(List<DockItem> items, int limit)
    {
        var remove = items.Where(i => i.IsDownloadedPin && i.CapturePin != CapturePinKind.None).Reverse()
            .Skip(Math.Clamp(limit, 1, 9)).ToHashSet();
        return items.RemoveAll(remove.Contains) > 0;
    }
}
