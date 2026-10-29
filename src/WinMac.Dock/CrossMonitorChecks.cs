using System.Diagnostics;
using System.Text.Json;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private Process? crossMonitorFixture;
    private TaskWindow[] crossMonitorWindows = [];
    private DockItem? crossMonitorPin;
    private string? crossMonitorOriginalDisplay;
    private bool crossMonitorOriginalTaskView, crossMonitorOriginalInspection;
    private MonitorData? crossMonitorHome, crossMonitorAway;
    private async Task PrepareCrossMonitorTest()
    {
        if (crossMonitorFixture != null || hoverTestInput != null) return;
        var displays = Native.Monitors();
        crossMonitorHome = displays.First(m => m.Device == monitor.Device);
        crossMonitorAway = displays.FirstOrDefault(m => m.Device != monitor.Device);
        if (crossMonitorAway == null) { Log.Write("Cross-monitor test requires a second connected monitor"); return; }
        crossMonitorOriginalDisplay = Settings.Monitor;
        crossMonitorOriginalTaskView = Settings.ShowInTaskView;
        crossMonitorOriginalInspection = inspection;
        try
        {
            string path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tests", "WinMac.Dock.Fixture", "bin", "Release", "net10.0-windows", "WinMac.Dock.Fixture.exe"));
            crossMonitorFixture = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            for (int i = 0; i < 100; i++)
            {
                crossMonitorWindows = tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out uint pid); return pid == crossMonitorFixture!.Id; }).ToArray();
                if (crossMonitorWindows.Length == 2) break;
                await Task.Delay(30);
            }
            if (crossMonitorWindows.Length != 2) throw new Exception("Cross-monitor fixture did not appear");
            await Task.Delay(400);
            crossMonitorPin = new DockItem { Name = "Cross-monitor test", Kind = ItemKind.Application, Target = path, ExecutablePath = path };
            Settings.Items.Add(crossMonitorPin);
            Settings.ShowInTaskView = false; inspection = true; 
            await PlaceCrossMonitorFixture(crossMonitorAway);
            lastSignature = ""; Reposition(true); Reconcile(); HandleCommand("--inspect");
            WriteCrossMonitorReport();
        }
        catch (Exception ex) { Log.Write(ex); FinishCrossMonitorTest(); }
    }
    private async Task PlaceCrossMonitorFixture(MonitorData display)
    {
        for (int i = 0; i < crossMonitorWindows.Length; i++)
        {
            var task = crossMonitorWindows[i];
            if (Native.IsIconic(task.Handle))
            {
                Native.ShowWindowAsync(task.Handle, 9);
                for (int attempt = 0; attempt < 100 && Native.IsIconic(task.Handle); attempt++) await Task.Delay(20);
            }
            Native.SetWindowPos(task.Handle, 0, display.Work.Left + 60 + i * 40, display.Work.Top + 80 + i * 40,
                Math.Min(500, display.Work.Width - 140), Math.Min(340, display.Work.Height - 160), 0x54);
        }
    }
    private async Task SwapCrossMonitorTest()
    {
        if (crossMonitorFixture == null || crossMonitorHome == null || crossMonitorAway == null) return;
        ResetMagnification();
        bool dockAtHome = monitor.Device == crossMonitorHome.Device;
        Settings.Monitor = dockAtHome ? crossMonitorAway.Device : crossMonitorHome.Device;
        await PlaceCrossMonitorFixture(dockAtHome ? crossMonitorHome : crossMonitorAway);
        lastSignature = ""; Reposition(true); Reconcile(); WriteCrossMonitorReport();
    }
    private void WriteCrossMonitorReport()
    {
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "cross-monitor-test.json"), JsonSerializer.Serialize(new {
            DockDisplay = monitor.Device, DockBounds = resting, Displays = Native.Monitors().Select(m => new { m.Device, m.Bounds, m.Work, m.Primary, m.Scale }),
            Windows = crossMonitorWindows.Select(w => new { Handle = w.Handle.ToInt64(), Display = Native.Monitors().First(m => m.Handle == Native.MonitorFromWindow(w.Handle, 2)).Device, Bounds = Native.Bounds(w.Handle), Minimized = Native.IsIconic(w.Handle) })
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private void FinishCrossMonitorTest()
    {
        if (crossMonitorFixture == null) return;
        foreach (var task in crossMonitorWindows) Native.PostMessage(task.Handle, 0x10, 0, 0);
        crossMonitorFixture.Dispose(); crossMonitorFixture = null;
        if (crossMonitorPin != null) Settings.Items.Remove(crossMonitorPin);
        Settings.Monitor = crossMonitorOriginalDisplay ?? "";
        Settings.ShowInTaskView = crossMonitorOriginalTaskView; inspection = crossMonitorOriginalInspection;
        crossMonitorPin = null; crossMonitorWindows = [];
        ResetMagnification(); lastSignature = ""; Reposition(true); Reconcile(); HandleCommand("--inspect-off");
    }
}
