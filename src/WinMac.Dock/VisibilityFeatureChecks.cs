using System.Text.Json;
using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckVisibilityFeatures()
    {
        if (rowTesting || hoverTestInput != null) return;
        var saved = Settings with { };
        var taskViewOverride = taskViewTestOverride;
        bool inspected = inspection, wasHidden = hidden, manual = manuallyHidden, waiting = startupWaiting, latched = edgeToggleHidden;
        var checks = new List<string>(); Exception? failure = null;
        void Check(bool ok, string name) { if (!ok) throw new Exception(name); checks.Add(name); }
        MenuFlyout? menu = null;
        Process? workAreaFixture = null;
        try
        {
            inspection = true; hoverTestInput = () => (0, false);
            taskViewTestOverride = false; Settings.HideDelayMs = 0;
            Settings.ReduceMotion = true; Settings.RevealDelayMs = 0; Settings.ReserveSpace = false;
            Settings.HideWindowsTaskbar = false; DisplayHideMode = HideMode.EdgeToggle; ApplySettings();
            foreach (var edge in Enum.GetValues<DockEdge>())
            {
                PositionEdge = edge; ApplySettings();
                edgeActivation.Reset(); edgeToggleHidden = true; HideDock(true);
                var away = new Native.POINT { X = (resting.Left + resting.Right) / 2, Y = (resting.Top + resting.Bottom) / 2 };
                EvaluateEdgeVisibility(away, false, false, false, false);
                // Exercise the same state transition as a physical edge contact.
                var contact = edge switch {
                    DockEdge.Top => new Native.POINT { X = away.X, Y = resting.Top - Settings.FloatingGap },
                    DockEdge.Left => new Native.POINT { X = resting.Left - Settings.FloatingGap, Y = away.Y },
                    DockEdge.Right => new Native.POINT { X = resting.Right + Settings.FloatingGap, Y = away.Y },
                    _ => new Native.POINT { X = away.X, Y = resting.Bottom + Settings.FloatingGap } };
                EvaluateEdgeVisibility(contact, true, false, false, false);
                Check(!hidden, $"{edge}: edge touch shows dock");
                EvaluateEdgeVisibility(contact, true, false, false, false);
                Check(!hidden, $"{edge}: stationary contact does not toggle again");
                EvaluateEdgeVisibility(away, false, true, false, false);
                Check(hidden && !edgeToggleHidden, $"{edge}: fullscreen hides without changing toggle state");
                EvaluateEdgeVisibility(away, false, false, false, false);
                Check(!hidden, $"{edge}: leaving fullscreen restores visible toggle state");
                EvaluateEdgeVisibility(contact, true, false, false, false);
                Check(hidden, $"{edge}: second deliberate contact hides dock");
            }
            DisplayHideMode = HideMode.AfterDelay; startupWaiting = true; HideDock(true);
            EvaluateEdgeVisibility(new Native.POINT(), false, false, false, false);
            Check(hidden && startupWaiting, "Start hidden remains hidden until activation");
            EvaluateEdgeVisibility(new Native.POINT(), true, false, false, false);
            Check(!hidden && !startupWaiting, "First activation ends startup waiting");
            foreach (var edge in Enum.GetValues<DockEdge>())
            foreach (var mode in Enum.GetValues<HideMode>())
            {
                PositionEdge = edge; DisplayHideMode = mode; ApplySettings(); ShowDock();
                await Task.Delay(100);
                var button = buttons.First(b => b.Entry.Item.Kind != ItemKind.Separator);
                menu = ItemMenu(button.Entry); ShowDockFlyout(menu, button.Button);
                Check(popupCount > 0, $"{edge}/{mode}: menu holds dock from opening phase");
                await Task.Delay(180);
                inspection = false; EvaluateVisibility(); inspection = true;
                Check(popupCount > 0 && !hidden, $"{edge}/{mode}: menu holds dock visible");
                var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).FirstOrDefault(p => p.Child is Microsoft.UI.Xaml.Controls.MenuFlyoutPresenter);
                Check(popup?.Child is FrameworkElement, $"{edge}/{mode}: menu opened");
                var presenter = (FrameworkElement)popup!.Child;
                var origin = presenter.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                double x = hostBounds.Left + origin.X * scale, y = hostBounds.Top + origin.Y * scale;
                bool outside = edge switch {
                    DockEdge.Top => y >= visibleBounds.Bottom - 2,
                    DockEdge.Left => x >= visibleBounds.Right - 2,
                    DockEdge.Right => x + presenter.ActualWidth * scale <= visibleBounds.Left + 2,
                    _ => y + presenter.ActualHeight * scale <= visibleBounds.Top + 2 };
                Check(outside, $"{edge}/{mode}: menu outside dock ({x:F0},{y:F0}, {presenter.ActualWidth:F0}x{presenter.ActualHeight:F0}; dock {visibleBounds})");
                menu.Hide(); menu = null; await Task.Delay(80);
            }
            uint taskbarState = WindowsTaskbar.State();
            var previousBars = new Dictionary<nint, bool>();
            Native.EnumWindows((hwnd, _) => { if (Native.Class(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") previousBars[hwnd] = Native.IsWindowVisible(hwnd); return true; }, 0);
            string fixturePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tests", "WinMac.Dock.Fixture", "bin", "Release", "net10.0-windows", "WinMac.Dock.Fixture.exe"));
            workAreaFixture = Process.Start(new ProcessStartInfo(fixturePath) { UseShellExecute = true }) ?? throw new InvalidOperationException("The work-area fixture could not start.");
            TaskWindow? maximizeFixture = null;
            for (int i = 0; i < 100 && maximizeFixture == null; i++)
            {
                maximizeFixture = tracker.Read().FirstOrDefault(w => { Native.GetWindowThreadProcessId(w.Handle, out uint pid); return pid == workAreaFixture.Id; });
                if (maximizeFixture == null) await Task.Delay(30);
            }
            if (maximizeFixture == null) throw new Exception("Work-area fixture did not appear");
            Native.ShowWindow(maximizeFixture.Handle, 3); await Task.Delay(250);
            Settings.HideWindowsTaskbar = true; ApplySettings(); await Task.Delay(650);
            var bars = new List<nint>(); Native.EnumWindows((hwnd, _) => { if (Native.Class(hwnd) is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") bars.Add(hwnd); return true; }, 0);
            Check(bars.Count > 0 && bars.All(h => !Native.IsWindowVisible(h)), "All Windows taskbars are hidden");
            var fixtureDisplay = Native.Monitors().First(m => m.Handle == Native.MonitorFromWindow(maximizeFixture.Handle, 2));
            var reclaimed = Native.Bounds(maximizeFixture.Handle);
            var fixtureBar = bars.First(h => Native.MonitorFromWindow(h, 2) == fixtureDisplay.Handle);
            var fixtureBarBounds = Native.Bounds(fixtureBar);
            bool reclaimedEdge = fixtureBarBounds.Top <= fixtureDisplay.Bounds.Top && fixtureBarBounds.Bottom > fixtureDisplay.Bounds.Top ? reclaimed.Top < fixtureBarBounds.Bottom
                : fixtureBarBounds.Bottom >= fixtureDisplay.Bounds.Bottom && fixtureBarBounds.Top < fixtureDisplay.Bounds.Bottom ? reclaimed.Bottom > fixtureBarBounds.Top
                : fixtureBarBounds.Left <= fixtureDisplay.Bounds.Left && fixtureBarBounds.Right > fixtureDisplay.Bounds.Left ? reclaimed.Left < fixtureBarBounds.Right
                : reclaimed.Right > fixtureBarBounds.Left;
            Check(reclaimedEdge,
                $"Maximized windows reclaim the hidden taskbar edge (window {reclaimed}, display {fixtureDisplay.Bounds}, taskbar {fixtureBarBounds})");
            Check((WindowsTaskbar.State() & 1) != 0, "Taskbar hiding enables Windows auto-hide to release reserved space");
            Check(File.Exists(Path.Combine(Log.DirectoryPath, "taskbar-visibility.json")), "Taskbar recovery state saved");
            Settings.HideWindowsTaskbar = false; ApplySettings(); await Task.Delay(500);
            Check(WindowsTaskbar.State() == taskbarState && !File.Exists(Path.Combine(Log.DirectoryPath, "taskbar-visibility.json")), "Previous taskbar state restored and recovery lease removed");
            Check(previousBars.All(p => !Native.IsWindow(p.Key) || Native.IsWindowVisible(p.Key) == p.Value), "Original taskbar window visibility restored");
            Settings.HideWindowsTaskbar = true; ApplySettings();
            Native.SetPropW(Handle, "WinMac.Dock.HideWindowsTaskbar", 0);
            windowsTaskbar.Recover();
            Settings.HideWindowsTaskbar = false; ApplySettings(); await Task.Delay(250);
            Check(previousBars.All(p => !Native.IsWindow(p.Key) || Native.IsWindowVisible(p.Key) == p.Value)
                && !File.Exists(Path.Combine(Log.DirectoryPath, "taskbar-visibility.json")), "Interrupted-exit recovery restores taskbar visibility");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            menu?.Hide();
            if (workAreaFixture != null)
                foreach (var task in tracker.Read(true)) { Native.GetWindowThreadProcessId(task.Handle, out uint pid); if (pid == workAreaFixture.Id) Native.PostMessage(task.Handle, 0x10, 0, 0); }
            workAreaFixture?.Dispose();
            PositionEdge = saved.Edge; DisplayHideMode = saved.HideMode; Settings.ReduceMotion = saved.ReduceMotion;
            Settings.RevealDelayMs = saved.RevealDelayMs; Settings.ReserveSpace = saved.ReserveSpace; Settings.HideWindowsTaskbar = saved.HideWindowsTaskbar;
            Settings.HideDelayMs = saved.HideDelayMs; taskViewTestOverride = taskViewOverride;
            inspection = true; ApplySettings();
            startupWaiting = waiting; edgeToggleHidden = latched; manuallyHidden = manual;
            if (wasHidden) HideDock(true); else ShowDock(!manual);
            hoverTestInput = null; inspection = inspected; ResetMagnification();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "visibility-features-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
