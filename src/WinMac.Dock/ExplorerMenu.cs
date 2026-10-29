using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;

namespace WinMac.Dock;
internal static partial class ShellService
{
    [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerServices
    {
        [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IExplorerBrowserWindow browser);
    }
    [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IExplorerBrowserWindow
    {
        [PreserveSig] int GetWindow(out nint window);
    }
    internal static Task<Dictionary<nint, string>> ExplorerWindowFoldersAsync(HashSet<nint> handles)
    {
        var completion = new TaskCompletionSource<Dictionary<nint, string>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var result = new Dictionary<nint, string>();
            object? shell = null, windows = null;
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                windows = ((dynamic)shell!).Windows();
                foreach (var window in (dynamic)windows!)
                {
                    object? document = null, folder = null, self = null;
                    IExplorerBrowserWindow? browser = null;
                    try
                    {
                        nint handle = (nint)Convert.ToInt64(window.HWND);
                        if (!handles.Contains(handle)) continue;
                        // ShellWindows includes inactive tabs with the same top-level
                        // HWND. Only the selected tab's browser child is visible.
                        if ((object)window is IExplorerServices services)
                        {
                            var service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837");
                            var iid = typeof(IExplorerBrowserWindow).GUID;
                            if (services.QueryService(ref service, ref iid, out browser) >= 0 && browser.GetWindow(out var tab) >= 0
                                && tab != 0 && !Native.IsWindowVisible(tab)) continue;
                        }
                        document = window.Document; folder = ((dynamic)document!).Folder; self = ((dynamic)folder!).Self;
                        string target = Convert.ToString(((dynamic)self!).Path) ?? "";
                        if (target.Length > 0) result[handle] = target;
                    }
                    catch (Exception ex) { Log.Write(ex); }
                    finally { Release(browser); Release(self); Release(folder); Release(document); Release(window); }
                }
            }
            catch (Exception ex) { Log.Write(ex); }
            finally { Release(windows); Release(shell); completion.TrySetResult(result); }
        }) { IsBackground = true, Name = "Dock Explorer locations" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        return completion.Task;
    }
    internal sealed record ExplorerLocation(string Name, string Target, bool Pinned);
    internal static IReadOnlyList<ExplorerLocation> ExplorerLocations()
    {
        object? shell = null, folder = null, items = null;
        var result = new List<ExplorerLocation>();
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            // Quick access exposes both pinned and frequent folders, in Shell order.
            folder = ((dynamic)shell!).NameSpace("shell:::{3936e9e4-d92c-4eee-a85a-bc16d5ea0819}");
            if (folder == null) return result;
            items = ((dynamic)folder).Items();
            foreach (var entry in (dynamic)items!)
            {
                try
                {
                    if (!(bool)entry.IsFolder) continue;
                    string target = Convert.ToString(entry.Path) ?? "";
                    string name = Convert.ToString(entry.Name) ?? "";
                    if (target.Length == 0 || name.Length == 0) continue;
                    bool pinned = entry.ExtendedProperty("System.Home.IsPinned") is true;
                    if (pinned) result.Add(new(name, target, true));
                }
                finally { Release(entry); }
            }
            Release(items); items = null; Release(folder); folder = null;
            string recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
            folder = ((dynamic)shell).NameSpace(recent);
            foreach (var path in Directory.EnumerateFiles(recent, "*.lnk").OrderByDescending(File.GetLastWriteTimeUtc))
            {
                object? entry = null, link = null, target = null;
                try
                {
                    entry = ((dynamic)folder!).ParseName(Path.GetFileName(path));
                    link = ((dynamic)entry!).GetLink;
                    target = ((dynamic)link!).Target;
                    if (target == null || !(bool)((dynamic)target).IsFolder) continue;
                    string targetPath = Convert.ToString(((dynamic)target).Path) ?? "";
                    string name = Convert.ToString(((dynamic)target).Name) ?? "";
                    if (targetPath.Length > 0 && name.Length > 0 && !result.Any(p => p.Target.Equals(targetPath, StringComparison.OrdinalIgnoreCase)))
                        result.Add(new(name, targetPath, false));
                    if (result.Count(p => !p.Pinned) >= 10) break;
                }
                catch (Exception ex) { Log.Write(ex); }
                finally { Release(target); Release(link); Release(entry); }
            }
        }
        catch (Exception ex) { Log.Write(ex); }
        finally { Release(items); Release(folder); Release(shell); }
        return result.DistinctBy(p => p.Target, StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

internal sealed partial class DockWindow
{
    private readonly Dictionary<nint, string> explorerFolderTargets = [];
    private Task<Dictionary<nint, string>>? explorerMenuFolders;
    private long explorerMenuFoldersStamp;
    private Task<Dictionary<nint, string>> ExplorerMenuFolders()
    {
        if (explorerMenuFolders == null || System.Diagnostics.Stopwatch.GetElapsedTime(explorerMenuFoldersStamp).TotalMilliseconds >= 500)
        {
            explorerMenuFoldersStamp = System.Diagnostics.Stopwatch.GetTimestamp();
            var handles = tracker.Read().Where(window => IsFileExplorer(new DockItem { ExecutablePath = window.ExecutablePath })).Select(window => window.Handle).ToHashSet();
            explorerMenuFolders = ShellService.ExplorerWindowFoldersAsync(handles);
        }
        return explorerMenuFolders;
    }
    private bool refreshingExplorerFolders;
    private long explorerFoldersStamp;
    private async Task RefreshExplorerFolderIcons()
    {
        if (closing || refreshingExplorerFolders || System.Diagnostics.Stopwatch.GetElapsedTime(explorerFoldersStamp).TotalMilliseconds < 500) return;
        var handles = buttons.Where(b => !b.Entry.IsPinned && IsFileExplorer(b.Entry.Item) && b.Entry.Window != null)
            .Select(b => b.Entry.Window!.Handle).ToHashSet();
        foreach (var handle in explorerFolderTargets.Keys.Where(h => !handles.Contains(h)).ToArray()) explorerFolderTargets.Remove(handle);
        if (handles.Count == 0) return;
        refreshingExplorerFolders = true; explorerFoldersStamp = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            var locations = await ShellService.ExplorerWindowFoldersAsync(handles);
            if (closing) return;
            foreach (var (handle, target) in locations)
            {
                if (explorerFolderTargets.TryGetValue(handle, out var previous) && previous.Equals(target, StringComparison.OrdinalIgnoreCase)) continue;
                explorerFolderTargets[handle] = target;
                foreach (var button in buttons.Where(b => !b.Entry.IsPinned && b.Entry.Window?.Handle == handle).ToArray())
                    if (button.Icon is Grid host && host.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().FirstOrDefault() is { } image
                        && host.Children.OfType<FontIcon>().FirstOrDefault() is { } fallback)
                        await LoadIcon(button.Entry.Item, image, fallback, host, false, handle);
            }
        }
        catch (Exception ex) { Log.Write(ex); }
        finally { refreshingExplorerFolders = false; }
    }
    private void AddExplorerLocations(MenuFlyout menu, DockItem? item = null)
    {
        var locations = ShellService.ExplorerLocations();
        bool expandedCommands = false, submenuBoundaryAdded = false;
        foreach (bool pinned in new[] { true, false }.OrderBy(pinned => (int)(item == null ? AdditionalCommandsVisibility.Submenu : pinned ? item.ExplorerPinnedCommands : item.ExplorerRecentCommands)))
        {
            var mode = item == null ? AdditionalCommandsVisibility.Submenu : pinned ? item.ExplorerPinnedCommands : item.ExplorerRecentCommands;
            if (mode == AdditionalCommandsVisibility.Hidden) continue;
            var entries = locations.Where(p => p.Pinned == pinned).ToArray();
            if (entries.Length == 0) continue;
            if (mode == AdditionalCommandsVisibility.Submenu && expandedCommands && !submenuBoundaryAdded)
            {
                menu.Items.Add(new MenuFlyoutSeparator());
                submenuBoundaryAdded = true;
            }
            var submenu = new MenuFlyoutSubItem { Text = pinned ? "Pinned" : "Recent", Icon = Glyph(pinned ? "\uE718" : "\uE81C") };
            foreach (var location in entries)
            {
                var icon = new ImageIcon();
                var entry = new MenuFlyoutItem { Text = location.Name, Icon = icon };
                entry.Click += (_, _) => TryAction(() => ShellService.Open(location.Target));
                submenu.Items.Add(entry);
                LoadExplorerLocationIcon(icon, location.Target);
            }
            if (mode == AdditionalCommandsVisibility.MainMenu)
            {
                expandedCommands = true;
                menu.Items.Add(new MenuFlyoutItem { Text = submenu.Text, IsEnabled = false });
                var commands = submenu.Items.ToArray(); submenu.Items.Clear();
                foreach (var command in commands) menu.Items.Add(command);
            }
            else menu.Items.Add(submenu);
        }
        if (menu.Items.Count > 0) menu.Items.Add(new MenuFlyoutSeparator());
    }
    private async void LoadExplorerLocationIcon(ImageIcon icon, string target)
    {
        // ICONONLY asks the Shell for the location's assigned artwork, including
        // known-folder and desktop.ini custom icons, instead of a folder preview.
        icon.Source = await IconService.Load(target, requestedPixels: IconRaster.Pixels(16, scale));
    }
}
