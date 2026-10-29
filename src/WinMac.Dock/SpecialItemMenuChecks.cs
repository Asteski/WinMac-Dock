using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async void CheckSpecialItemMenus(bool requestedOnly = false)
    {
        Exception? failure = null; var checks = new List<string>();
        try
        {
            static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
            foreach (var module in new[] { (ItemKind.Search, "Show Search", "\uE721"), (ItemKind.TaskView, "Show Task view", "\uE7C4") })
                Require(ItemMenu(new(new() { Kind = module.Item1 }, null, true)).Items.OfType<MenuFlyoutItem>().Any(i => i.Text == module.Item2 && i.Icon is FontIcon glyph && glyph.Glyph == module.Item3), "Module label/glyph incorrect");
            foreach (var capture in new[] { (CapturePinKind.Screenshot, "Open Screenshots folder"), (CapturePinKind.Recording, "Open Screen recordings folder") })
            {
                var menu = ItemMenu(new(new() { Kind = ItemKind.File, IsDownloadedPin = true, CapturePin = capture.Item1, Target = @"C:\capture-fixture\capture.png" }, null, true));
                Require(menu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == capture.Item2) && !menu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Open Downloads folder"), "Capture folder label incorrect");
            }
            var quoted = ShortcutCommand.Create("\"C:\\Program Files\\Example\\app.exe\" --profile \"My profile\"", "Example");
            Require(quoted.Target == @"C:\Program Files\Example\app.exe" && quoted.Arguments == "--profile \"My profile\"" && quoted.Name == "Example", "Quoted shortcut lost arguments");
            var alias = ShortcutCommand.Create("notepad.exe example.txt", "");
            Require(File.Exists(alias.CustomIcon) && alias.CustomIcon.EndsWith("notepad.exe", StringComparison.OrdinalIgnoreCase), "Executable alias icon was not resolved");
            Require(await IconService.Load(alias.CustomIcon, imageFile: true, requestedPixels: 32) != null, "Resolved shortcut icon did not load");
            var regedit = ShortcutCommand.Create("regedit.exe", "");
            Require(File.Exists(regedit.CustomIcon) && regedit.CustomIcon.EndsWith("regedit.exe", StringComparison.OrdinalIgnoreCase), "Registry Editor icon was not resolved");
            Require(await IconService.Load(regedit.CustomIcon, imageFile: true, requestedPixels: 32) != null, "Registry Editor icon did not load");
            Require(ShortcutCommand.Create("ms-settings:", "").CustomIcon == ShortcutCommand.DefaultIcon, "Non-executable command lost its fallback icon");
            Require(alias.Target == "notepad.exe" && alias.Arguments == "example.txt", "Command alias parsing failed");
            Require(ShortcutCommand.Create("https://example.com/a?q=1", "").Target == "https://example.com/a?q=1", "URL shortcut parsing failed");
            Require(ShortcutCommand.Create("ms-settings:", "Settings").Target == "ms-settings:", "URI shortcut parsing failed");
            bool rejected = false;
            try { ShortcutCommand.Create("\"unclosed", ""); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "Unclosed shortcut quotes accepted");
            Require(BackgroundMenu().Items.OfType<MenuFlyoutSubItem>().Single(i => i.Text == "Add new item").Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "New shortcut"), "New Shortcut menu missing");
            checks.Add("Capture and module labels, New Shortcut menu, quoted arguments, aliases, URLs, URI handlers and malformed input verified");
            if (requestedOnly)
            {
                var shortcutMenu = BackgroundMenu().Items.OfType<MenuFlyoutSubItem>().Single(i => i.Text == "Add new item").Items.OfType<MenuFlyoutItem>().Single(i => i.Text == "New shortcut");
                Require(shortcutMenu.Icon == null, "New Shortcut still has a submenu glyph");
                var sample = new DockItem { Kind = ItemKind.Application, Name = "User launch fixture", Target = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "tests", "WinMac.Dock.Fixture", "bin", "Release", "net10.0-windows", "WinMac.Dock.Fixture.exe")) };
                foreach (bool preferAdmin in new[] { false, true })
                {
                    var launchMenu = ItemMenu(new(sample with { RunAsAdministrator = preferAdmin }, null, true));
                    Require(IsElevated ? launchMenu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Run as a user" && i.Icon is FontIcon { Glyph: "\uE7EE" }) && !launchMenu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Run as administrator")
                        : launchMenu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == (preferAdmin ? "Open without elevation" : "Run as administrator")), "Elevation-aware launch menu incorrect");
                }
                string report = Path.Combine(Log.DirectoryPath, "shortcut-user-token-" + Guid.NewGuid().ToString("N") + ".json");
                try
                {
                    ShellService.LaunchWithoutElevation(sample with { RunAsAdministrator = true, Arguments = "--elevation-probe \"" + report + "\"" });
                    for (int attempt = 0; attempt < 100 && !File.Exists(report); attempt++) await Task.Delay(100);
                    Require(File.Exists(report), "User launch did not return a token report");
                    using var token = System.Text.Json.JsonDocument.Parse(File.ReadAllText(report));
                    Require(!token.RootElement.GetProperty("Elevated").GetBoolean(), "Run as a user produced an elevated process");
                    checks.Add($"Elevation-aware launch menu and actual unelevated child token verified (dock elevated: {IsElevated})");
                }
                finally { if (File.Exists(report)) File.Delete(report); }
                var originalTasks = stressTasks;
                try
                {
                    foreach (var app in new[] {
                        new DockItem { Kind = ItemKind.Application, Name = "Settings", Target = "ms-settings:", ExecutablePath = @"C:\Windows\ImmersiveControlPanel\SystemSettings.exe" },
                        new DockItem { Kind = ItemKind.Application, Name = "Task Manager", Target = "Taskmgr.exe", ExecutablePath = @"C:\Windows\System32\Taskmgr.exe" } })
                    {
                        var task = new TaskWindow(Handle, "Window list fixture title", app.ExecutablePath, "", default, false, false);
                        stressTasks = [task];
                        foreach (bool pinned in new[] { true, false })
                        {
                            var labels = ItemMenu(new(app, task, pinned)).Items.OfType<MenuFlyoutItem>().Select(i => i.Text).ToArray();
                            Require(!labels.Any(s => s.Contains(task.Title)), "Singleton menu contains a window title");
                            Require(labels.Any(s => s.Contains(app.Name == "Settings" ? "System Settings" : "Task Manager")), "Singleton activation command missing");
                        }
                    }
                    checks.Add("Running Task Manager and Settings omit window lists while retaining activation commands, pinned and unpinned");
                    ShowNewShortcut();
                    await Task.Delay(250);
                    Require(shortcutWindow?.Title == "Add new shortcut", "Shortcut window failed to open");
                    double dpi = Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(shortcutWindow!)) / 96d;
                    Require(shortcutWindow!.AppWindow.Size.Width == (int)Math.Round(600 * dpi) && shortcutWindow.AppWindow.Size.Height >= (int)Math.Round(200 * dpi), "Shortcut window size differs from expected dimensions");
                    shortcutWindow!.Close();
                    checks.Add("New Shortcut WinUI window opens and closes successfully");
                }
                finally { stressTasks = originalTasks; }
                return;
            }
            var start = ItemMenu(new(new() { Kind = ItemKind.StartMenu, Name = "Start Menu" }, null, true));
            Require(start.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Show Start Menu" && i.Icon is FontIcon { Glyph: "\uE8FC" }), "Start Menu label/glyph missing");
            Require(!start.Items.OfType<MenuFlyoutSubItem>().Any(i => i.Text == "Options"), "Start Menu unexpectedly has Options");
            checks.Add("Start Menu has Show Start Menu with E8FC and no Options");
            var settings = new DockItem { Kind = ItemKind.Application, Name = "Settings", Target = "ms-settings:", RunAsAdministrator = true };
            void CheckSettings(TaskWindow? window)
            {
                var menu = ItemMenu(new(settings, window, true));
                Require(menu.Items.Take(systemSettingsCategories.Length).OfType<MenuFlyoutItem>().Select(i => i.Text).SequenceEqual(systemSettingsCategories.Select(i => i.Label)), "Settings categories missing or out of order");
                Require(menu.Items[systemSettingsCategories.Length] is MenuFlyoutSeparator, "Settings categories have no trailing separator");
                foreach (var expected in new[] { ("Network & internet", "\uE701"), ("Apps & features", "\uE74C"), ("Privacy & security", "\uEA18"), ("Windows Update", "\uF0C5") })
                    Require(menu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == expected.Item1 && i.Icon is FontIcon glyph && glyph.Glyph == expected.Item2), "Settings category glyph/label incorrect: " + expected.Item1);
                string expectedAction = window != null && !Native.IsIconic(window.Handle) ? "Minimize System Settings" : "Open System Settings";
                Require(menu.Items.OfType<MenuFlyoutItem>().Count(i => i.Text == expectedAction) == 1, "Settings launch label duplicated or missing");
                Require(!menu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text is "Run as administrator" or "Open without elevation" or "Open" or "Open new window"), "Settings exposes inappropriate launch action");
                var options = menu.Items.OfType<MenuFlyoutSubItem>().SingleOrDefault(i => i.Text == "Options");
                if (window == null) Require(options == null, "Closed Settings exposes Options");
                else Require(options != null && options.Items.Count == 1 && options.Items[0] is ToggleMenuFlyoutItem { Text: "Always on top" }, "Running Settings must offer only Always on top");
            }
            CheckSettings(null);
            checks.Add("Closed Settings has category shortcuts and no Options or administrator launch");
            var live = tracker.Read().FirstOrDefault();
            if (live != null) { CheckSettings(live); checks.Add("Running Settings has the same categories and only Always on top in Options"); }
            var launch = ShellService.LaunchInfo(settings, true);
            Require(launch.FileName == "ms-settings:" && launch.Verb != "runas", "Settings launch must use its singleton URI without elevation");
            checks.Add("Settings ignores legacy administrator preferences and launches via ms-settings");
            var taskManager = new DockItem { Kind = ItemKind.Application, Name = "Task Manager", Target = "Taskmgr.exe", RunAsAdministrator = true };
            var terminal = new DockItem { Kind = ItemKind.Application, Name = "Terminal", Target = "wt.exe" };
            var store = new DockItem { Kind = ItemKind.Application, Name = "Store", Target = "ms-windows-store://home" };
            foreach (var window in new TaskWindow?[] { null, live }.Distinct())
            {
                var taskMenu = ItemMenu(new(taskManager, window, true));
                string taskLabel = window != null && !Native.IsIconic(window.Handle) ? "Minimize Task Manager" : "Open Task Manager";
                Require(taskMenu.Items.OfType<MenuFlyoutItem>().Count(i => i.Text == taskLabel && i.Icon is FontIcon { Glyph: "\uF120" }) == 1, "Task Manager launch label/glyph incorrect");
                Require(!taskMenu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text is "Run as administrator" or "Open without elevation"), "Task Manager exposes elevation");
                var taskOptions = taskMenu.Items.OfType<MenuFlyoutSubItem>().SingleOrDefault(i => i.Text == "Options");
                Require(window == null ? taskOptions == null : taskOptions?.Items.Count == 1 && taskOptions.Items[0] is ToggleMenuFlyoutItem { Text: "Always on top" }, "Task Manager options incorrect");
                var terminalMenu = ItemMenu(new(terminal, window, true));
                var terminalLabels = terminalMenu.Items.OfType<MenuFlyoutItem>().Select(i => i.Text).ToArray();
                Require(window == null ? terminalLabels.Contains("Open Terminal") : terminalLabels.Contains("Open in new window") && terminalLabels.Contains("Open in new tab") && !terminalLabels.Contains("Open Terminal"), "Terminal launch actions incorrect");
                Require(!MenuItems(terminalMenu.Items).Any(i => i.Text == "Open file location"), "Terminal exposes file location");
                var storeMenu = ItemMenu(new(store, window, true));
                Require(storeMenu.Items.Take(5).OfType<MenuFlyoutItem>().Select(i => i.Text).SequenceEqual(new[] { "Home", "Apps", "Gaming", "Library", "Downloads" }) && storeMenu.Items[5] is MenuFlyoutSeparator, "Store category order or separator incorrect");
                string storeLabel = window != null && !Native.IsIconic(window.Handle) ? "Minimize Microsoft Store" : "Open Microsoft Store";
                Require(storeMenu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == storeLabel && i.Icon is FontIcon { Glyph: "\uEA04" }), "Store launch label/glyph incorrect");
                var storeOptions = storeMenu.Items.OfType<MenuFlyoutSubItem>().SingleOrDefault(i => i.Text == "Options");
                Require(window == null ? storeOptions == null : storeOptions?.Items.Count == 1 && storeOptions.Items[0] is ToggleMenuFlyoutItem { Text: "Always on top" }, "Store options incorrect");
            }
            Require(ShellService.LaunchInfo(taskManager, true).Verb != "runas", "Task Manager retained administrator launch preference");
            checks.Add("Task Manager has Open Task Manager, no elevation, and only Always on top while running");
            checks.Add("Terminal has Open Terminal and no file-location action, open or closed");
            checks.Add("Store has five category shortcuts, EA04 open/minimize action, and only Always on top");
            var actualTerminal = Settings.Items.FirstOrDefault(ShellService.IsTerminal) ?? terminal;
            var profiles = ShellService.TerminalProfiles(actualTerminal);
            var profileMenu = ItemMenu(new(actualTerminal with { AdditionalCommands = AdditionalCommandsVisibility.Submenu }, null, true)).Items.OfType<MenuFlyoutSubItem>().FirstOrDefault(i => i.Text == "Profiles");
            if (profiles.Count > 0) Require(profileMenu != null && profileMenu.Items.Count > 0, "Configured Terminal profiles were not exposed");
            const string selector = "{574e775e-4f2a-5b96-ac1e-a2962a402336}";
            var packagedTerminal = new DockItem { AppUserModelId = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", ExecutablePath = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_test\WindowsTerminal.exe", WorkingDirectory = AppContext.BaseDirectory };
            var profileLaunch = ShellService.TerminalLaunchInfo(packagedTerminal, true, selector);
            Require(Path.GetFileName(profileLaunch.FileName) == "wt.exe" && !profileLaunch.FileName.StartsWith(@"C:\Program Files\WindowsApps\", StringComparison.OrdinalIgnoreCase), "Packaged Terminal bypasses its execution alias");
            Require(profileLaunch.WorkingDirectory == Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) && !profileLaunch.ArgumentList.Contains("--startingDirectory"), "Terminal profile directory is overridden");
            checks.Add("Packaged Terminal uses its execution alias and preserves profile starting directories");
            Require(ShellService.TerminalLaunchInfo(actualTerminal, true, selector).ArgumentList.SequenceEqual(new[] { "--window", "new", "new-tab", "--profile", selector }), "Terminal new-window command incorrect");
            Require(ShellService.TerminalLaunchInfo(actualTerminal, false).ArgumentList.SequenceEqual(new[] { "--window", "0", "new-tab" }), "Terminal new-tab command incorrect");
            checks.Add($"Terminal exposes {profiles.Count} visible configured profiles and explicit new-window/new-tab commands");
            var snippingTool = new DockItem { Kind = ItemKind.Application, AppUserModelId = "Microsoft.ScreenSketch_8wekyb3d8bbwe!App" };
            Require(ShellService.IsSnippingTool(snippingTool), "Snipping Tool app identity not recognized");
            foreach (var mode in Enum.GetValues<AdditionalCommandsVisibility>())
            {
                var commands = new MenuFlyout();
                AddAdditionalCommands(commands, snippingTool with { AdditionalCommands = mode });
                var submenu = commands.Items.OfType<MenuFlyoutSubItem>().SingleOrDefault();
                Require(mode == AdditionalCommandsVisibility.Submenu ? submenu?.Text == "Tasks" && submenu.Items.OfType<MenuFlyoutItem>().Count() == snippingToolCommands.Length
                    : mode == AdditionalCommandsVisibility.Hidden ? commands.Items.Count == 0
                    : commands.Items.OfType<MenuFlyoutItem>().Select(i => i.Text).SequenceEqual(snippingToolCommands.Select(i => i.Label)), "Snipping Tool command visibility mismatch: " + mode);
            }
            var driveMenu = ItemMenu(new(new DockItem { Kind = ItemKind.Folder, Target = @"Z:\", IsAutoUsbPin = true }, null, true));
            Require(driveMenu.Items.OfType<MenuFlyoutItem>().Any(i => i.Text == "Eject"), "Automatically attached drive has no eject command");
            checks.Add("Snipping Tool commands support all visibility modes; attached drives expose Eject");
            var background = BackgroundMenu();
            var explorerLocations = ShellService.ExplorerLocations();
            var explorerMenu = ItemMenu(new(new DockItem { Kind = ItemKind.Application, Target = "explorer.exe" }, null, true));
            foreach (var group in explorerLocations.GroupBy(p => p.Pinned))
            {
                string label = group.Key ? "Pinned" : "Recent";
                var submenu = explorerMenu.Items.OfType<MenuFlyoutSubItem>().Single(i => i.Text == label);
                Require(submenu.Items.OfType<MenuFlyoutItem>().Select(i => i.Text).SequenceEqual(group.Select(p => p.Name)), "Explorer location list mismatch: " + label);
                Require(submenu.Items.OfType<MenuFlyoutItem>().All(i => i.Icon is ImageIcon), "Explorer locations must use Shell images: " + label);
            }
            int explorerIcons = 0;
            foreach (var location in explorerLocations)
            {
                var source = await IconService.Load(location.Target, requestedPixels: IconRaster.Pixels(16, scale));
                Require(source != null, "Shell icon unavailable for Explorer location: " + location.Name);
                explorerIcons++;
            }
            checks.Add($"Loaded actual Shell icons for all {explorerIcons} Explorer locations");
            Require(explorerLocations.Select(p => p.Target).Distinct(StringComparer.OrdinalIgnoreCase).Count() == explorerLocations.Count, "Explorer locations contain duplicate targets");
            checks.Add($"Explorer exposes {explorerLocations.Count(p => p.Pinned)} pinned and {explorerLocations.Count(p => !p.Pinned)} recent folders without duplicate targets");
            Require(background.Items.OfType<MenuFlyoutItem>().Any(i => i.Text is "Enable hiding" or "Disable hiding"), "Hiding toggle missing");
            checks.Add("Dock menu exposes its hiding toggle");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            File.WriteAllText(Path.Combine(Log.DirectoryPath, requestedOnly ? "shortcut-menus-test.json" : "special-item-menus-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }
}


