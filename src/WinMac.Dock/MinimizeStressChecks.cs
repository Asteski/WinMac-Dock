using System.Diagnostics;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private async Task CheckMinimizeStress()
    {
        if (rowTesting || hoverTestInput != null) return;
        bool inspected = inspection, only = Settings.ShowMinimizedApplicationsOnly;
        var pins = Settings.Items.ToList();
        Process? fixture = null; Exception? failure = null;
        var checks = new List<string>();
        TaskWindow[] tasks = [];
        async Task Wait(Func<bool> condition, string message)
        {
            for (int i = 0; i < 800; i++) { if (condition()) return; await Task.Delay(25); }
            throw new Exception(message);
        }
        try
        {
            inspection = true;
            Settings.ShowMinimizedApplicationsOnly = false;
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tests", "WinMac.Dock.Fixture", "bin", "Release", "net10.0-windows", "WinMac.Dock.Fixture.exe"));
            var start = new ProcessStartInfo(path) { UseShellExecute = true };
            start.ArgumentList.Add("--foreground-client"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            fixture = Process.Start(start);
            await Wait(() => { tasks = tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out uint pid); return pid == fixture!.Id; }).ToArray(); return tasks.Length == 2; }, "Fixture did not appear");
            await Task.Delay(500);
            var pin = new DockItem { Name = "Minimize stress fixture", Target = path, ExecutablePath = path, Kind = ItemKind.Application };
            Settings.Items.Add(pin); lastSignature = ""; Reconcile();
            for (int cycle = 0; cycle < 24; cycle++)
            {
                var target = tasks[cycle % 2]; var other = tasks[1 - cycle % 2];
                // Use a single-window entry to test an unfocused window even
                // when the user's normal configuration groups both fixtures.
                var entry = new DockEntry(pin, target, true);
                ShellService.Activate(target, false);
                await Wait(() => !Native.IsIconic(target.Handle), "Restore did not complete");
                ShellService.Activate(other, false);
                await Wait(() => { if (Native.GetForegroundWindow() != other.Handle) ShellService.Activate(other, false); return Native.GetForegroundWindow() == other.Handle; }, "Background scenario did not establish foreground");
                Reconcile();
                var button = buttons.First(b => b.Entry.Item.Id == pin.Id);
                if (cycle % 2 == 0 && Motion)
                {
                    hoverTestInput = () => ((hoverWave?.Length ?? 1) * .5, true);
                    UpdateMagnification(0);
                }
                // Repeated pulses overlap while the window changes state, as
                // they do when a user retries a click during shell animation.
                for (int repeat = 0; repeat < 4; repeat++)
                {
                    PulseActivatedItem(button.Icon);
                    ExecuteClickAction(entry, DockClickAction.Minimize, button.Button, button.Icon, other.Handle);
                    await Task.Delay(15);
                }
                await Wait(() => Native.IsIconic(target.Handle), $"Background minimize failed in cycle {cycle}");
                ShellService.Activate(target, false);
                await Wait(() => !Native.IsIconic(target.Handle) && Native.GetForegroundWindow() == target.Handle, $"Restore/focus failed in cycle {cycle}");
                ExecuteClickAction(entry, DockClickAction.Default, button.Button, button.Icon, target.Handle);
                await Wait(() => Native.IsIconic(target.Handle), $"Focused default click did not minimize in cycle {cycle}");
                hoverTestInput = null; ResetMagnification();
                checks.Add($"Cycle {cycle + 1}: background minimize, rapid feedback, restore/focus and focused toggle");
            }
            await Task.Delay(400);
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            hoverTestInput = null; ResetMagnification();
            Settings.Items = pins; Settings.ShowMinimizedApplicationsOnly = only;
            inspection = inspected; lastSignature = ""; Reconcile();
            foreach (var task in tasks) Native.PostMessage(task.Handle, 0x10, 0, 0);
            fixture?.Dispose();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "minimize-stress-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
