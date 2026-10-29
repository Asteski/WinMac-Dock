using System.Text.Json;
namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckIconRegressions()
    {
        var paths = new[] { @"C:\Program Files\GIGABYTE\Control Center\GCC.exe", @"C:\Program Files (x86)\UltraISO\UltraISO.exe" };
        var results = new List<object>();
        Exception? failure = null;
        try
        {
            foreach (var path in paths)
            {
                if (!File.Exists(path)) continue;
                var item = new DockItem { Kind = ItemKind.Application, Target = path, ExecutablePath = path, AppUserModelId = "Desktop.Test.Identity" };
                if (IconService.PathFor(item) != path) throw new Exception("Desktop identity hid executable artwork");
                var before = await IconService.Load(path, requestedPixels: IconPixels) ?? throw new Exception("Missing icon: " + path);
                if (!ReferenceEquals(before, IconService.TryGetLoaded(path, requestedPixels: IconPixels))) throw new Exception("Loaded icon was unavailable synchronously during a row rebuild");
                if (IconService.TryGetLoaded(path, requestedPixels: IconPixels == 512 ? 256 : 512) == null) throw new Exception("Resizing cannot retain existing artwork while the new raster loads");
                IconService.InvalidateAll();
                if (IconService.TryGetLoaded(path, requestedPixels: IconPixels) != null) throw new Exception("Retained artwork survived theme invalidation");
                var after = await IconService.Load(path, requestedPixels: IconPixels) ?? throw new Exception("Missing refreshed icon: " + path);
                if (ReferenceEquals(before, after)) throw new Exception("Stale cached icon survived theme refresh");
                IconService.Invalidate(path);
                var simultaneous = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => IconService.Load(path, requestedPixels: IconPixels)));
                if (simultaneous[0] == null || simultaneous.Any(source => !ReferenceEquals(source, simultaneous[0]))) throw new Exception("Concurrent icon requests decoded duplicate artwork");
                results.Add(new { Path = path, SharedRequests = simultaneous.Length, Raster = IconService.Describe(after) });
            }
            foreach (var package in new Windows.Management.Deployment.PackageManager().FindPackagesForUser("").Where(p => p.Id.Name is "OpenAI.ChatGPT-Desktop" or "OpenAI.Codex"))
            {
                var path = "shell:AppsFolder\\" + package.Id.FamilyName + (package.Id.Name == "OpenAI.Codex" ? "!App" : "!ChatGPT");
                var light = IconService.PackageIconAsset(path, IconPixels, false);
                var dark = IconService.PackageIconAsset(path, IconPixels, true);
                if (light == null || dark == null || light == dark) throw new Exception("Packaged app theme variants were not selected");
                // Exercise decoding both real installed variants without changing
                // the OS theme or the dock's selected theme.
                var lightImage = await IconService.Load(light, imageFile: true, requestedPixels: IconPixels);
                var darkImage = await IconService.Load(dark, imageFile: true, requestedPixels: IconPixels);
                if (lightImage == null || darkImage == null) throw new Exception("Packaged theme artwork could not be decoded");
                results.Add(new { Path = path, Light = light, Dark = dark, LightRaster = IconService.Describe(lightImage), DarkRaster = IconService.Describe(darkImage) });
            }
            var recycle = await IconService.Load("shell:RecycleBinFolder", requestedPixels: IconPixels);
            if (recycle == null) throw new Exception("Registered Recycle Bin icon could not be loaded");
            results.Add(new { Path = "shell:RecycleBinFolder", RegisteredIcon = IconService.RecycleIconLocation(), Raster = IconService.Describe(recycle) });
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "icon-regressions-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Results = results }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
