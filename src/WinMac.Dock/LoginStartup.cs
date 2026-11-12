using Microsoft.Win32;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace WinMac.Dock;

internal static class LoginStartup
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string LegacyValue = "WinMac Dock";
    private const string TaskFolder = "WinMac";
    private static readonly object gate = new();

    internal static void Configure(bool enabled)
    {
        lock (gate)
        {
            using var user = WindowsIdentity.GetCurrent();
            string sid = user.User?.Value ?? throw new InvalidOperationException("The current Windows user could not be identified.");
            string taskName = StartupTaskDefinition.Name;
            object? service = null, root = null, folder = null, registered = null;
            try
            {
                service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!);
                ((dynamic)service!).Connect();
                root = ((dynamic)service).GetFolder(@"\");
                string folderPath = @"\" + TaskFolder;
                try { folder = ((dynamic)service).GetFolder(folderPath); }
                catch (Exception ex) when (unchecked((uint)ex.HResult) is 0x80070002 or 0x80070003)
                {
                    if (enabled)
                    {
                        try { folder = ((dynamic)root).CreateFolder(TaskFolder, null); }
                        catch (Exception conflict) when (unchecked((uint)conflict.HResult) == 0x800700B7)
                        { folder = ((dynamic)service).GetFolder(folderPath); }
                    }
                }
                if (enabled)
                {
                    string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The dock executable could not be located.");
                    string xml = StartupTaskDefinition.CreateXml(executable, sid);
                    // TASK_CREATE_OR_UPDATE; interactive current user; no password
                    // or elevation. Leave the legacy entry intact if this fails.
                    registered = ((dynamic)folder!).RegisterTask(taskName, xml, 6, sid, null, 3, null);
                }
                // Register the replacement before removing the old task, so a
                // registration failure leaves the existing sign-in action intact.
                if (!enabled && folder != null) DeleteTask(folder, taskName);
                DeleteTask(root!, taskName);
                DeleteTask(root!, StartupTaskDefinition.LegacyNameForUser(sid));
                using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                key?.DeleteValue(LegacyValue, throwOnMissingValue: false);
                Log.Write(enabled ? "Sign-in startup: per-user scheduled task registered in the WinMac folder (zero delay)." : "Sign-in startup disabled: scheduled task and legacy Run entry removed.");
            }
            finally
            {
                foreach (var instance in new[] { registered, folder, root, service })
                    if (instance != null && Marshal.IsComObject(instance)) Marshal.ReleaseComObject(instance);
            }
        }
    }

    private static void DeleteTask(object folder, string name)
    {
        try { ((dynamic)folder).DeleteTask(name, 0); }
        catch (Exception ex) when (unchecked((uint)ex.HResult) is 0x80070002 or 0x8004130F) { }
    }

    internal static void EnsureConfigured(bool enabled)
    {
        // Refresh the action after a folder move, and
        // recreate a missing task when startup is enabled in Dock settings.
        if (enabled) Configure(true);
    }
}
