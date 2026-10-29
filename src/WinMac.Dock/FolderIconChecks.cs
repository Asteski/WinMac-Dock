using System.Text.Json;
using Windows.Storage;

namespace WinMac.Dock;

internal static class FolderIconChecks
{
    internal static async Task Run()
    {
        var results = new List<object>();
        Exception? failure = null;
        bool missing = false;
        try
        {
            var programs = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            var shortcuts = Directory.EnumerateFiles(programs, "*.lnk", SearchOption.AllDirectories).Take(20).ToArray();
            if (shortcuts.Length == 0) throw new Exception("No Programs shortcuts available for integration checks.");
            var fixture = Path.Combine(AppContext.BaseDirectory, "flyout-icon-fixtures");
            Directory.CreateDirectory(fixture);
            var assignedUrl = Path.Combine(fixture, "Assigned.URL");
            File.WriteAllText(assignedUrl, "[InternetShortcut]\r\nURL=steam://rungameid/0\r\nIconFile=%SystemRoot%\\System32\\shell32.dll\r\nIconIndex=-16769\r\n");
            var missingIconUrl = Path.Combine(fixture, "Missing icon.url");
            File.WriteAllText(missingIconUrl, "[InternetShortcut]\r\nURL=https://example.com\r\nIconFile=missing.ico\r\nIconIndex=0\r\n");
            var urls = Directory.EnumerateFiles(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "*.url")
                .Concat(new[] { assignedUrl, missingIconUrl });
            foreach (var path in shortcuts.Concat(urls).Append(programs).Append(Path.Combine(Environment.SystemDirectory, "notepad.exe")))
            {
                string? storageError = null;
                if (File.Exists(path))
                {
                    try { _ = await StorageFile.GetFileFromPathAsync(path); }
                    catch (Exception ex) { storageError = $"0x{ex.HResult:X8}: {ex.Message}"; }
                }
                foreach (int pixels in new[] { 32, 48, 96 })
                {
                    var image = await IconService.LoadFlyout(path, !Directory.Exists(path), pixels);
                    var direct = await Task.Run(() => IconService.ReadShortcutIcon(path, pixels));
                    if (path == assignedUrl && direct == null) throw new Exception("Assigned URL resource icon was not extracted.");
                    if (direct != null && pixels == 48)
                        File.WriteAllBytes(Path.Combine(fixture, Path.GetFileName(path) + ".png"), direct);
                    results.Add(new { Path = path, Pixels = pixels, StorageError = storageError, Loaded = image != null, DirectShortcutIcon = direct != null, Raster = IconService.Describe(image) });
                    if (image == null) { missing = true; continue; }
                    if (!ReferenceEquals(image, await IconService.LoadFlyout(path, !Directory.Exists(path), pixels)))
                        throw new Exception("Flyout cache did not reuse artwork: " + path);
                    IconService.Invalidate(path);
                    var refreshed = await IconService.LoadFlyout(path, !Directory.Exists(path), pixels);
                    if (refreshed == null || ReferenceEquals(image, refreshed)) throw new Exception("Flyout invalidation failed: " + path);
                }
            }
            if (missing)
                throw new Exception("One or more flyout icons were blank.");
        }
        catch (Exception ex) { failure = ex; }
        var output = Path.Combine(AppContext.BaseDirectory, "flyout-icons-test.json");
        File.WriteAllText(output, JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Results = results }, new JsonSerializerOptions { WriteIndented = true }));
        Environment.ExitCode = failure == null ? 0 : 1;
        Microsoft.UI.Xaml.Application.Current.Exit();
    }
}
