namespace WinMac.Dock.Icons;
public static class BundledIconLibrary
{
    public static System.IO.Stream OpenDownloadsBadge() => typeof(BundledIconLibrary).Assembly.GetManifestResourceStream("WinMac.Dock.Icons.DownloadsBadge.svg")
        ?? throw new System.IO.InvalidDataException("Embedded Downloads badge is missing.");
}
