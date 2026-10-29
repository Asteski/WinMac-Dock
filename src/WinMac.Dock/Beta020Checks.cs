using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text.Json;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private async Task CheckBeta020()
    {
        var checks = new List<string>(); Exception? failure = null;
        var saved = Settings with { Items = Settings.Items.ToList() };
        var savedTasks = stressTasks;
        bool inspected = inspection;
        ItemPropertiesWindow? editor = null;
        var directory = Path.Combine(Path.GetTempPath(), "WinMac-beta020-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "Preview.txt"), "Stack preview fixture");
            var folder = new DockItem { Kind = ItemKind.Folder, Name = "Folder fixture", Target = directory, ShowThumbnail = true };
            var explorer = DefaultSpecialItem("fileexplorer");
            var menu = ItemMenu(new(explorer, null, true));
            if (!menu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Unpin from dock")) throw new Exception("Explorer cannot be unpinned");
            var options = menu.Items.OfType<MenuFlyoutSubItem>().Single(i => i.Text == "Options");
            var commands = options.Items.OfType<MenuFlyoutItem>().Select(i => i.Text).ToArray();
            if (commands.Contains("Restart Explorer") || commands.FirstOrDefault() != "Restore all windows") throw new Exception("Explorer Options retained Restart Explorer or lost Restore all windows");
            checks.Add("Explorer can be unpinned; Options starts with Restore all windows and has no Restart Explorer command");
            menu = ItemMenu(new(folder, null, true));
            options = menu.Items.OfType<MenuFlyoutSubItem>().Single(i => i.Text == "Options");
            if (!new[] { "Sort by", "Show content as", "Display as" }.All(text => options.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == text))) throw new Exception("Folder Options differs from Downloads");
            editor = new ItemPropertiesWindow(this, folder);
            if (!editor.HasFolderOptions) throw new Exception("Ordinary folder Properties lacks the Downloads controls");
            editor.Close(); editor = null;
            checks.Add("Ordinary folders expose Downloads menu and Properties controls");
            inspection = true; Settings.ReduceMotion = false; Settings.ShowMinimizedApplicationsOnly = true; Settings.RedirectWindowAnimations = true;
            Settings.Items.Add(folder); Settings.DockBorderColor = "#123456"; Settings.DockBorderOpacity = 40;
            Settings.ItemIndicatorColor = "#FEDCBA"; Settings.ItemIndicatorOpacity = 30;
            stressTasks = []; ApplySettings();
            for (int i = 0; i < 100 && !folderPreviewCache.ContainsKey(folder.Id); i++) await Task.Delay(30);
            if (!folderPreviewCache.TryGetValue(folder.Id, out var preview) || preview.Sources.Count == 0) throw new Exception("Stack preview did not load");
            var task = new TaskWindow((nint)0x7ffffffd, "Minimized fixture", @"C:\WinMac-beta-fixture.exe", "", default, true, false);
            for (int cycle = 0; cycle < 4; cycle++)
            {
                stressTasks = cycle % 2 == 0 ? [task] : [];
                Reconcile();
                var icon = (Grid)buttons.Single(b => b.Entry.Item.Id == folder.Id).Icon;
                if (!icon.Children.OfType<Image>().Any(i => ReferenceEquals(i.Source, preview.Sources[0]))) throw new Exception("Folder stack became empty during minimized desktop membership change");
                await Task.Delay(DockMotion.RearrangeMs + 100);
            }
            checks.Add("Stack artwork survives four task membership changes with minimized-only enabled");
            if (dockBorder.BorderBrush is not SolidColorBrush { Color: { A: 102, R: 18, G: 52, B: 86 } }) throw new Exception("Border alpha/color was not applied");
            stressTasks = [task]; Reconcile(); await Task.Delay(DockMotion.RearrangeMs + 100);
            if (!runningIndicators.Values.Any(i => i.Background is SolidColorBrush { Color: { A: 76, R: 254, G: 220, B: 186 } })) throw new Exception("Indicator alpha/color was not applied");
            checks.Add("Border and running indicator render the configured RGB and alpha");
            var materialBounds = Native.Bounds(materialHandle);
            if (materialBounds != resting || Native.Bounds(Handle) != hostBounds) throw new Exception("Material window differs from the visible dock");
            if ((Native.GetWindowLongPtrW(materialHandle, -16).ToInt64() & 0x40000) == 0) throw new Exception("Material frame is missing the DWM shadow style");
            checks.Add("Native material frame follows the visible panel and retains its DWM shadow style");
            ResetMagnification(); root.UpdateLayout();
            var restingIcons = buttons.ToDictionary(b => b.Button, b => b.Icon.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()));
            for (int cycle = 0; cycle < 3; cycle++)
            {
                PrepareHoverSurface(); root.UpdateLayout();
                foreach (var b in buttons)
                {
                    var point = b.Icon.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    var before = restingIcons[b.Button];
                    if (Math.Abs(point.X - before.X) * scale > .1 || Math.Abs(point.Y - before.Y) * scale > .1) throw new Exception("Reparenting hover artwork moved its resting position");
                }
                ResetMagnification(); root.UpdateLayout();
                foreach (var b in buttons)
                {
                    var point = b.Icon.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    var before = restingIcons[b.Button];
                    if (Math.Abs(point.X - before.X) * scale > .1 || Math.Abs(point.Y - before.Y) * scale > .1) throw new Exception("Hover-out changed the resting artwork position");
                }
            }
            checks.Add($"Three hover entry/exit cycles retain every icon's physical position at {scale * 100:0}% scaling");
            var missingPin = new DockItem { Kind = ItemKind.File, Name = "Missing-target fixture", Target = Path.Combine(directory, "missing.txt"), CustomIcon = IconResources.PathFor(BundledIcons.Application) };
            Settings.Items.Add(missingPin); stressTasks = []; ResetMagnification(); ApplySettings(); FinishRowTransition();
            var missingIcon = (Grid)buttons.Single(b => b.Entry.Item.Id == missingPin.Id).Icon;
            if (missingIcon.Children.OfType<FontIcon>().Single().Glyph != "\uE71A" || missingIcon.Children.OfType<Image>().Single().Source != null)
                throw new Exception("Missing target did not override its custom icon with E71A");
            File.WriteAllText(missingPin.Target, "Restored target"); Reconcile(); FinishRowTransition();
            for (int i = 0; i < 100; i++)
            {
                missingIcon = (Grid)buttons.Single(b => b.Entry.Item.Id == missingPin.Id).Icon;
                if (missingIcon.Children.OfType<Image>().Single().Source != null) break;
                await Task.Delay(30);
            }
            if (missingIcon.Children.OfType<Image>().Single().Source == null) throw new Exception("Restored target kept its missing-target placeholder");
            File.Delete(missingPin.Target); Reconcile(); FinishRowTransition();
            missingIcon = (Grid)buttons.Single(b => b.Entry.Item.Id == missingPin.Id).Icon;
            if (missingIcon.Children.OfType<FontIcon>().Single().Glyph != "\uE71A") throw new Exception("Deleted target did not switch to E71A");
            checks.Add("Missing pins show E71A instead of custom artwork and refresh when their targets return or disappear");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            editor?.Close(); FinishRowTransition(); stressTasks = savedTasks; inspection = inspected;
            Settings.Items = saved.Items; Settings.ReduceMotion = saved.ReduceMotion; Settings.RedirectWindowAnimations = saved.RedirectWindowAnimations;
            Settings.ShowMinimizedApplicationsOnly = saved.ShowMinimizedApplicationsOnly;
            Settings.DockBorderColor = saved.DockBorderColor; Settings.DockBorderOpacity = saved.DockBorderOpacity;
            Settings.ItemIndicatorColor = saved.ItemIndicatorColor; Settings.ItemIndicatorOpacity = saved.ItemIndicatorOpacity;
            ApplySettings();
            try { File.Delete(Path.Combine(directory, "Preview.txt")); Directory.Delete(directory); } catch { }
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "beta-020-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
