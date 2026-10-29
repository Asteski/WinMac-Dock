using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async void CheckBundledIconResources()
    {
        Exception? failure = null; int checks = 0;
        try
        {
            foreach (var reference in new[] { BundledIcons.Application, BundledIcons.Tray, BundledIcons.MenuLight, BundledIcons.MenuDark,
                BundledIcons.Start, BundledIcons.Search, BundledIcons.TaskViewLight, BundledIcons.TaskViewDark,
                BundledIcons.RecycleEmptyDark, BundledIcons.RecycleFullDark, "{Icons},-107", "{Icons},-108" })
            foreach (int pixels in new[] { 16, 32, 48, 64, 256 })
            {
                using var icon = IconResources.Load(reference, pixels);
                if (icon.Width != pixels || icon.Height != pixels) throw new Exception("Wrong native resource icon size: " + reference);
                var source = await IconService.Load(reference, requestedPixels: pixels);
                if (source == null) throw new Exception("Could not render resource: " + reference);
                if (!ReferenceEquals(source, IconService.TryGetLoaded(reference, requestedPixels: pixels))) throw new Exception("Resource cache identity mismatch: " + reference);
                checks++;
            }
            if (IconService.ResourceIconCount(Path.Combine(AppContext.BaseDirectory, "WinMac.Dock.Icons.dll")) != 8) throw new Exception("Icon DLL contains the wrong number of icon groups.");
            if (IconService.ResourceIconCount(Path.Combine(AppContext.BaseDirectory, "WinMac.Dock.exe")) != 4) throw new Exception("Executable contains the wrong number of icon groups.");
            var fixture = new Window();
            try
            {
                IconResources.SetWindowIcon(fixture);
                if (Native.WindowIcon(WinRT.Interop.WindowNative.GetWindowHandle(fixture)) == 0) throw new Exception("Native window icon is missing.");
                checks++;
            }
            finally { fixture.Close(); }
            if (Directory.Exists(Path.Combine(AppContext.BaseDirectory, "assets")) && Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "assets"), "*.ico", SearchOption.AllDirectories).Any()) throw new Exception("Loose ICO assets remain in the publish output.");
            await IconResources.LoadDownloadsBadge(new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource()); checks++;
            var legacy = await IconService.Load("{Assets}/start.ico", requestedPixels: 32);
            if (legacy == null) throw new Exception("Legacy settings icon path did not load.");
            checks++;
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "icon-resources-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Error = failure?.ToString() }));
        Close();
    }
}
