namespace WinMac.Dock.Core;

public static class ShortcutIconTarget
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint SearchPathW(string? path, string file, string? extension, int length, System.Text.StringBuilder buffer, nint filePart);

    public static string Resolve(string target)
    {
        target = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (File.Exists(target)) return Path.GetFullPath(target);
        if (target.IndexOfAny(['\\', '/', ':']) >= 0) return target;
        string extension = Path.GetExtension(target);
        if (extension.Length != 0 && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return target;
        if (!OperatingSystem.IsWindows()) return target;
        var buffer = new System.Text.StringBuilder(32768);
        uint length = SearchPathW(null, target, extension.Length == 0 ? ".exe" : null, buffer.Capacity, buffer, 0);
        if (length > 0 && length < buffer.Capacity && File.Exists(buffer.ToString())) return buffer.ToString();
        // ShellExecute also recognizes application registrations outside PATH.
        string app = extension.Length == 0 ? target + ".exe" : target;
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + app);
            string? registered = (key?.GetValue(null) as string)?.Trim('"');
            if (!string.IsNullOrWhiteSpace(registered))
            {
                registered = Environment.ExpandEnvironmentVariables(registered);
                if (File.Exists(registered)) return registered;
            }
        }
        return target;
    }
    public static string DefaultIcon => Path.Combine(Environment.SystemDirectory, "imageres.dll") + ",-163";
}
