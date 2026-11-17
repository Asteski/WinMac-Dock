using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;

internal sealed partial class SettingsWindow
{
    private bool recycleSoundPreviewPlaying;
    private static readonly string ApplicationSortPath = Path.Combine(Log.DirectoryPath, "applications-sort.txt");
    private int applicationSortIndex = ReadApplicationSort();

    private static int ReadApplicationSort()
    {
        try
        {
            if (File.Exists(ApplicationSortPath) && int.TryParse(File.ReadAllText(ApplicationSortPath), out int index)
                && index is >= 0 and <= 2) return index;
        }
        catch (Exception ex) { Log.Write(ex); }
        return 0;
    }
    private static readonly string[] ItemSettingsPages = ["Modules", "Folders", "Applications"];

    private static void AddItemSettingsNavigation(NavigationView nav)
    {
        var items = new NavigationViewItem { Content = "Dock items", Icon = DockWindow.Glyph("\uE81E"), SelectsOnInvoked = false };
        foreach (var page in ItemSettingsPages)
            items.MenuItems.Add(new NavigationViewItem
            {
                Content = page,
                Tag = page,
                Icon = DockWindow.Glyph(page switch
                {
                    "Modules" => "\uE950",
                    "Folders" => "\uE8B7",
                    "Applications" => "\uE74C",
                    _ => "\uE946"
                })
            });
        nav.MenuItems.Add(items);
    }

