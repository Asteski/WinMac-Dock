namespace WinMac.Dock.Core;

public static class DockClickPolicy
{
    public static string? Identity(DockItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.AppUserModelId)) return "app:" + item.AppUserModelId.ToUpperInvariant();
        if (item.Arguments.Contains("--app", StringComparison.OrdinalIgnoreCase)) return null;
        var path = string.IsNullOrEmpty(item.ExecutablePath) ? item.Target : item.ExecutablePath;
        return string.IsNullOrWhiteSpace(path) ? null : "exe:" + path.Replace('/', '\\').ToUpperInvariant();
    }
    public static ClickActions Resolve(DockSettings settings, DockEntry entry)
    {
        if (entry.IsPinned && entry.Item.ClickActions != null) return entry.Item.ClickActions;
        var identity = Identity(entry.Item);
        if (!entry.IsPinned && identity != null && settings.TaskClickOverrides.FirstOrDefault(r => r.Identity == identity) is { } rule) return rule.Actions;
        return settings.DefaultClickActions;
    }
    public static TaskWindow? Primary(DockEntry entry, IReadOnlyList<TaskWindow> current, nint foreground)
    {
        var handles = entry.Windows.Select(w => w.Handle).ToHashSet();
        return current.FirstOrDefault(w => handles.Contains(w.Handle) && w.Handle == foreground)
            ?? entry.Windows.Select(member => current.FirstOrDefault(w => w.Handle == member.Handle)).FirstOrDefault(w => w != null);
    }
    public static IReadOnlyList<TaskWindow> CycleTargets(DockEntry entry, IReadOnlyList<TaskWindow> current, bool groupWindows, bool combinePinnedApps = true)
    {
        if (entry.IsPinned && !combinePinnedApps) return Array.Empty<TaskWindow>();
        if (entry.IsPinned || groupWindows) return Similar(entry, current);
        var handles = entry.Windows.Select(window => window.Handle).ToHashSet();
        return current.Where(window => handles.Contains(window.Handle)).ToArray();
    }
    public static IReadOnlyList<TaskWindow> Similar(DockEntry entry, IReadOnlyList<TaskWindow> current)
    {
        var identity = Identity(entry.Item);
        var handles = entry.Windows.Select(w => w.Handle).ToHashSet();
        return current.Where(w => handles.Contains(w.Handle) || (identity != null && identity == Identity(new DockItem { Target = w.ExecutablePath, AppUserModelId = w.AppUserModelId }))).ToArray();
    }
}
