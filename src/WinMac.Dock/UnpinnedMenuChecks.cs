using Microsoft.UI.Xaml.Controls;
using System.Text.Json;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private void CheckUnpinnedMenus()
    {
        if (!Program.SeparatorAudit) return;
        var originalItems = Settings.Items;
        var originalTasks = stressTasks;
        int checks = 0;
        Exception? failure = null;
        try
        {
            var pin = new DockItem { Id = "menu-fixture", Kind = ItemKind.Application, Name = "Menu fixture", Target = Environment.ProcessPath!, ExecutablePath = Environment.ProcessPath!, AppUserModelId = "menu.fixture" };
            TaskWindow[] windows = [new(Handle, "First fixture window", pin.Target, pin.AppUserModelId, default, false, false),
                new(materialHandle, "Second fixture window", pin.Target, pin.AppUserModelId, default, true, false)];
            stressTasks = windows.ToList();
            foreach (bool pinned in new[] { false, true })
            foreach (bool grouped in new[] { false, true })
            foreach (bool combined in new[] { false, true })
            {
                Settings.Items = pinned ? [pin] : [];
                var entries = TaskReconciler.Reconcile(Settings.Items, windows, grouped, combined);
                foreach (var entry in entries.Where(e => !e.IsPinned))
                {
                    var menu = ItemMenu(entry);
                    var labels = menu.Items.OfType<MenuFlyoutItem>().Select(i => i.Text).ToArray();
                    foreach (var window in entry.Windows)
                        if (!labels.Contains((Native.IsIconic(window.Handle) ? "Open" : "Minimize") + " \"" + window.Title + "\""))
                            throw new Exception($"Missing window {window.Title}: pinned={pinned}, grouped={grouped}, combined={combined}");
                    if (labels.Any(s => s is "Open new window" or "Open in new window" or "Run as administrator" or "Open without elevation"))
                        throw new Exception("Unpinned menu contains app launch commands");
                    if (!labels.Contains("Close window")) throw new Exception("Unpinned menu lost Close window");
                    var options = menu.Items.OfType<MenuFlyoutSubItem>().SingleOrDefault(i => i.Text == "Options");
                    if (options?.Items.OfType<MenuFlyoutItem>().Any(i => i.Text is "Restore all windows" or "Minimize all windows" or "Close all windows") == true)
                        throw new Exception("Unpinned menu contains pinned-only bulk actions");
                    checks++;
                }
                if (pinned)
                {
                    var menu = ItemMenu(entries.First(e => e.IsPinned));
                    if (!menu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Open new window"))
                        throw new Exception("Pinned menu lost Open new window");
                    checks++;
                }
            }
        }
        catch (Exception ex) { failure = ex; }
        finally
        {
            stressTasks = originalTasks; Settings.Items = originalItems;
            File.WriteAllText(Path.Combine(Log.DirectoryPath, "unpinned-menu-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Checks = checks, Error = failure?.ToString() }, new JsonSerializerOptions { WriteIndented = true }));
            Close();
        }
    }
}