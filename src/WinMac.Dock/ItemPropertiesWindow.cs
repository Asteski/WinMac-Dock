using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace WinMac.Dock;
internal sealed class ItemPropertiesWindow : Window
{
    private readonly DockWindow dock;
    private readonly DockItem item;
    private readonly DockItem? recycleDefaults;
    private readonly StackPanel panel = new() { Spacing = 12, Padding = new Thickness(24, 20, 24, 20) };
    private readonly TextBox name, target, arguments, working, icon, darkIcon, identity;
    private readonly TextBox recycleEmpty = new(), recycleFull = new(), recycleEmptyDark = new(), recycleFullDark = new();
    private readonly List<Func<Task>> recyclePreviewUpdates = [];
    private readonly Button apply = new() { Content = "Apply", Width = 84, IsEnabled = false };
    private readonly List<Func<object?>> editValues = [];
    private readonly HashSet<FrameworkElement> trackedEdits = [];
    private string appliedValues = "";
    private string CurrentValues() => System.Text.Json.JsonSerializer.Serialize(editValues.Select(read => read()).ToArray());
    private void UpdateApplyState() => apply.IsEnabled = appliedValues.Length > 0 && CurrentValues() != appliedValues;
    private void SetIconPath(TextBox box, string path)
    {
        box.Text = path;
        // These backing controls are outside the visual tree. Do not depend on
        // an unloaded TextBox dispatching TextChanged to mark an icon edit.
        UpdateApplyState();
    }
    private void TrackEdits(FrameworkElement element)
    {
        if (!trackedEdits.Add(element)) return;
        switch (element)
        {
            case TextBox box:
                editValues.Add(() => box.Text);
                box.TextChanged += (_, _) => UpdateApplyState();
                break;
            case CheckBox box:
                editValues.Add(() => box.IsChecked);
                box.Checked += (_, _) => UpdateApplyState();
                box.Unchecked += (_, _) => UpdateApplyState();
                box.Indeterminate += (_, _) => UpdateApplyState();
                break;
            case ToggleSwitch toggle:
                editValues.Add(() => toggle.IsOn);
                toggle.Toggled += (_, _) => UpdateApplyState();
                break;
            case ComboBox combo:
                editValues.Add(() => combo.SelectedIndex);
                combo.SelectionChanged += (_, _) => UpdateApplyState();
                break;
            case Slider slider:
                editValues.Add(() => slider.Value);
                slider.ValueChanged += (_, _) => UpdateApplyState();
                break;
            case NumberBox number:
                editValues.Add(() => double.IsNaN(number.Value) ? null : (object)number.Value);
                number.ValueChanged += (_, _) => UpdateApplyState();
                break;
            case Panel container:
                foreach (var child in container.Children.OfType<FrameworkElement>()) TrackEdits(child);
                break;
            case Border { Child: FrameworkElement child }:
                TrackEdits(child);
                break;
            case ContentControl { Content: FrameworkElement child }:
                TrackEdits(child);
                break;
        }
    }
    private readonly Image preview = new() { Width = 32, Height = 32, Stretch = Stretch.Uniform };
    private readonly ToggleSwitch? thumbnail;
    private readonly ComboBox? additionalCommands;
    private readonly ComboBox? snippingFolders;
    private readonly ComboBox? xboxGames;
    private readonly ComboBox? edgeFavorites;
    private readonly ComboBox? edgeProfiles;
    private readonly ComboBox? edgeWorkspaces;
    private readonly ComboBox? explorerRecent, explorerPinned;
    private readonly Native.SubclassProc fixedHeightProc;
    private readonly ComboBox? downloadsView;
    private readonly ComboBox? downloadsSort;
    private readonly ComboBox? downloadsDisplay;
    private readonly ToggleSwitch? downloadsDescending;
    private readonly ToggleSwitch? downloadsFilesOnly;
    private readonly CheckBox? hideFolderName;
    private readonly CheckBox? downloadsStackBase;
    private readonly CheckBox? pinNewDownloads;
    private readonly CheckBox? openNewDownloads;
    private readonly CheckBox? unpinOpenedDownloads;
    private readonly NumberBox? downloadPinLimit;
    private readonly Slider? downloadsGridRows;
    private readonly Slider? downloadsGridColumns;
    private readonly CheckBox? pinUsbDrives;
    private readonly CheckBox? pinNewCaptures, unpinOpenedCaptures;
    private readonly TextBox? screenshotFolder, recordingFolder;
    private readonly NumberBox? capturePinLimit;
    private readonly TextBlock? captureSettingsError;
    private readonly CheckBox? runAsAdministrator;
    private readonly CheckBox? confirmRecycleBinEmpty;
    private readonly CheckBox? playRecycleBinSounds;
    internal bool HasFolderOptions => downloadsView != null && downloadsSort != null && downloadsDisplay != null
        && downloadsDescending != null && downloadsFilesOnly != null && hideFolderName != null && downloadsGridRows != null && downloadsGridColumns != null;
    private readonly WindowChrome chrome;
    private int previewRevision;
    private bool closed;
    private readonly string windowIcon = Path.Combine(Path.GetTempPath(), "WinMac-properties-" + Guid.NewGuid().ToString("N") + ".ico");
    public ItemPropertiesWindow(DockWindow dock, DockItem item)
    {
        this.dock = dock; this.item = item;
        fixedHeightProc = FixedHeightWindowProc;
        Title = $"{item.Name} Properties"; SystemBackdrop = new MicaBackdrop();
        IconResources.SetWindowIcon(this);
        AppWindow.TitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.ShowIconAndSystemMenu;
        panel.RequestedTheme = dock.Settings.Theme switch { DockTheme.Light => ElementTheme.Light, DockTheme.Dark => ElementTheme.Dark, _ => ElementTheme.Default };
        name = new TextBox { Text = item.Name, HorizontalAlignment = HorizontalAlignment.Stretch };
        var heading = new Grid { ColumnSpacing = 14 };
        heading.ColumnDefinitions.Add(new() { Width = new GridLength(32) }); heading.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        preview.VerticalAlignment = VerticalAlignment.Bottom; preview.Margin = new Thickness(0, 0, 0, 0);
        heading.Children.Add(preview); Grid.SetColumn(name, 1); heading.Children.Add(name); panel.Children.Add(heading);
        bool recycle = item.Kind == ItemKind.RecycleBin;
        recycleDefaults = recycle ? DockWindow.DefaultSpecialItem("recyclebin") : null;
        bool startMenu = item.Kind == ItemKind.StartMenu;
        bool simplified = recycle || startMenu || item.Kind is ItemKind.TaskView or ItemKind.Search || DockWindow.IsFileExplorer(item) || ShellService.IsSystemSettings(item) || ShellService.IsMicrosoftStore(item) || ShellService.IsTaskManager(item) || ShellService.IsXbox(item) || ShellService.IsSnippingTool(item);
        target = simplified ? new TextBox { Text = item.Target } : Field("Target", item.Target, true, true);
        bool downloads = item.Kind == ItemKind.Folder;
        arguments = downloads || simplified ? new TextBox { Text = item.Arguments } : Field("Arguments", item.Arguments);
        working = downloads || simplified ? new TextBox { Text = item.WorkingDirectory } : Field("Working directory", item.WorkingDirectory, true);
        identity = downloads || simplified ? new TextBox { Text = item.AppUserModelId } : Field("Application identity (AUMID)", item.AppUserModelId);
        AddSeparator();
        icon = new TextBox { Text = item.CustomIcon };
        darkIcon = new TextBox { Text = item.DarkModeIcon };
        if (recycle)
        {
            recycleEmpty.Text = item.RecycleEmptyIcon; recycleFull.Text = item.RecycleFullIcon;
            recycleEmptyDark.Text = item.RecycleEmptyDarkIcon; recycleFullDark.Text = item.RecycleFullDarkIcon;
            AddRecycleIconRow("Default / Light mode icons", recycleEmpty, recycleFull, false);
            AddRecycleIconRow("Dark mode icons (optional)", recycleEmptyDark, recycleFullDark, true);
        }
        else AddItemIconCards();
        AddSeparator();
        if (item.Kind == ItemKind.Folder && DockWindow.IsDownloads(item))
        {

            pinNewDownloads = new CheckBox { Content = "Pin newly downloaded files", IsChecked = item.Folder.PinNewDownloads };
            downloadPinLimit = new NumberBox { Minimum = 1, Maximum = 9, Value = Math.Clamp(item.Folder.DownloadPinLimit, 1, 9), SmallChange = 1, LargeChange = 1,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 120, MinWidth = 0, IsEnabled = item.Folder.PinNewDownloads };
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(downloadPinLimit, "Maximum pinned downloads");
            ToolTipService.SetToolTip(downloadPinLimit, "Number of recent downloads to keep pinned (1–9)");
            downloadPinLimit.ValueChanged += (_, _) =>
            {
                double value = double.IsNaN(downloadPinLimit.Value) ? item.Folder.DownloadPinLimit : downloadPinLimit.Value;
                double whole = Math.Clamp(Math.Round(value), 1, 9);
                if (downloadPinLimit.Value != whole) downloadPinLimit.Value = whole;
            };
            pinNewDownloads.Checked += (_, _) => downloadPinLimit.IsEnabled = true;
            pinNewDownloads.Unchecked += (_, _) => downloadPinLimit.IsEnabled = false;
            var limit = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            limit.Children.Add(new TextBlock { Text = "Limit pins to:", VerticalAlignment = VerticalAlignment.Center });
            limit.Children.Add(downloadPinLimit);
            var downloadPinRow = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 0, 8, 0) };
            downloadPinRow.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            downloadPinRow.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            downloadPinRow.Children.Add(pinNewDownloads);
            Grid.SetColumn(limit, 1); downloadPinRow.Children.Add(limit);
            panel.Children.Add(downloadPinRow);
            openNewDownloads = new CheckBox { Content = "Open files automatically after downloading", IsChecked = item.Folder.OpenNewDownloadsAutomatically };
            panel.Children.Add(openNewDownloads);
            unpinOpenedDownloads = new CheckBox { Content = "Unpin after opening", IsChecked = item.Folder.UnpinDownloadsAfterOpening };
            ToolTipService.SetToolTip(unpinOpenedDownloads, "Remove automatically pinned downloads after opening them from the dock.");
            panel.Children.Add(unpinOpenedDownloads);
            AddSeparator();
        }
        var launchOptions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24 };
        if (DockWindow.IsFileExplorer(item))
        {
            var locations = new Grid { ColumnSpacing = 12 };
            locations.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            locations.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            explorerRecent = CommandsDropdown("Recent", item.ExplorerRecentCommands);
            explorerPinned = CommandsDropdown("Pinned", item.ExplorerPinnedCommands);
            locations.Children.Add(explorerRecent); Grid.SetColumn(explorerPinned, 1); locations.Children.Add(explorerPinned);
            panel.Children.Add(locations);
            pinUsbDrives = new CheckBox { Content = "Automatically pin connected USB drives and memory cards", IsChecked = item.PinUsbDrives };
            panel.Children.Add(pinUsbDrives);
            panel.Children.Add(new TextBlock { Text = "Unpin automatically when disconnected. Manually pinned drives are kept.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
        }
        else if (ShellService.IsMicrosoftEdge(item))
        {
            edgeFavorites = CommandsDropdown("Favorites", item.EdgeFavoritesCommands);
            edgeWorkspaces = CommandsDropdown("Workspaces", item.EdgeWorkspacesCommands);
            edgeProfiles = CommandsDropdown("Profiles", item.EdgeProfilesCommands);
            var commands = new Grid { ColumnSpacing = 12 };
            foreach (var dropdown in new[] { edgeFavorites, edgeWorkspaces, edgeProfiles })
            {
                commands.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(dropdown, commands.Children.Count);
                dropdown.MinWidth = 0;
                commands.Children.Add(dropdown);
            }
            panel.Children.Add(commands);
        }
        else if (ShellService.IsXbox(item))
        {
            additionalCommands = CommandsDropdown("Categories", item.AdditionalCommands);
            xboxGames = CommandsDropdown("Games", item.XboxGamesCommands);
            var row = new Grid { ColumnSpacing = 12 };
            foreach (var dropdown in new[] { additionalCommands, xboxGames })
            {
                row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(dropdown, row.Children.Count); dropdown.MinWidth = 0; row.Children.Add(dropdown);
            }
            panel.Children.Add(row);
        }
        else if (ShellService.IsSnippingTool(item))
        {
            additionalCommands = CommandsDropdown("Tasks", item.AdditionalCommands);
            snippingFolders = CommandsDropdown("Folders", item.Snipping.FoldersCommands);
            var row = new Grid { ColumnSpacing = 12 };
            foreach (var dropdown in new[] { additionalCommands, snippingFolders })
            {
                row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(dropdown, row.Children.Count); dropdown.MinWidth = 0; row.Children.Add(dropdown);
            }
            panel.Children.Add(row);
        }
        else if (ShellService.IsSystemSettings(item) || ShellService.IsMicrosoftStore(item) || ShellService.IsTerminal(item) || ShellService.IsXbox(item) || ShellService.IsSnippingTool(item))
        {
            additionalCommands = CommandsDropdown(ShellService.IsTerminal(item) ? "Profiles" : "Additional commands", item.AdditionalCommands);
            panel.Children.Add(additionalCommands);
        }
        if (item.Kind == ItemKind.RecycleBin)
        {
            confirmRecycleBinEmpty = new CheckBox { Content = "Display confirmation dialog before deleting", IsChecked = item.ConfirmRecycleBinEmpty };
            launchOptions.Children.Add(confirmRecycleBinEmpty);
            playRecycleBinSounds = new CheckBox { Content = "Play sounds", IsChecked = item.PlayRecycleBinSounds };
            launchOptions.Children.Add(playRecycleBinSounds);
        }
        else if (ShellService.SupportsAdministratorLaunch(item) && !downloads && !startMenu)
        {
            runAsAdministrator = new CheckBox { Content = "Run as administrator", IsChecked = item.RunAsAdministrator };
            launchOptions.Children.Add(runAsAdministrator);
        }
        panel.Children.Add(launchOptions);
        if (ShellService.IsSnippingTool(item))
        {
            AddSeparator();
            pinNewCaptures = new CheckBox { Content = "Pin new screenshots and screen recordings", IsChecked = item.Snipping.PinNewCaptures };
            panel.Children.Add(pinNewCaptures);
            capturePinLimit = new NumberBox { Header = "Maximum capture pins", Minimum = 1, Maximum = 9, SmallChange = 1,
                Value = Math.Clamp(item.Snipping.PinLimit, 1, 9), SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline };
            panel.Children.Add(capturePinLimit);
            unpinOpenedCaptures = new CheckBox { Content = "Unpin after opening", IsChecked = item.Snipping.UnpinAfterOpening };
            panel.Children.Add(unpinOpenedCaptures);
            string screenshots = "", recordings = "";
            string? error = null;
            try { var locations = SnippingToolSettings.Read(); screenshots = locations.Screenshots; recordings = locations.Recordings; }
            catch (Exception ex) { error = ex.Message; Log.Write(ex); }
            screenshotFolder = CaptureFolderField("Screenshots folder", screenshots);
            recordingFolder = CaptureFolderField("Screen recordings folder", recordings);
            captureSettingsError = new TextBlock { Text = error ?? "These folders also apply to Snipping Tool.", TextWrapping = TextWrapping.Wrap, Opacity = .75 };
            panel.Children.Add(captureSettingsError);
        }
        if (!downloads && item.Kind is ItemKind.File or ItemKind.Folder)
        {
            thumbnail = new ToggleSwitch { Header = "Show as thumbnail in dock", IsOn = item.ShowThumbnail || item.Folder.StackIcon };
            panel.Children.Add(thumbnail);
        }
        if (downloads)
        {
            downloadsView = new ComboBox { Header = "Show content as", ItemsSource = new[] { FolderView.Grid, FolderView.List, FolderView.Menu }, SelectedIndex = item.Folder.View switch { FolderView.Grid => 0, FolderView.List => 1, FolderView.Menu => 2, _ => -1 }, MinWidth = 180 };
            downloadsSort = new ComboBox { Header = "Sort by", ItemsSource = new[] { "Name", "Size", "Date modified", "Date created", "Type" }, SelectedIndex = (int)item.Folder.Sort, MinWidth = 180 };
            downloadsDisplay = new ComboBox { Header = "Display as", ItemsSource = new[] { "Folder", "Stack" }, SelectedIndex = item.ShowThumbnail || item.Folder.StackIcon ? 1 : 0, MinWidth = 180 };
            var downloadsOptions = new Grid { ColumnSpacing = 12 };
            var dropdowns = new[] { downloadsView, downloadsSort, downloadsDisplay };
            for (int column = 0; column < dropdowns.Length; column++)
            {
                downloadsOptions.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
                dropdowns[column].MinWidth = 0;
                dropdowns[column].HorizontalAlignment = HorizontalAlignment.Stretch;
                Grid.SetColumn(dropdowns[column], column);
                downloadsOptions.Children.Add(dropdowns[column]);
            }
            panel.Children.Add(downloadsOptions);
            if (DockWindow.IsDownloads(item))
            {
                downloadsStackBase = new CheckBox { Content = "Show Downloads icon beneath stack", IsChecked = item.Folder.ShowDownloadsIconUnderStack, IsEnabled = downloadsDisplay.SelectedIndex == 1 };
                downloadsDisplay.SelectionChanged += (_, _) => downloadsStackBase.IsEnabled = downloadsDisplay.SelectedIndex == 1;
                panel.Children.Add(downloadsStackBase);
            }
            downloadsDescending = new ToggleSwitch { Header = DockWindow.IsDownloads(item) ? "Display files from bottom to top" : "Sort ascending", IsOn = !item.Folder.Descending };
            var gridLayout = new Grid { ColumnSpacing = 24, Margin = new Thickness(0, 12, 0, 0) };
            gridLayout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            gridLayout.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            downloadsGridRows = GridSizeSlider("Grid rows", item.Folder.GridRows);
            downloadsGridColumns = GridSizeSlider("Grid columns", item.Folder.GridColumns);
            gridLayout.Children.Add(downloadsGridRows); Grid.SetColumn(downloadsGridColumns, 1); gridLayout.Children.Add(downloadsGridColumns);
            panel.Children.Add(gridLayout);
            downloadsFilesOnly = new ToggleSwitch { Header = "Show files only", IsOn = item.Folder.FilesOnly };
            var toggles = new Grid { ColumnSpacing = 24 };
            toggles.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            toggles.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            toggles.Children.Add(downloadsDescending); Grid.SetColumn(downloadsFilesOnly, 1); toggles.Children.Add(downloadsFilesOnly);
            panel.Children.Add(toggles);
            hideFolderName = new CheckBox { Content = "Hide folder name header", IsChecked = item.Folder.HideFolderNameInGrid ?? false, IsEnabled = downloadsView.SelectedIndex is 0 or 1 };
            downloadsView.SelectionChanged += (_, _) => hideFolderName.IsEnabled = downloadsView.SelectedIndex is 0 or 1;
            panel.Children.Add(hideFolderName);
        }
        else { downloadsView = null; downloadsSort = null; }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var ok = new Button { Content = "OK", Width = 84 }; ok.Click += (_, _) => Save(true);
        var cancel = new Button { Content = "Cancel", Width = 84 }; cancel.Click += (_, _) => Close();
        apply.Click += (_, _) => Save(false);
        actions.Children.Add(ok); actions.Children.Add(cancel); actions.Children.Add(apply); panel.Children.Add(actions);
        chrome = new WindowChrome(this, new ScrollViewer { Content = panel, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, panel.RequestedTheme);
        chrome.Root.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; Close(); } }), true);
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.IsMaximizable = false;
        Native.SetWindowSubclass(WinRT.Interop.WindowNative.GetWindowHandle(this), fixedHeightProc, 104, 0);
        panel.RequestedTheme = ElementTheme.Default;
        foreach (var box in new[] { target, icon, darkIcon, identity }) box.TextChanged += (_, _) => _ = RefreshPreview();
        panel.ActualThemeChanged += (_, _) => _ = RefreshPreview();
        panel.Loaded += (_, _) => { FitHeight(); _ = RefreshPreview(); };
        Closed += (_, _) => { Native.RemoveWindowSubclass(WinRT.Interop.WindowNative.GetWindowHandle(this), fixedHeightProc, 104); closed = true; previewRevision++; try { File.Delete(windowIcon); } catch { } };
        AppWindow.Resize(new Windows.Graphics.SizeInt32(600, 650));
        TrackEdits(panel);
        // Icon cards edit backing text boxes rather than displaying them.
        // Include those values so Browse and Reset enable Apply as well.
        foreach (var box in new[] { icon, darkIcon, recycleEmpty, recycleFull, recycleEmptyDark, recycleFullDark }) TrackEdits(box);
        appliedValues = CurrentValues();
        UpdateApplyState();
    }
    private void FitHeight()
    {
        panel.Measure(new Windows.Foundation.Size(584, double.PositiveInfinity));
        var client = Native.ClientBounds(WinRT.Interop.WindowNative.GetWindowHandle(this));
        double scale = panel.XamlRoot?.RasterizationScale ?? 1;
        int chrome = Math.Max(0, AppWindow.Size.Height - client.Height);
        var monitors = Native.Monitors();
        int maximum = (monitors.FirstOrDefault(m => m.Bounds.Contains(AppWindow.Position.X, AppWindow.Position.Y)) ?? monitors.First(m => m.Primary)).Work.Height;
        AppWindow.Resize(new Windows.Graphics.SizeInt32((int)Math.Round(600 * scale), Math.Min(maximum, (int)Math.Ceiling((panel.DesiredSize.Height + 32) * scale) + chrome)));
    }
    private nint FixedHeightWindowProc(nint hwnd, uint message, nuint wp, nint lp, nuint id, nuint data)
    {
        var result = Native.DefSubclassProc(hwnd, message, wp, lp);
        if (message == 0x0084)
        {
            // Remove vertical resize grips without restricting programmatic sizing.
            return result.ToInt64() switch
            {
                12 or 15 => 1, // top/bottom -> client
                13 or 16 => 10, // left corners -> left edge
                14 or 17 => 11, // right corners -> right edge
                _ => result
            };
        }
        return result;
    }
    private static ComboBox CommandsDropdown(string header, AdditionalCommandsVisibility value) => new() { Header = header, ItemsSource = new[] { "Show in main menu", "Show in submenu", "Hide" }, SelectedIndex = Math.Clamp((int)value, 0, 2), HorizontalAlignment = HorizontalAlignment.Stretch };
    private static Slider GridSizeSlider(string header, int value)
    {
        var slider = new Slider { Header = header, Minimum = 1, Maximum = 7, StepFrequency = 1,
            SnapsTo = Microsoft.UI.Xaml.Controls.Primitives.SliderSnapsTo.StepValues,
            Value = Math.Clamp(value, 1, 7), IsThumbToolTipEnabled = true, HorizontalAlignment = HorizontalAlignment.Stretch };
        return slider;
    }
    private TextBox CaptureFolderField(string header, string value)
    {
        var box = new TextBox { Header = header, Text = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        panel.Children.Add(box);
        var browse = new Button { Content = "Browse\u2026" }; panel.Children.Add(browse);
        browse.Click += async (_, _) =>
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FolderPicker(); picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var folder = await picker.PickSingleFolderAsync(); if (folder != null && !closed) box.Text = folder.Path;
            }
            catch (Exception ex) { Log.Write(ex); if (captureSettingsError != null) captureSettingsError.Text = ex.Message; }
        };
        return box;
    }
    private TextBox Field(string header, string value, bool browse = false, bool location = false)
    {
        var box = new TextBox { Header = header, Text = value, HorizontalAlignment = HorizontalAlignment.Stretch };
        if (!browse) { panel.Children.Add(box); return box; }
        panel.Children.Add(box);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var button = new Button { Content = "Browse\u2026" };
        if (location)
        {
            var openLocation = new Button { Content = item.Kind == ItemKind.Folder ? "Open folder location" : "Open file location" };
            openLocation.Click += (_, _) => ShellService.OpenFileLocation(box.Text);
            buttons.Children.Add(openLocation);
        }
        buttons.Children.Add(button);
        panel.Children.Add(buttons);
        button.Click += async (_, _) =>
        {
            try
            {
                if (header == "Target" && item.Kind == ItemKind.Folder)
                {
                    var folderPicker = new Windows.Storage.Pickers.FolderPicker(); folderPicker.FileTypeFilter.Add("*");
                    WinRT.Interop.InitializeWithWindow.Initialize(folderPicker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    var folder = await folderPicker.PickSingleFolderAsync(); if (folder != null && !closed) box.Text = folder.Path;
                    return;
                }
                var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add("*");
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                var file = await picker.PickSingleFileAsync(); if (file != null && !closed)
                {
                    var selected = header.Contains("icon", StringComparison.OrdinalIgnoreCase) ? await ChooseIconSource(file.Path) : file.Path;
                    if (selected != null && !closed) box.Text = selected;
                }
            }
            catch (Exception ex) { Log.Write(ex); if (!closed) await new ContentDialog { XamlRoot = panel.XamlRoot, Title = "Cannot browse", Content = ex.Message, CloseButtonText = "OK" }.ShowAsync(); }
        };
        return box;
    }
    private void AddItemIconCards()
    {
        var row = new Grid { ColumnSpacing = 24 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (int column = 0; column < 2; column++)
        {
            bool dark = column == 1;
            var box = dark ? darkIcon : icon;
            var card = new StackPanel { Spacing = 10 };
            card.Children.Add(new TextBlock { Text = dark ? "Dark mode icon (optional)" : "Default / Light mode icon", TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
            var image = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center };
            card.Children.Add(image);
            var filename = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, MaxLines = 1 };
            card.Children.Add(filename);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
            var reset = new Button { Content = "Reset" };
            var browse = new Button { Content = "Browse\u2026" };
            actions.Children.Add(reset); actions.Children.Add(browse); card.Children.Add(actions);
            int revision = 0;
            async Task Update()
            {
                int version = ++revision;
                string custom = box.Text.Trim();
                string path = string.IsNullOrEmpty(custom)
                    ? dark && !string.IsNullOrWhiteSpace(icon.Text) ? icon.Text.Trim() : IconService.PathFor(Draft() with { CustomIcon = "", DarkModeIcon = "" })
                    : custom;
                string defaultPath = IconService.PathFor(Draft() with { CustomIcon = "", DarkModeIcon = "" });
                string resolved = BundledIconPath.Resolve(custom, Path.Combine(AppContext.BaseDirectory, "assets"));
                bool isDefault = string.IsNullOrEmpty(custom) || string.Equals(resolved, defaultPath, StringComparison.OrdinalIgnoreCase);
                filename.Text = isDefault ? "Default" : Path.GetFileName(custom);
                reset.IsEnabled = !isDefault;
                ToolTipService.SetToolTip(filename, isDefault ? null : path);
                var source = await IconService.Load(path, imageFile: !string.IsNullOrEmpty(custom), requestedPixels: 64);
                if (!closed && version == revision) image.Source = source;
            }
            card.Loaded += async (_, _) => await Update();
            box.TextChanged += async (_, _) => await Update();
            if (dark) icon.TextChanged += async (_, _) => await Update();
            target.TextChanged += async (_, _) => await Update();
            reset.Click += async (_, _) =>
            {
                SetIconPath(box, dark ? IconService.PathFor(Draft() with { CustomIcon = "", DarkModeIcon = "" }) : "");
                await Update();
            };
            browse.Click += async (_, _) =>
            {
                try
                {
                    var picker = new Windows.Storage.Pickers.FileOpenPicker();
                    foreach (var extension in new[] { ".ico", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".svg", ".dll", ".exe" }) picker.FileTypeFilter.Add(extension);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    var file = await picker.PickSingleFileAsync();
                    if (file == null || closed) return;
                    var selected = await ChooseIconSource(file.Path);
                    if (selected == null || closed) return;
                    IconService.Invalidate(selected); SetIconPath(box, selected); await Update();
                }
                catch (Exception ex) { Log.Write(ex); }
            };
            Grid.SetColumn(card, column); row.Children.Add(card);
        }
        panel.Children.Add(row);
    }
    private async Task<string?> ChooseIconSource(string path)
    {
        if (!new[] { ".dll", ".exe" }.Contains(Path.GetExtension(path).ToLowerInvariant())) return path;
        int count = IconService.ResourceIconCount(path);
        if (count <= 0)
        {
            await new ContentDialog { XamlRoot = panel.XamlRoot, Title = "No icons found", Content = "This file contains no selectable icons.", CloseButtonText = "OK" }.ShowAsync();
            return null;
        }
        var grid = new GridView { SelectionMode = ListViewSelectionMode.Single, IsItemClickEnabled = false, MaxHeight = 380, MinWidth = 420 };
        var dialog = new ContentDialog { XamlRoot = panel.XamlRoot, Title = "Choose an icon", Content = grid, PrimaryButtonText = "OK", CloseButtonText = "Cancel", IsPrimaryButtonEnabled = false, DefaultButton = ContentDialogButton.Primary };
        grid.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = grid.SelectedItem != null;
        bool dismissed = false;
        dialog.Closed += (_, _) => dismissed = true;
        var result = dialog.ShowAsync();
        try
        {
            for (int index = 0; index < count; index++)
            {
                if (closed || dismissed) break;
                var source = await IconService.Load(path + "," + index, imageFile: true, requestedPixels: 48);
                if (dismissed || closed) break;
                if (source == null) continue;
                var tile = new StackPanel { Width = 64, Spacing = 6, Padding = new Thickness(8), Tag = index };
                tile.Children.Add(new Image { Source = source, Width = 48, Height = 48, Stretch = Stretch.Uniform });
                tile.Children.Add(new TextBlock { Text = index.ToString(), HorizontalAlignment = HorizontalAlignment.Center });
                ToolTipService.SetToolTip(tile, "Icon " + index);
                grid.Items.Add(tile);
            }
            return await result == ContentDialogResult.Primary && grid.SelectedItem is StackPanel selected ? path + "," + selected.Tag : null;
        }
        finally { dialog.Hide(); }
    }
    private void AddSeparator() => panel.Children.Add(new Border { Height = 1, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(80, 128, 128, 128)), Margin = new Thickness(0, 2, 0, 2) });
    private void AddRecycleIconRow(string label, TextBox empty, TextBox full, bool dark, bool startMenu = false)
    {
        panel.Children.Add(new TextBlock { Text = label, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
        var row = new Grid { ColumnSpacing = 24 };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        if (!startMenu) row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (int column = 0; column < (startMenu ? 1 : 2); column++)
        {
            bool isFull = column == 1;
            var box = isFull ? full : empty;
            string defaultPath = startMenu || recycleDefaults == null ? "" : dark
                ? (isFull ? recycleDefaults.RecycleFullDarkIcon : recycleDefaults.RecycleEmptyDarkIcon)
                : (isFull ? recycleDefaults.RecycleFullIcon : recycleDefaults.RecycleEmptyIcon);
            var card = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
            if (!startMenu) card.Children.Add(new TextBlock { Text = isFull ? "Full" : "Empty", HorizontalAlignment = HorizontalAlignment.Center });
            var image = new Image { Width = 48, Height = 48, Stretch = Stretch.Uniform };
            card.Children.Add(image);
            var filename = new TextBlock { TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1 };
            card.Children.Add(filename);
            var browse = new Button { Content = "Browse\u2026", HorizontalAlignment = HorizontalAlignment.Center };
            var reset = new Button { Content = "Reset", HorizontalAlignment = HorizontalAlignment.Center };
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center };
            actions.Children.Add(reset); actions.Children.Add(browse); card.Children.Add(actions);
            int revision = 0;
            async Task Update()
            {
                int version = ++revision;
                string path = startMenu ? (dark && !string.IsNullOrWhiteSpace(darkIcon.Text) ? darkIcon.Text.Trim() : icon.Text.Trim()) : IconService.RecycleCustomPath(Draft(), isFull, dark);
                if (startMenu && string.IsNullOrWhiteSpace(path)) path = IconResources.PathFor(BundledIcons.Start);
                var assets = Path.Combine(AppContext.BaseDirectory, "assets");
                bool isDefault = string.IsNullOrWhiteSpace(box.Text)
                    || (!string.IsNullOrWhiteSpace(defaultPath) && string.Equals(
                        BundledIconPath.Resolve(box.Text.Trim(), assets), BundledIconPath.Resolve(defaultPath, assets), StringComparison.OrdinalIgnoreCase));
                reset.IsEnabled = !isDefault;
                filename.Text = isDefault ? "Default" : Path.GetFileName(path);
                ToolTipService.SetToolTip(filename, isDefault ? null : path);
                try
                {
                    var source = string.IsNullOrWhiteSpace(path) ? await IconService.LoadRecycleState(isFull, 64) : await IconService.Load(path, imageFile: true, requestedPixels: 64);
                    if (!closed && version == revision) image.Source = source;
                }
                catch (Exception ex) { Log.Write(ex); }
            }
            recyclePreviewUpdates.Add(Update);
            card.Loaded += async (_, _) => await Update();
            browse.Click += async (_, _) =>
            {
                try
                {
                    var picker = new Windows.Storage.Pickers.FileOpenPicker();
                    foreach (var extension in new[] { ".ico", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".svg", ".dll", ".exe" }) picker.FileTypeFilter.Add(extension);
                    WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
                    var file = await picker.PickSingleFileAsync();
                    if (file != null && !closed)
                    {
                        var selected = await ChooseIconSource(file.Path);
                        if (selected == null || closed) return;
                        IconService.Invalidate(selected);
                        SetIconPath(box, selected);
                        await RefreshRecyclePreviews();
                    }
                }
                catch (Exception ex) { Log.Write(ex); }
            };
            reset.Click += async (_, _) => { SetIconPath(box, defaultPath); await RefreshRecyclePreviews(); };
            Grid.SetColumn(card, column); row.Children.Add(card);
        }
        panel.Children.Add(row);
    }
    private async Task RefreshRecyclePreviews()
    {
        await Task.WhenAll(recyclePreviewUpdates.Select(update => update()));
        await RefreshPreview();
    }
    private DockItem Draft() => item with { Target = target.Text.Trim(), ExecutablePath = target.Text.Trim().EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target.Text.Trim() : target.Text.Trim() != item.Target ? "" : item.ExecutablePath,
        CustomIcon = icon.Text.Trim(), DarkModeIcon = darkIcon.Text.Trim(), AppUserModelId = identity.Text.Trim(),
        RecycleEmptyIcon = recycleEmpty.Text.Trim(), RecycleFullIcon = recycleFull.Text.Trim(), RecycleEmptyDarkIcon = recycleEmptyDark.Text.Trim(), RecycleFullDarkIcon = recycleFullDark.Text.Trim() };
    private async Task RefreshPreview()
    {
        if (closed || panel.XamlRoot == null) return;
        int version = ++previewRevision;
        try
        {
            var draft = Draft();
            // The properties preview follows this window's actual theme, including unsaved icon edits.
            string custom = panel.ActualTheme == ElementTheme.Dark && !string.IsNullOrEmpty(draft.DarkModeIcon) ? draft.DarkModeIcon : draft.CustomIcon;
            if (draft.Kind == ItemKind.RecycleBin) custom = IconService.RecycleCustomPath(draft, ShellService.RecycleCount() > 0, panel.ActualTheme == ElementTheme.Dark);
            var path = string.IsNullOrEmpty(custom) ? IconService.PathFor(draft with { DarkModeIcon = "", CustomIcon = "" }) : custom;
            var source = await IconService.Load(path, imageFile: !string.IsNullOrEmpty(custom), requestedPixels: 32);
            if (closed || version != previewRevision) return;
            preview.Source = source; chrome.Icon.Source = source;
            if (source == null) return;
            int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
            var frames = await Task.WhenAll(sizes.Select(size => IconService.Load(path, imageFile: !string.IsNullOrEmpty(custom), requestedPixels: size)));
            if (closed || version != previewRevision) return;
            int small = Array.FindIndex(sizes, size => size >= 16 * panel.XamlRoot.RasterizationScale);
            chrome.Icon.Source = frames[small < 0 ? 0 : small] ?? source;
            if (IconService.WriteWindowIcon(frames.OfType<ImageSource>().ToArray(), windowIcon)) AppWindow.SetIcon(windowIcon);
        }
        catch (Exception ex) { if (!closed) Log.Write(ex); }
    }
    private void Save(bool close)
    {
        if (screenshotFolder != null && recordingFolder != null)
        {
            try
            {
                var current = SnippingToolSettings.Read();
                if (!string.Equals(screenshotFolder.Text.Trim(), current.Screenshots, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(recordingFolder.Text.Trim(), current.Recordings, StringComparison.OrdinalIgnoreCase))
                    SnippingToolSettings.Apply(screenshotFolder.Text, recordingFolder.Text);
                dock.InvalidateCapturePaths();
                item.Snipping.PinNewCaptures = pinNewCaptures?.IsChecked == true;
                item.Snipping.UnpinAfterOpening = unpinOpenedCaptures?.IsChecked == true;
                item.Snipping.PinLimit = capturePinLimit == null || double.IsNaN(capturePinLimit.Value) ? 5 : (int)Math.Clamp(Math.Round(capturePinLimit.Value), 1, 9);
                if (captureSettingsError != null) captureSettingsError.Text = "These folders also apply to Snipping Tool.";
            }
            catch (Exception ex) { Log.Write(ex); if (captureSettingsError != null) captureSettingsError.Text = "Could not apply Snipping Tool folders: " + ex.Message; return; }
        }
        var draft = Draft(); var oldArguments = item.Arguments;
        item.Name = string.IsNullOrWhiteSpace(name.Text) ? item.Name : name.Text.Trim(); item.Target = draft.Target;
        name.Text = item.Name;
        item.Arguments = arguments.Text; item.WorkingDirectory = working.Text.Trim(); item.CustomIcon = draft.CustomIcon;
        item.DarkModeIcon = draft.DarkModeIcon; item.AppUserModelId = draft.AppUserModelId; item.ExecutablePath = draft.ExecutablePath;
        if (item.Kind == ItemKind.RecycleBin)
        {
            item.RecycleEmptyIcon = draft.RecycleEmptyIcon; item.RecycleFullIcon = draft.RecycleFullIcon;
            item.RecycleEmptyDarkIcon = draft.RecycleEmptyDarkIcon; item.RecycleFullDarkIcon = draft.RecycleFullDarkIcon;
        }
        if (runAsAdministrator != null) item.RunAsAdministrator = runAsAdministrator.IsChecked == true;
        if (edgeWorkspaces != null) item.EdgeWorkspacesCommands = (AdditionalCommandsVisibility)Math.Clamp(edgeWorkspaces.SelectedIndex, 0, 2);
        if (edgeProfiles != null) item.EdgeProfilesCommands = (AdditionalCommandsVisibility)Math.Clamp(edgeProfiles.SelectedIndex, 0, 2);
        if (edgeFavorites != null) item.EdgeFavoritesCommands = (AdditionalCommandsVisibility)Math.Clamp(edgeFavorites.SelectedIndex, 0, 2);
        if (xboxGames != null) item.XboxGamesCommands = (AdditionalCommandsVisibility)Math.Clamp(xboxGames.SelectedIndex, 0, 2);
        if (snippingFolders != null) item.Snipping.FoldersCommands = (AdditionalCommandsVisibility)Math.Clamp(snippingFolders.SelectedIndex, 0, 2);
        if (additionalCommands != null) item.AdditionalCommands = (AdditionalCommandsVisibility)Math.Clamp(additionalCommands.SelectedIndex, 0, 2);
        if (pinUsbDrives != null) item.PinUsbDrives = pinUsbDrives.IsChecked == true;
        if (explorerRecent != null) item.ExplorerRecentCommands = (AdditionalCommandsVisibility)Math.Clamp(explorerRecent.SelectedIndex, 0, 2);
        if (explorerPinned != null) item.ExplorerPinnedCommands = (AdditionalCommandsVisibility)Math.Clamp(explorerPinned.SelectedIndex, 0, 2);
        if (!ShellService.SupportsAdministratorLaunch(item)) item.RunAsAdministrator = false;
        if (confirmRecycleBinEmpty != null) item.ConfirmRecycleBinEmpty = confirmRecycleBinEmpty.IsChecked == true;
        if (playRecycleBinSounds != null) item.PlayRecycleBinSounds = playRecycleBinSounds.IsChecked == true;
        if (item.Target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && item.Arguments != oldArguments && !string.IsNullOrEmpty(item.ExecutablePath)) item.Target = item.ExecutablePath;
        if (thumbnail != null) { item.ShowThumbnail = thumbnail.IsOn; item.Folder.StackIcon = false; }
        if (downloadsDisplay != null) { item.ShowThumbnail = downloadsDisplay.SelectedIndex == 1; item.Folder.StackIcon = false; }
        if (downloadsView?.SelectedItem is FolderView view) item.Folder.View = view;
        if (downloadsSort != null) item.Folder.Sort = (FolderSort)Math.Clamp(downloadsSort.SelectedIndex, 0, 4);
        if (downloadsDescending != null) item.Folder.Descending = !downloadsDescending.IsOn;
        if (downloadsFilesOnly != null) item.Folder.FilesOnly = downloadsFilesOnly.IsOn;
        if (hideFolderName != null) item.Folder.HideFolderNameInGrid = hideFolderName.IsChecked == true;
        if (downloadsStackBase != null) item.Folder.ShowDownloadsIconUnderStack = downloadsStackBase.IsChecked == true;
        if (downloadsGridRows != null) item.Folder.GridRows = (int)Math.Round(downloadsGridRows.Value);
        if (downloadsGridColumns != null) item.Folder.GridColumns = (int)Math.Round(downloadsGridColumns.Value);
        if (openNewDownloads != null) item.Folder.OpenNewDownloadsAutomatically = openNewDownloads.IsChecked == true;
        if (pinNewDownloads != null) item.Folder.PinNewDownloads = pinNewDownloads.IsChecked == true;
        if (unpinOpenedDownloads != null) item.Folder.UnpinDownloadsAfterOpening = unpinOpenedDownloads.IsChecked == true;
        if (downloadPinLimit != null)
        {
            item.Folder.DownloadPinLimit = double.IsNaN(downloadPinLimit.Value) ? item.Folder.DownloadPinLimit : (int)Math.Clamp(Math.Round(downloadPinLimit.Value), 1, 9);
            downloadPinLimit.Value = item.Folder.DownloadPinLimit;
        }
        dock.ApplySettings();
        appliedValues = CurrentValues();
        UpdateApplyState();
        if (close) Close();
    }
    private List<(int Size, byte[] Png)> ReadWindowIconFrames()
    {
        using var reader = new BinaryReader(File.OpenRead(windowIcon));
        if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1) throw new Exception("Window icon is not an ICO container");
        int count = reader.ReadUInt16(); var frames = new List<(int Size, byte[] Png)>();
        for (int i = 0; i < count; i++)
        {
            reader.BaseStream.Position = 6 + i * 16;
            int width = reader.ReadByte(); if (width == 0) width = 256;
            reader.BaseStream.Position += 7;
            int length = reader.ReadInt32(), offset = reader.ReadInt32(); reader.BaseStream.Position = offset;
            frames.Add((width, reader.ReadBytes(length)));
        }
        if (!new[] { 16, 20, 24, 32, 40, 48, 64, 256 }.All(size => frames.Any(f => f.Size == size))) throw new Exception("Missing native/DPI icon frames");
        return frames;
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool DestroyIcon(nint icon);
}
