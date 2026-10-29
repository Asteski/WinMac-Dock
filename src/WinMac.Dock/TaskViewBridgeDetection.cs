using Microsoft.Win32;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    internal (string Text, string? ColorResource) TaskViewBridgeStatus()
    {
        bool windhawk = File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windhawk", "windhawk.exe"));
        bool installed = false, enabled = false;
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(hive, view);
                using var engine = root.OpenSubKey(@"SOFTWARE\Windhawk\Engine");
                if (engine == null) continue;
                windhawk = true;
                using var mods = engine.OpenSubKey("Mods");
                if (mods == null) continue;
                foreach (var name in mods.GetSubKeyNames().Where(name => name.Equals("winmac-dock-task-view", StringComparison.OrdinalIgnoreCase) || name.EndsWith("@winmac-dock-task-view", StringComparison.OrdinalIgnoreCase)))
                {
                    installed = true;
                    using var mod = mods.OpenSubKey(name);
                    enabled |= Convert.ToInt32(mod?.GetValue("Disabled", 0)) == 0;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException) { }
        }
        // Explorer leaves the version property on the dock after unloading the
        // bridge. Always read current configuration first and require a live broker.
        if (installed && !enabled) return ("Bridge mod is installed but disabled. Enable it in Windhawk to use this option.", "SystemFillColorCautionBrush");
        bool connected = Native.GetPropW(Handle, "WinMac.Dock.TaskViewBridgeVersion") != 0
            && Native.FindWindowExW(-3, 0, "WinMac.Dock.TaskViewBroker.V1", null) != 0;
        if (connected) return ("Bridge detected and connected.", null);
        if (!windhawk) return ("Windhawk was not detected. Install Windhawk and enable the bridge mod to use this option.", "SystemFillColorCriticalBrush");
        if (!installed) return ("Windhawk was detected, but the bridge mod was not. Install and enable the bridge mod to use this option.", "SystemFillColorCriticalBrush");
        return ("Bridge mod is installed and enabled, but has not connected. Check that Windhawk is running and the mod is loaded in Explorer.", "SystemFillColorCautionBrush");
    }
}
