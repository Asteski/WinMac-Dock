using System.Runtime.InteropServices;

namespace WinMac.Dock;

internal sealed class MinimizedTaskbarTabs : IDisposable
{
    private const string Marker = "WinMac.Dock.HiddenTaskbarTab";
    [ComImport, Guid("56FDF342-FD6D-11D0-958A-006097C9A090"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList
    {
        [PreserveSig] int HrInit();
        [PreserveSig] int AddTab(nint window);
        [PreserveSig] int DeleteTab(nint window);
        [PreserveSig] int ActivateTab(nint window);
        [PreserveSig] int SetActiveAlt(nint window);
    }
    private ITaskbarList? taskbar;
    private readonly Dictionary<nint, uint> removed = [];
    private bool replay;
    internal IReadOnlyCollection<nint> Removed => removed.Keys;
    private ITaskbarList Taskbar
    {
        get
        {
            if (taskbar != null) return taskbar;
            var created = (ITaskbarList)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("56FDF344-FD6D-11D0-958A-006097C9A090"))!)!;
            try { Marshal.ThrowExceptionForHR(created.HrInit()); taskbar = created; return created; }
            catch { Marshal.ReleaseComObject(created); throw; }
        }
    }
    private static bool SameWindow(nint window, uint pid)
    {
        if (!Native.IsWindow(window)) return false;
        Native.GetWindowThreadProcessId(window, out var actual);
        return actual == pid;
    }
    private bool Restore(nint window, uint pid)
    {
        if (!SameWindow(window, pid)) return true;
        // A window that subsequently hides itself (for example to its own
        // notification icon) must not gain a ghost taskbar tab on release.
        if (!Native.IsWindowVisible(window)) { Native.RemovePropW(window, Marker); return true; }
        try
        {
            if (Taskbar.AddTab(window) < 0) return false;
            Native.RemovePropW(window, Marker); return true;
        }
        catch (Exception ex) { Log.Write(ex); return false; }
    }
    internal void Recover()
    {
        // Window properties survive an abnormal dock exit. Restore those tabs
        // before applying the current preference, without changing app styles.
        Native.EnumWindows((window, _) =>
        {
            var pid = unchecked((uint)Native.GetPropW(window, Marker).ToInt64());
            if (pid != 0 && SameWindow(window, pid))
            {
                removed[window] = pid;
                if (Restore(window, pid)) removed.Remove(window);
            }
            return true;
        }, 0);
    }
    internal void Sync(DockSettings settings, IReadOnlyList<TaskWindow> tasks)
    {
        bool enabled = settings.RedirectWindowAnimations && settings.ShowMinimizedApplicationsOnly;
        var eligible = tasks.Where(w => enabled && w.Minimized).Select(w => w.Handle).ToHashSet();
        foreach (var pair in removed.ToArray())
            if (!eligible.Contains(pair.Key) && Restore(pair.Key, pair.Value)) removed.Remove(pair.Key);
        foreach (var window in eligible)
        {
            Native.GetWindowThreadProcessId(window, out var pid);
            if (removed.TryGetValue(window, out var previous) && previous == pid && !replay) continue;
            // Don't remove a tab without a recoverable marker, including when
            // Windows denies access to a higher-integrity application.
            if (!Native.SetPropW(window, Marker, (nint)pid)) continue;
            try
            {
                if (Taskbar.DeleteTab(window) >= 0) removed[window] = pid;
                else Native.RemovePropW(window, Marker);
            }
            catch (Exception ex) { Native.RemovePropW(window, Marker); Log.Write(ex); }
        }
        replay = false;
    }
    internal void ExplorerRestarted()
    {
        if (taskbar != null) { Marshal.ReleaseComObject(taskbar); taskbar = null; }
        replay = true;
    }
    public void Dispose()
    {
        foreach (var pair in removed.ToArray()) if (Restore(pair.Key, pair.Value)) removed.Remove(pair.Key);
        if (taskbar != null) { Marshal.ReleaseComObject(taskbar); taskbar = null; }
    }
}

internal sealed partial class DockWindow
{
    private readonly MinimizedTaskbarTabs minimizedTabs = new();
    private bool MinimizedOnly => Settings.RedirectWindowAnimations && Settings.ShowMinimizedApplicationsOnly;
    private PixelRect? PendingMinimizeTarget(TaskWindow task)
    {
        var prospective = tracker.Read().Where(w => w.Handle != task.Handle).Append(task with { Minimized = true }).ToArray();
        var entries = TaskReconciler.Reconcile(Settings.Items, prospective, Settings.GroupWindows, Settings.CombinePinnedIconsWithRunningApplications, true);
        var flags = entries.Select(e => e.Item.Kind != ItemKind.Separator).ToList();
        int index = entries.FindIndex(e => e.Windows.Any(w => w.Handle == task.Handle));
        if (index < 0) return null;
        int pins = entries.Count(e => e.IsPinned);
        if (entries.Count > pins && (pins == 0 || entries[pins - 1].Item.Kind != ItemKind.Separator)) { flags.Insert(pins, false); if (index >= pins) index++; }
        double rowMargins = (Vertical ? Settings.DockMarginTop + Settings.DockMarginBottom : Settings.DockMarginLeft + Settings.DockMarginRight) / scale;
        double crossMargins = (Vertical ? Settings.DockMarginLeft + Settings.DockMarginRight : Settings.DockMarginTop + Settings.DockMarginBottom) / scale;
        double available = (Vertical ? monitor.Work.Height : monitor.Work.Width) / scale - rowMargins;
        var fit = DockAutoFit.Calculate(Settings.IconSize, Settings.WindowsTaskbarHighlights ? 20 : 12, flags, Settings.IconSpacing, available,
            Motion && DisplayMagnificationEnabled ? Settings.HoverMagnificationPercent : 0, Settings.MagnificationSpan, SeparatorWidth);
        double spacing = DockAutoFit.Spacing(Settings.IconSpacing, fit.IconSize, Settings.IconSize);
        double button = fit.IconSize + fit.Padding;
        var widths = flags.Select(icon => icon ? button : SeparatorWidth).ToArray();
        double start = DockSpacing.Starts(widths, flags, spacing)[index];
        int length = Math.Min(DockGeometry.Pixels(DockSpacing.Length(widths, flags, spacing) + rowMargins, scale),
            Vertical ? monitor.Work.Height : monitor.Work.Width);
        var panel = DockGeometry.PlaceEdge(appbar.IsRegistered ? appbar.Area : monitor.Work, length,
            DockGeometry.Pixels(button + crossMargins + (fit.Overflow ? 16 : 0), scale), PositionEdge, Settings.FloatingGap, PositionAlignment);
        int size = DockGeometry.Pixels(button, scale);
        int x = panel.Left + Settings.DockMarginLeft + (Vertical ? 0 : DockGeometry.Pixels(start, scale));
        int y = panel.Top + Settings.DockMarginTop + (Vertical ? DockGeometry.Pixels(start, scale) : 0);
        x = Math.Clamp(x, panel.Left, Math.Max(panel.Left, panel.Right - size));
        y = Math.Clamp(y, panel.Top, Math.Max(panel.Top, panel.Bottom - size));
        return new(x, y, x + size, y + size);
    }
}