    private void EnsureItemSettingsPage(string page)
    {
        if (categories.ContainsKey(page)) return;
        categories[page] = new StackPanel { Spacing = 18, Padding = new Thickness(28, 4, 20, 28) };
        StackPanel? installed = null, system = null;
        var applicationCards = new List<(string Title, DockItem Item, StackPanel Group, UIElement Card)>();
        if (page == "Applications")
        {
            AddApplicationSort(applicationCards);
            installed = AddApplicationGroup("Installed apps", "\uE71D", expanded: false);
            system = AddApplicationGroup("System components", "\uE782", expanded: false);
        }

        foreach (var (title, item, key) in ItemSettingsCards(page))
        {
            var group = key == "fileexplorer" || (system != null && IsSystemApplication(item)) ? system : installed;
            AddItemCard(page, title, item, key, group);
            if (group != null) applicationCards.Add((title, item, group, group.Children.Last()));
        }
        SortApplicationCards(applicationCards);

        if (installed is { Children.Count: 0 })
            installed.Children.Add(new TextBlock { Text = "Pin an app to configure it here.", Margin = new Thickness(12) });
        if (page == "Folders" && categories[page].Children.Count == 0)
        {
            categories[page].Padding = new Thickness(24);
            categories[page].Children.Add(new TextBlock
            {
                Text = "No folders are currently pinned.",
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private void AddApplicationSort(List<(string Title, DockItem Item, StackPanel Group, UIElement Card)> cards)
    {
        var sort = new ComboBox
        {
            ItemsSource = new[] { "Name (A to Z)", "Name (Z to A)", "Date installed" },
            SelectedIndex = applicationSortIndex
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(sort, "Sort by");
        ToolTipService.SetToolTip(sort, "Date installed uses the package install date or the executable creation date when available.");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var glyph = DockWindow.Glyph("\uE8CB");
        glyph.FontSize = 12;
        glyph.Opacity = .65;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(glyph);
        row.Children.Add(new TextBlock { Text = "Sort by:", Opacity = .65, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(sort);
        categories["Applications"].Children.Add(row);
        sort.SelectionChanged += (_, _) =>
        {
            if (sort.SelectedIndex < 0) return;
            applicationSortIndex = sort.SelectedIndex;
            try
            {
                Directory.CreateDirectory(Log.DirectoryPath);
                File.WriteAllText(ApplicationSortPath, applicationSortIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            catch (Exception ex) { Log.Write(ex); }
            SortApplicationCards(cards);
        };
    }

    private void SortApplicationCards(List<(string Title, DockItem Item, StackPanel Group, UIElement Card)> cards)
    {
        var dates = new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        if (applicationSortIndex == 2 && cards.Any(card => card.Item.AppUserModelId.Contains('!')))
        {
            try
            {
                foreach (var package in new Windows.Management.Deployment.PackageManager().FindPackagesForUser(""))
                    dates.TryAdd(package.Id.FamilyName, package.InstalledDate);
            }
            catch (Exception ex) { Log.Write(ex); }
        }

        DateTimeOffset InstallDate(DockItem item)
        {
            int separator = item.AppUserModelId.IndexOf('!');
            if (separator > 0 && dates.TryGetValue(item.AppUserModelId[..separator], out var date)) return date;
            var path = Environment.ExpandEnvironmentVariables(string.IsNullOrEmpty(item.ExecutablePath) ? item.Target : item.ExecutablePath);
            return File.Exists(path) ? File.GetCreationTimeUtc(path) : DateTimeOffset.MinValue;
        }

        foreach (var group in cards.GroupBy(card => card.Group))
        {
            var ordered = applicationSortIndex switch
            {
                1 => group.OrderByDescending(card => card.Title, StringComparer.CurrentCultureIgnoreCase),
                2 => group.OrderByDescending(card => InstallDate(card.Item)).ThenBy(card => card.Title, StringComparer.CurrentCultureIgnoreCase),
                _ => group.OrderBy(card => card.Title, StringComparer.CurrentCultureIgnoreCase)
            };
            var children = group.Key.Children;
            children.Clear();
            foreach (var card in ordered) children.Add(card.Card);
        }
    }

    private IEnumerable<(string Title, DockItem Item, string? Key)> ItemSettingsCards(string page)
    {
        switch (page)
        {
            case "Modules":
                yield return Special("Downloads", "downloads", DockWindow.IsDownloads);
                yield return Special("Recycle Bin", "recyclebin", item => item.Kind == ItemKind.RecycleBin);
                yield return Special("Start menu", "start", item => item.Kind == ItemKind.StartMenu);
                yield return Special("Task view", "taskview", item => item.Kind == ItemKind.TaskView, ItemKind.TaskView);
                yield return Special("Search", "search", item => item.Kind == ItemKind.Search, ItemKind.Search);
                yield break;
            case "Applications":
                yield return Special("File Explorer", "fileexplorer", DockWindow.IsFileExplorer);
                break;
        }

        var kind = page == "Folders" ? ItemKind.Folder : ItemKind.Application;
        foreach (var item in dock.Settings.Items)
            if (item.Kind == kind && !item.IsAutoUsbPin && !item.IsDownloadedPin
                && !DockWindow.IsDownloads(item) && !DockWindow.IsFileExplorer(item))
                yield return (ShellService.IsMicrosoftStore(item) ? "Store" : item.Name, item, null);
    }

    private IEnumerable<string> ItemSettingsSearchLabels(string page)
    {
        foreach (var (title, item, key) in ItemSettingsCards(page))
        {
            if (page != title) yield return title;
            if (item.Kind == ItemKind.RecycleBin)
            {
                foreach (var label in new[] { "Empty icon", "Full icon", "Dark empty icon", "Dark full icon",
                    "Confirm before emptying", "Play Recycle Bin sounds", "Move to Recycle Bin sound", "Empty Recycle Bin sound" })
                    yield return label;
                continue;
            }
            if (key == "downloads")
            {
                yield return "Pin newly downloaded files";
                yield return "Open files automatically after downloading";
                yield return "Unpin downloads after opening";
            }
            if (key == "fileexplorer") yield return "Automatically pin connected drives";
            foreach (var command in CommandSettings(item, key)) yield return command.Label;
            if (ShellService.IsSnippingTool(item))
            {
                yield return "Pin new screenshots and screen recordings";
                yield return "Unpin captures after opening";
            }
            if (item.Kind == ItemKind.Folder)
            {
                yield return "Folder view";
                yield return "Stack folder contents on the icon";
            }
            yield return "Default / light icon";
            yield return "Dark icon";
        }
    }

    private const string SettingsIconFont = "ms-appx:///assets/fonts/SettingsIcons.ttf#Settings Fluent Icons";

    private static IEnumerable<(string Label, AdditionalCommandsVisibility Value, Action<AdditionalCommandsVisibility> Set)> CommandSettings(DockItem item, string? key)
    {
        if (key == "fileexplorer")
        {
            yield return ("Recent", item.ExplorerRecentCommands, value => item.ExplorerRecentCommands = value);
            yield return ("Pinned", item.ExplorerPinnedCommands, value => item.ExplorerPinnedCommands = value);
        }
        else if (ShellService.IsMicrosoftEdge(item))
        {
            yield return ("Favorites", item.EdgeFavoritesCommands, value => item.EdgeFavoritesCommands = value);
            yield return ("Workspaces", item.EdgeWorkspacesCommands, value => item.EdgeWorkspacesCommands = value);
            yield return ("Profiles", item.EdgeProfilesCommands, value => item.EdgeProfilesCommands = value);
        }
        else if (ShellService.IsXbox(item))
        {
            yield return ("Categories", item.AdditionalCommands, value => item.AdditionalCommands = value);
            yield return ("Games", item.XboxGamesCommands, value => item.XboxGamesCommands = value);
        }
        else if (ShellService.IsSnippingTool(item))
        {
            yield return ("Tasks", item.AdditionalCommands, value => item.AdditionalCommands = value);
            yield return ("Folders", item.Snipping.FoldersCommands, value => item.Snipping.FoldersCommands = value);
        }
        else if (ShellService.IsTerminal(item))
            yield return ("Profiles", item.AdditionalCommands, value => item.AdditionalCommands = value);
        else if (ShellService.IsSystemSettings(item) || ShellService.IsMicrosoftStore(item))
            yield return ("Additional commands", item.AdditionalCommands, value => item.AdditionalCommands = value);
    }

    private StackPanel AddApplicationGroup(string title, string glyph, bool expanded)
    {
        var cards = new StackPanel { Spacing = 12 };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var icon = DockWindow.Glyph(glyph);
        if (title == "System components") icon.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(SettingsIconFont);
        header.Children.Add(icon);
        header.Children.Add(new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center });
        var group = new Expander
        {
            Header = header,
            Content = cards,
            IsExpanded = expanded,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        categories["Applications"].Children.Add(group);
        return cards;
    }

    private static bool IsSystemApplication(DockItem item) =>
        ShellService.IsSystemSettings(item) || ShellService.IsTaskManager(item)
        || ShellService.IsMicrosoftEdge(item)
        || ShellService.IsMicrosoftStore(item) || ShellService.IsXbox(item) || ShellService.IsSnippingTool(item) || ShellService.IsTerminal(item) || IsWsl(item)
        || IsUnderWindows(item.ExecutablePath) || IsUnderWindows(item.Target);

    private static FontIcon SystemApplicationGlyph(DockItem item)
    {
        if (DockWindow.IsFileExplorer(item)) return DockWindow.Glyph("\uEC50");
        if (ShellService.IsSystemSettings(item)) return DockWindow.Glyph("\uE713");
        if (ShellService.IsTaskManager(item)) return DockWindow.Glyph("\uF120");
        if (ShellService.IsMicrosoftEdge(item))
            return new FontIcon { Glyph = "\uE3AA", FontSize = 16, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(SettingsIconFont) };
        if (ShellService.IsMicrosoftStore(item))
            return new FontIcon { Glyph = "\uEA96", FontSize = 16, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("ms-appx:///assets/fonts/StoreIcons.ttf#Store Fluent Icons") };
        if (ShellService.IsXbox(item))
            return new FontIcon { Glyph = "\uE3E3", FontSize = 16, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("ms-appx:///assets/fonts/XboxFluent.ttf#Xbox Fluent") };
        if (ShellService.IsSnippingTool(item)) return DockWindow.Glyph("\uE722");
        if (ShellService.IsTerminal(item) || IsWsl(item))
        {
            var terminal = DockWindow.Glyph("\uF1FD");
            terminal.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(SettingsIconFont);
            return terminal;
        }
        return DockWindow.Glyph("\uE74C");
    }

    private static bool IsWsl(DockItem item) =>
        string.Equals(Path.GetFileName(item.ExecutablePath), "wsl.exe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetFileName(item.Target), "wsl.exe", StringComparison.OrdinalIgnoreCase)
        || item.AppUserModelId.StartsWith("MicrosoftCorporationII.WindowsSubsystemForLinux_", StringComparison.OrdinalIgnoreCase)
        || item.Target.StartsWith(@"shell:AppsFolder\MicrosoftCorporationII.WindowsSubsystemForLinux_", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderWindows(string path)
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (windows.Length == 0 || !Path.IsPathFullyQualified(path)) return false;
        try
        {
            return Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(windows) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException) { return false; }
        catch (IOException) { return false; }
    }

    private (string Title, DockItem Item, string Key) Special(string title, string key, Func<DockItem, bool> matches, ItemKind? kind = null)
    {
        var item = dock.Settings.Items.FirstOrDefault(matches);
        if (item == null)
        {
            var defaults = kind is { } moduleKind ? new DockItem { Kind = moduleKind, Name = title }
                : DockWindow.DefaultSpecialItem(key);
            item = ItemPreferences.Restore(dock.Settings, defaults, key) ?? defaults;
        }
        return (title, item, key);
    }

    private void AddItemCard(string page, string title, DockItem item, string? key, StackPanel? destination = null)
    {
        var body = new StackPanel { Spacing = page is "Modules" or "Folders" ? 0 : 10, Padding = page is "Modules" or "Folders" ? new Thickness(20, 0, 20, 0) : new Thickness(20, 16, 20, 18) };
        var card = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Background="{ThemeResource CardBackgroundFillColorDefaultBrush}"
                    BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                    BorderThickness="1" CornerRadius="8"/>
            """);
        card.Child = body;
        var container = destination ?? categories[page];
        if (page != title)
        {
            var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            string? glyph = key switch
            {
                "start" => "\uE8FC",
                "taskview" => "\uE7C4",
                "search" => "\uE721",
                "downloads" => "\uE896",
                "recyclebin" => "\uE74D",
                _ => null
            };
            if (glyph != null) heading.Children.Add(DockWindow.Glyph(glyph));
            else if (page == "Applications" && (DockWindow.IsFileExplorer(item) || IsSystemApplication(item)))
                heading.Children.Add(SystemApplicationGlyph(item));
            heading.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            heading.Tag = title;
            container.Children.Add(SettingsGroup(heading, card));
        }
        else container.Children.Add(card);

        void AddCardRow(string label, FrameworkElement control)
        {
            var row = SettingRow(label, control);
            if (page is "Modules" or "Folders") ApplyCardRowLayout(row, body.Children.Count > 0);
            body.Children.Add(row);
        }

        void Changed()
        {
            if (key != null) ItemPreferences.Remember(dock.Settings, item, key);
            PreviewSettings();
        }

        void Toggle(string label, bool initial, Action<bool> set)
        {
            var toggle = new ToggleSwitch { IsOn = initial };
            toggle.Toggled += (_, _) => { set(toggle.IsOn); Changed(); };
            AddCardRow(label, toggle);
        }

        void Icon(string label, string value, Action<string> set, string defaultValue = "")
        {
            var browse = new Button { Content = "Browse..." };
            string bundledDefault = item.Kind switch
            {
                ItemKind.StartMenu => BundledIcons.Start,
                ItemKind.Search => BundledIcons.Search,
                ItemKind.TaskView => label == "Dark icon" ? BundledIcons.TaskViewDark : BundledIcons.TaskViewLight,
                _ => ""
            };
            bool IsDefault(string path) => path == defaultValue || (bundledDefault.Length > 0
                && string.Equals(BundledIcons.Portable(path), bundledDefault, StringComparison.OrdinalIgnoreCase));
            var reset = new Button { Content = "Reset", IsEnabled = !IsDefault(value) };
            var file = new TextBlock { Text = IsDefault(value) ? "Default" : Path.GetFileName(value), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 180, VerticalAlignment = VerticalAlignment.Center };
            void Assign(string path)
            {
                value = path;
                set(path);
                file.Text = IsDefault(path) ? "Default" : Path.GetFileName(path);
                reset.IsEnabled = !IsDefault(path);
                Changed();
            }
            browse.Click += async (_, _) =>
            {
                try
                {
                    var path = DockFileDialog.PickFile(WinRT.Interop.WindowNative.GetWindowHandle(this), iconsOnly: true);
                    if (path == null) return;
                    var selected = await ShortcutIconPicker.Choose(path, body.ActualTheme);
                    if (selected != null && body.XamlRoot != null) Assign(selected);
                }
                catch (Exception ex) { dock.TryAction(() => throw ex); }
            };
            reset.Click += (_, _) => Assign(defaultValue);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            buttons.Children.Add(file);
            buttons.Children.Add(reset);
            var preview = new Button { Content = "Preview" };
            preview.Click += async (_, _) =>
            {
                var previewImage = new Image { Width = 48, Height = 48, HorizontalAlignment = HorizontalAlignment.Center };
                var previewName = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, MaxWidth = 260 };
                var mode = new TextBlock
                {
                Text = label switch { "Default / light icon" => "Default", "Dark icon" => "Dark mode", _ => label },
                HorizontalAlignment = HorizontalAlignment.Center
                };
                var content = new StackPanel { Spacing = 8, Padding = new Thickness(4) };
                content.Children.Add(previewImage);
                content.Children.Add(previewName);
                content.Children.Add(mode);
                var previewTheme = label.StartsWith("Dark", StringComparison.Ordinal) ? ElementTheme.Dark : ElementTheme.Light;
                content.RequestedTheme = previewTheme;
                var presenterStyle = new Style(typeof(FlyoutPresenter));
                presenterStyle.Setters.Add(new Setter(FrameworkElement.RequestedThemeProperty, previewTheme));
                presenterStyle.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
                var flyout = new Flyout { Content = content, FlyoutPresenterStyle = presenterStyle };
                string path = value;
                if (string.IsNullOrWhiteSpace(path))
                    path = item.Kind == ItemKind.TaskView && string.IsNullOrWhiteSpace(item.CustomIcon)
                        ? IconResources.PathFor(label == "Dark icon" ? BundledIcons.TaskViewDark : BundledIcons.TaskViewLight)
                        : IconService.PathFor(item with { DarkModeIcon = "" });
                path = BundledIconPath.Resolve(Environment.ExpandEnvironmentVariables(path), Path.Combine(AppContext.BaseDirectory, "assets"));
                previewName.Text = Path.GetFileName(path);
                ToolTipService.SetToolTip(previewImage, path);
                ToolTipService.SetToolTip(previewName, path);
                flyout.Closed += (_, _) => { previewImage.Source = null; flyout.Content = null; };
                flyout.ShowAt(preview);
                int pixels = IconRaster.Pixels(48, body.XamlRoot?.RasterizationScale ?? 1);
                try
                {
                    var source = await IconService.Load(path, imageFile: true, requestedPixels: pixels);
                    if (flyout.Content != null) previewImage.Source = source;
                }
                catch (Exception ex) { Log.Write(ex); }
            };
            buttons.Children.Add(preview);
            buttons.Children.Add(browse);
            AddCardRow(label, buttons);
        }

        void Sound(string label, string path, Action<string> set, bool empty)
        {
            var name = new TextBlock { VerticalAlignment = VerticalAlignment.Center, MaxWidth = 140, TextTrimming = TextTrimming.CharacterEllipsis };
            var reset = new Button { Content = "Reset" };
            var browse = new Button { Content = "Browse..." };

            void Update(string value)
            {
                name.Text = value.Length == 0 ? "Default" : Path.GetFileName(value);
                ToolTipService.SetToolTip(name, value.Length == 0 ? null : value);
                reset.IsEnabled = value.Length > 0;
            }

            void SetVolume(int percent)
            {
                if (empty) item.RecycleEmptySoundVolumePercent = percent;
                else item.RecycleDropSoundVolumePercent = percent;
            }

            void Assign(string value)
            {
                if (!string.Equals(path, value, StringComparison.OrdinalIgnoreCase))
                    SetVolume(DockItem.DefaultRecycleSoundVolumePercent);
                path = value;
                set(value);
                Update(value);
                Changed();
            }

            Update(path);
            reset.Click += (_, _) => Assign("");
            browse.Click += (_, _) => dock.TryAction(() =>
            {
                var selected = DockFileDialog.PickFile(WinRT.Interop.WindowNative.GetWindowHandle(this), audioOnly: true);
                if (selected == null) return;
                if (!string.Equals(Path.GetExtension(selected), ".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    using var player = new System.Media.SoundPlayer(selected);
                    player.Load();
                }
                Assign(selected);
            });
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            actions.Children.Add(name);
            actions.Children.Add(reset);
            var preview = new Button { Content = new FontIcon { Glyph = "\uE768", FontSize = 14 } };
            ToolTipService.SetToolTip(preview, "Play sound preview");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(preview, $"Preview {label}");
            preview.Click += async (_, _) =>
            {
                if (recycleSoundPreviewPlaying) return;
                recycleSoundPreviewPlaying = true;
                preview.IsEnabled = false;
                try
                {
                    bool played = await ShellService.PlayRecycleSoundAsync(empty, item.RecycleSoundVolume(empty), path);
                    if (!played && body.XamlRoot != null)
                        await new ContentDialog { Title = "Cannot play sound", Content = "Check that the WAV or MP3 file is available and valid.", CloseButtonText = "OK", XamlRoot = body.XamlRoot }.ShowAsync();
                }
                catch (Exception ex) { Log.Write(ex); }
                finally
                {
                    recycleSoundPreviewPlaying = false;
                    preview.IsEnabled = true;
                }
            };
            actions.Children.Add(preview);
            var slider = new Slider { Minimum = 0, Maximum = DockItem.MaxRecycleSoundVolumePercent, StepFrequency = 1, Width = 200 };
            var volumeLabel = new TextBlock();
            var volumePanel = new StackPanel { Spacing = 8 };
            volumePanel.Children.Add(volumeLabel);
            volumePanel.Children.Add(slider);
            var volumeFlyout = new Flyout { Content = volumePanel };
            var volume = new Button { Content = new FontIcon { Glyph = "\uE767", FontSize = 16 }, Flyout = volumeFlyout };
            ToolTipService.SetToolTip(volume, $"{label} volume");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(volume, $"{label} volume");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, $"{label} volume (percent)");
            bool syncingVolume = false;
            volumeFlyout.Opening += (_, _) =>
            {
                syncingVolume = true;
                slider.Value = item.RecycleSoundVolume(empty);
                volumeLabel.Text = $"{item.RecycleSoundVolume(empty)}%";
                syncingVolume = false;
            };
            slider.ValueChanged += (_, _) =>
            {
                if (syncingVolume) return;
                SetVolume((int)Math.Round(slider.Value));
                volumeLabel.Text = $"{item.RecycleSoundVolume(empty)}%";
                Changed();
            };
            actions.Children.Add(volume);
            actions.Children.Add(browse);
            AddCardRow(label, actions);
        }

        if (item.Kind == ItemKind.RecycleBin)
        {
            var defaults = DockWindow.DefaultSpecialItem("recyclebin");
            Icon("Empty icon", item.RecycleEmptyIcon, value => item.RecycleEmptyIcon = value, defaults.RecycleEmptyIcon);
            Icon("Full icon", item.RecycleFullIcon, value => item.RecycleFullIcon = value, defaults.RecycleFullIcon);
            Icon("Dark empty icon", item.RecycleEmptyDarkIcon, value => item.RecycleEmptyDarkIcon = value, defaults.RecycleEmptyDarkIcon);
            Icon("Dark full icon", item.RecycleFullDarkIcon, value => item.RecycleFullDarkIcon = value, defaults.RecycleFullDarkIcon);
            Toggle("Confirm before emptying", item.ConfirmRecycleBinEmpty, value => item.ConfirmRecycleBinEmpty = value);
            Toggle("Play Recycle Bin sounds", item.PlayRecycleBinSounds, value => item.PlayRecycleBinSounds = value);
            Sound("Move to Recycle Bin sound", item.RecycleDropSoundPath, path => item.RecycleDropSoundPath = path, empty: false);
            Sound("Empty Recycle Bin sound", item.RecycleEmptySoundPath, path => item.RecycleEmptySoundPath = path, empty: true);
            return;
        }

        if (key == "downloads")
        {
            Toggle("Pin newly downloaded files", item.Folder.PinNewDownloads, value => item.Folder.PinNewDownloads = value);
            Toggle("Open files automatically after downloading", item.Folder.OpenNewDownloadsAutomatically, value => item.Folder.OpenNewDownloadsAutomatically = value);
            Toggle("Unpin downloads after opening", item.Folder.UnpinDownloadsAfterOpening, value => item.Folder.UnpinDownloadsAfterOpening = value);
        }
        if (key == "fileexplorer")
            Toggle("Automatically pin connected drives", item.PinUsbDrives, value => item.PinUsbDrives = value);
        if (ShellService.IsSnippingTool(item))
        {
            Toggle("Pin new screenshots and screen recordings", item.Snipping.PinNewCaptures, value => item.Snipping.PinNewCaptures = value);
            Toggle("Unpin captures after opening", item.Snipping.UnpinAfterOpening, value => item.Snipping.UnpinAfterOpening = value);
        }
        if (item.Kind == ItemKind.Folder)
        {
            var view = new ComboBox { ItemsSource = Enum.GetValues<FolderView>(), SelectedItem = item.Folder.View };
            view.SelectionChanged += (_, _) => { if (view.SelectedItem is FolderView selected) { item.Folder.View = selected; Changed(); } };
            AddCardRow("Folder view", view);
            Toggle("Stack folder contents on the icon", item.Folder.StackIcon, value => item.Folder.StackIcon = value);
        }
        foreach (var (label, value, set) in CommandSettings(item, key))
        {
            var menu = new ComboBox
            {
                ItemsSource = new[] { "Show in main menu", "Show in submenu", "Hide" },
                SelectedIndex = Math.Clamp((int)value, 0, 2)
            };
            menu.SelectionChanged += (_, _) =>
            {
                if (menu.SelectedIndex < 0) return;
                set((AdditionalCommandsVisibility)menu.SelectedIndex);
                Changed();
            };
            AddCardRow(label, menu);
        }
        Icon("Default / light icon", item.CustomIcon, value => item.CustomIcon = value);
        Icon("Dark icon", item.DarkModeIcon, value => item.DarkModeIcon = value);
    }
}
