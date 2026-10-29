namespace WinMac.Dock.Core;

// Keep each pass stable even when activating a window changes its Z order or
// asynchronous minimize notifications arrive after the next click.
public sealed class WindowClickSequence
{
    private HashSet<nint> members = new();
    private Queue<nint> pending = new();
    private bool minimize;

    public (nint Handle, bool Minimize)? Next(IReadOnlyList<TaskWindow> windows, nint foreground)
    {
        var live = windows.Select(w => w.Handle).ToHashSet();
        if (live.Count == 0) { members.Clear(); pending.Clear(); return null; }
        bool changed = !members.SetEquals(live);
        // Title-bar buttons, taskbar actions and shortcuts can complete part
        // of a pass without going through the dock. Never spend a click on a
        // window that is already in the state this pass is trying to reach.
        if (!changed)
            pending = new Queue<nint>(pending.Where(handle => windows.Any(w => w.Handle == handle && w.Minimized != minimize)));
        if (changed || pending.Count == 0)
        {
            minimize = changed ? windows.All(w => !w.Minimized) : !minimize;
            members = live;
            var ordered = windows.OrderByDescending(w => w.Handle == foreground);
            pending = new Queue<nint>(ordered.Where(w => w.Minimized != minimize).Select(w => w.Handle));
        }
        return pending.Count == 0 ? null : (pending.Dequeue(), minimize);
    }
}
