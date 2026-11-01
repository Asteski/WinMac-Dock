using System.Diagnostics;
using System.Security.Principal;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private bool taskViewOpen;
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
    private bool desktopPeekActive;
    private nint desktopPeekHook;
    private Native.WinEventProc? desktopPeekCallback;
    private bool KeepDesktopPeekVisible => Settings.KeepVisibleOnDesktop && desktopPeekActive;
    private void InitializeDesktopPeek()
    {
        desktopPeekCallback = (_, evt, _, _, _, _, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (closing) return;
            desktopPeekActive = evt == 0x21; // EVENT_SYSTEM_PEEKSTART / PEEKEND
            ApplyDesktopVisibility();
            EvaluateVisibility();
            MaintainZOrder();
        });
        desktopPeekHook = Native.SetWinEventHook(0x21, 0x22, 0, desktopPeekCallback, 0, 0, 0);
    }
    private void ApplyDesktopVisibility()
    {
        foreach (var window in new[] { Handle, materialHandle })
        {
            if (window == 0) continue;
            int cloaked = 0; // This option must not cloak the Dock when using Show desktop.
            Native.DwmSetWindowAttribute(window, 13, ref cloaked, 4); // DWMWA_CLOAK
            Native.ExcludeFromDesktopPeek(window, Settings.KeepVisibleOnDesktop);
            int excluded = Settings.KeepVisibleOnDesktop ? 1 : 0;
            Native.DwmSetWindowAttribute(window, 12, ref excluded, 4);
            Native.DwmSetWindowAttribute(window, 11, ref excluded, 4);
        }
    }
    private void MaintainZOrder()
    {
        if (!initialized || closing) return;
        ApplyDesktopVisibility();
        if (hidden || visibilitySliding || popupCount > 0) return;
        if (!taskViewOpen && !KeepDesktopPeekVisible && HasExternalFullscreenForeground()) return;
        SyncSurfaceBandOrder();
        var foreground = tracker.Describe(Native.GetForegroundWindow());
        bool fullscreen = foreground != null && !foreground.Minimized && foreground.Bounds.Left <= monitor.Bounds.Left
            && foreground.Bounds.Top <= monitor.Bounds.Top && foreground.Bounds.Right >= monitor.Bounds.Right && foreground.Bounds.Bottom >= monitor.Bounds.Bottom;
        if (!taskViewOpen && !KeepDesktopPeekVisible && fullscreen && !FullscreenExclusions.Contains(Settings, foreground)) return;
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
}
