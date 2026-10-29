namespace WinMac.Dock.Core;

public static class TransferPolicy
{
    public static bool Move(IEnumerable<string> sources, string destination, bool control, bool shift)
    {
        if (control) return false;
        if (shift) return true;
        var root = Path.GetPathRoot(destination);
        return !string.IsNullOrEmpty(root) && sources.All(p => StringComparer.OrdinalIgnoreCase.Equals(Path.GetPathRoot(p), root));
    }
}
