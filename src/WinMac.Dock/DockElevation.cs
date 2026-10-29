using System.Diagnostics;
using System.Security.Principal;
using System.Text.Json;

namespace WinMac.Dock;

internal static class DockElevation
{
    // Called only by the first instance, before creating XAML windows. A
    // secondary launch should forward its command without another UAC prompt.
    internal static bool RelaunchIfRequested(string[] args)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return false;
            string path = Path.Combine(Log.DirectoryPath, "settings.json");
            if (!File.Exists(path)) return false;
            using var settings = JsonDocument.Parse(File.ReadAllText(path));
            if (!settings.RootElement.TryGetProperty(nameof(DockSettings.AlwaysRunAsAdministrator), out var enabled)
                || enabled.ValueKind != JsonValueKind.True) return false;

            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" };
            start.ArgumentList.Add("--wait-for-exit");
            start.ArgumentList.Add(Environment.ProcessId.ToString());
            foreach (var arg in args) start.ArgumentList.Add(arg);
            // The replacement waits for this process to release the singleton
            // mutex. An already elevated replacement never relaunches itself.
            using var replacement = Process.Start(start);
            return replacement != null;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Log.Write("Administrator startup cancelled; continuing without elevation.");
        }
        catch (Exception ex) { Log.Write(ex); }
        return false;
    }
}
