using System.Text.Json;
using System.Diagnostics;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckAdministratorLaunch()
    {
        var checks = new List<string>(); Exception? failure = null;
        bool hadSettings = settingsWindow != null;
        string fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tests", "WinMac.Dock.Fixture", "bin", "Release", "net10.0-windows", "WinMac.Dock.Fixture.exe"));
        string link = Path.Combine(Log.DirectoryPath, "administrator-fixture.lnk");
        Process? ordinary = null;
        try
        {
            var sample = new DockItem { Kind = ItemKind.Application, Name = "Administrator test", Target = fixture, ExecutablePath = fixture, Arguments = "--probe arguments", RunAsAdministrator = true };
            var checkbox = ShellService.LaunchInfo(sample); var menu = ShellService.LaunchInfo(sample with { RunAsAdministrator = false }, true);
            if (checkbox.Verb != "runas" || menu.Verb != checkbox.Verb || checkbox.FileName != menu.FileName || checkbox.Arguments != menu.Arguments) throw new Exception("Checkbox and menu disagree on elevation");
            checks.Add("Checkbox and item menu share the runas launch plan");
            void CheckMenu(DockEntry entry)
            {
                var labels = ItemMenu(entry).Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().Select(i => i.Text).ToArray();
                if (entry.Item.RunAsAdministrator)
                {
                    if (labels.Count(s => s == "Open") != 1 || labels.Count(s => s == "Open without elevation") != 1 || labels.Contains("Run as administrator"))
                        throw new Exception("Administrator menu does not express the saved preference and one-time override");
                }
                else if (!labels.Contains("Run as administrator") || !labels.Contains(entry.Window == null ? "Open" : "Open new window") || labels.Contains("Open without elevation"))
                    throw new Exception("Ordinary launch menu labels changed unexpectedly");
            }
            CheckMenu(new DockEntry(sample, null, true));
            CheckMenu(new DockEntry(sample with { RunAsAdministrator = false }, null, true));
            checks.Add("Stopped-item menu labels reflect the saved administrator preference");
            object? shell = null, shortcut = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                shortcut = ((dynamic)shell!).CreateShortcut(link);
                ((dynamic)shortcut).TargetPath = fixture; ((dynamic)shortcut).Arguments = "--shortcut-preserved"; ((dynamic)shortcut).Save();
            }
            finally { if (shortcut != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shortcut); if (shell != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell); }
            var linkInfo = ShellService.LaunchInfo(new DockItem { Target = link }, true);
            if (linkInfo.FileName != fixture || linkInfo.Arguments != "--shortcut-preserved") throw new Exception("Shortcut target or arguments were lost");
            checks.Add("Shortcut elevation resolves the current executable and preserves arguments");
            var terminal = Settings.Items.FirstOrDefault(i => i.AppUserModelId.StartsWith("Microsoft.WindowsTerminal_", StringComparison.OrdinalIgnoreCase))
                ?? new DockItem { Name = "Terminal", Target = "shell:AppsFolder\\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", AppUserModelId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", Kind = ItemKind.Application };
            var terminalInfo = ShellService.LaunchInfo(terminal, true);
            if (Path.GetFileName(terminalInfo.FileName) != "wt.exe" || !terminalInfo.Arguments.StartsWith("--window new ") || terminalInfo.Verb != "runas") throw new Exception("Terminal did not resolve its registered elevation alias");
            checks.Add("Installed Terminal resolves to its registered wt.exe alias and requests a new elevated window");
            async Task Probe(string name, Action<string> launch, bool expectedElevation = true)
            {
                string report = Path.Combine(Log.DirectoryPath, "administrator-" + name + ".json");
                if (File.Exists(report)) File.Delete(report);
                launch(report);
                for (int attempt = 0; attempt < 100 && !File.Exists(report); attempt++) await Task.Delay(100);
                if (!File.Exists(report)) throw new Exception(name + " did not produce an elevation report");
                using var result = JsonDocument.Parse(File.ReadAllText(report));
                if (result.RootElement.GetProperty("Elevated").GetBoolean() != expectedElevation) throw new Exception(name + " launched with the wrong elevation token");
                checks.Add(name + (expectedElevation ? ": actual child process has an administrator token" : ": actual child process has an unelevated token"));
            }
            await Probe("one-time-ordinary", report => ShellService.LaunchWithoutElevation(sample with { Arguments = "--elevation-probe \"" + report + "\"" }), false);
            if (!sample.RunAsAdministrator) throw new Exception("One-time override changed the saved preference");
            await Probe("checkbox", report => ShellService.Launch(sample with { Arguments = "--elevation-probe \"" + report + "\"" }));
            await Probe("shortcut-menu", report => ShellService.LaunchAsAdministrator(new DockItem { Target = link, Arguments = "--elevation-probe \"" + report + "\"" }));
            await Probe("terminal-menu", report => ShellService.LaunchAsAdministrator(terminal with { Arguments = "new-tab \"" + fixture + "\" --elevation-probe \"" + report + "\"" }));
            ordinary = Process.Start(new ProcessStartInfo(fixture) { UseShellExecute = true });
            TaskWindow[] tasks = [];
            for (int attempt = 0; attempt < 50 && tasks.Length == 0; attempt++)
            {
                await Task.Delay(100);
                tasks = tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out var pid); return pid == ordinary!.Id; }).ToArray();
            }
            if (tasks.Length == 0 || ShellService.WindowIsElevated(tasks[0].Handle) != IsElevated)
                throw new Exception($"Existing window elevation detection failed: windows={tasks.Length}, token={(tasks.Length == 0 ? null : ShellService.WindowIsElevated(tasks[0].Handle))}, dock={IsElevated}, exited={ordinary!.HasExited}");
            checks.Add("Existing task token elevation is detected");
            foreach (bool elevated in new[] { false, true })
            {
                CheckMenu(new DockEntry(sample with { RunAsAdministrator = elevated }, tasks[0], true));
                CheckMenu(new DockEntry(sample with { RunAsAdministrator = elevated }, tasks[0], true, tasks));
            }
            checks.Add("Running and grouped-item menus retain both enabled launch choices");
            if (!IsElevated)
                await Probe("existing-task-checkbox", report =>
                {
                    var entry = new DockEntry(sample with { Arguments = "--elevation-probe \"" + report + "\"" }, tasks[0], true, tasks);
                    ExecuteClickAction(entry, DockClickAction.Default, root, root, tasks[0].Handle);
                });
            if ((settingsWindow != null) != hadSettings) throw new Exception("Administrator launch opened Settings");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            if (ordinary != null)
            {
                foreach (var window in tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out var pid); return pid == ordinary.Id; })) ShellService.Close(window);
                ordinary.Dispose();
            }
            if (File.Exists(link)) File.Delete(link);
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "administrator-launch-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
