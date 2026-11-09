namespace WinMac.Dock.Core;

public sealed class WindowExclusions
{
    public List<string> WindowTitles { get; set; } = [];
    public List<string> ProcessNames { get; set; } = [];
    public List<string> ClassNames { get; set; } = [];

    public bool Matches(TaskWindow window)
    {
        return MatchesAny(WindowTitles, window.Title)
            || MatchesAny(ClassNames, window.ClassName)
            || (ProcessNames.Count > 0 && MatchesAny(ProcessNames, ProcessName(window.ExecutablePath)));
    }

    private static bool MatchesAny(List<string> patterns, string value) => value.Length > 0 && patterns.Any(pattern =>
        pattern.Contains('*') || pattern.Contains('?')
            ? System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern.Replace("\\", "\\\\"), value, ignoreCase: true)
            : string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase));

    internal void Normalize()
    {
        WindowTitles = Normalize(WindowTitles);
        ProcessNames = Normalize((ProcessNames ?? []).Select(name => ProcessName(name?.Trim() ?? "")));
        ClassNames = Normalize(ClassNames);
    }

    private static List<string> Normalize(IEnumerable<string>? values) => (values ?? [])
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static string ProcessName(string value)
    {
        var name = Path.GetFileName(value);
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}
