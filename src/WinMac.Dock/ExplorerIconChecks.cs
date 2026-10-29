using Microsoft.UI.Xaml.Controls;
using System.Diagnostics;
using System.Text.Json;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private async Task CheckExplorerFolderIcons()
    {
        bool group = Settings.GroupWindows, combine = Settings.CombinePinnedIconsWithRunningApplications, minimized = Settings.ShowMinimizedApplicationsOnly;
        bool inspected = inspection;
        Exception? failure = null; nint fixture = 0;
        string directory = Path.Combine(Path.GetTempPath(), "WinMac-explorer-icons-" + Guid.NewGuid().ToString("N"));
        var checks = new List<string>();
        try
        {
            Directory.CreateDirectory(directory);
            using var process = Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/n,\"{directory}\"") { UseShellExecute = true });
            for (int attempt = 0; attempt < 200 && fixture == 0; attempt++)
            {
                await Task.Delay(100);
                var candidates = tracker.Read(true).Where(w => IsFileExplorer(new DockItem { ExecutablePath = w.ExecutablePath })).Select(w => w.Handle).ToHashSet();
                if (candidates.Count == 0) continue;
                var targets = await ShellService.ExplorerWindowFoldersAsync(candidates);
                fixture = targets.FirstOrDefault(pair => pair.Value.Equals(directory, StringComparison.OrdinalIgnoreCase)).Key;
            }
            if (fixture == 0) throw new Exception("The temporary Explorer window was not identified within 20 seconds");
            checks.Add("Shell reports the actual folder of a newly opened Explorer window");
            inspection = true; Settings.GroupWindows = false; Settings.CombinePinnedIconsWithRunningApplications = false; Settings.ShowMinimizedApplicationsOnly = false;
            ApplySettings();
            bool matched = false;
            for (int attempt = 0; attempt < 60; attempt++)
            {
                Reconcile();
                await Task.Delay(100);
                var button = buttons.FirstOrDefault(b => !b.Entry.IsPinned && b.Entry.Window?.Handle == fixture);
                if (button.Icon is not Grid host || host.Children.OfType<Image>().FirstOrDefault() is not { } image) continue;
                var expected = await IconService.Load(directory, requestedPixels: IconPixels);
                if (expected != null && ReferenceEquals(expected, image.Source)) { matched = true; break; }
            }
            if (!matched) throw new Exception("Separate Explorer item did not display its actual folder artwork");
            checks.Add("Separate Explorer dock item uses the folder's Shell artwork");
            var menu = new MenuFlyout();
            MenuWindow(menu, tracker.Describe(fixture) ?? throw new Exception("Explorer fixture disappeared"), () => { });
            var menuIcon = (ImageIcon)menu.Items.OfType<MenuFlyoutItem>().Single().Icon;
            var expectedMenuIcon = await IconService.Load(directory, requestedPixels: IconPixels);
            if (Settings.ShowMenuGlyphs)
            {
                for (int attempt = 0; attempt < 60 && !ReferenceEquals(menuIcon.Source, expectedMenuIcon); attempt++) await Task.Delay(100);
                if (expectedMenuIcon == null || !ReferenceEquals(menuIcon.Source, expectedMenuIcon)) throw new Exception("Explorer window menu did not use the actual folder icon");
                checks.Add("Explorer window menu uses the active folder's Shell artwork");
            }

            var pinnedExplorer = buttons.FirstOrDefault(b => b.Entry.IsPinned && IsFileExplorer(b.Entry.Item));
            if (pinnedExplorer.Icon is Grid pinnedHost && pinnedHost.Children.OfType<Image>().FirstOrDefault() is { } pinnedImage)
            {
                bool custom = !string.IsNullOrEmpty(IconService.CustomPathFor(pinnedExplorer.Entry.Item));
                var expected = await IconService.Load(IconService.PathFor(pinnedExplorer.Entry.Item), thumbnail: pinnedExplorer.Entry.Item.ShowThumbnail && !custom, imageFile: custom, requestedPixels: IconPixels);
                if (expected != null && !ReferenceEquals(expected, pinnedImage.Source)) throw new Exception("Pinned Explorer launcher lost its configured icon");
                checks.Add("Pinned Explorer launcher retains its configured artwork");
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            // Explorer may open the fixture as a tab in a user's existing
            // window. Quit only the Shell entry for our unique test location.
            object? shell = null, shellWindows = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!); shellWindows = ((dynamic)shell!).Windows();
                foreach (var entry in (dynamic)shellWindows!)
                {
                    try
                    {
                        string url = Convert.ToString(entry.LocationURL) ?? "";
                        if (Uri.TryCreate(url, UriKind.Absolute, out var location) && location.IsFile
                            && location.LocalPath.TrimEnd('\\').Equals(directory, StringComparison.OrdinalIgnoreCase)) entry.Quit();
                    }
                    finally { System.Runtime.InteropServices.Marshal.ReleaseComObject(entry); }
                }
            }
            catch (Exception ex) { Log.Write(ex); }
            finally
            {
                if (shellWindows != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shellWindows);
                if (shell != null) System.Runtime.InteropServices.Marshal.ReleaseComObject(shell);
            }
            Settings.GroupWindows = group; Settings.CombinePinnedIconsWithRunningApplications = combine; Settings.ShowMinimizedApplicationsOnly = minimized;
            inspection = inspected; ApplySettings();
            try { Directory.Delete(directory); } catch (Exception ex) { Log.Write(ex); }
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "explorer-folder-icons-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
