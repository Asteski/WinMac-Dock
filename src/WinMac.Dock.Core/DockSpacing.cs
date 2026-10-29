namespace WinMac.Dock.Core;

public static class DockSpacing
{
    public static double Gap(bool previousIcon, bool nextIcon, double spacing) => spacing < 0 && !(previousIcon && nextIcon) ? 0 : spacing;
    public static double[] Starts(IReadOnlyList<double> widths, IReadOnlyList<bool> icons, double spacing)
    {
        var starts = new double[widths.Count];
        for (int i = 1; i < starts.Length; i++) starts[i] = starts[i - 1] + widths[i - 1] + Gap(icons[i - 1], icons[i], spacing);
        return starts;
    }
    public static double Length(IReadOnlyList<double> widths, IReadOnlyList<bool> icons, double spacing) => widths.Count == 0 ? 0 : Starts(widths, icons, spacing)[^1] + widths[^1];
}

public static class FullscreenExclusions
{
    public static bool Matches(string rule, TaskWindow window)
    {
        rule = Environment.ExpandEnvironmentVariables(rule.Trim().Trim('"')).Replace('/', '\\');
        if (rule.Length == 0) return false;
        if (rule.Equals(window.AppUserModelId, StringComparison.OrdinalIgnoreCase)) return true;
        var executable = window.ExecutablePath.Replace('/', '\\');
        if (rule.Contains('\\') || rule.Contains(':')) return rule.Equals(executable, StringComparison.OrdinalIgnoreCase);
        return rule.Equals(executable[(executable.LastIndexOf('\\') + 1)..], StringComparison.OrdinalIgnoreCase);
    }
    public static bool Contains(DockSettings settings, TaskWindow? window) => window != null && settings.FullscreenExclusions.Any(rule => Matches(rule, window));
    public static string Identity(DockItem item) => item.AppUserModelId.Contains('!') ? item.AppUserModelId : string.IsNullOrEmpty(item.ExecutablePath) ? item.Target : item.ExecutablePath;
}
