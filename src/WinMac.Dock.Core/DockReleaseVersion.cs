namespace WinMac.Dock.Core;

public readonly record struct DockReleaseVersion(Version Number, bool Prerelease) : IComparable<DockReleaseVersion>
{
    public static bool TryParse(string? text, bool prerelease, out DockReleaseVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var tag = text.Trim().TrimStart('v', 'V').Split('+')[0];
        var parts = tag.Split('-', 2);
        if (!Version.TryParse(parts[0], out var number)) return false;
        version = new(new Version(number.Major, number.Minor, Math.Max(0, number.Build)), prerelease || parts.Length > 1);
        return true;
    }

    public int CompareTo(DockReleaseVersion other)
    {
        int order = Number.CompareTo(other.Number);
        return order != 0 ? order : other.Prerelease.CompareTo(Prerelease);
    }
}
