using System.Diagnostics;
using System.Security.Principal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private bool taskViewOpen;
    private bool? taskViewTestOverride;
    private static bool IsElevated
    {
        get { using var identity = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator); }
    }
    private void RestartDock(bool elevate)
    {
        if (MonitorOverride != null) { App.Dock?.RestartDock(elevate); return; }
        try
        {
            store.Save(Settings);
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true };
            if (elevate) start.Verb = "runas";
            start.ArgumentList.Add("--wait-for-exit"); start.ArgumentList.Add(Environment.ProcessId.ToString());
            start.ArgumentList.Add(settingsWindow != null ? "--settings" : manuallyHidden ? "--hide" : "--startup");
            // Start first: cancelling UAC must leave this instance running.
            using var replacement = Process.Start(start);
            if (replacement != null) Close();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex) { Log.Write(ex); _ = Message("Could not restart the dock", ex.Message); }
    }
    private bool UpdateAutoFit(IReadOnlyList<DockEntry> entries)
    {
        if (dragging || popupCount > 0) return false;
        var displays = Native.Monitors();
        var display = displays.FirstOrDefault(m => (m.Device == SelectedMonitor || m.Identity == SelectedMonitor)) ?? displays.FirstOrDefault(m => m.Primary) ?? displays[0];
        var flags = DownloadPinLayout.IconFlags(entries);
        double margins = Vertical ? Settings.DockMarginTop + Settings.DockMarginBottom : Settings.DockMarginLeft + Settings.DockMarginRight;
        double available = ((Vertical ? display.Work.Height : display.Work.Width) - margins) / display.Scale;
        var fit = DockAutoFit.Calculate(Settings.IconSize, Settings.WindowsTaskbarHighlights ? 20 : 12, flags, Settings.IconSpacing, available,
            Motion && DisplayMagnificationEnabled ? Settings.HoverMagnificationPercent : 0, Settings.MagnificationSpan, 1 / display.Scale);
        fit = DockAutoFit.PixelAligned(fit, display.Scale);
        double spacing = DockAutoFit.PixelSpacing(DockAutoFit.Spacing(Settings.IconSpacing, fit.IconSize, Settings.IconSize), display.Scale);
        if (fit.IconSize == DisplayIconSize && fit.Padding == DisplayPadding && fit.Overflow == dockOverflow
            && spacing == DisplaySpacing && displayRequestedSize == Settings.IconSize) return false;
        ResetMagnification();
        displayIconSize = fit.IconSize; displayPadding = fit.Padding; dockOverflow = fit.Overflow;
        displaySpacing = spacing; displayRequestedSize = Settings.IconSize;
        lastSignature = "";
        if (!dockOverflow) scroll.ChangeView(0, 0, null, true);
        return true;
    }
    private void MaintainZOrder()
    {
        if (!initialized || closing || hidden || visibilitySliding || popupCount > 0) return;
        if (!taskViewOpen && HasExternalFullscreenForeground()) return;
        SyncSurfaceBandOrder();
        var foreground = tracker.Describe(Native.GetForegroundWindow());
        bool fullscreen = foreground != null && !foreground.Minimized && foreground.Bounds.Left <= monitor.Bounds.Left
            && foreground.Bounds.Top <= monitor.Bounds.Top && foreground.Bounds.Right >= monitor.Bounds.Right && foreground.Bounds.Bottom >= monitor.Bounds.Bottom;
        if (!taskViewOpen && fullscreen && !FullscreenExclusions.Contains(Settings, foreground)) return;
        bool covered = (Native.GetWindowLongPtrW(Handle, -20).ToInt64() & 8) == 0;
        for (var above = Native.GetWindow(Handle, 3); above != 0 && !covered; above = Native.GetWindow(above, 3))
        {
            if (!Native.IsWindowVisible(above)) continue;
            if (IsWindowsTaskbar(above)) continue;
            Native.GetWindowThreadProcessId(above, out var pid);
            if (Native.DwmGetInt(above, 14, out int cloaked, 4) == 0 && cloaked != 0) continue;
            if (above == materialHandle || (pid != Environment.ProcessId && Native.Class(above) is not ("#32768" or "tooltips_class32"))) covered = true;
        }
        if (covered)
        {
            Native.SetWindowPos(Handle, -1, 0, 0, 0, 0, 0x13);
            if (materialVisible) Native.SetWindowPos(materialHandle, Handle, 0, 0, 0, 0, 0x13);
        }
        KeepTaskbarsAboveDock();
    }
    private static bool IsWindowsTaskbar(nint window) => Native.Class(window) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    private void KeepTaskbarsAboveDock()
    {
        if (!initialized || closing || hidden || taskViewOpen || HasExternalFullscreenForeground()) return;
        // Keep both dock surfaces topmost. Lowering them behind an arbitrary
        // taskbar can also put them behind application windows in that chain.
        // Instead, raise only visible taskbars that are currently below us.
        var bars = new List<nint>();
        for (var below = Native.GetWindow(Handle, 2); below != 0; below = Native.GetWindow(below, 2))
            if (IsWindowsTaskbar(below) && Native.IsWindowVisible(below)
                && (Native.DwmGetInt(below, 14, out int cloaked, 4) != 0 || cloaked == 0)) bars.Add(below);
        foreach (var bar in bars) Native.SetWindowPos(bar, -1, 0, 0, 0, 0, 0x13);
    }
    private async Task CheckWindowBehavior()
    {
        bool createdSettings = settingsWindow == null, originalInspection = inspection;
        Process? fixture = null; Exception? failure = null; var checks = new List<string>();
        try
        {
            inspection = true; ShowDock(); ShowSettings(); await Task.Delay(200); Reconcile();
            var own = tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out var pid); return pid == Environment.ProcessId; }).ToArray();
            if (own.Length != 1 || own[0].Handle != tracker.OwnTaskWindow) throw new Exception("Settings window missing, or internal dock surfaces leaked into task list");
            checks.Add("Only Settings appears as an application task");
            ShellService.Activate(own[0], true); await Task.Delay(150);
            if (!Native.IsIconic(own[0].Handle)) throw new Exception("Settings task could not be minimized");
            ShellService.Activate(own[0], false); await Task.Delay(150);
            if (Native.IsIconic(own[0].Handle)) throw new Exception("Settings task could not be restored");
            checks.Add("Settings task minimizes and restores");
            var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tests", "WinMac.Dock.Fixture", "bin", "Release", "net10.0-windows", "WinMac.Dock.Fixture.exe"));
            fixture = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            TaskWindow? window = null;
            for (int attempt = 0; attempt < 50 && window == null; attempt++)
            {
                await Task.Delay(100);
                window = tracker.Read().FirstOrDefault(w => { Native.GetWindowThreadProcessId(w.Handle, out var pid); return pid == fixture!.Id; });
            }
            if (window == null) throw new Exception("Fixture did not create a task window");
            await Task.Delay(350);
            nint foreground = Native.GetForegroundWindow();
            Native.SetWindowPos(window.Handle, -1, resting.Left, resting.Top - 150, 400, 300, 0x10 | 0x40);
            bool IsAbove(nint first, nint second)
            {
                for (var h = Native.GetWindow(second, 3); h != 0; h = Native.GetWindow(h, 3)) if (h == first) return true;
                return false;
            }
            if (!IsAbove(window.Handle, Handle)) throw new Exception("Could not arrange an external topmost window over the dock");
            await Task.Delay(250);
            if (IsAbove(window.Handle, Handle)) throw new Exception($"Dock stayed behind external topmost: hidden={hidden}, sliding={visibilitySliding}, popups={popupCount}, foreground={Native.Title(Native.GetForegroundWindow())}, active={tracker.Describe(Native.GetForegroundWindow())}, dock={resting}, fixture={Native.Bounds(window.Handle)}");
            if (foreground != Native.GetForegroundWindow()) throw new Exception("Restoring dock priority stole foreground focus");
            checks.Add("Dock rises above another topmost window without taking focus");
            var taskbars = new List<nint>();
            Native.EnumWindows((hwnd, _) => { if (IsWindowsTaskbar(hwnd) && Native.IsWindowVisible(hwnd)) taskbars.Add(hwnd); return true; }, 0);
            if (taskbars.Any(bar => !IsAbove(bar, Handle))) throw new Exception("Dock is above a visible Windows taskbar");
            if (materialVisible && !IsAbove(Handle, materialHandle)) throw new Exception("Dock material is above its icons");
            if ((Native.GetWindowLongPtrW(Handle, -20).ToInt64() & 8) == 0) throw new Exception("Dock lost its topmost state");
            checks.Add("Visible taskbars remain above both topmost dock surfaces, and application windows remain below");
            foreach (var sample in new[]
            {
                new DockItem { Kind = ItemKind.Application, Name = "System Settings", Target = "ms-settings:" },
                new DockItem { Kind = ItemKind.Application, Name = "Task Manager", Target = "Taskmgr.exe" },
                new DockItem { Kind = ItemKind.Application, Name = "Microsoft Store", Target = "ms-windows-store://home" }
            })
            {
                var entry = new DockEntry(sample, window, true);
                InvokeSingletonMenu(entry); await Task.Delay(200);
                if (!Native.IsIconic(window.Handle)) throw new Exception(sample.Name + " action did not minimize its existing window");
                var minimized = ItemMenu(entry).Items.OfType<MenuFlyoutItem>().Select(i => i.Text).ToArray();
                if (!minimized.Contains("Open " + sample.Name)) throw new Exception(sample.Name + " minimized window does not offer Open");
                InvokeSingletonMenu(entry); await Task.Delay(200);
                if (Native.IsIconic(window.Handle)) throw new Exception(sample.Name + " action did not restore its existing window");
                var restored = ItemMenu(entry).Items.OfType<MenuFlyoutItem>().Select(i => i.Text).ToArray();
                if (!restored.Contains("Minimize " + sample.Name)) throw new Exception(sample.Name + " restored window does not offer Minimize");
            }
            checks.Add("Settings, Task Manager, and Store toggle their existing windows and update Open/Minimize labels");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            if (fixture != null)
            {
                foreach (var task in tracker.Read().Where(w => { Native.GetWindowThreadProcessId(w.Handle, out var pid); return pid == fixture.Id; })) ShellService.Close(task);
                fixture.Dispose();
            }
            if (createdSettings) settingsWindow?.Close();
            inspection = originalInspection; Reconcile();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "window-behavior-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
    private async Task CheckAutoFit()
    {
        var original = Settings.Items.ToList(); var originalEdge = PositionEdge;
        bool originalInspection = inspection;
        Exception? failure = null; var results = new List<object>();
        try
        {
            inspection = true;
            var sample = new DockItem { Kind = ItemKind.Application, Target = Environment.ProcessPath!, Name = "Fit test" };
            foreach (var edge in Enum.GetValues<DockEdge>())
            foreach (int count in new[] { 8, 30, 50, 70, 150, 8 })
            {
                PositionEdge = edge;
                Settings.Items = Enumerable.Range(0, count).Select(i => sample with { Id = "fit-" + i }).ToList();
                lastSignature = ""; Reposition(true); Reconcile(); root.UpdateLayout(); await Task.Delay(100);
                double available = (Vertical ? hostBounds.Height : hostBounds.Width) / scale;
                double content = DockSpacing.Length(strip.Children.OfType<FrameworkElement>().Select(e => e is Button ? ButtonSize : SeparatorWidth).ToArray(), strip.Children.Select(e => e is Button).ToArray(), DisplaySpacing);
                if (!dockOverflow && content > available + 1) throw new Exception($"{edge}/{count}: content {content} exceeds viewport {available} at icon size {DisplayIconSize}");
                if (DisplayIconSize > Settings.IconSize || DisplayIconSize < 16) throw new Exception("Auto-fit exceeded icon-size bounds");
                double requiredExpansion = Motion ? DockAutoFit.ExpansionBudget(DisplayIconSize, buttons.Count, Settings.HoverMagnificationPercent, Settings.MagnificationSpan) : 0;
                double actualExpansion = available - (Vertical ? resting.Height : resting.Width) / scale;
                if (!dockOverflow && DisplayIconSize > 16 && actualExpansion + 1 / scale < requiredExpansion)
                    throw new Exception($"{edge}/{count}: magnification needs {requiredExpansion} DIP but has only {actualExpansion}");
                if (dockOverflow)
                {
                    scroll.ChangeView(Vertical ? null : scroll.ScrollableWidth, Vertical ? scroll.ScrollableHeight : null, null, true);
                    await Task.Delay(100);
                    var last = strip.Children.OfType<FrameworkElement>().Last();
                    var p = last.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    if ((Vertical ? p.Y + last.ActualHeight : p.X + last.ActualWidth) > (Vertical ? hostBounds.Height : hostBounds.Width) / scale + 2)
                        throw new Exception("Scrolling did not expose the final item");
                }
                results.Add(new { Edge = edge.ToString(), Count = count, Requested = Settings.IconSize, Actual = DisplayIconSize, Padding = DisplayPadding, Overflow = dockOverflow, Available = available, Content = content, RequiredExpansion = requiredExpansion, ActualExpansion = actualExpansion });
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { ResetMagnification(); Settings.Items = original; PositionEdge = originalEdge; inspection = originalInspection; lastSignature = ""; Reconcile(); Reposition(true); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "autofit-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Results = results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
