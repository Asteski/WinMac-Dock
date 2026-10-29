using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace WinMac.Dock;
internal static class IconResources
{
    internal static string PathFor(string reference) => BundledIcons.Resolve(reference, Path.Combine(AppContext.BaseDirectory, "assets"));
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string file, int index, uint flags, out nint large, out nint small, uint size);
    internal static System.Drawing.Icon Load(string reference, int pixels = 32)
    {
        string path = PathFor(reference);
        int comma = path.LastIndexOf(',');
        if (comma < 0 || !int.TryParse(path[(comma + 1)..], out int id)) throw new ArgumentException("Expected an icon resource reference.", nameof(reference));
        nint large = 0, small = 0;
        try
        {
            Marshal.ThrowExceptionForHR(SHDefExtractIcon(path[..comma], id, 0, out large, out small, (uint)pixels));
            if (large == 0) throw new InvalidOperationException("Bundled icon resource was not found: " + reference);
            using var borrowed = System.Drawing.Icon.FromHandle(large);
            return (System.Drawing.Icon)borrowed.Clone();
        }
        finally { if (large != 0) Native.DestroyIcon(large); if (small != 0) Native.DestroyIcon(small); }
    }
    internal static async Task LoadDownloadsBadge(Microsoft.UI.Xaml.Media.Imaging.SvgImageSource source)
    {
        using var input = WinMac.Dock.Icons.BundledIconLibrary.OpenDownloadsBadge();
        using var bytes = new MemoryStream(); input.CopyTo(bytes);
        using var stream = new Windows.Storage.Streams.InMemoryRandomAccessStream();
        using var writer = new Windows.Storage.Streams.DataWriter(stream);
        writer.WriteBytes(bytes.ToArray()); await writer.StoreAsync(); stream.Seek(0);
        if (await source.SetSourceAsync(stream) != Microsoft.UI.Xaml.Media.Imaging.SvgImageSourceLoadStatus.Success)
            throw new InvalidDataException("Could not decode the embedded Downloads badge.");
    }
    internal static void SetWindowIcon(Window window)
    {
        var icon = Load(BundledIcons.Application);
        try { window.AppWindow.SetIcon(Microsoft.UI.Win32Interop.GetIconIdFromIcon(icon.Handle)); }
        catch { icon.Dispose(); throw; }
        window.Closed += (_, _) => icon.Dispose();
    }
}
