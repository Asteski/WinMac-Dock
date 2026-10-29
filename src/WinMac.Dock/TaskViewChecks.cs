using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private int requestedTaskViewBand;
    private void RequestTaskViewBand(int band)
    {
        if (requestedTaskViewBand == band) return;
        
        Native.SetPropW(Handle, "WinMac.Dock.TaskViewBand", band);
        if (materialHandle != 0) Native.SetPropW(materialHandle, "WinMac.Dock.TaskViewBand", band);
        requestedTaskViewBand = band;
        // Wake the existing Explorer-owned broker immediately. Waiting for
        // its 200ms fallback timer leaves the dock covered at Task View entry.
        var broker = Native.FindWindowExW(-3, 0, "WinMac.Dock.TaskViewBroker.V1", null);
        if (broker != 0) Native.PostMessage(broker, 0x0113, 1, 0);
    }
    private async Task CheckTaskViewBridge()
    {
        bool original = Settings.ShowInTaskView;
        bool? originalOverride = taskViewTestOverride;
        Exception? failure = null; var results = new List<object>();
        bool actualTaskView = Native.IsTaskViewOpen();
        try
        {
            foreach (var scenario in new[] { (Enabled: false, Open: false), (Enabled: true, Open: false), (Enabled: true, Open: true), (Enabled: true, Open: false), (Enabled: true, Open: true), (Enabled: false, Open: true), (Enabled: true, Open: false) })
            {
                var watch = System.Diagnostics.Stopwatch.StartNew();
                Settings.ShowInTaskView = scenario.Enabled; taskViewTestOverride = scenario.Open; EvaluateVisibility();
                uint expected = scenario.Enabled && scenario.Open ? 4u : 1u;
                for (int i = 0; i < 100 && (Native.WindowBand(Handle) != expected || Native.WindowBand(materialHandle) != expected); i++) await Task.Delay(30);
                uint dockBand = Native.WindowBand(Handle), backgroundBand = Native.WindowBand(materialHandle);
                if (dockBand != expected || backgroundBand != expected) throw new Exception($"Bridge did not apply {expected}: dock={dockBand}, background={backgroundBand}");
                SyncSurfaceBandOrder();
                results.Add(new { Enabled = scenario.Enabled, TaskViewOpen = scenario.Open, DockBand = dockBand, MaterialBand = backgroundBand, BridgeVersion = Native.GetPropW(Handle, "WinMac.Dock.TaskViewBridgeVersion").ToInt64(), ApplyMs = watch.Elapsed.TotalMilliseconds });
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { Settings.ShowInTaskView = original; taskViewTestOverride = originalOverride; EvaluateVisibility(); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "task-view-bridge-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), ActualTaskViewOpen = actualTaskView, Results = results }, new JsonSerializerOptions { WriteIndented = true }));
    }
    private async Task CheckTaskView()
    {
        if (rowTesting || hoverTestInput != null) return;
        var original = Settings with { };
        bool inspected = inspection, manual = manuallyHidden, wasHidden = hidden;
        Exception? failure = null; var checks = new List<string>(); string? nativeDesktopCheck = null;
        Window? fixture = null;
        nint originalOwnTask = tracker.OwnTaskWindow;
        long moves = 0, clips = rowClipErrors;
        try
        {
            inspection = false; taskViewTestOverride = true;
            Settings.ReduceMotion = true;
            foreach (var mode in Enum.GetValues<HideMode>())
            {
                DisplayHideMode = mode; manuallyHidden = false;
                Settings.ShowInTaskView = false; EvaluateVisibility();
                if (!hidden || visibilitySliding) throw new Exception($"Task View did not immediately hide dock in {mode}");
                Settings.ShowInTaskView = true; EvaluateVisibility();
                if (hidden || visibilitySliding) throw new Exception($"Task View did not show dock in {mode}");
                checks.Add($"Task View overrides {mode} in both toggle states");
            }
            manuallyHidden = true; EvaluateVisibility();
            if (!hidden || !manuallyHidden) throw new Exception("Task View discarded shortcut hide");
            checks.Add("Task View respects explicit shortcut hide");
            taskViewTestOverride = false; manuallyHidden = false; DisplayHideMode = HideMode.AlwaysVisible; EvaluateVisibility();
            if (hidden) throw new Exception("Leaving Task View did not restore normal visibility");
            checks.Add("Leaving Task View resumes ordinary visibility");

            inspection = true; rowTesting = true; hoverTestInput = () => (0, false);
            Settings.ReduceMotion = false; Settings.ReserveSpace = false; Settings.GroupWindows = false;
            Settings.ShowItemsFromAllVirtualDesktops = false; Settings.AnimationMs = 240;
            PositionEdge = DockEdge.Bottom; PositionAlignment = DockAlignment.Center;
            ApplySettings(); FinishRowTransition(); ResetMagnification();
            var tasks = tracker.Read();
            var extra = new TaskWindow((nint)0x7ffffffe, "Desktop-specific test window", @"C:\WinMac-Desktop-Fixture.exe", "", default, false, false);
            foreach (var edge in Enum.GetValues<DockEdge>())
            foreach (var alignment in Enum.GetValues<DockAlignment>())
            {
                PositionEdge = edge; PositionAlignment = alignment; stressTasks = tasks; ApplySettings();
                FinishRowTransition(); ResetMagnification();
                var host = hostBounds; long before = hoverWindowMoves;
                for (int i = 0; i < 4; i++)
                {
                    stressTasks = i % 2 == 0 ? tasks.Append(extra).ToArray() : tasks;
                    Reconcile();
                    if (!rowAnimating) throw new Exception($"Desktop row did not animate: {edge}/{alignment}");
                    if (hostBounds != host || Native.Bounds(Handle) != host || Native.Bounds(materialHandle) != MaterialPanel)
                        throw new Exception($"Desktop membership moved the input host or detached the material frame: {edge}/{alignment}");
                    await Task.Delay(DockMotion.RearrangeMs + 100);
                    if (rowAnimating || MaterialPanel != resting) throw new Exception("Desktop row failed to settle");
                }
                moves += hoverWindowMoves - before;
                checks.Add($"{edge}/{alignment}: four desktop membership changes keep the input host stationary and material attached to the panel");
            }
            stressTasks = null;
            PositionEdge = DockEdge.Bottom; PositionAlignment = DockAlignment.Center; ApplySettings(); FinishRowTransition();
            // The public desktop manager can only move this process's own
            // windows. Use a disposable window, never a user's application.
            fixture = new Window { Title = "Dock desktop test", Content = new TextBlock { Text = "Temporary dock desktop test" } };
            var fixtureHandle = WinRT.Interop.WindowNative.GetWindowHandle(fixture);
            tracker.OwnTaskWindow = fixtureHandle;
            fixture.AppWindow.Show(false);
            await Task.Delay(500);
            var home = Native.WindowDesktop(fixtureHandle);
            var away = tracker.Read(true).Select(w => Native.WindowDesktop(w.Handle)).FirstOrDefault(id => id != null && id != Guid.Empty && id != home);
            if (away == null)
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops");
                if (key?.GetValue("VirtualDesktopIDs") is byte[] ids)
                    for (int i = 0; i + 16 <= ids.Length; i += 16)
                    {
                        var id = new Guid(ids.AsSpan(i, 16));
                        if (id != Guid.Empty && id != home) { away = id; break; }
                    }
            }
            if (home != null && away != null)
            {
                await Task.Delay(500); var host = hostBounds; long before = hoverWindowMoves;
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    if (!Native.MoveToDesktop(fixtureHandle, away.Value)) throw new Exception("Could not move own fixture to another desktop");
                    await Task.Delay(900);
                    if (buttons.Any(b => b.Entry.Windows.Any(w => w.Handle == fixtureHandle))) throw new Exception("Off-desktop fixture remains in row");
                    if (!Native.MoveToDesktop(fixtureHandle, home.Value)) throw new Exception("Could not restore own fixture desktop");
                    await Task.Delay(900);
                    if (!buttons.Any(b => b.Entry.Windows.Any(w => w.Handle == fixtureHandle))) throw new Exception("Returned fixture missing from row");
                    if (hostBounds != host || Native.Bounds(Handle) != host || Native.Bounds(materialHandle) != resting) throw new Exception("Real desktop membership moved input host or detached material frame");
                }
                moves += hoverWindowMoves - before;
                nativeDesktopCheck = "Passed: own fixture moved away and back across existing desktops three times";
            }
            else nativeDesktopCheck = "Not exercised: no existing second desktop with an application window was discoverable";
            if (moves != 0 || rowClipErrors != clips) throw new Exception($"Desktop row instability: native moves={moves}, clips={rowClipErrors - clips}");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            FinishRowTransition(); stressTasks = null; rowTesting = false; hoverTestInput = null; taskViewTestOverride = null;
            if (fixture != null)
            {
                fixture.Close();
            }
            tracker.OwnTaskWindow = originalOwnTask;
            Settings.ShowInTaskView = original.ShowInTaskView; DisplayHideMode = original.HideMode; Settings.ReduceMotion = original.ReduceMotion;
            Settings.ReserveSpace = original.ReserveSpace; Settings.GroupWindows = original.GroupWindows;
            Settings.ShowItemsFromAllVirtualDesktops = original.ShowItemsFromAllVirtualDesktops;
            Settings.AnimationMs = original.AnimationMs; PositionEdge = original.Edge; PositionAlignment = original.Alignment;
            inspection = inspected; manuallyHidden = manual;
            ResetMagnification(); ApplySettings();
            if (wasHidden) HideDock(immediate: true); else if (hidden) ShowDock(restoreManualReservation: !manual);
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "task-view-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(),
            NativeSurfaceMoves = moves, ClipViolations = rowClipErrors - clips, NativeDesktopCheck = nativeDesktopCheck, Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
