namespace WinMac.Dock.Core;

public sealed record DownloadFile(string Path, long Size, DateTime Modified);

// Each enabled session starts with a baseline; existing files are never imported.
public sealed class NewDownloadTracker
{
    private Dictionary<string, DownloadFile>? previous;
    private readonly HashSet<string> handled = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> stableSince = new(StringComparer.OrdinalIgnoreCase);
    public bool HasPending => stableSince.Count > 0;
    public static bool IsCandidate(string path) => !new[] { ".crdownload", ".part", ".partial", ".download", ".tmp", ".temp" }
        .Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
        && !System.IO.Path.GetFileName(path).StartsWith("~", StringComparison.Ordinal);

    public IReadOnlyList<string> Observe(IEnumerable<DownloadFile> files, DateTime now)
    {
        var snapshot = files.ToArray();
        var incomplete = snapshot.Where(f => !IsCandidate(f.Path)).Select(f => System.IO.Path.ChangeExtension(f.Path, null)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var current = snapshot.Where(f => IsCandidate(f.Path)).ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
        if (previous == null)
        {
            previous = current;
            handled.UnionWith(current.Keys);
            return [];
        }
        foreach (var removed in previous.Keys.Except(current.Keys, StringComparer.OrdinalIgnoreCase))
        { handled.Remove(removed); stableSince.Remove(removed); }
        var ready = new List<string>();
        foreach (var file in current.Values)
        {
            if (handled.Contains(file.Path)) continue;
            if (incomplete.Contains(file.Path)) { stableSince[file.Path] = now; continue; }
            if (!previous.TryGetValue(file.Path, out var old) || old.Size != file.Size || old.Modified != file.Modified)
                stableSince[file.Path] = now;
            else if (stableSince.TryGetValue(file.Path, out var since) && now - since >= TimeSpan.FromMilliseconds(150))
            { ready.Add(file.Path); handled.Add(file.Path); stableSince.Remove(file.Path); }
        }
        previous = current;
        return ready;
    }
}

public static class DownloadPinLayout
{
    public static bool RemoveMissing(List<DockItem> items, string folder, IReadOnlySet<string> existing) =>
        items.RemoveAll(i => i.IsDownloadedPin && string.Equals(Path.GetDirectoryName(i.Target), folder, StringComparison.OrdinalIgnoreCase) && !existing.Contains(i.Target)) > 0;

    public static bool Trim(List<DockItem> items, int limit)
    {
        var pins = items.Where(i => i.IsDownloadedPin && i.CapturePin == CapturePinKind.None).ToList();
        var remove = pins.Take(Math.Max(0, pins.Count - Math.Clamp(limit, 1, 9))).ToHashSet();
        return items.RemoveAll(i => remove.Contains(i)) > 0;
    }

    public static List<DockItem> OrderPins(IEnumerable<DockItem> items) => items.OrderBy(i => i.IsDownloadedPin ? 2 : i.IsAutoUsbPin ? 1 : 0).ToList();
    public static List<AutomaticItemKind> NormalizeOrder(IEnumerable<AutomaticItemKind>? order) =>
        (order ?? []).Where(kind => Enum.IsDefined(kind)).Concat(Enum.GetValues<AutomaticItemKind>()).Distinct().ToList();

    private static AutomaticItemKind? AutomaticKind(DockEntry entry)
    {
        if (!entry.IsPinned) return AutomaticItemKind.Windows;
        if (entry.Item.IsAutoUsbPin) return AutomaticItemKind.ExternalDisks;
        if (!entry.Item.IsDownloadedPin) return null;
        return entry.Item.CapturePin switch
        {
            CapturePinKind.Screenshot => AutomaticItemKind.Screenshots,
            CapturePinKind.Recording => AutomaticItemKind.Recordings,
            _ => AutomaticItemKind.Downloads
        };
    }

    public static List<DockEntry> OrderEntries(IEnumerable<DockEntry> entries, IEnumerable<AutomaticItemKind>? order)
    {
        var kinds = NormalizeOrder(order);
        return entries.OrderBy(entry => AutomaticKind(entry) is { } kind ? kinds.IndexOf(kind) : -1).ToList();
    }

    public static bool NeedsSeparator(DockEntry? previous, DockEntry entry) =>
        previous == null ? !entry.IsPinned : previous.Item.Kind != ItemKind.Separator
            && entry.Item.Kind != ItemKind.Separator && AutomaticKind(previous) != AutomaticKind(entry);
    public static List<bool> IconFlags(IReadOnlyList<DockEntry> entries)
    {
        var flags = new List<bool>();
        DockEntry? previous = null;
        foreach (var entry in entries)
        {
            if (NeedsSeparator(previous, entry))
                flags.Add(false);
            flags.Add(entry.Item.Kind != ItemKind.Separator);
            previous = entry;
        }
        return flags;
    }
}
