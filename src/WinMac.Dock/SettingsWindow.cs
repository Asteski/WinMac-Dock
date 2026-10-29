using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;
internal sealed partial class SettingsWindow : Window
{
    private readonly DockWindow dock;
    private readonly WindowChrome chrome;
    private readonly StackPanel panel = new() { Spacing = 16, Padding = new Thickness(28) };
    private readonly ContentControl content = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
    private readonly ScrollViewer categoryScroll = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock categoryTitle = new() { FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
    private readonly TextBlock categoryDescription = new() { TextWrapping = TextWrapping.Wrap, Opacity = .7 };
    private readonly Dictionary<string, StackPanel> categories = new();
    private string selectedCategory = "General";
    private TextBlock? taskbarStatus;
    internal void SyncTaskbarStatus(string? error)
    {
        if (taskbarStatus == null) return;
        taskbarStatus.Text = error ?? "Enables Windows auto-hide and moves the taskbar to the bottom before hiding it. Its original position and auto-hide setting are restored when turned off or when the dock exits.";
    }
    private bool dialogOpen;
    private string? placementDisplay;
    private DisplayDockLayout Placement => dock.Settings.LayoutForDisplay(placementDisplay ?? dock.DisplayIdentity);
    private HideMode LayoutHideMode { get => Placement.HideMode ?? dock.Settings.HideMode; set => Placement.HideMode = value; }
    private bool LayoutMagnificationEnabled { get => Placement.MagnificationEnabled ?? dock.Settings.MagnificationEnabled; set => Placement.MagnificationEnabled = value; }
    private readonly List<TextBlock> displayScopeNotes = [];
    private string EditingDisplayLabel => Native.Monitors().FirstOrDefault(m => m.Identity == (placementDisplay ?? dock.DisplayIdentity))?.Label ?? "Disconnected display";
    private bool MultipleDisplays => Native.Monitors().Count > 1;
    private string SettingsTitle() => MultipleDisplays ? "WinMac Dock Settings - " + EditingDisplayLabel : "WinMac Dock Settings";
    private string DisplayScopeText() => $"Applies only to {EditingDisplayLabel}. Saved separately for each display.";
    private void AddDisplayScopeNote()
    {
        var row = (Grid)panel.Children[^1];
        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        row.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var note = new TextBlock { Text = DisplayScopeText(), Visibility = MultipleDisplays ? Visibility.Visible : Visibility.Collapsed, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        void UpdateColor() => note.Foreground = new SolidColorBrush(note.ActualTheme == ElementTheme.Dark
            ? Windows.UI.Color.FromArgb(255, 96, 175, 255) : Windows.UI.Color.FromArgb(255, 0, 95, 184));
        note.ActualThemeChanged += (_, _) => UpdateColor(); UpdateColor();
        Grid.SetRow(note, 1); Grid.SetColumnSpan(note, 2); row.Children.Add(note);
        displayScopeNotes.Add(note);
    }
    private void UpdateSettingsTitle()
    {
        Title = SettingsTitle();
        chrome.SetTitle(Title);
        bool multipleDisplays = MultipleDisplays;
        foreach (var note in displayScopeNotes)
        {
            note.Text = DisplayScopeText();
            note.Visibility = multipleDisplays ? Visibility.Visible : Visibility.Collapsed;
        }
    }
    internal void SelectPlacementDisplay(string identity)
    {
        placementDisplay = identity;
        ShowSettings();
    }
    private void AddAppearanceColor(string label, string current, Action<string> changed)
    {
        var input = new TextBox { Header = label, Text = current, PlaceholderText = "Automatic (or #RRGGBB)", MaxLength = 7 };
        var picker = new ColorPicker { IsAlphaEnabled = false, IsAlphaSliderVisible = false, IsAlphaTextInputVisible = false,
            Color = AppearanceColor.Resolve(current, Microsoft.UI.Colors.Gray, 100) };
        var choose = new Button { Content = "Choose color", Flyout = new Flyout { Content = picker }, VerticalAlignment = VerticalAlignment.Bottom };
        picker.ColorChanged += (_, e) =>
        {
            string value = $"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}";
            input.Text = value; changed(value); PreviewSettings();
        };
        var reset = new Button { Content = "Automatic", VerticalAlignment = VerticalAlignment.Bottom };
        reset.Click += (_, _) => { input.Text = ""; changed(""); PreviewSettings(); };
        input.LostFocus += (_, _) =>
        {
            string value = input.Text.Trim();
            if (value.Length != 0 && !(value.Length == 7 && value[0] == '#' && value.Skip(1).All(Uri.IsHexDigit)))
            {
                input.Header = label + " — enter #RRGGBB or leave blank";
                return;
            }
            input.Header = label; changed(value.ToUpperInvariant()); PreviewSettings();
        };
        var row = new Grid { ColumnSpacing = 8, Tag = label };
        row.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new() { Width = GridLength.Auto }); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        row.Children.Add(input); Grid.SetColumn(choose, 1); row.Children.Add(choose); Grid.SetColumn(reset, 2); row.Children.Add(reset);
        panel.Children.Add(row);
    }
    private string appliedSettings = "";
    private readonly Button apply = new() { Content = "Apply", Width = 84, IsEnabled = false };
    private void UpdateApplyState() => apply.IsEnabled = appliedSettings.Length > 0
        && System.Text.Json.JsonSerializer.Serialize(dock.Settings) != appliedSettings;
    private void PreviewSettings()
    {
        dock.ApplySettings();
        UpdateApplyState();
    }
    private bool closingWithResult;
    private ComboBox? dockPosition;
    private ComboBox? dockAlignment;
    public SettingsWindow(DockWindow dock, string? displayIdentity = null)
    {
        this.dock = dock;
        placementDisplay = displayIdentity ?? dock.DisplayIdentity; IconResources.SetWindowIcon(this); AppWindow.TitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.ShowIconAndSystemMenu; Title = SettingsTitle(); SystemBackdrop = new MicaBackdrop(); SizeForCurrentMonitor();
        var layout = new Grid(); layout.ColumnDefinitions.Add(new() { Width = new GridLength(190) }); layout.ColumnDefinitions.Add(new() { Width = new(1, GridUnitType.Star) });
        var nav = new StackPanel { Spacing = 6, Padding = new Thickness(16, 18, 10, 18), Background = new SolidColorBrush(Windows.UI.Color.FromArgb(18, 128, 128, 128)) };
        foreach (var category in new[] { "General", "Appearance", "Position", "Behaviour", "Effects", "Advanced" })
        {
            var categoryButton = new Button { Content = category, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Stretch };
            categoryButton.Click += (_, _) => { selectedCategory = category; ShowCategory(category); };
            nav.Children.Add(categoryButton);
        }
        var page = new Grid(); page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var pageHeader = new StackPanel { Spacing = 8, Padding = new Thickness(28, 28, 28, 16) }; pageHeader.Children.Add(categoryTitle); pageHeader.Children.Add(categoryDescription);
        Grid.SetRow(categoryScroll, 1); page.Children.Add(pageHeader); page.Children.Add(categoryScroll);
        Grid.SetColumn(nav, 0); layout.Children.Add(nav); Grid.SetColumn(content, 1); layout.Children.Add(content); content.Content = page;
        var root = new Grid(); root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) }); root.RowDefinitions.Add(new() { Height = GridLength.Auto });
        root.Children.Add(layout);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Padding = new Thickness(20, 12, 20, 16) };
        var ok = new Button { Content = "OK", Width = 84 }; ok.Click += (_, _) => { if (!CommitSettings()) return; closingWithResult = true; Close(); };
        var cancel = new Button { Content = "Cancel", Width = 84 }; cancel.Click += (_, _) => { RestoreAppliedSettings(); closingWithResult = true; Close(); };
        apply.Click += (_, _) => CommitSettings();
        actions.Children.Add(ok); actions.Children.Add(cancel); actions.Children.Add(apply); Grid.SetRow(actions, 1); root.Children.Add(actions);
        appliedSettings = System.Text.Json.JsonSerializer.Serialize(dock.Settings);
        chrome = new WindowChrome(this, root, dock.Settings.Theme switch { DockTheme.Light => ElementTheme.Light, DockTheme.Dark => ElementTheme.Dark, _ => ElementTheme.Default });
        chrome.Root.Loaded += async (_, _) =>
        {
            SizeForCurrentMonitor();
            chrome.Icon.Source = await IconService.Load(IconResources.PathFor(BundledIcons.Application), imageFile: true, requestedPixels: 32);
        };

        uint lastDpi = Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        AppWindow.Changed += (_, _) =>
        {
            uint dpi = Native.GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
            if (dpi == lastDpi) return;
            lastDpi = dpi; SizeForCurrentMonitor();
        };
        ShowSettings();
        appliedSettings = System.Text.Json.JsonSerializer.Serialize(dock.Settings);
        UpdateApplyState();
        Closed += (_, _) => { if (!closingWithResult) RestoreAppliedSettings(); };
    }
    private void SizeForCurrentMonitor()
    {
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        double dpiScale = Native.GetDpiForWindow(handle) / 96d;
        if (dpiScale <= 0) dpiScale = 1;
        var displays = Native.Monitors();
        var display = displays.FirstOrDefault(m => m.Bounds.Contains(AppWindow.Position.X, AppWindow.Position.Y))
            ?? displays.First(m => m.Primary);
        var work = display.Work;
        int margin = (int)Math.Round(16 * dpiScale);
        int width = Math.Min((int)Math.Round(1000 * dpiScale), Math.Max(1, work.Width - 2 * margin));
        int height = Math.Min((int)Math.Round(805 * dpiScale), Math.Max(1, work.Height - 2 * margin));
        int x = Math.Clamp(AppWindow.Position.X, work.Left, Math.Max(work.Left, work.Right - width));
        int y = Math.Clamp(AppWindow.Position.Y, work.Top, Math.Max(work.Top, work.Bottom - height));
        AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(x, y, width, height));
    }
    private bool CommitSettings()
    {
        var previous = System.Text.Json.JsonSerializer.Deserialize<DockSettings>(appliedSettings);
        bool configured = false;
        dock.TryAction(() =>
        {
            if (previous?.StartAtLogin != dock.Settings.StartAtLogin)
                LoginStartup.Configure(dock.Settings.StartAtLogin);
            configured = true;
        });
        if (!configured) return false;
        appliedSettings = System.Text.Json.JsonSerializer.Serialize(dock.Settings);
        UpdateApplyState();
        return true;
    }
    private void RestoreAppliedSettings()
    {
        var saved = System.Text.Json.JsonSerializer.Deserialize<DockSettings>(appliedSettings);
        if (saved == null) return;
        foreach (var property in typeof(DockSettings).GetProperties().Where(property => property.CanWrite)) property.SetValue(dock.Settings, property.GetValue(saved));
        PreviewSettings();
    }
    private void Header(string title, string description)
    {
        dockPosition = null;
        dockAlignment = null;
        panel.Children.Clear(); panel.Children.Add(new TextBlock { Text = title, FontSize = 28, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold }); panel.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = .7 });
    }
    private void ShowSettings()
    {
        UpdateSettingsTitle();
        displayScopeNotes.Clear();
        Header("General", "Changes apply immediately. Cancel restores the last applied settings.");
        AddToggle("Show tray icon", dock.Settings.ShowTrayIcon, value => { dock.Settings.ShowTrayIcon = value; PreviewSettings(); });
        var theme = AddRow("Appearance", new ComboBox { ItemsSource = new[] { "Windows default", "Light", "Dark" }, SelectedIndex = (int)dock.Settings.Theme });
        theme.SelectionChanged += (_, _) => { if (theme.SelectedIndex >= 0) { dock.Settings.Theme = (DockTheme)theme.SelectedIndex; chrome.Root.RequestedTheme = dock.Settings.Theme switch { DockTheme.Light => ElementTheme.Light, DockTheme.Dark => ElementTheme.Dark, _ => ElementTheme.Default }; PreviewSettings(); } };
        AddBackdropControls();
        AddIconSizeSlider();
        AddIconSizeSlider(folderFlyout: true);
        var edge = AddRow("Dock position", new ComboBox { ItemsSource = DockWindow.PositionChoices.Select(edge => edge.ToString()).ToArray(), SelectedIndex = Array.IndexOf(DockWindow.PositionChoices, Placement.Edge) });
        AddDisplayScopeNote();
        dockPosition = edge;
        edge.SelectionChanged += (_, _) => { if (edge.SelectedIndex >= 0 && Placement.Edge != DockWindow.PositionChoices[edge.SelectedIndex]) { Placement.Edge = DockWindow.PositionChoices[edge.SelectedIndex]; PreviewSettings(); ShowSettings(); } };
        var alignmentLabels = Placement.Edge is DockEdge.Bottom or DockEdge.Top ? new[] { "Left", "Center", "Right" } : new[] { "Top", "Center", "Bottom" };
        var alignment = AddRow("Dock alignment", new ComboBox { ItemsSource = alignmentLabels, SelectedIndex = (int)Placement.Alignment });
        AddDisplayScopeNote();
        dockAlignment = alignment;
        alignment.SelectionChanged += (_, _) => { if (alignment.SelectedIndex >= 0 && Placement.Alignment != (DockAlignment)alignment.SelectedIndex) { Placement.Alignment = (DockAlignment)alignment.SelectedIndex; PreviewSettings(); } };
        panel.Children.Add(new TextBlock { Text = "Inner dock margins (physical pixels)", FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        AddMarginControls();
        AddSlider("Space between icons", dock.Settings.IconSpacing, dock.SetIconSpacing, minimum: -16);
        panel.Children.Add(new TextBlock { Text = "Negative values tighten adjacent icons and narrow their highlights to prevent overlap. Space beside separators stays at its zero-spacing size.", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
        AddSlider("Gap from screen edge", dock.Settings.FloatingGap, value => { dock.Settings.FloatingGap = value; PreviewSettings(); });
        AddToggle("Activate dock through screen-edge gap", dock.Settings.ActivateThroughScreenEdgeGap, value => { dock.Settings.ActivateThroughScreenEdgeGap = value; PreviewSettings(); });
        AddToggle("Show border around dock", dock.Settings.ShowDockBorder, value => { dock.Settings.ShowDockBorder = value; PreviewSettings(); });
        AddAppearanceColor("Dock border color", dock.Settings.DockBorderColor, value => dock.Settings.DockBorderColor = value);
        AddPercentSlider("Dock border opacity", dock.Settings.DockBorderOpacity, value => { dock.Settings.DockBorderOpacity = value; PreviewSettings(); });
        AddToggle("Enable rounded corners", dock.Settings.RoundedCorners, value => { dock.Settings.RoundedCorners = value; PreviewSettings(); });
        var spanFullWidth = AddToggle("Span the full screen width when docked", dock.Settings.SpanFullWidth, value => { dock.Settings.SpanFullWidth = value; PreviewSettings(); });
        var spanOnlyMaximized = AddToggle("Span only when windows fill the screen", dock.Settings.SpanFullWidthOnlyWhenMaximized, value => { dock.Settings.SpanFullWidthOnlyWhenMaximized = value; PreviewSettings(); });
        var squareWhenSpanning = AddToggle("Disable rounded corners while spanning", dock.Settings.SquareCornersWhenSpanning, value => { dock.Settings.SquareCornersWhenSpanning = value; PreviewSettings(); });
        spanOnlyMaximized.IsEnabled = spanFullWidth.IsOn;
        squareWhenSpanning.IsEnabled = spanFullWidth.IsOn && dock.Settings.RoundedCorners;
        spanFullWidth.Toggled += (_, _) => { spanOnlyMaximized.IsEnabled = spanFullWidth.IsOn; squareWhenSpanning.IsEnabled = spanFullWidth.IsOn && dock.Settings.RoundedCorners; };
        var groupCorner = AddRow("Group indicator corner", new ComboBox { ItemsSource = new[] { "Top right", "Top left", "Bottom right", "Bottom left" }, SelectedIndex = (int)dock.Settings.GroupIndicatorCorner });
        groupCorner.SelectionChanged += (_, _) => { if (groupCorner.SelectedIndex >= 0) { dock.Settings.GroupIndicatorCorner = (GroupIndicatorCorner)groupCorner.SelectedIndex; PreviewSettings(); } };
        var groupShape = AddRow("Group indicator shape", new ComboBox { ItemsSource = new[] { "Circle", "Square", "Squircle (4px corners)" }, SelectedIndex = (int)dock.Settings.GroupIndicatorShape });
        groupShape.SelectionChanged += (_, _) => { if (groupShape.SelectedIndex >= 0) { dock.Settings.GroupIndicatorShape = (GroupIndicatorShape)groupShape.SelectedIndex; PreviewSettings(); } };
        AddToggle("Use square group indicators when rounded corners are off", dock.Settings.SquareGroupIndicatorsWhenUnrounded, value => { dock.Settings.SquareGroupIndicatorsWhenUnrounded = value; PreviewSettings(); });
        AddToggle("Show icon highlights", dock.Settings.ShowIconHighlights, value => { dock.Settings.ShowIconHighlights = value; PreviewSettings(); });
        var highlightMode = AddRow("Icon highlight style", new ComboBox { ItemsSource = new[] { "Background", "Border", "Background + border" }, SelectedIndex = (int)dock.Settings.HighlightMode });
        highlightMode.SelectionChanged += (_, _) => { if (highlightMode.SelectedIndex >= 0) { dock.Settings.HighlightMode = (DockHighlightMode)highlightMode.SelectedIndex; PreviewSettings(); } };
        var indicatorAccent = AddRow("Accent color for running indicators", new ComboBox { ItemsSource = new[] { "Off", "Focused application", "All running applications" }, SelectedIndex = (int)dock.Settings.IndicatorAccent });
        indicatorAccent.SelectionChanged += (_, _) => { if (indicatorAccent.SelectedIndex >= 0) { dock.Settings.IndicatorAccent = (IndicatorAccentMode)indicatorAccent.SelectedIndex; PreviewSettings(); } };

        AddPercentSlider("Running indicator position", dock.Settings.IndicatorPosition, value => { dock.Settings.IndicatorPosition = value; PreviewSettings(); });
        AddAppearanceColor("Item indicator color", dock.Settings.ItemIndicatorColor, value => dock.Settings.ItemIndicatorColor = value);
        AddPercentSlider("Item indicator opacity", dock.Settings.ItemIndicatorOpacity, value => { dock.Settings.ItemIndicatorOpacity = value; PreviewSettings(); });
        panel.Children.Add(new TextBlock { Text = "0% places the indicator at the dock edge; 100% moves it nearest the icon.", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
        AddToggle("Keep indicators at the icon base", dock.Settings.ForceIndicatorsToItemBase, value => { dock.Settings.ForceIndicatorsToItemBase = value; PreviewSettings(); });
        var minimizedOnly = AddToggle("Show minimized applications only", dock.Settings.ShowMinimizedApplicationsOnly, value => { dock.Settings.ShowMinimizedApplicationsOnly = value; PreviewSettings(); });
        minimizedOnly.IsEnabled = dock.Settings.RedirectWindowAnimations;
        AddToggle("Minimize to dock", dock.Settings.RedirectWindowAnimations, value => { dock.Settings.RedirectWindowAnimations = value; minimizedOnly.IsEnabled = value; PreviewSettings(); });
        panel.Children.Add(new TextBlock { Text = "Windows minimize and restore animations use the matching dock icon as their destination. Windows controls the animation style; applications with their own startup effects can behave differently.", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
        panel.Children.Add(new TextBlock { Text = "Unpinned windows appear in the dock only while minimized. Minimized windows, including pinned applications, leave the Windows taskbar until restored. Pinned launchers remain visible. Requires Minimize to dock.", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
        var modes = new[] { "Always visible", "Hide after delay", "Hide for maximized applications", "Hide when active window overlaps", "Activation acts as a toggle" };
        var mode = AddRow("Dock visibility", new ComboBox { ItemsSource = modes, SelectedIndex = (int)LayoutHideMode });
        AddDisplayScopeNote();
        AddToggle("Hide Windows taskbar", dock.Settings.HideWindowsTaskbar, value => { dock.Settings.HideWindowsTaskbar = value; PreviewSettings(); });
        taskbarStatus = new TextBlock { Tag = "Hide Windows taskbar", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 };
        SyncTaskbarStatus(null);
        panel.Children.Add(taskbarStatus);
        var startHidden = AddToggle("Start dock hidden", dock.Settings.StartHidden, value => { dock.Settings.StartHidden = value; dock.Save(); });
        var reserve = AddToggle("Reserve space for the dock", dock.Settings.ReserveSpace, value => { dock.Settings.ReserveSpace = value; PreviewSettings(); });
        void RefreshVisibilityOptions() { startHidden.IsEnabled = LayoutHideMode != HideMode.AlwaysVisible; reserve.IsEnabled = LayoutHideMode is HideMode.AlwaysVisible or HideMode.Maximized; }
        mode.SelectionChanged += (_, _) => { if (mode.SelectedIndex < 0) return; LayoutHideMode = (HideMode)mode.SelectedIndex; RefreshVisibilityOptions(); PreviewSettings(); };
        panel.Children.Add(new TextBlock { Text = "Activation acts as a toggle: touch the dock's screen edge to show or hide it. Move away from the edge before touching it again. The dock stays in that state; fullscreen hiding still applies.", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
        RefreshVisibilityOptions();
        panel.Children.Add(new TextBlock { Text = "Delay, overlap and edge-toggle modes use an overlay without reserved space. Maximized-app mode keeps its reservation stable while hidden.", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
        AddToggle("Disable app attention animations", dock.Settings.PreventAttentionBlinking, value => { dock.Settings.PreventAttentionBlinking = value; PreviewSettings(); });
        var attentionAnimation = AddRow("App attention animation", new ComboBox { ItemsSource = new[] { "Blink", "Bounce" }, SelectedIndex = (int)dock.Settings.AttentionAnimation });
        attentionAnimation.SelectionChanged += (_, _) => { if (attentionAnimation.SelectedIndex >= 0) { dock.Settings.AttentionAnimation = (AttentionAnimation)attentionAnimation.SelectedIndex; PreviewSettings(); } };
        AddToggle("Show dock when an app needs attention", dock.Settings.ShowDockForAttention, value => { dock.Settings.ShowDockForAttention = value; PreviewSettings(); });
        panel.Children.Add(new TextBlock { Text = "Keeps a hidden dock visible until the app requesting attention is focused. Fullscreen hiding and Task View visibility still take priority.", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
        var reveal = AddRow("Mouse reveal behavior", new ComboBox { ItemsSource = new[] { "Temporary overlay (keep released space)", "Restore dock reservation" }, SelectedIndex = (int)dock.Settings.ManualRevealBehavior });
        reveal.SelectionChanged += (_, _) => { if (reveal.SelectedIndex >= 0) { dock.Settings.ManualRevealBehavior = (ManualRevealBehavior)reveal.SelectedIndex; PreviewSettings(); } };
        panel.Children.Add(new TextBlock { Text = "The temporary overlay remains visible while you use it, then returns to the hidden state when the pointer leaves. The keyboard shortcut always restores the normal dock reservation.", Tag = "Mouse reveal behavior", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
        AddNumber("Temporary overlay hide delay (ms)", dock.Settings.ManualOverlayHideDelayMs, 0, 5000, value => { dock.Settings.ManualOverlayHideDelayMs = value; PreviewSettings(); });
        AddToggle("Use the entire screen edge to reveal or hide the dock", dock.Settings.FullScreenEdgeTrigger, value => { dock.Settings.FullScreenEdgeTrigger = value; PreviewSettings(); });
        AddToggle("Keep a mouse-reveal strip when hidden", dock.Settings.KeepHiddenEdgeTrigger, value => { dock.Settings.KeepHiddenEdgeTrigger = value; PreviewSettings(); });
        panel.Children.Add(new TextBlock { Text = "Turn this off to hide the dock completely while keeping invisible screen-edge activation.", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
        AddToggle("Hide immediately for fullscreen applications", dock.Settings.HideImmediatelyForFullscreen, value => { dock.Settings.HideImmediatelyForFullscreen = value; PreviewSettings(); });
        var fullscreenExclusions = new Button { Content = "Fullscreen exclusions…", HorizontalAlignment = HorizontalAlignment.Stretch };
        fullscreenExclusions.Click += async (_, _) => await EditFullscreenExclusions(); panel.Children.Add(fullscreenExclusions);
        var monitors = Native.Monitors();
        var displayOptions = new[] { "All Monitors" }.Concat(monitors.Select(m => m.Label)).ToList();
        int selectedDisplay = dock.Settings.ShowOnAllMonitors ? 0 : 1 + Math.Max(0, monitors.FindIndex(m => m.Identity == dock.Settings.Monitor || m.Device == dock.Settings.Monitor || string.IsNullOrEmpty(dock.Settings.Monitor) && m.Primary));
        var display = AddRow("Display", new ComboBox { ItemsSource = displayOptions, SelectedIndex = selectedDisplay });
        display.SelectionChanged += (_, _) =>
        {
            if (display.SelectedIndex < 0) return;
            dock.Settings.ShowOnAllMonitors = display.SelectedIndex == 0;
            if (display.SelectedIndex > 0) dock.Settings.Monitor = monitors[display.SelectedIndex - 1].Identity;
            PreviewSettings();
            placementDisplay = display.SelectedIndex > 0 ? monitors[display.SelectedIndex - 1].Identity : dock.DisplayIdentity;
            ShowSettings();
        };
        AddToggle("Group windows of the same application", dock.Settings.GroupWindows, value => { dock.Settings.GroupWindows = value; PreviewSettings(); });
        var groupedClick = AddRow("When clicking an app with multiple windows", new ComboBox { ItemsSource = new[] { "Show window list", "Activate last used window", "Cycle windows" }, SelectedIndex = (int)dock.Settings.GroupedClickBehavior });
        groupedClick.SelectionChanged += (_, _) => { if (groupedClick.SelectedIndex >= 0) { dock.Settings.GroupedClickBehavior = (GroupedClickBehavior)groupedClick.SelectedIndex; PreviewSettings(); } };
        AddToggle("Combine dock icons with running applications", dock.Settings.CombinePinnedIconsWithRunningApplications, value => { dock.Settings.CombinePinnedIconsWithRunningApplications = value; PreviewSettings(); });
        AddToggle("Show items from all virtual desktops", dock.Settings.ShowItemsFromAllVirtualDesktops, value => { dock.Settings.ShowItemsFromAllVirtualDesktops = value; PreviewSettings(); });
        AddToggle("Show dock in Task View", dock.Settings.ShowInTaskView, value => { dock.Settings.ShowInTaskView = value; PreviewSettings(); });
        var bridgeHint = new TextBlock { TextWrapping = TextWrapping.Wrap, Opacity = .7, FontSize = 12 };
        bridgeHint.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "Showing the dock in Task View requires Windhawk and the " });
        bridgeHint.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = "WinMac Dock Task View Bridge", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        bridgeHint.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " mod. " });
        var bridgeStatus = dock.TaskViewBridgeStatus();
        var statusRun = bridgeStatus.ColorResource == null ? new Microsoft.UI.Xaml.Documents.Run()
            : (Microsoft.UI.Xaml.Documents.Run)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Run xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Foreground='{ThemeResource " + bridgeStatus.ColorResource + "}'/>");
        statusRun.Text = bridgeStatus.Text;
        bridgeHint.Inlines.Add(statusRun);
        panel.Children.Add(bridgeHint);
        panel.Children.Add(new TextBlock { Text = "When disabled, pinned icons remain launchers and running windows appear separately after the divider.", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
        AddToggle("Show icon labels", dock.Settings.ShowIconLabels, value => { dock.Settings.ShowIconLabels = value; PreviewSettings(); });
        AddToggle("Show file extensions", dock.Settings.ShowFileExtensionsInTooltips, value => { dock.Settings.ShowFileExtensionsInTooltips = value; PreviewSettings(); });
        AddToggle("Show menu icons", dock.Settings.ShowMenuGlyphs, value => { dock.Settings.ShowMenuGlyphs = value; PreviewSettings(); });
        AddPercentSlider("Hover magnification", dock.Settings.HoverMagnificationPercent, value => { dock.Settings.HoverMagnificationPercent = value; PreviewSettings(); });
        panel.Children.Add(new TextBlock { Text = "Hover magnification grows icons toward the desktop and moves neighboring items aside. 100% doubles the icon size. Disabling animations also disables magnification.", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
        AddOddSlider("Magnification span", dock.Settings.MagnificationSpan, value => { dock.Settings.MagnificationSpan = value; PreviewSettings(); });
        AddNumber("Magnification transition (ms)", dock.Settings.MagnificationMs, 0, 1000, value => { dock.Settings.MagnificationMs = value; PreviewSettings(); });
        AddNumber("Dock animation (ms)", dock.Settings.AnimationMs, 0, 1000, value => { dock.Settings.AnimationMs = value; PreviewSettings(); });
        AddNumber("Running app click speed (ms)", dock.Settings.RunningClickAnimationMs, 50, 2000, value => { dock.Settings.RunningClickAnimationMs = value; PreviewSettings(); });
        AddNumber("Launch bounce speed (ms)", dock.Settings.LaunchBounceMs, 100, 2000, value => { dock.Settings.LaunchBounceMs = value; PreviewSettings(); });
        AddNumber("Automatic hide delay (ms)", dock.Settings.HideDelayMs, 0, 5000, value => { dock.Settings.HideDelayMs = value; PreviewSettings(); });
        AddNumber("Edge reveal delay (ms)", dock.Settings.RevealDelayMs, 0, 2000, value => { dock.Settings.RevealDelayMs = value; PreviewSettings(); });
        var magnification = AddToggle("Enable magnification", LayoutMagnificationEnabled, value => { LayoutMagnificationEnabled = value; PreviewSettings(); });
        AddDisplayScopeNote();
        magnification.IsEnabled = !dock.Settings.ReduceMotion;
        var magnificationRow = (FrameworkElement)panel.Children[^1];
        magnificationRow.Opacity = dock.Settings.ReduceMotion ? .5 : 1;
        AddToggle("Follow Windows animation settings", dock.Settings.FollowWindowsAnimations, value => { dock.Settings.FollowWindowsAnimations = value; PreviewSettings(); });
        AddToggle("Disable animations", dock.Settings.ReduceMotion, value => { dock.Settings.ReduceMotion = value; magnification.IsEnabled = !value; magnificationRow.Opacity = value ? .5 : 1; PreviewSettings(); });
        AddToggle("Show Dock settings in item context menus", dock.Settings.ShowDockSettingsInItemMenus, value => { dock.Settings.ShowDockSettingsInItemMenus = value; PreviewSettings(); });
        AddToggle("Use a keyboard shortcut to show or hide the dock", dock.Settings.HotkeyEnabled, value => { dock.Settings.HotkeyEnabled = value; PreviewSettings(); });
        AddShortcutRow();
        AddToggle("Launch WinMac Dock at sign-in", dock.Settings.StartAtLogin, value => { dock.Settings.StartAtLogin = value; PreviewSettings(); });
        var folder = new Button { Content = "Open configuration folder" }; folder.Click += (_, _) => dock.TryAction(() => { Directory.CreateDirectory(Log.DirectoryPath); ShellService.Open(Log.DirectoryPath); }); panel.Children.Add(folder);
        AddToggle("Always run as administrator", dock.Settings.AlwaysRunAsAdministrator, value => { dock.Settings.AlwaysRunAsAdministrator = value; PreviewSettings(); });
        panel.Children.Add(new TextBlock { Text = "Applies after restarting the dock. Windows will ask for permission when elevation is required.", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
        // A fresh Settings window always begins with the everyday controls.
        selectedCategory = "General";
        OrganizeCategories();
    }
    private void OrganizeCategories()
    {
        categories.Clear();
        var descriptions = new Dictionary<string, string>
        {
            ["General"] = "Everyday controls, startup, and dock management.",
            ["Appearance"] = "Theme, size, background material, and visual indicators.",
            ["Position"] = "Where the dock appears and how it fits the display.",
            ["Behaviour"] = "Visibility, windows, and workspace behavior.",
            ["Effects"] = "Dock borders, corners, and visual styling.",
            ["Advanced"] = "Motion and interaction tuning."
        };
        foreach (var (name, pageDescription) in descriptions)
        {
            var categoryPanel = new StackPanel { Spacing = 18, Padding = new Thickness(28, 4, 20, 28), HorizontalAlignment = HorizontalAlignment.Stretch };
            categories[name] = categoryPanel;
        }

        var sections = new Dictionary<(string Category, string Title), StackPanel>();
        StackPanel Section(string category, string title, string description)
        {
            var key = (category, title);
            if (sections.TryGetValue(key, out var existing)) return existing;
            var body = new StackPanel { Spacing = 12, Padding = new Thickness(20, 16, 20, 18) };
            var card = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = body
            };
            body.Children.Add(new TextBlock { Text = title, FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            if (!string.IsNullOrEmpty(description)) body.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = .68, FontSize = 12, Margin = new Thickness(0, -6, 0, 4) });
            categories[category].Children.Add(card); sections[key] = body; return body;
        }
        (string Category, string Section, string Description) Classify(string text, string currentCategory, string currentSection)
        {
            if (text == "Use the entire screen edge to reveal or hide the dock") return ("Behaviour", "Visibility", "Control when the dock stays visible and how it returns.");
            if (text == "Always run as administrator" || text.StartsWith("Applies after restarting the dock.", StringComparison.Ordinal)) return ("General", "Permissions", "Choose the permissions the dock requests when it starts.");
            if (text == "Show tray icon") return ("General", "Tray icon", "Control whether the dock appears in the notification area.");
            if (text.StartsWith("Mouse reveal", StringComparison.OrdinalIgnoreCase) || text.StartsWith("The temporary overlay", StringComparison.OrdinalIgnoreCase)) return ("Behaviour", "Visibility", "Control when the dock stays visible and how it returns.");
            if (text.StartsWith("Running app click speed", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Launch bounce", StringComparison.OrdinalIgnoreCase)) return ("Advanced", "Motion", "Fine-tune hover and launch animation timing.");
            if (text.StartsWith("Hover magnification", StringComparison.OrdinalIgnoreCase)) return ("Advanced", "Motion", "Fine-tune hover and launch animation timing.");
            if (text.Contains("shortcut", StringComparison.OrdinalIgnoreCase) || text.Contains("sign-in", StringComparison.OrdinalIgnoreCase) || text.Contains("configuration folder", StringComparison.OrdinalIgnoreCase)) return ("General", "Startup & shortcuts", "Choose how the dock starts and how you reveal it.");
            if (text.Contains("indicator", StringComparison.OrdinalIgnoreCase)) return ("Appearance", "Indicators", "Configure grouping marks and running-app indicators.");
            if (text == "Show menu icons") return ("Advanced", "Context menus", "Choose which commands and icons appear in item menus.");
            if (text == "Folder flyout icon size" || text == "Show file extensions" || text.Contains("icon label", StringComparison.OrdinalIgnoreCase)) return ("Appearance", "Icons", "Configure icon labels and highlights.");
            if (text.Contains("margin", StringComparison.OrdinalIgnoreCase) || text.Contains("gap", StringComparison.OrdinalIgnoreCase) || text.Contains("Space between", StringComparison.OrdinalIgnoreCase) || text.StartsWith("Negative values", StringComparison.OrdinalIgnoreCase)) return ("Position", "Spacing & margins", "Adjust icon spacing, dock gaps, and margins.");
            if (text is "Show icon highlights" or "Icon highlight style") return ("Appearance", "Icons", "Configure icon labels and highlights.");
            if (text.Contains("Dock border", StringComparison.OrdinalIgnoreCase)) return ("Effects", "Dock styling", "Refine the dock surface and item highlight treatment.");
            if (text.Contains("Dock background", StringComparison.OrdinalIgnoreCase) || text.Contains("material", StringComparison.OrdinalIgnoreCase) || text.Contains("opacity", StringComparison.OrdinalIgnoreCase) || text.Contains("tint", StringComparison.OrdinalIgnoreCase)) return ("Appearance", "Background material", "Choose the dock surface and its transparency.");
            if (text.Contains("Span the full screen width", StringComparison.OrdinalIgnoreCase) || text.Contains("Span only when windows fill the screen", StringComparison.OrdinalIgnoreCase) || text.Contains("while spanning", StringComparison.OrdinalIgnoreCase)) return ("Effects", "Dock styling", "Refine the dock surface and item highlight treatment.");
            if (text.Contains("highlight", StringComparison.OrdinalIgnoreCase) || text.Contains("rounded", StringComparison.OrdinalIgnoreCase) || text.Contains("border around", StringComparison.OrdinalIgnoreCase)) return ("Effects", "Dock styling", "Refine the dock surface and item highlight treatment.");
            if (text.Contains("Appearance", StringComparison.OrdinalIgnoreCase) || text.Contains("icon size", StringComparison.OrdinalIgnoreCase)) return ("Appearance", "Theme & size", "Set the overall look and preferred icon size.");
            if (text.Contains("position", StringComparison.OrdinalIgnoreCase) || text.Contains("alignment", StringComparison.OrdinalIgnoreCase) || text.Contains("margin", StringComparison.OrdinalIgnoreCase) || text.Contains("gap", StringComparison.OrdinalIgnoreCase) || text.Contains("Display", StringComparison.OrdinalIgnoreCase) || text.Contains("Space between", StringComparison.OrdinalIgnoreCase)) return ("Position", text.Contains("margin", StringComparison.OrdinalIgnoreCase) ? "Spacing & margins" : "Dock placement", "Set the dock edge, display, alignment, and available space.");
            if (text.Contains("fullscreen", StringComparison.OrdinalIgnoreCase) || text.Contains("Visibility", StringComparison.OrdinalIgnoreCase) || text.Contains("hide", StringComparison.OrdinalIgnoreCase) || text.Contains("reveal", StringComparison.OrdinalIgnoreCase) || text.Contains("Task View", StringComparison.OrdinalIgnoreCase) || text.Contains("attention", StringComparison.OrdinalIgnoreCase) || text.Contains("taskbar", StringComparison.OrdinalIgnoreCase)) return ("Behaviour", "Visibility", "Control when the dock stays visible and how it returns.");
            if (text.Contains("minimiz", StringComparison.OrdinalIgnoreCase) || text.Contains("window", StringComparison.OrdinalIgnoreCase) || text.Contains("virtual desktop", StringComparison.OrdinalIgnoreCase) || text.Contains("Group windows", StringComparison.OrdinalIgnoreCase) || text.Contains("Combine dock", StringComparison.OrdinalIgnoreCase)) return ("Behaviour", "Windows & tasks", "Set how running windows appear and interact with the dock.");
            if (text.Contains("Show Dock settings in item context menus", StringComparison.OrdinalIgnoreCase)) return ("Advanced", "Context menus", "Choose which commands appear in item menus.");
            if (text.Contains("magnif", StringComparison.OrdinalIgnoreCase) || text.Contains("animation", StringComparison.OrdinalIgnoreCase) || text.Contains("motion", StringComparison.OrdinalIgnoreCase)) return ("Advanced", "Motion", "Fine-tune hover and launch animation timing.");
            return (currentCategory, currentSection, "");
        }

        panel.Children.RemoveAt(0); panel.Children.RemoveAt(0);
        var category = "General"; var section = "Startup & shortcuts"; var description = "Choose how the dock starts and how you reveal it.";
        foreach (var child in panel.Children.ToArray())
        {
            var text = (child as FrameworkElement)?.Tag?.ToString() ?? child switch { TextBlock t => t.Text ?? "", ContentControl c => c.Content?.ToString() ?? "", _ => child.GetType().GetProperty("Header")?.GetValue(child)?.ToString() ?? "" };
            if (text == "Inner dock margins (physical pixels)") { category = "Position"; section = "Spacing & margins"; description = "Adjust the dock footprint on screen."; continue; }
            if (child is not Slider || !string.IsNullOrWhiteSpace(text)) (category, section, description) = Classify(text, category, section);
            panel.Children.Remove(child); Section(category, section, description).Children.Add(child);
        }
        ShowCategory(selectedCategory);
    }
    private void ShowCategory(string category)
    {
        if (!categories.TryGetValue(category, out var selected)) return;
        categoryTitle.Text = category;
        categoryDescription.Text = category switch
        {
            "General" => "Everyday controls, startup, and dock management.",
            "Appearance" => "Theme, size, background material, and visual indicators.",
            "Position" => "Where the dock appears and how it fits the display.",
            "Behaviour" => "Visibility, windows, and workspace behavior.",
            "Effects" => "Dock borders, corners, and visual styling.",
            _ => "Motion and interaction tuning."
        };
        categoryScroll.Content = selected;
        categoryScroll.ChangeView(null, 0, null, true);
    }
    public void SyncDockPosition()
    {
        if (!dock.Settings.ShowOnAllMonitors || !MultipleDisplays) placementDisplay = dock.DisplayIdentity;
        UpdateSettingsTitle();
        if (dockPosition != null && dockPosition.SelectedIndex != Array.IndexOf(DockWindow.PositionChoices, Placement.Edge)) dockPosition.SelectedIndex = Array.IndexOf(DockWindow.PositionChoices, Placement.Edge);
        if (dockAlignment != null && dockAlignment.SelectedIndex != (int)Placement.Alignment) dockAlignment.SelectedIndex = (int)Placement.Alignment;
    }
    private void AddBackdropControls()
    {
        var modes = new[] { DockMaterial.Default, DockMaterial.None, DockMaterial.Acrylic, DockMaterial.Mica };
        var material = AddRow("Dock background effect", new ComboBox { ItemsSource = new[] { "Default", "None", "Acrylic", "Mica" }, SelectedIndex = Array.IndexOf(modes, dock.Settings.Material) });
        var controls = new StackPanel { Spacing = 12 }; panel.Children.Add(controls);
        void Refresh()
        {
            controls.Children.Clear();
            bool plain = dock.Settings.Material == DockMaterial.None;

            var status = new TextBlock { Text = dock.BackdropStatus, TextWrapping = TextWrapping.Wrap, Opacity = .7, FontSize = 12 };
            if (dock.Settings.Material == DockMaterial.Default)
            {
                controls.Children.Add(new TextBlock { Text = "Windows taskbar background. Colors and transparency are managed by Windows.", TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
                controls.Children.Add(status);
                return;
            }

            var opacityLabel = new TextBlock { Text = $"{(plain ? "Background opacity" : "Color tint strength")}: {dock.Settings.BackgroundStrength}%" };
            var opacity = new Slider { Minimum = 0, Maximum = 100, Value = dock.Settings.BackgroundStrength, StepFrequency = 1, IsThumbToolTipEnabled = true, HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = AccentBrush() };
            opacity.ValueChanged += (_, e) => { if (!double.IsNaN(e.NewValue)) { var value = (int)Math.Round(e.NewValue); opacityLabel.Text = $"{(plain ? "Background opacity" : "Color tint strength")}: {value}%"; dock.Settings.BackgroundStrength = value; PreviewSettings(); status.Text = dock.BackdropStatus; } };
            if (!plain)
            {
                var luminosityName = dock.Settings.Material == DockMaterial.Acrylic ? "Background opacity" : "Wallpaper tint opacity";
                var luminosityLabel = new TextBlock { Text = $"{luminosityName}: {dock.Settings.MaterialLuminosity}%" };
                var luminosity = new Slider { Minimum = 0, Maximum = 100, Value = dock.Settings.MaterialLuminosity, StepFrequency = 1, IsThumbToolTipEnabled = true, HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = AccentBrush() };
                luminosity.ValueChanged += (_, e) => { if (!double.IsNaN(e.NewValue)) { var value = (int)Math.Round(e.NewValue); luminosityLabel.Text = $"{luminosityName}: {value}%"; dock.Settings.MaterialLuminosity = value; PreviewSettings(); status.Text = dock.BackdropStatus; } };
                controls.Children.Add(luminosityLabel); controls.Children.Add(luminosity);
            }
            controls.Children.Add(opacityLabel); controls.Children.Add(opacity);
            var hint = plain ? "0% is transparent; 100% is solid. No blur is applied."
                : dock.Settings.Material == DockMaterial.Acrylic ? "Background opacity has the strongest effect on translucency. Color tint strength changes the color balance and can be subtle with a neutral tint. Windows controls the blur radius."
                : "Mica uses the desktop wallpaper, not windows behind the dock. Wallpaper tint opacity changes its shading; it does not reveal live windows.";
            controls.Children.Add(new TextBlock { Text = hint, TextWrapping = TextWrapping.Wrap, Opacity = .65, FontSize = 12 });
            controls.Children.Add(status);
        }
        material.SelectionChanged += (_, _) => { if (material.SelectedIndex >= 0) { dock.Settings.Material = modes[material.SelectedIndex]; PreviewSettings(); Refresh(); } };
        Refresh();
    }
    private T AddRow<T>(string label, T control) where T : FrameworkElement
    {
        panel.Children.Add(SettingRow(label, control)); return control;
    }
    // Matches the selected ToggleSwitch track, including the user's system accent.
    private static Brush AccentBrush() => (Brush)Application.Current.Resources["SystemControlHighlightAltAccentBrush"];
    private ToggleSwitch AddToggle(string text, bool value, Action<bool> change)
    {
        var control = new ToggleSwitch { IsOn = value };
        control.Toggled += (_, _) => change(control.IsOn);
        panel.Children.Add(SettingRow(text, control)); return control;
    }
    private static Grid ToggleRow(string text, bool value, Action<bool> change)
    {
        var control = new ToggleSwitch { IsOn = value };
        control.Toggled += (_, _) => change(control.IsOn);
        return SettingRow(text, control);
    }
    private static Grid SettingRow(string label, FrameworkElement control)
    {
        var row = new Grid { ColumnSpacing = 20, MinHeight = 36, Tag = label };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var caption = label switch
        {
            "Appearance" => "Theme",
            "Preferred dock icon size" => "Icon size",
            "Group indicator corner" => "Group indicator position",
            "Accent color for running indicators" => "Indicator accent color",
            "Running indicator position" => "Indicator position",
            "Keep indicators at the icon base" => "Keep indicators at icon base",
            "Use square group indicators when rounded corners are off" => "Square indicators with square corners",
            "Use a keyboard shortcut to show or hide the dock" => "Enable dock shortcut",
            "Hide immediately for fullscreen applications" => "Hide for fullscreen apps",
            "When clicking an app with multiple windows" => "Multi-window click action",
            "Combine dock icons with running applications" => "Combine pinned and running apps",
            "Disable app attention animations" => "Disable attention animations",
            "Show dock when an app needs attention" => "Show dock for app attention",
            "Show items from all virtual desktops" => "Show all virtual desktops",
            _ => label
        };
        var text = new TextBlock { Text = caption, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        control.HorizontalAlignment = HorizontalAlignment.Right;
        if (control is ToggleSwitch toggle)
        {
            toggle.MinWidth = 0;
            toggle.OnContent = "";
            toggle.OffContent = "";
            var state = new TextBlock { Text = toggle.IsOn ? "On" : "Off", VerticalAlignment = VerticalAlignment.Center };
            toggle.Toggled += (_, _) => state.Text = toggle.IsOn ? "On" : "Off";
            toggle.RegisterPropertyChangedCallback(Control.IsEnabledProperty, (_, _) => state.Opacity = toggle.IsEnabled ? 1 : .4);
            var group = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, HorizontalAlignment = HorizontalAlignment.Right };
            group.Children.Add(state);
            group.Children.Add(toggle);
            Grid.SetColumn(text, 0); Grid.SetColumn(group, 1); row.Children.Add(text); row.Children.Add(group);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(toggle, caption);
            return row;
        }
        else if (control is Control field)
        {
            field.MinWidth = 0;
            field.Width = 220;
        }
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, caption);
        Grid.SetColumn(text, 0); Grid.SetColumn(control, 1); row.Children.Add(text); row.Children.Add(control); return row;
    }
    private void AddNumber(string header, int value, int min, int max, Action<int> change)
    {
        var box = AddRow(header, new NumberBox { Value = value, Minimum = min, Maximum = max, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact });
        box.ValueChanged += (_, e) => { if (!double.IsNaN(e.NewValue)) change((int)e.NewValue); };
    }
    private void AddIconSizeSlider(bool folderFlyout = false)
    {
        int[] sizes = [16, 20, 24, 28, 32, 40, 48, 56, 64];
        int currentSize = folderFlyout ? dock.Settings.FolderFlyoutIconSize : dock.Settings.IconSize;
        string title = folderFlyout ? "Folder flyout icon size" : "Preferred dock icon size";
        int selectedIndex = Enumerable.Range(0, sizes.Length).MinBy(i => Math.Abs(sizes[i] - currentSize));
        var slider = new Slider
        {
            Minimum = 0, Maximum = sizes.Length - 1, Value = selectedIndex,
            StepFrequency = 1, SmallChange = 1, LargeChange = 1,
            SnapsTo = Microsoft.UI.Xaml.Controls.Primitives.SliderSnapsTo.StepValues, IsThumbToolTipEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch, Foreground = AccentBrush()
        };
        var sizeLabel = new TextBlock { Text = $"{currentSize} px", VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        var control = new Grid { Width = 220, ColumnSpacing = 12 };
        control.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        control.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        control.Children.Add(slider);
        Grid.SetColumn(sizeLabel, 1);
        control.Children.Add(sizeLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(slider, folderFlyout ? title : "Icon size");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(slider, sizeLabel.Text);
        panel.Children.Add(SettingRow(title, control));
        slider.ValueChanged += (_, e) =>
        {
            int size = sizes[Math.Clamp((int)Math.Round(e.NewValue), 0, sizes.Length - 1)];
            sizeLabel.Text = $"{size} px";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(slider, sizeLabel.Text);
            if (folderFlyout) dock.Settings.FolderFlyoutIconSize = size;
            else dock.Settings.IconSize = size;
            PreviewSettings();
        };
    }
    private Slider AddSlider(string header, int value, Action<int> change, int minimum = 0)
    {
        var slider = AddRow(header, new Slider { Minimum = minimum, Maximum = 8, Value = value, StepFrequency = 1, IsThumbToolTipEnabled = true, Foreground = AccentBrush() });
        slider.ValueChanged += (_, e) =>
        {
            if (double.IsNaN(e.NewValue)) return;
            int next = (int)Math.Round(e.NewValue); change(next);
        };
        return slider;
    }
    private void AddMarginControls()
    {
        bool syncing = false;
        var rows = new List<FrameworkElement>();
        void SetAll(int value)
        {
            dock.Settings.DockMarginLeft = dock.Settings.DockMarginTop = dock.Settings.DockMarginRight = dock.Settings.DockMarginBottom = value;
            PreviewSettings();
        }
        var link = AddToggle("Adjust all margins together", dock.Settings.LinkDockMargins, _ => { });
        var all = AddSlider("Margins", dock.Settings.DockMarginLeft, value => { if (!syncing) SetAll(value); });
        var allRow = (FrameworkElement)panel.Children[^1];
        var values = new[] { dock.Settings.DockMarginLeft, dock.Settings.DockMarginTop, dock.Settings.DockMarginRight, dock.Settings.DockMarginBottom };
        Action<int>[] changes = [value => dock.Settings.DockMarginLeft = value, value => dock.Settings.DockMarginTop = value, value => dock.Settings.DockMarginRight = value, value => dock.Settings.DockMarginBottom = value];
        var sliders = new List<Slider>();
        var labels = new[] { "Left margin", "Top margin", "Right margin", "Bottom margin" };
        for (int i = 0; i < labels.Length; i++)
        {
            var change = changes[i];
            sliders.Add(AddSlider(labels[i], values[i], value => { if (syncing) return; change(value); PreviewSettings(); }));
            rows.Add((FrameworkElement)panel.Children[^1]);
        }
        void UpdateRows()
        {
            allRow.Visibility = link.IsOn ? Visibility.Visible : Visibility.Collapsed;
            foreach (var row in rows) row.Visibility = link.IsOn ? Visibility.Collapsed : Visibility.Visible;
        }
        link.Toggled += (_, _) =>
        {
            syncing = true;
            if (link.IsOn) { all.Value = sliders[0].Value; SetAll((int)all.Value); }
            else foreach (var slider in sliders) slider.Value = all.Value;
            dock.Settings.LinkDockMargins = link.IsOn;
            syncing = false; UpdateRows(); PreviewSettings();
        };
        UpdateRows();
    }
    private void AddPercentSlider(string header, int value, Action<int> change)
    {
        var slider = AddRow(header, new Slider { Minimum = 0, Maximum = 100, Value = value, StepFrequency = 1, IsThumbToolTipEnabled = true, Foreground = AccentBrush() });
        slider.ValueChanged += (_, e) => { var next = (int)Math.Round(e.NewValue); change(next); };
    }
    private void AddOddSlider(string header, int value, Action<int> change)
    {
        var slider = AddRow(header, new Slider { Minimum = 1, Maximum = 9, Value = value, StepFrequency = 1, IsThumbToolTipEnabled = true, Foreground = AccentBrush() });
        slider.ValueChanged += (_, e) => { var next = Math.Clamp((int)Math.Round(e.NewValue), 1, 9); change(next); };
    }
    private void AddShortcutRow()
    {
        var description = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        description.Children.Add(new TextBlock { Text = "Dock activation shortcut", FontSize = 14 });
        description.Children.Add(new TextBlock { Text = "Customize the shortcut to show or hide the dock.", Opacity = .68, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        var edit = new Button { HorizontalAlignment = HorizontalAlignment.Right, HorizontalContentAlignment = HorizontalAlignment.Right, Padding = new Thickness(10, 6, 10, 6) };
        SetShortcutButtonContent(edit);
        edit.Click += async (_, _) => await EditShortcutAsync(edit);
        var row = new Grid { ColumnSpacing = 20, MinHeight = 54, Tag = "shortcut" };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(description, 0); Grid.SetColumn(edit, 1); row.Children.Add(description); row.Children.Add(edit);
        panel.Children.Add(row);
    }
    private void SetShortcutButtonContent(Button button)
    {
        var keys = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Right };
        var modifiers = dock.Settings.HotkeyModifiers;
        foreach (var key in new[]
        {
            (modifiers & 2) != 0 ? "Ctrl" : "",
            (modifiers & 1) != 0 ? "Alt" : "",
            (modifiers & 4) != 0 ? "Shift" : "",
            dock.Settings.HotkeyKey != 0 ? DockHotkey.KeyName(dock.Settings.HotkeyKey) : "Set shortcut"
        }.Where(x => !string.IsNullOrEmpty(x)))
        {
            keys.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["ShortcutKeyStyle"],
                CornerRadius = new CornerRadius(6), Padding = new Thickness(10, 6, 10, 6),
                Child = new TextBlock { Text = key, Style = (Style)Application.Current.Resources["ShortcutKeyTextStyle"] }
            });
        }
        keys.Children.Add(new FontIcon { Glyph = "\uE70F", FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) });
        button.Content = keys;
    }
    private async Task EditShortcutAsync(Button opener)
    {
        uint modifiers = dock.Settings.HotkeyModifiers;
        int key = dock.Settings.HotkeyKey;
        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var badgeHost = new Grid { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        badgeHost.Children.Add(badges);
        var capture = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(35, 0, 0, 0)), CornerRadius = new CornerRadius(8), Padding = new Thickness(24), MinHeight = 88, Content = badgeHost, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch };
        void Render()
        {
            badges.Children.Clear();
            foreach (var text in new[] { (modifiers & 2) != 0 ? "Ctrl" : "", (modifiers & 1) != 0 ? "Alt" : "", (modifiers & 4) != 0 ? "Shift" : "", key != 0 ? DockHotkey.KeyName(key) : "Press a key" }.Where(text => !string.IsNullOrEmpty(text)))
                badges.Children.Add(new Border { Style = (Style)Application.Current.Resources["ShortcutKeyStyle"], CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Child = new TextBlock { Text = text, Style = (Style)Application.Current.Resources["ShortcutKeyTextStyle"] } });
        }
        uint Modifier(Windows.System.VirtualKey value) => value switch { Windows.System.VirtualKey.Control => 2, Windows.System.VirtualKey.Menu => 1, Windows.System.VirtualKey.Shift => 4, _ => 0 };
        capture.KeyDown += (_, e) =>
        {
            uint modifier = Modifier(e.Key);
            if (modifier != 0) { if (key != 0) { key = 0; modifiers = modifier; } else modifiers |= modifier; Render(); e.Handled = true; return; }
            if (DockHotkey.IsValidKey((int)e.Key)) { key = (int)e.Key; Render(); e.Handled = true; }
        };
        var reset = new HyperlinkButton { Content = "Reset" }; reset.Click += (_, _) => { modifiers = 3; key = 0x44; Render(); };
        var clear = new HyperlinkButton { Content = "Clear" }; clear.Click += (_, _) => { modifiers = 0; key = 0; Render(); };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, HorizontalAlignment = HorizontalAlignment.Center }; actions.Children.Add(reset); actions.Children.Add(clear);
        var content = new StackPanel { Spacing = 16 }; content.Children.Add(new TextBlock { Text = "Press the shortcut you want to use. It must include Ctrl, Alt, or Shift.", TextWrapping = TextWrapping.Wrap }); content.Children.Add(capture); content.Children.Add(actions);
        var dialog = new ContentDialog { XamlRoot = ((FrameworkElement)Content).XamlRoot, Title = "Activation shortcut", Content = content, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
        dialog.Opened += (_, _) => capture.Focus(FocusState.Programmatic);
        dialog.PrimaryButtonClick += (_, e) => { if (modifiers == 0 || !DockHotkey.IsValidKey(key)) e.Cancel = true; };
        Render();
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        dock.Settings.HotkeyModifiers = modifiers; dock.Settings.HotkeyKey = key; PreviewSettings();
        SetShortcutButtonContent(opener);
    }
    public async void ShowApps()
    {
        Header("Add an application", "Includes installed desktop apps, packaged apps, and registered browser apps. You can also drop a shortcut directly onto the dock.");
        var search = new TextBox { PlaceholderText = "Search applications", HorizontalAlignment = HorizontalAlignment.Stretch }; panel.Children.Add(search);
        var status = new TextBlock { Text = "Reading installed applications…" }; panel.Children.Add(status);
        var list = new ListView { MaxHeight = 480, SelectionMode = ListViewSelectionMode.Single, DisplayMemberPath = "Name" }; panel.Children.Add(list);
        var add = new Button { Content = "Pin selected application", IsEnabled = false }; panel.Children.Add(add);
        list.SelectionChanged += (_, _) => add.IsEnabled = list.SelectedItem != null;
        add.Click += (_, _) => { if (list.SelectedItem is DockItem item) { dock.AddItem(item with { Id = Guid.NewGuid().ToString("N") }); status.Text = item.Name + " pinned"; } };
        try
        {
            // Shell automation enumerates on an STA, away from the UI thread.
            var apps = await RunSta(ShellService.InstalledApps);
            list.ItemsSource = apps; status.Text = $"{apps.Count} applications";
            search.TextChanged += (_, _) => list.ItemsSource = apps.Where(a => a.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase)).ToList();
        }
        catch (Exception ex) { status.Text = "Could not enumerate applications: " + ex.Message; }
    }
    private static Task<T> RunSta<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(); var thread = new Thread(() => { try { completion.SetResult(action()); } catch (Exception ex) { completion.SetException(ex); } }); thread.SetApartmentState(ApartmentState.STA); thread.IsBackground = true; thread.Start(); return completion.Task;
    }
    private void ShowItems()
    {
        Header("Dock items", dock.Settings.LockIconPositions ? "Icon positions are locked. Hold Alt while dragging to rearrange them." : "Drag pinned items on the dock to reorder them, or edit their launch details here.");
        foreach (var item in dock.Settings.Items)
        {
            var row = new Button { Content = item.Name, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left }; row.Click += (_, _) => Edit(item); panel.Children.Add(row);
        }
    }
    private void EditDefaultClickActions()
    {
        Header("Click actions", "Used by every dock icon and running task. Changes apply immediately.");
        var controls = new StackPanel { Spacing = 12 }; panel.Children.Add(controls);
        AddClickActionSelectors(controls, dock.Settings.DefaultClickActions, () => { dock.Save(); UpdateApplyState(); });
        ClickActionHint();
    }
    private static readonly (DockClickAction Action, string Label)[] clickActionOptions =
    [
        (DockClickAction.Default, "Default (open / activate)"),
        (DockClickAction.Nothing, "Nothing"), (DockClickAction.Minimize, "Minimize"),
        (DockClickAction.Close, "Close"), (DockClickAction.BringToFront, "Bring to Front"),
        (DockClickAction.BringToFrontOrMinimize, "Bring to Front / Minimize"),
        (DockClickAction.OpenTaskMenu, "Open Task Menu"), (DockClickAction.MinimizeSimilar, "Minimize Similar"),
        (DockClickAction.RestoreSimilar, "Restore Similar"), (DockClickAction.CloseSimilar, "Close Similar"),
        (DockClickAction.Open, "Open / launch")
    ];
    private static void AddClickActionSelectors(StackPanel controls, ClickActions actions, Action changed)
    {
        foreach (var (button, label) in new[] { (DockMouseButton.Left, "Left click"), (DockMouseButton.Right, "Right click"), (DockMouseButton.Middle, "Middle click") })
        {
            var box = new ComboBox { ItemsSource = clickActionOptions.Select(o => o.Label).ToArray(), SelectedIndex = Array.FindIndex(clickActionOptions, o => o.Action == actions.For(button)) };
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedIndex < 0) return;
                var value = clickActionOptions[box.SelectedIndex].Action;
                if (button == DockMouseButton.Left) actions.Left = value;
                else if (button == DockMouseButton.Right) actions.Right = value;
                else actions.Middle = value;
                changed();
            };
            controls.Children.Add(SettingRow(label, box));
        }
    }
    private void ClickActionHint() => panel.Children.Add(new TextBlock { Text = "Default opens files and folder stacks, activates a task, or opens the grouped window picker. Open / launch opens the target directly. Window actions do nothing without a running window. Similar means all windows of the same application, including when grouping is off. Close asks the application to close normally; it does not force quit. You can always edit actions through Settings → Dock items, even if right-click is Nothing.", TextWrapping = TextWrapping.Wrap, Opacity = .7 });
    public void Edit(DockItem item)
    {
        dock.EditItem(item);
    }
    public async Task ShowMessage(string title, string text)
    {
        if (dialogOpen) return; dialogOpen = true;
        try { await new ContentDialog { XamlRoot = ((FrameworkElement)Content).XamlRoot, Title = title, Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, CloseButtonText = "OK" }.ShowAsync(); }
        finally { dialogOpen = false; }
    }
}
