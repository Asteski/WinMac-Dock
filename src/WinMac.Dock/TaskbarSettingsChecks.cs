using System.Text.Json;
namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async void CheckTaskbarSettings()
    {
        var checks = new List<string>(); Exception? failure = null; bool liveCycleCompleted = false;
        uint originalState = WindowsTaskbar.State();
        var original = new Native.APPBARDATA { Size = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Native.APPBARDATA>() };
        bool canReadPosition = Native.SHAppBarMessage(5, ref original) != 0;
        var visibility = new Dictionary<nint, bool>();
        Native.EnumWindows((window, _) => { if (Native.Class(window) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") visibility[window] = Native.IsWindowVisible(window); return true; }, 0);
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks.Add(name); }
        void CheckRestored()
        {
            var current = new Native.APPBARDATA { Size = original.Size };
            Native.SHAppBarMessage(5, ref current);
            Check(WindowsTaskbar.State() == originalState && current.Edge == original.Edge, "Original taskbar position and auto-hide state restored");
            Check(visibility.All(p => !Native.IsWindow(p.Key) || Native.IsWindowVisible(p.Key) == p.Value), "Original taskbar visibility restored");
            Check(!File.Exists(Path.Combine(Log.DirectoryPath, "taskbar-visibility.json")), "Recovery journal removed only after successful restoration");
        }
        try
        {
            safetyTimer.Stop(); reconcileTimer.Stop(); themeTimer.Stop(); visibilityTimer.Stop();
            Settings.HideWindowsTaskbar = false; Settings.ReserveSpace = false;
            Check(visibility.Count > 0, "Explorer taskbar is available");
            if (!canReadPosition)
            {
                Settings.HideWindowsTaskbar = true; SyncWindowsTaskbar();
                Check(windowsTaskbar.Error != null, "Unreadable shell settings abort taskbar hiding");
                Check(!File.Exists(Path.Combine(Log.DirectoryPath, "taskbar-visibility.json")), "Unreadable preferences never overwrite the recovery record");
                Check(visibility.All(p => !Native.IsWindow(p.Key) || Native.IsWindowVisible(p.Key) == p.Value), "Blocked preparation leaves original taskbar visibility unchanged");
            }
            else
            {
            Settings.HideWindowsTaskbar = true; SyncWindowsTaskbar();
            Check(windowsTaskbar.Error == null, "Taskbar preparation succeeded: " + windowsTaskbar.Error);
            await Task.Delay(300);
            Check((WindowsTaskbar.State() & 1) != 0, "Windows auto-hide enabled");
            var prepared = new Native.APPBARDATA { Size = original.Size }; Native.SHAppBarMessage(5, ref prepared);
            Check(prepared.Edge == 3, "Windows taskbar at bottom before hiding");
            Check(visibility.Keys.All(window => !Native.IsWindowVisible(window)), "All taskbar windows hidden");
            Check(File.Exists(Path.Combine(Log.DirectoryPath, "taskbar-visibility.json")), "Original preferences durably saved");
            Settings.HideWindowsTaskbar = false; SyncWindowsTaskbar(); await Task.Delay(300); CheckRestored();
            Settings.HideWindowsTaskbar = true; SyncWindowsTaskbar();
            windowsTaskbar.Recover(); Settings.HideWindowsTaskbar = false;
            await Task.Delay(300); CheckRestored();
            liveCycleCompleted = true;
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { Settings.HideWindowsTaskbar = false; SyncWindowsTaskbar(); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "taskbar-settings-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, LiveCycleCompleted = liveCycleCompleted, Error = failure?.ToString(), OriginalState = originalState, OriginalEdge = original.Edge, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
        Close();
    }
}
