using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckPassiveFocusUpdates()
    {
        if (!Program.SeparatorAudit) return;
        Window? fixture = null;
        Exception? failure = null;
        int checks = 0;
        try
        {
            safetyTimer.Stop(); reconcileTimer.Stop(); themeTimer.Stop(); visibilityTimer.Stop();
            Settings.ReserveSpace = false; Settings.HideWindowsTaskbar = false; Settings.ShowTrayIcon = false; Settings.HotkeyEnabled = false;
            inspection = true; ApplySettings(persist: false); ShowDock();
            
            fixture = new Window { Title = "WinMac fullscreen focus fixture", Content = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.DarkSlateGray) } };
            var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)fixture.AppWindow.Presenter;
            presenter.SetBorderAndTitleBar(false, false);
            var fixtureHandle = WinRT.Interop.WindowNative.GetWindowHandle(fixture);
            fixture.Activate(); Native.SetForegroundWindow(fixtureHandle);
            await Task.Delay(50);
            fixture.AppWindow.SetPresenter(Microsoft.UI.Windowing.AppWindowPresenterKind.FullScreen);
            await Task.Delay(50);
            Native.SetWindowPos(fixtureHandle, 0, monitor.Bounds.Left, monitor.Bounds.Top, monitor.Bounds.Width, monitor.Bounds.Height, 0x14);
            await Task.Delay(150);
            if (Native.GetForegroundWindow() != fixtureHandle) throw new Exception("Could not establish the fullscreen fixture as foreground");
            var bounds = Native.Bounds(fixtureHandle);
            if (bounds.Left > monitor.Bounds.Left || bounds.Top > monitor.Bounds.Top || bounds.Right < monitor.Bounds.Right || bounds.Bottom < monitor.Bounds.Bottom)
                throw new Exception("Fixture did not cover the full monitor");
            foreach (int size in new[] { 16, 40, 64, 40 })
            {
                // Deliberately omit SWP_NOACTIVATE to exercise the native guard.
                Native.SetWindowPos(Handle, -1, 0, 0, 0, 0, 0x3);
                Native.SetWindowPos(materialHandle, Handle, 0, 0, 0, 0, 0x3);
                Settings.IconSize = size; ApplySettings(persist: false);
                Reposition(false); Reconcile(); ShowDock();
                Native.ShowWindowAsync(Handle, 4);
                await Task.Delay(150);
                if (Native.GetForegroundWindow() != fixtureHandle) throw new Exception("Passive dock updates displaced the fullscreen fixture");
                checks++;
            }
            var menu = BackgroundMenu();
            ShowDockFlyout(menu, dockBorder, new Windows.Foundation.Point(100, 20));
            await Task.Delay(100);
            var foreground = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(foreground, out var pid);
            if (foreground == fixtureHandle || pid != Environment.ProcessId) throw new Exception("Explicit dock menu could not acquire focus");
            checks++; menu.Hide();
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            fixture?.Close();
            File.WriteAllText(Path.Combine(Log.DirectoryPath, "passive-focus-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
}