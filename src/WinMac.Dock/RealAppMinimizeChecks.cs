using System.Diagnostics;
using System.Text.Json;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckRealAppMinimize()
    {
        if (rowTesting || hoverTestInput != null) return;
        bool inspected = inspection, only = Settings.ShowMinimizedApplicationsOnly;
        var originalForeground = Native.GetForegroundWindow();
        var targets = tracker.Read().Where(w => w.Title == "ChatGPT" || new[] { "msedge.exe", "Notion.exe", "gimp-3.exe", "explorer.exe" }.Contains(Path.GetFileName(w.ExecutablePath), StringComparer.OrdinalIgnoreCase)).ToArray();
        var results = new List<object>();
        Process? fixture = null;
        TaskWindow[] helpers = [];
        async Task<bool> Wait(Func<bool> condition, int milliseconds = 2500)
        {
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < milliseconds) { if (condition()) return true; await Task.Delay(25); }
            return condition();
        }
        bool passed = true;
        void Save() => File.WriteAllText(Path.Combine(Log.DirectoryPath, "real-app-minimize-test.json"), JsonSerializer.Serialize(new { Passed = passed, Targets = targets.Select(t => new { t.Title, Handle = t.Handle.ToInt64(), t.ExecutablePath }), Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        try
        {
            inspection = true;
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tests", "WinMac.Dock.Fixture", "bin", "Release", "net10.0-windows", "WinMac.Dock.Fixture.exe"));
            var start = new ProcessStartInfo(path) { UseShellExecute = true };
            start.ArgumentList.Add("--foreground-client"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            fixture = Process.Start(start);
            await Wait(() => { helpers = tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out uint pid); return pid == fixture!.Id; }).ToArray(); return helpers.Length == 2; });
            if (helpers.Length != 2) throw new Exception("Test helper windows missing");
            if (!await Wait(() => { ShellService.Activate(helpers[0], false); return Native.GetForegroundWindow() == helpers[0].Handle; }, 20000)) throw new Exception("Test helper needs one normal activation to establish foreground permission");
            foreach (bool minimizedOnly in new[] { false, true })
            foreach (var target in targets)
            for (int cycle = 0; cycle < 4; cycle++)
            {
                Settings.ShowMinimizedApplicationsOnly = minimizedOnly;
                ShellService.Activate(target, false);
                bool restored = await Wait(() => !Native.IsIconic(target.Handle));
                ShellService.Activate(helpers[0], false);
                bool background = await Wait(() => Native.GetForegroundWindow() == helpers[0].Handle);
                ShellService.Activate(target, true);
                bool minimized = await Wait(() => Native.IsIconic(target.Handle));
                ShellService.Activate(target, false);
                bool focused = await Wait(() => !Native.IsIconic(target.Handle) && Native.GetForegroundWindow() == target.Handle);
                Reconcile();
                var button = buttons.FirstOrDefault(b => b.Entry.Windows.Any(w => w.Handle == target.Handle));
                nint selected = 0; bool defaultMinimized = false;
                if (button.Button != null)
                {
                    selected = DockClickPolicy.Primary(button.Entry, tracker.Read(), target.Handle)?.Handle ?? 0;
                    ExecuteClickAction(button.Entry, DockClickAction.BringToFront, button.Button, button.Icon, target.Handle);
                    // Reproduce the native focus transfer made by a WinUI
                    // button before its pointer event, without waiting for
                    // delayed foreground hooks to catch up.
                    Native.SetForegroundWindow(Handle);
                    CaptureClickForeground();
                    InvokeClick(button.Entry, DockMouseButton.Left, button.Button, button.Icon);
                    defaultMinimized = await Wait(() => Native.IsIconic(target.Handle));
                }
                else if (minimizedOnly && !Settings.Items.Any(pin => TaskReconciler.Matches(pin, target))) defaultMinimized = true;
                passed &= restored && background && minimized && focused && defaultMinimized;
                results.Add(new { target.Title, Handle = target.Handle.ToInt64(), Cycle = cycle, minimizedOnly, restored, background, minimized, focused, Selected = selected.ToInt64(), CapturedForeground = foregroundBeforeClick.ToInt64(), defaultMinimized }); Save();
            }
        }
        catch (Exception ex) { passed = false; results.Add(new { Error = ex.ToString() }); Log.Write(ex); }
        finally
        {
            foreach (var target in targets.Where(t => Native.IsWindow(t.Handle)))
            {
                if (target.Minimized) ShellService.Activate(target, true);
                else Native.ShowWindowAsync(target.Handle, target.Maximized ? 3 : 9);
            }
            foreach (var helper in helpers) Native.PostMessage(helper.Handle, 0x10, 0, 0);
            fixture?.Dispose(); inspection = inspected; Settings.ShowMinimizedApplicationsOnly = only; Reconcile();
            Native.SetForegroundWindow(originalForeground); Save();
        }
    }
}
