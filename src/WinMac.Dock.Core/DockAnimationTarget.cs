namespace WinMac.Dock.Core;

public static class DockAnimationTarget
{
    public static int FindIndex(IReadOnlyList<DockEntry> entries, nint window, TaskWindow? task)
    {
        // Match ownership before identity: Explorer shares one process and app
        // identity across independent folder windows and the pinned launcher.
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].Windows.Any(w => w.Handle == window)) return i;
        if (task != null)
            for (int i = 0; i < entries.Count; i++)
                if (TaskReconciler.Matches(entries[i].Item, task)) return i;
        return -1;
    }
}
