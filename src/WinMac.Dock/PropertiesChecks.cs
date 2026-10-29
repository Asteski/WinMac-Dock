namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckRecycleProperties()
    {
        ItemPropertiesWindow? editor = null;
        Exception? failure = null;
        try
        {
            ShowSettings();
            await Task.Delay(250);
            if (settingsWindow == null) throw new InvalidOperationException("Dock Settings did not open.");
            settingsWindow.Close();
            editor = new ItemPropertiesWindow(this, new DockItem { Kind = ItemKind.RecycleBin, Name = "Recycle Bin" });
            editor.Activate(); await editor.CheckRecycleProperties();
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { editor?.Close(); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "recycle-properties-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString() }));
    }
    private async Task CheckPropertiesEditor()
    {
        Exception? failure = null;
        var checks = new List<string>();
        var directory = Path.Combine(Path.GetTempPath(), "WinMac-editor-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        ItemPropertiesWindow? editor = null;
        bool hadSettings = settingsWindow != null;
        var originalTheme = Settings.Theme;
        try
        {
            string light = Path.Combine(directory, "light.png"), dark = Path.Combine(directory, "dark.png");
            foreach (var pair in new[] { (light, System.Drawing.Color.OrangeRed), (dark, System.Drawing.Color.DeepSkyBlue) })
            {
                using var bitmap = new System.Drawing.Bitmap(32, 32); using var graphics = System.Drawing.Graphics.FromImage(bitmap);
                graphics.Clear(System.Drawing.Color.Transparent);
                using var translucent = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(128, pair.Item2));
                using var opaque = new System.Drawing.SolidBrush(pair.Item2);
                graphics.FillRectangle(translucent, 2, 2, 28, 28); graphics.FillRectangle(opaque, 8, 8, 16, 16);
                bitmap.Save(pair.Item1, System.Drawing.Imaging.ImageFormat.Png);
            }
            var sample = new DockItem { Name = "Editor fixture", Target = Environment.ProcessPath!, Kind = ItemKind.Application };
            editor = EditItem(new DockItem { Kind = ItemKind.StartMenu, Name = "Start Menu", CustomIcon = "{Assets}/start.ico" });
            await editor.CheckDefaultIconCards(); editor.Close(); editor = null;
            checks.Add("Start Menu bundled icon shows Default with centered headings and disabled Reset buttons");
            string before = System.Text.Json.JsonSerializer.Serialize(sample);
            editor = EditItem(sample);
            if ((settingsWindow != null) != hadSettings) throw new Exception("Opening properties opened Settings");
            checks.Add("Properties opens independently of Settings");
            await editor.CheckEditor(light, dark, false); editor = null;
            if (before != System.Text.Json.JsonSerializer.Serialize(sample)) throw new Exception("Cancel changed the item");
            checks.Add("Cancel discards all draft edits");
            editor = EditItem(sample); await editor.CheckEditor(light, dark, true); editor = null;
            if (sample.CustomIcon != light || sample.DarkModeIcon != dark || sample.Name != "Properties regression" || sample.Arguments != "--example") throw new Exception("Save lost edited fields");
            if (!sample.RunAsAdministrator) throw new Exception("Save lost administrator option");
            checks.Add("Save commits default and dark icons and launch fields");
            checks.Add("Both themes update the 32px preview and native window icon");
            checks.Add("Window icon contains small and high-DPI PNG frames with straight-alpha transparency");
            Settings.Theme = DockTheme.Light; UpdateTheme();
            if (IconService.PathFor(sample) != light) throw new Exception("Dock did not choose the default icon in light mode");
            Settings.Theme = DockTheme.Dark; UpdateTheme();
            if (IconService.PathFor(sample) != dark) throw new Exception("Dock did not choose the override in dark mode");
            sample.DarkModeIcon = "";
            if (IconService.PathFor(sample) != light) throw new Exception("Missing dark override did not fall back to the default icon");
            checks.Add("Dock theme changes choose dark override with default fallback");
            Settings.Theme = originalTheme; UpdateTheme();
            ShowSettings();
            await Task.Delay(200);
            if (Native.WindowIcon(WinRT.Interop.WindowNative.GetWindowHandle(settingsWindow!)) == 0) throw new Exception("Settings window has no icon");
            checks.Add("Settings has a native window icon");
            var tray = CreateTrayMenu();
            int position = tray.Items.ToList().FindIndex(i => i is Microsoft.UI.Xaml.Controls.MenuFlyoutSubItem { Text: "Dock position" });
            if (position < 0 || tray.Items[position + 1] is not Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator
                || tray.Items[position + 2] is not Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text: "Elevate" }
                || tray.Items[position + 3] is not Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text: "Reload" }
                || tray.Items[position + 4] is not Microsoft.UI.Xaml.Controls.MenuFlyoutItem { Text: "Hide tray icon" }
                || tray.Items[position + 5] is not Microsoft.UI.Xaml.Controls.MenuFlyoutSeparator) throw new Exception("Tray restart controls are not below Dock position");
            checks.Add("Tray Dock position is followed by a separator, Elevate, Reload, Hide tray icon and a separator");
            var discord = Settings.Items.FirstOrDefault(i => i.Name.Equals("Discord", StringComparison.OrdinalIgnoreCase));
            if (discord != null)
            {
                editor = EditItem(discord); await editor.CheckNativeTaskbarIcon("discord"); editor.Close(); editor = null;
                checks.Add("Real Discord Properties native small icon matches its exported frame");
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { Settings.Theme = originalTheme; UpdateTheme(); editor?.Close(); if (!hadSettings) settingsWindow?.Close(); try { Directory.Delete(directory, true); } catch { } }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "properties-editor-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
