using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace WinMac.Dock;

internal static class LoginStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValue = "WinMac Dock";
    private static readonly object gate = new();

    internal static void Configure(bool enabled)
    {
        lock (gate)
        {
            using var user = WindowsIdentity.GetCurrent();
            string sid = user.User?.Value ?? throw new InvalidOperationException("The current Windows user could not be identified.");
            string taskName = StartupTaskDefinition.Name;
            object? service = null, folder = null, registered = null;
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
                ((dynamic)service!).Connect();
                folder = ((dynamic)service).GetFolder(@"\");
                if (enabled)
                {
                    string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The dock executable could not be located.");
                    string xml = StartupTaskDefinition.CreateXml(executable, sid);
                    // TASK_CREATE_OR_UPDATE; interactive current user; no password
                    // or elevation. Leave the legacy entry intact if this fails.
                    registered = ((dynamic)folder).RegisterTask(taskName, xml, 6, sid, null, 3, null);
                }
                // Register the replacement before removing the old task, so a
                // registration failure leaves the existing sign-in action intact.
                var remove = enabled ? new[] { StartupTaskDefinition.LegacyNameForUser(sid) }
                    : new[] { taskName, StartupTaskDefinition.LegacyNameForUser(sid) };
                foreach (var name in remove)
                {
                    try { ((dynamic)folder).DeleteTask(name, 0); }
                    catch (Exception ex) when (unchecked((uint)ex.HResult) is 0x80070002 or 0x8004130F) { }
                }
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                key?.DeleteValue(LegacyValue, throwOnMissingValue: false);
                Log.Write(enabled ? "Sign-in startup: per-user scheduled task registered (zero delay)." : "Sign-in startup disabled: scheduled task and legacy Run entry removed.");
            }
            finally
            {
                foreach (var instance in new[] { registered, folder, service })
                    if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
            }
        }
    }

    internal static void EnsureConfigured(bool enabled)
    {
        // Refresh the action after a folder move, and
        // recreate a missing task when startup is enabled in Dock settings.
        if (enabled) Configure(true);
    }
}
