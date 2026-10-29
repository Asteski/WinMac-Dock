using System.Diagnostics;
using System.Text.Json;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckMinimizedOnly()
    {
        if (rowTesting || hoverTestInput != null) return;
        var pins = Settings.Items.ToList();
        bool only = Settings.ShowMinimizedApplicationsOnly, redirect = Settings.RedirectWindowAnimations;
        bool group = Settings.GroupWindows, inspected = inspection;
        Process? fixture = null; Exception? failure = null; var checks = new List<string>();
        TaskWindow[] tasks = [];
        try
        {
            inspection = true;
            hoverTestInput = () => (0, false);
            Settings.ShowMinimizedApplicationsOnly = true; Settings.RedirectWindowAnimations = true; Settings.GroupWindows = true;
            Reconcile();
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tests", "WinMac.Dock.Fixture", "bin", "Release", "net10.0-windows", "WinMac.Dock.Fixture.exe"));
            fixture = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            async Task Wait(Func<bool> predicate, string error)
            {
                for (int i = 0; i < 100; i++) { if (predicate()) return; await Task.Delay(30); }
                throw new Exception(error);
            }
            await Wait(() => { tasks = tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out var pid); return pid == fixture!.Id; }).ToArray(); return tasks.Length == 2; }, "Fixture windows did not appear");
            await Task.Delay(600); Reconcile();
            bool Represented(nint hwnd) => buttons.Any(b => b.Entry.Windows.Any(w => w.Handle == hwnd));
            if (tasks.Any(t => Represented(t.Handle))) throw new Exception("Normal unpinned windows appeared in minimized-only dock");
            if (tasks.Any(t => AnimationTarget(t.Handle) == null)) throw new Exception("Absent unpinned window has no predicted minimize destination");
            checks.Add("Normal unpinned windows remain outside dock but have a planned minimize target");
            var planned = AnimationTarget(tasks[0].Handle);
            Native.ShowWindow(tasks[0].Handle, 6);
            await Wait(() => Represented(tasks[0].Handle) && minimizedTabs.Removed.Contains(tasks[0].Handle), "Minimized window did not enter dock and leave taskbar");
            await Wait(() => !rowAnimating, "Minimized row did not settle");
            var actual = AnimationTarget(tasks[0].Handle);
            // WinUI rounds centered layout to the physical pixel grid.
            if (planned == null || actual == null || Math.Abs(planned.Value.Left - actual.Value.Left) > 1 || Math.Abs(planned.Value.Top - actual.Value.Top) > 1
                || Math.Abs(planned.Value.Right - actual.Value.Right) > 1 || Math.Abs(planned.Value.Bottom - actual.Value.Bottom) > 1)
                throw new Exception($"Predicted minimize destination differs from actual icon: {planned} -> {actual}");
            if (Represented(tasks[1].Handle) || minimizedTabs.Removed.Contains(tasks[1].Handle)) throw new Exception("Normal sibling was affected by minimized group");
            checks.Add("One minimized window enters its predicted dock slot and removes its taskbar tab; normal sibling stays separate");
            ShellService.Activate(tasks[0], false);
            await Wait(() => !Represented(tasks[0].Handle) && !minimizedTabs.Removed.Contains(tasks[0].Handle), "Dock restore did not return taskbar ownership");
            if (Native.IsIconic(tasks[0].Handle) || Native.GetPropW(tasks[0].Handle, "WinMac.Dock.HiddenTaskbarTab") != 0) throw new Exception("Restore left a minimized window or recovery marker");
            checks.Add("Restoring through dock removes its dock entry and restores taskbar tab");
            foreach (var task in tasks) Native.ShowWindow(task.Handle, 6);
            await Wait(() => tasks.All(t => minimizedTabs.Removed.Contains(t.Handle)), "Both minimized windows were not owned by dock");
            if (buttons.Single(b => b.Entry.Windows.Any(w => w.Handle == tasks[0].Handle)).Entry.Windows.Count != 2) throw new Exception("Minimized windows did not group");
            checks.Add("Multiple minimized windows group without losing individual taskbar ownership");
            Settings.Items.Add(new DockItem { Kind = ItemKind.Application, Name = "Temporary fixture pin", Target = path, ExecutablePath = path });
            Reconcile();
            if (tasks.Any(t => !minimizedTabs.Removed.Contains(t.Handle))) throw new Exception("Pinning released minimized taskbar entries");
            ShellService.Activate(tasks[0], false);
            await Wait(() => !minimizedTabs.Removed.Contains(tasks[0].Handle), "Restoring a pinned window did not return its taskbar tab");
            if (!buttons.Any(b => b.Entry.IsPinned && b.Entry.Windows.Any(w => w.Handle == tasks[0].Handle))) throw new Exception("Restoring removed the pinned launcher");
            Native.ShowWindow(tasks[0].Handle, 6);
            await Wait(() => minimizedTabs.Removed.Contains(tasks[0].Handle), "Minimizing a pinned window did not remove its taskbar tab");
            checks.Add("Pinned minimized windows leave the taskbar; restoring returns the tab and preserves the pin");
            Settings.Items = pins.ToList(); Reconcile();
            await Wait(() => tasks.All(t => minimizedTabs.Removed.Contains(t.Handle)), "Unpinning did not reapply minimized ownership");
            Settings.RedirectWindowAnimations = false; Reconcile();
            if (MinimizedOnly || tasks.Any(t => minimizedTabs.Removed.Contains(t.Handle))) throw new Exception("Disabling Minimize to dock left taskbar tabs hidden");
            checks.Add("Disabling parent option suspends minimized-only mode and restores taskbar tabs");
            Settings.RedirectWindowAnimations = true; Reconcile();
            minimizedTabs.Recover();
            if (tasks.Any(t => Native.GetPropW(t.Handle, "WinMac.Dock.HiddenTaskbarTab") != 0)) throw new Exception("Startup recovery left taskbar markers");
            checks.Add("Recovery restores marked tabs left by an interrupted dock");
            minimizedTabs.ExplorerRestarted(); Reconcile();
            await Wait(() => tasks.All(t => minimizedTabs.Removed.Contains(t.Handle)), "Shell reconnection did not reapply taskbar ownership");
            checks.Add("Shell reconnection reinitializes taskbar interface and reapplies ownership");
            Native.ShowWindow(tasks[0].Handle, 0); Reconcile();
            if (minimizedTabs.Removed.Contains(tasks[0].Handle) || Native.GetPropW(tasks[0].Handle, "WinMac.Dock.HiddenTaskbarTab") != 0)
                throw new Exception("A window hiding to its own tray retained dock ownership");
            checks.Add("A window hiding itself is released without creating a ghost taskbar entry");
            Native.ShowWindow(tasks[0].Handle, 5); Native.ShowWindow(tasks[0].Handle, 6);
            Settings.ShowMinimizedApplicationsOnly = false; Reconcile();
            if (tasks.Any(t => minimizedTabs.Removed.Contains(t.Handle))) throw new Exception("Disabling filter did not restore tabs");
            checks.Add("Disabling minimized-only restores ordinary dock and taskbar behavior");
            using (var shutdown = new MinimizedTaskbarTabs())
            {
                shutdown.Sync(Settings with { ShowMinimizedApplicationsOnly = true, RedirectWindowAnimations = true }, tracker.Read(true).Where(w => tasks.Any(t => t.Handle == w.Handle)).ToArray());
                if (shutdown.Removed.Count != 2) throw new Exception("Shutdown fixture could not acquire both tabs");
            }
            if (tasks.Any(t => Native.GetPropW(t.Handle, "WinMac.Dock.HiddenTaskbarTab") != 0 || !Native.IsIconic(t.Handle)))
                throw new Exception("Shutdown release left a marker or restored the window unnecessarily");
            checks.Add("Shutdown restores taskbar tabs while leaving the applications minimized");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            Settings.ShowMinimizedApplicationsOnly = false; minimizedTabs.Sync(Settings, tracker.Read(true));
            foreach (var task in tasks) if (Native.IsWindow(task.Handle)) Native.PostMessage(task.Handle, 0x10, 0, 0);
            fixture?.Dispose();
            Settings.Items = pins; Settings.ShowMinimizedApplicationsOnly = only; Settings.RedirectWindowAnimations = redirect;
            Settings.GroupWindows = group; inspection = inspected; ApplySettings();
            hoverTestInput = null; ResetMagnification();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "minimized-only-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
