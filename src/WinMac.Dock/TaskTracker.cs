namespace WinMac.Dock;
internal sealed class TaskTracker : IDisposable
{
    private readonly Native.WinEventProc callback;
    private readonly List<nint> hooks = [];
    private readonly Dictionary<uint, (string Path, string Id)> cache = [];
    private readonly Dictionary<nint, long> activationOrder = [];
    private long activationSerial;
    public IEnumerable<TaskWindow> MostRecentlyUsed(IEnumerable<TaskWindow> windows) => windows.OrderByDescending(w => activationOrder.GetValueOrDefault(w.Handle));
    public event Action? Changed;
    public event Action? ForegroundChanged;
    private static nint ownTaskWindow;
    public nint OwnTaskWindow { get => ownTaskWindow; set => ownTaskWindow = value; }
    public nint LastExternalForeground { get; private set; } = Native.GetForegroundWindow();
    public void RecordForeground(nint foreground)
    {
        if (foreground == 0 || !Native.IsWindow(foreground)) return;
        
        Native.GetWindowThreadProcessId(foreground, out uint pid);
        if (pid != Environment.ProcessId || foreground == OwnTaskWindow)
        {
            if (foreground != LastExternalForeground || !activationOrder.ContainsKey(foreground)) activationOrder[foreground] = ++activationSerial;
            LastExternalForeground = foreground;
        }
    }
    public void ObserveForeground()
    {
        var foreground = Native.GetForegroundWindow();
        if (foreground == 0) return;
        RecordForeground(foreground);
    }
    public TaskTracker()
    {
        callback = (_, evt, hwnd, obj, child, thread, time) =>
        {
            // Delivered hooks can lag behind a dock-initiated activation, or
            // arrive after a newer foreground transition. Sample current state
            // rather than replacing it with the old event's HWND.
            if (evt == 3) {  ObserveForeground(); ForegroundChanged?.Invoke(); }
            if (obj == 0 || evt < 0x8000) Changed?.Invoke();
        };
        hooks.Add(Native.SetWinEventHook(3, 3, 0, callback, 0, 0, 2));
        hooks.Add(Native.SetWinEventHook(0x8000, 0x8003, 0, callback, 0, 0, 2));
        hooks.Add(Native.SetWinEventHook(0x800B, 0x800C, 0, callback, 0, 0, 2));
        hooks.Add(Native.SetWinEventHook(0x16, 0x17, 0, callback, 0, 0, 2));
        // DWM cloaking changes when switching virtual desktops.
        hooks.Add(Native.SetWinEventHook(0x8017, 0x8018, 0, callback, 0, 0, 2));
    }
    public List<TaskWindow> Read(bool? allDesktops = null)
    {
        var result = new List<TaskWindow>();
        var livePids = new HashSet<uint>();
        Native.EnumWindows((hwnd, _) =>
        {
            var task = Describe(hwnd, livePids, allDesktops); if (task != null) result.Add(task); return true;
        }, 0);
        foreach (var pid in cache.Keys.Where(x => !livePids.Contains(x)).ToArray()) cache.Remove(pid);
        foreach (var handle in activationOrder.Keys.Where(h => !Native.IsWindow(h)).ToArray()) activationOrder.Remove(handle);
        return result;
    }
    public TaskWindow? Describe(nint hwnd, HashSet<uint>? livePids = null, bool? allDesktops = null)
    {
        if (hwnd == 0 || !Native.IsWindowVisible(hwnd)) return null;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid == Environment.ProcessId && hwnd != OwnTaskWindow) return null;
        var style = Native.GetWindowLongPtrW(hwnd, -20).ToInt64();
        if (hwnd != OwnTaskWindow && ((style & 0x80) != 0 || ((style & 0x40000) == 0 && Native.GetWindow(hwnd, 4) != 0))) return null;
        var cls = Native.Class(hwnd);
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return null;
        if (Native.IsTaskViewWindow(hwnd)) return null;
        var currentDesktop = Native.IsWindowOnCurrentVirtualDesktop(hwnd);
        var includeAll = allDesktops ?? DockWindow.ShowItemsFromAllVirtualDesktops;
        if (!includeAll && !currentDesktop) return null;
        if (Native.DwmGetInt(hwnd, 14, out int cloaked, 4) == 0 && cloaked != 0)
        {
            // Shell cloaking on another desktop is allowed in all-desktop mode.
            // App-cloaked windows (such as inactive UWP hosts) remain excluded.
            if (!includeAll || currentDesktop || (cloaked & 1) != 0) return null;
        }
        var title = Native.Title(hwnd); if (string.IsNullOrWhiteSpace(title)) return null;
        livePids?.Add(pid);
        if (!cache.TryGetValue(pid, out var identity)) cache[pid] = identity = (Native.ProcessPath(pid), Native.ProcessAppId(pid));
        var appId = ShellService.WindowAppId(hwnd);
        return new(hwnd, title, identity.Path, string.IsNullOrEmpty(appId) ? identity.Id : appId, Native.Bounds(hwnd), Native.IsIconic(hwnd), Native.IsZoomed(hwnd));
    }
    public void Dispose() { foreach (var hook in hooks) if (hook != 0) Native.UnhookWinEvent(hook); }
}
