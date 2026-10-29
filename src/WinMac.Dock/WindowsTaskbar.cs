using System.Runtime.InteropServices;
using System.Text.Json;

namespace WinMac.Dock;

internal sealed class WindowsTaskbar : IDisposable
{
    private sealed record SavedState(Dictionary<string, bool> Visibility,
        Dictionary<string, TaskbarConfiguration>? Configuration = null);
    private readonly string recoveryFile = Path.Combine(Log.DirectoryPath, "taskbar-visibility.json");
    private readonly Dictionary<string, bool> originalVisibility = [];
    private readonly Dictionary<string, TaskbarConfiguration> originalConfiguration = [];
    private bool active, failed, syncing;
    private long nextRestoreAttempt;
    internal string? Error { get; private set; }
    internal Action<bool>? SetSuppressed { get; set; }
    private static List<(nint Handle, string Key)> Bars()
    {
        var bars = new List<(nint, string)>(); var monitors = Native.Monitors();
        Native.EnumWindows((hwnd, _) =>
        {
            string cls = Native.Class(hwnd);
            if (cls is not ("Shell_TrayWnd" or "Shell_SecondaryTrayWnd")) return true;
            var display = monitors.FirstOrDefault(m => m.Handle == Native.MonitorFromWindow(hwnd, 2));
            bars.Add((hwnd, cls + ":" + (display?.Device ?? ""))); return true;
        }, 0);
        return bars;
    }
    internal static uint State()
    {
        var data = new Native.APPBARDATA { Size = (uint)Marshal.SizeOf<Native.APPBARDATA>() };
        return (uint)Native.SHAppBarMessage(4, ref data);
    }
    private static Native.APPBARDATA ReadPosition()
    {
        var data = new Native.APPBARDATA { Size = (uint)Marshal.SizeOf<Native.APPBARDATA>() };
        if (Native.SHAppBarMessage(5, ref data) == 0 || data.Edge > 3)
            throw new InvalidOperationException("Could not read Windows taskbar settings.");
        return data;
    }
    private sealed class Configuration(nint window) : ITaskbarConfiguration
    {
        public uint State
        {
            get { _ = ReadPosition(); return WindowsTaskbar.State(); }
            set
            {
                var data = new Native.APPBARDATA { Size = (uint)Marshal.SizeOf<Native.APPBARDATA>(), Window = window, Param = (nint)value };
                Native.SHAppBarMessage(10, ref data);
            }
        }
        public uint Edge
        {
            get
            {
                if (Native.Class(window) == "Shell_TrayWnd")
                {
                    return ReadPosition().Edge;
                }
                var display = Native.Monitors().FirstOrDefault(m => m.Handle == Native.MonitorFromWindow(window, 2))
                    ?? throw new InvalidOperationException("The taskbar display is unavailable.");
                var bounds = Native.Bounds(window); var monitor = display.Bounds;
                return bounds.Width >= bounds.Height
                    ? (Math.Abs(bounds.Top - monitor.Top) < Math.Abs(bounds.Bottom - monitor.Bottom) ? 1u : 3u)
                    : (Math.Abs(bounds.Left - monitor.Left) < Math.Abs(bounds.Right - monitor.Right) ? 0u : 2u);
            }
            set
            {
                if (value > 3 || !Native.SetTaskbarEdge(window, value))
                    throw new InvalidOperationException("Windows could not change the taskbar position.");
            }
        }
    }
    private bool SaveRecovery()
    {
        try
        {
            Directory.CreateDirectory(Log.DirectoryPath);
            File.WriteAllText(recoveryFile + ".tmp", JsonSerializer.Serialize(new SavedState(originalVisibility, originalConfiguration)));
            File.Move(recoveryFile + ".tmp", recoveryFile, true); return true;
        }
        catch (Exception ex) { Log.Write(ex); Error = "Could not save the taskbar recovery settings."; return false; }
    }
    internal void Recover()
    {
        if (!File.Exists(recoveryFile)) return;
        try
        {
            var saved = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(recoveryFile));
            if (saved == null) return;
            foreach (var pair in saved.Visibility) originalVisibility[pair.Key] = pair.Value;
            if (saved.Configuration != null)
                foreach (var pair in saved.Configuration) originalConfiguration[pair.Key] = pair.Value;
            active = true; Restore(force: true); if (active) failed = true;
        }
        catch (Exception ex) { Log.Write(ex); Error = ex.Message; failed = true; }
    }
    internal bool Sync(bool hide)
    {
        if (syncing) return false;
        syncing = true;
        try { return SyncCore(hide); }
        finally { syncing = false; }
    }
    private bool SyncCore(bool hide)
    {
        if (!hide) { failed = false; bool restoredChanged = Restore(); if (!active) Error = null; return restoredChanged; }
        if (failed) return false;
        var bars = Bars();
        if (bars.Count == 0) return false;
        bool changed = !active;
        try
        {
            // A pending recovery must be completed before starting a new lease.
            if (!active && File.Exists(recoveryFile)) { Recover(); if (active || failed) return false; }
            var added = new List<string>();
            foreach (var bar in bars)
                if (!originalVisibility.ContainsKey(bar.Key))
                {
                    bool visible = Native.IsWindowVisible(bar.Handle);
                    var config = TaskbarConfiguration.Capture(new Configuration(bar.Handle));
                    // A newly connected display inherits the global auto-hide state
                    // from the shell, which may already be temporarily enabled.
                    if (active && originalConfiguration.Count > 0)
                        config = config with { State = originalConfiguration.First().Value.State };
                    originalVisibility[bar.Key] = visible;
                    originalConfiguration[bar.Key] = config;
                    added.Add(bar.Key);
                }
            // Persist before touching Explorer, including the original position
            // and complete auto-hide/always-on-top state, for crash recovery.
            if ((!active || added.Count > 0) && !SaveRecovery())
            {
                foreach (string key in added) { originalVisibility.Remove(key); originalConfiguration.Remove(key); }
                failed = true; return false;
            }
            active = true;
            if (changed || added.Count > 0) SetSuppressed?.Invoke(false);
            // All displays must reach the bottom before changing global auto-hide.
            foreach (var bar in bars)
            {
                var config = new Configuration(bar.Handle);
                if (config.Edge != 3) { SetSuppressed?.Invoke(false); config.Edge = 3; }
            }
            foreach (var bar in bars)
                originalConfiguration[bar.Key].PrepareToHide(new Configuration(bar.Handle));
            SetSuppressed?.Invoke(true);
            foreach (var bar in bars)
                if (Native.IsWindowVisible(bar.Handle))
                {
                    Native.ShowWindow(bar.Handle, 0);
                    Native.SetWindowPos(bar.Handle, 0, 0, 0, 0, 0, 0x37);
                }
            Error = null;
            return changed;
        }
        catch (Exception ex)
        {
            Log.Write(ex); Error = ex.Message; failed = true;
            Restore(force: true); return true;
        }
    }
    private bool Restore(bool force = false)
    {
        SetSuppressed?.Invoke(false);
        if (!active || (!force && Environment.TickCount64 < nextRestoreAttempt)) return false;
        nextRestoreAttempt = Environment.TickCount64 + 2000;
        var bars = Bars();
        if (bars.Count == 0) return false;
        bool restored = true;
        // Always make Explorer accessible even if restoring a setting fails.
        foreach (var bar in bars)
        {
            try
            {
                if (originalConfiguration.TryGetValue(bar.Key, out var config)) config.Restore(new Configuration(bar.Handle));
            }
            catch (Exception ex) { if (Error != ex.Message) Log.Write(ex); Error = ex.Message; restored = false; }
            if (originalVisibility.TryGetValue(bar.Key, out bool visible)) Native.ShowWindow(bar.Handle, visible ? 8 : 0);
        }
        // Keep the journal for disconnected displays as well as failed restores.
        if (originalVisibility.Keys.Any(key => !bars.Any(bar => bar.Key == key))) restored = false;
        if (restored)
        {
            try { File.Delete(recoveryFile); }
            catch (Exception ex) { Log.Write(ex); Error = ex.Message; return true; }
            active = false; originalVisibility.Clear(); originalConfiguration.Clear();
        }
        return true;
    }
    public void Dispose() => Restore(force: true);
}
