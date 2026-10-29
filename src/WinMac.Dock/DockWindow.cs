using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.ViewManagement;
using WinRT;
using Microsoft.UI.Composition.SystemBackdrops;

namespace WinMac.Dock;
internal sealed partial class DockWindow : Window
{
    internal static readonly DockEdge[] PositionChoices = [DockEdge.Top, DockEdge.Left, DockEdge.Bottom, DockEdge.Right];
    internal static bool ShowItemsFromAllVirtualDesktops { get; set; }
    private sealed class DockRootGrid : Grid
    {
        public void SetDragCursor(bool allowed) => ProtectedCursor = Microsoft.UI.Input.InputSystemCursor.Create(allowed ? Microsoft.UI.Input.InputSystemCursorShape.Arrow : Microsoft.UI.Input.InputSystemCursorShape.UniversalNo);
    }
    private readonly DockRootGrid root = new();
    private readonly Border dockBorder = new() { IsHitTestVisible = false, BackgroundSizing = BackgroundSizing.InnerBorderEdge };
    private readonly Border insertionMarker = new() { Width = 3, CornerRadius = new CornerRadius(1.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly StackPanel strip = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom, Padding = new Thickness(8, 0, 8, 4) };
    private readonly ScrollViewer scroll;
    private readonly List<(Button Button, FrameworkElement Icon, DockEntry Entry)> buttons = [];
    private readonly SettingsStore store = new(Path.Combine(Program.StartupCommand == "--test-beta-defaults"
        ? Path.Combine(Path.GetTempPath(), "WinMac-beta-defaults-" + Environment.ProcessId) : Log.DirectoryPath, "settings.json"));
    private MenuFlyout? trayMenu;
    private readonly TaskTracker tracker = new();
    private readonly DispatcherTimer visibilityTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly DispatcherTimer reconcileTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private readonly DispatcherTimer themeTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private string themeIdentity = "";
    private int themeRevision;
    private string renderedIconTheme = "";
    private readonly DispatcherTimer safetyTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer recycleTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly Native.SubclassProc subclass;
    private readonly UISettings uiSettings = new();
    private readonly AccessibilitySettings accessibility = new();
    private readonly System.Windows.Forms.NotifyIcon tray;
    private readonly AppBar appbar;
    private MonitorData monitor = null!;
    private PixelRect resting;
    private PixelRect visibleBounds;
    private readonly Dictionary<FrameworkElement, float> hoverTargets = [];
    private readonly Dictionary<Button, Border> runningIndicators = [];
    private readonly Dictionary<Button, Border> groupIndicators = [];
    private readonly Dictionary<FrameworkElement, FrameworkElement> dragVisuals = [];
    private readonly Canvas dragOverlay = new() { IsHitTestVisible = false };
    private Grid? dragVisualParent;
    private int dragVisualIndex;
    private Thickness dragVisualMargin;
    private Windows.Foundation.Point dragVisualOrigin;
    private readonly Dictionary<Button, Border> taskbarHighlights = [];
    private readonly HashSet<Button> hoveredButtons = [], pressedButtons = [];
    private long animationRectRequests;
    private PixelRect? lastAnimationTarget;
    private double scale = 1;
    private int popupCount;
    private bool dragging, hidden, manuallyHidden, closing, positioning, initialized;
    private string? internalDragId;
    private DateTime? hideSince, revealSince, manualHideSince;
    private string lastSignature = "";
    private nint lastStyledForeground;
    private int lastStyledThemeRevision = -1;
    private ElementTheme lastStyledTheme;
    private nint foregroundBeforeClick;
    private List<TaskWindow> windows = [];
    private SettingsWindow? settingsWindow;
    private ItemPropertiesWindow? propertiesWindow;
    private long recycleCount = -1;
    private string recycleIconIdentity = "";
    private Button? gapHoveredButton;
    private bool gapLeftPressed, gapRightPressed, gapMiddlePressed;
    private readonly uint explorerRestart = Native.RegisterWindowMessage("TaskbarCreated");
    private readonly uint shellMessage = Native.RegisterWindowMessage("SHELLHOOK");
    private readonly DockBackdrop backdrop;
    private bool inspection;
    private bool keyboardFocus;
    private readonly Dictionary<string, DateTime> pendingLaunches = [];
    private readonly Dictionary<string, FileSystemWatcher> stackWatchers = [];
    private readonly Dictionary<Grid, int> folderPreviewVersions = [];
    private readonly Dictionary<string, (int Theme, int Pixels, List<Microsoft.UI.Xaml.Media.ImageSource> Sources)> folderPreviewCache = [];
    private bool hoverLayoutExpanded;
    private bool iconResizeCheckRunning;
    private int? backgroundMenuInsertionSlot;
    private readonly HashSet<FrameworkElement> dragReorderElements = [];
    private bool dragDropAllowed = true;
    private double draggedVisualOpacity = 1;
    private readonly Dictionary<FrameworkElement, double> dragRestingStarts = [];
    private readonly Dictionary<FrameworkElement, double> dragPreviewOffsets = [];
    private bool preparingItemDrag;
    private int dragTargetSlot;
    private readonly Dictionary<Border, Visibility> dragIndicatorVisibility = [];
    private FrameworkElement? draggedElement;
    private FrameworkElement? draggedVisual;
    private readonly Border hoverLabel = new() { Visibility = Visibility.Collapsed, IsHitTestVisible = false, Background = new SolidColorBrush(Windows.UI.Color.FromArgb(220, 30, 30, 34)), CornerRadius = new CornerRadius(5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = new TextBlock { TextAlignment = TextAlignment.Center, Padding = new Thickness(8, 4, 8, 4), Foreground = new SolidColorBrush(Microsoft.UI.Colors.White) } };
    private Windows.Foundation.Point dragStartRoot;
    public IReadOnlyList<DockEntry> RunningEntries => buttons.Where(b => b.Entry.Windows.Count > 0).Select(b => b.Entry).ToArray();
    public ClickActions ClickActionsFor(DockEntry entry) => Settings.DefaultClickActions;
    public nint Handle { get; }
    public DockSettings Settings { get; }
    public double AvailablePopupHeight => Math.Max(200, Math.Min(620, monitor.Work.Height / scale - 120));
    public double AvailableFolderPopupHeight => Math.Max(200, monitor.Work.Height / scale - 120);
    public double AvailablePopupWidth => Math.Max(240, monitor.Work.Width / scale - 32);
    private bool SystemMotionSuppressed => Settings.FollowWindowsAnimations && !uiSettings.AnimationsEnabled;
    private bool EffectiveIconHighlights => Settings.ShowIconHighlights || SystemMotionSuppressed;
    public bool Motion => !Settings.ReduceMotion && !SystemMotionSuppressed && !accessibility.HighContrast;
    public double RasterScale => scale;
    private int IconPixels => IconRaster.Pixels(DockGeometry.HoverSizePercent(DisplayIconSize, Settings.HoverMagnificationPercent, 1), scale);
    private bool Vertical => PositionEdge is DockEdge.Left or DockEdge.Right;
    private double displayIconSize;
    private double displayPadding;
    private double? displaySpacing;
    private int displayRequestedSize;
    private bool reconciling;
    private bool dockOverflow;
    private double DisplayIconSize => displayIconSize > 0 ? displayIconSize : Settings.IconSize;
    private double DisplayPadding => displayPadding > 0 ? displayPadding : Settings.WindowsTaskbarHighlights ? 20 : 12;
    private double ButtonSize => DisplayIconSize + DisplayPadding;
    private double ButtonCrossSize => ButtonSize + MarginDip(Vertical ? Settings.DockMarginLeft + Settings.DockMarginRight : Settings.DockMarginTop + Settings.DockMarginBottom);
    private double DisplaySpacing => displaySpacing ?? Settings.IconSpacing;
    private double SeparatorWidth => 1 / scale;
    private double FitRatio => DisplayIconSize / (double)(displayRequestedSize > 0 ? displayRequestedSize : Settings.IconSize);
    private double HoverOverflow => DisplayMagnificationEnabled ? Math.Max(0, DockGeometry.HoverSizePercent(DisplayIconSize, Settings.HoverMagnificationPercent, 1) - DisplayIconSize) : 0;
    private double MarginDip(int physicalPixels) => physicalPixels / Math.Max(1, scale);
    private double CurrentDockThickness => (dockOverflow ? 16 : 0) + (Vertical
        ? ButtonSize + MarginDip(Settings.DockMarginLeft + Settings.DockMarginRight)
        : ButtonSize + MarginDip(Settings.DockMarginTop + Settings.DockMarginBottom));
    private FlyoutPlacementMode PopupPlacement => PositionEdge == DockEdge.Left ? FlyoutPlacementMode.Right : PositionEdge == DockEdge.Right ? FlyoutPlacementMode.Left : PositionEdge == DockEdge.Top ? FlyoutPlacementMode.Bottom : FlyoutPlacementMode.Top;
    private double Along(Windows.Foundation.Point point) => Vertical ? point.Y : point.X;
    public string BackdropStatus => materialBackdrop?.Status ?? backdrop.Status;

    internal readonly string? MonitorOverride;
    private string SelectedMonitor => MonitorOverride ?? Settings.Monitor;
    internal string DisplayIdentity => monitor?.Identity ?? ResolveDisplay().Identity;
    private MonitorData ResolveDisplay()
    {
        var displays = Native.Monitors();
        return displays.FirstOrDefault(m => m.Identity == SelectedMonitor || m.Device == SelectedMonitor)
            ?? displays.FirstOrDefault(m => m.Primary) ?? displays[0];
    }
    internal DockEdge PositionEdge { get => Settings.LayoutForDisplay(DisplayIdentity).Edge; set => Settings.LayoutForDisplay(DisplayIdentity).Edge = value; }
    internal DockAlignment PositionAlignment { get => Settings.LayoutForDisplay(DisplayIdentity).Alignment; set => Settings.LayoutForDisplay(DisplayIdentity).Alignment = value; }
    internal HideMode DisplayHideMode { get => Settings.LayoutForDisplay(DisplayIdentity).HideMode ?? Settings.HideMode; set => Settings.LayoutForDisplay(DisplayIdentity).HideMode = value; }
    internal bool DisplayMagnificationEnabled { get => Settings.LayoutForDisplay(DisplayIdentity).MagnificationEnabled ?? Settings.MagnificationEnabled; set => Settings.LayoutForDisplay(DisplayIdentity).MagnificationEnabled = value; }
    public DockWindow(DockSettings? sharedSettings = null, string? monitorOverride = null)
    {
        Title = "WinMac Dock";
        MonitorOverride = monitorOverride;
        try { Settings = sharedSettings ?? LoadStartupSettings(); }
        catch (Exception ex) { Log.Write(ex); Settings = Defaults(); if (File.Exists(store.Path)) File.Copy(store.Path, store.Path + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true); }
        // Also cover shared settings and the fallback after a load error.
        // Persist before creating controls or allowing settings edits.
        if (!File.Exists(store.Path))
        {
            store.Save(Settings);
            Log.Write("Created missing settings file: " + store.Path);
        }
        monitor = ResolveDisplay();
        hidden = DockStartup.Hidden(Settings, Program.StartupCommand, DisplayHideMode);
        manuallyHidden = Program.StartupCommand == "--hide";
        startupWaiting = hidden && !manuallyHidden && DisplayHideMode != HideMode.EdgeToggle;
        edgeToggleHidden = hidden; previousHideMode = DisplayHideMode;
        root.Opacity = hidden ? 0 : 1;
        Handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        Native.RegisterPassiveWindow(Handle);
        gapInput = new ScreenEdgeGapInput(AcceptGapRightClick, GapRightClick);
        ShowItemsFromAllVirtualDesktops = Settings.ShowItemsFromAllVirtualDesktops;
        var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true;
        AppWindow.IsShownInSwitchers = false;
        backdrop = new DockBackdrop(this);
        root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        strip.UseLayoutRounding = false;
        scroll = new ScrollViewer { UseLayoutRounding = false, Content = strip, Height = DisplayIconSize + 16, HorizontalScrollMode = ScrollMode.Enabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollMode = ScrollMode.Disabled, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, VerticalAlignment = VerticalAlignment.Bottom };
        root.Children.Add(dockBorder);
        root.Children.Add(scroll);
        insertionMarker.Background = (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"];
        root.Children.Add(hoverArtwork); root.Children.Add(insertionMarker); root.Children.Add(dragOverlay); Content = root;
        root.AllowDrop = true;
        root.DragOver += DockDragOver;
        root.DragLeave += (_, _) => { dragging = internalDragId != null; insertionMarker.Visibility = Visibility.Collapsed; };
        root.Drop += DockDrop;
        root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(PointerMoved), true);
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            var point = e.GetCurrentPoint(root).Position;
            
            HoldHoverForClick(point);
        }), true);
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler(EnlargedIconPressed), true);
        root.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            if (e.GetCurrentPoint(root).Properties.IsRightButtonPressed)
                backgroundMenuInsertionSlot = InsertionSlot(Along(e.GetCurrentPoint(strip).Position), false);
        }), true);
        root.LayoutUpdated += (_, _) => { SnapRestingSeparators(); QueueHoverGeometryCheck(); };
        scroll.ViewChanged += (_, _) => QueueHoverGeometryCheck();
        root.PointerEntered += PointerMoved;
        root.PointerExited += (_, _) =>
        {
            if (hoverTestInput != null) return;
            Native.GetCursorPos(out var pointer);
            if (!HoverContains(pointer)) ResetMagnification(false);
        };
        root.KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { root.Focus(FocusState.Programmatic); ResetMagnification(); } };
        root.GotFocus += (_, e) => { if (e.OriginalSource is Control control) keyboardFocus = control.FocusState == FocusState.Keyboard; };
        scroll.PointerWheelChanged += (_, e) =>
        {
            if (!dockOverflow || Vertical) return;
            scroll.ChangeView(scroll.HorizontalOffset - e.GetCurrentPoint(scroll).Properties.MouseWheelDelta / 3d, null, null, true);
            e.Handled = true;
        };
        root.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            if (Native.GetAsyncKeyState(0x12) < 0) ShowDockMenuAtPointer(preferSeparator: true);
            else ShowDockFlyout(BackgroundMenu(), dockBorder, e.TryGetPosition(root, out var position) ? position : null);
        };
        appbar = new(Handle);
        subclass = WindowMessage; Native.SetWindowSubclass(Handle, subclass, 1, 0);
        Native.RegisterShellHookWindow(Handle);
        var exStyle = Native.GetWindowLongPtrW(Handle, -20).ToInt64();
        Native.SetWindowLongPtrW(Handle, -20, (nint)((exStyle & ~(0x100L | 0x200L | 0x20000L | 0x08000000L)) | 0x80));
        var style = Native.GetWindowLongPtrW(Handle, -16).ToInt64();
        Native.SetWindowLongPtrW(Handle, -16, (nint)((style & ~(0x00800000L | 0x00400000L | 0x00040000L)) | 0x80000000L));
        var corner = 1; Native.DwmSetWindowAttribute(Handle, 33, ref corner, 4);
        var borderColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE: no DWM outline.
        Native.DwmSetWindowAttribute(Handle, 34, ref borderColor, 4);
        Native.SetWindowPos(Handle, 0, 0, 0, 0, 0, 0x20 | 0x1 | 0x2 | 0x4 | 0x10);
        tray = new System.Windows.Forms.NotifyIcon { Text = "WinMac Dock", Icon = IconResources.Load(BundledIcons.Tray), Visible = false };
        tray.MouseClick += (_, e) => DispatcherQueue.TryEnqueue(() => { if (e.Button == System.Windows.Forms.MouseButtons.Right) ShowTrayMenu(); else HandleCommand("--toggle"); });
        tracker.Changed += () => DispatcherQueue.TryEnqueue(() => { if (!reconcileTimer.IsEnabled) reconcileTimer.Start(); });
        tracker.ForegroundChanged += () => DispatcherQueue.TryEnqueue(() => { DismissFlyoutsOnExternalFocus(); EvaluateVisibility(); });
        reconcileTimer.Tick += (_, _) => { reconcileTimer.Stop(); Reconcile(); };
        visibilityTimer.Tick += (_, _) => { tracker.ObserveForeground(); EvaluateVisibility(); MaintainZOrder(); };
        themeTimer.Tick += (_, _) => { if (ReadThemeIdentity() != themeIdentity || renderedMotionState != $"{Motion}|{EffectiveIconHighlights}") UpdateTheme(); };
        safetyTimer.Tick += (_, _) => { EnsureDockIsNotMinimized(); Reconcile(); UpdateRecycle(); };
        recycleTimer.Tick += (_, _) => UpdateRecycle();
        Closed += (_, _) => Cleanup();
        root.ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(() => { UpdateChrome(); Reconcile(); });
        uiSettings.ColorValuesChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateTheme);
        uiSettings.AdvancedEffectsEnabledChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateTheme);
        // Some unpackaged Windows builds do not expose this WinRT event; WM_SETTINGCHANGE remains authoritative.
        try { accessibility.HighContrastChanged += (_, _) => DispatcherQueue.TryEnqueue(UpdateTheme); } catch (COMException) { }
    }

    private DockSettings LoadStartupSettings()
    {
        var recovery = Path.Combine(AppContext.BaseDirectory, "settings.restore.json");
        if (Program.StartupCommand.StartsWith("--test-", StringComparison.Ordinal) || !File.Exists(recovery)) return store.Load(Defaults);
        // This user-supplied recovery is consumed once, retaining both configurations.
        var receipt = store.Path + ".recovery-applied";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(recovery)));
        if (File.Exists(receipt) && File.ReadAllText(receipt) == hash) return store.Load(Defaults);
        var settings = store.RestoreFrom(recovery);
        try { File.WriteAllText(receipt, hash); }
        catch (Exception ex) { Log.Write(ex); } // A receipt error must not replace a successful recovery with defaults.
        Log.Write("Restored saved configuration from settings.restore.json (" + settings.Items.Count + " dock items).");
        return settings;
    }
    private static DockSettings Defaults() => BetaDefaults.CreateSettings(Native.Downloads(), Environment.GetFolderPath(Environment.SpecialFolder.Windows), Path.Combine(AppContext.BaseDirectory, "assets"), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    internal static DockItem DefaultSpecialItem(string key) => BetaDefaults.CreateItem(key, Native.Downloads(), Environment.GetFolderPath(Environment.SpecialFolder.Windows), Path.Combine(AppContext.BaseDirectory, "assets"), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
    private void ShowTrayMenu()
    {
        // A WinUI flyout keeps the same commands and styling as the dock menu.
        // Anchor it at the mouse position, which is the notification icon for this event.
        SetExplicitVisibility(true);
        Native.GetCursorPos(out var cursor);
        var bounds = Native.Bounds(Handle);
        trayMenu?.Hide();
        var menu = CreateTrayMenu();
        trayMenu = menu;
        menu.Closed += (_, _) => { if (trayMenu == menu) trayMenu = null; Native.PostMessage(Handle, 0, 0, 0); };
        // Notification-area menus need a foreground owner for light dismissal.
        Native.SetForegroundWindow(Handle);
        menu.ShowAt(root, new FlyoutShowOptions { Position = new Windows.Foundation.Point(cursor.X - bounds.Left, cursor.Y - bounds.Top) });
    }
    private MenuFlyout CreateTrayMenu()
    {
        var menu = BackgroundMenu(fromTray: true);
        int position = menu.Items.ToList().FindIndex(i => i is MenuFlyoutSubItem { Text: "Dock position" });
        int insert = position + 1;
        if (menu.Items[insert] is not MenuFlyoutSeparator) menu.Items.Insert(insert, new MenuFlyoutSeparator());
        insert++;
        var elevate = new MenuFlyoutItem { Text = IsElevated ? "Elevated" : "Elevate", IsEnabled = !IsElevated }; elevate.Click += (_, _) => RestartDock(true);
        var reload = new MenuFlyoutItem { Text = "Reload" }; reload.Click += (_, _) => RestartDock(false);
        menu.Items.Insert(insert++, elevate); menu.Items.Insert(insert++, reload);
        var about = new MenuFlyoutItem { Text = "About", Icon = Glyph("\uE946") };
        about.Click += (_, _) => ShowAbout();
        var hideTray = new MenuFlyoutItem { Text = "Hide tray icon" };
        hideTray.Click += (_, _) => { menu.Hide(); Settings.ShowTrayIcon = false; ApplySettings(); };
        menu.Items.Insert(insert++, hideTray);
        menu.Items.Insert(insert, new MenuFlyoutSeparator());
        int closeIndex = menu.Items.ToList().FindIndex(item => item is MenuFlyoutItem { Text: "Exit" });
        menu.Items.Insert(closeIndex, new MenuFlyoutSeparator());
        menu.Items.Insert(closeIndex + 1, about);
        return menu;
    }
    private bool startupShellReady;
    private readonly HashSet<Task> startupLoads = [];
    internal event Action? StartupReady;
    internal bool StartupComplete { get; private set; }
    private void TrackStartupLoad(Task task)
    {
        if (!StartupComplete) startupLoads.Add(task);
    }
    private async Task WaitForStartupLoads()
    {
        while (!closing && startupLoads.Count > 0)
        {
            var batch = startupLoads.ToArray();
            startupLoads.Clear();
            try { await Task.WhenAll(batch); }
            catch (Exception ex) { Log.Write(ex); }
        }
    }
    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        TaskbarModuleTrigger.WarmUp();
        _ = InitializeFully();
    }
    private async Task InitializeFully()
    {
        try
        {
            if (MonitorOverride == null) { minimizedTabs.Recover(); windowsTaskbar.Recover(); }
            startupShellReady = true;
            // Keep both native surfaces hidden while the real task list,
            // material, icons and folder previews are loaded.
            UpdateTheme();
            ApplyHotkey();
            Native.SetPropW(Handle, "WinMac.Dock.HideWindowsTaskbar", 0);
            RefreshAttentionAnimations();
            UpdateRecycle();
            await WaitForStartupLoads();
            if (closing) return;
            Reposition(true);
            await WaitForStartupLoads();
            if (closing) return;
            StartupComplete = true;

            
            AppWindow.Show(false);
            SetHoverClip();
            PaintMaterialSurface(0);
            tray.Visible = MonitorOverride == null && Settings.ShowTrayIcon;
            visibilityTimer.Start(); safetyTimer.Start(); themeTimer.Start(); recycleTimer.Start();
            EvaluateVisibility();
            StartupReady?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Write(ex);
        }
    }
    private string renderedMotionState = "";
    private void UpdateTheme() => UpdateTheme(reconcile: true);
    private void UpdateTheme(bool reconcile)
    {
        var bg = uiSettings.GetColorValue(UIColorType.Background);
        root.RequestedTheme = WindowsUsesDarkApps() ? ElementTheme.Dark : ElementTheme.Light;
        if (!accessibility.HighContrast && Settings.Theme != DockTheme.System)
            root.RequestedTheme = Settings.Theme == DockTheme.Light ? ElementTheme.Light : ElementTheme.Dark;
        if (startupShellReady) ApplyMaterialSurface(bg);
        else
        {
            // A lightweight surface can paint immediately. Native material
            // creates another HWND and is installed after the initial frame.
            var solid = accessibility.HighContrast ? bg : root.RequestedTheme == ElementTheme.Dark
                ? Windows.UI.Color.FromArgb(245, 32, 32, 32) : Windows.UI.Color.FromArgb(245, 243, 243, 243);
            dockBorder.Background = new SolidColorBrush(solid);
        }
        root.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        UpdateChrome();
        themeIdentity = ReadThemeIdentity();
        renderedMotionState = $"{Motion}|{EffectiveIconHighlights}";
        var iconTheme = $"{themeIdentity}|{root.RequestedTheme}|{bg}|{uiSettings.GetColorValue(UIColorType.Accent)}|{uiSettings.GetColorValue(UIColorType.AccentLight2)}|{accessibility.HighContrast}|{renderedMotionState}";
        if (renderedIconTheme != iconTheme)
        {
            renderedIconTheme = iconTheme;
            themeRevision++;
            IconService.SetTheme(root.RequestedTheme == ElementTheme.Dark, iconTheme);
            lastSignature = "";
        }
        if (reconcile) Reconcile();
    }
    private void UpdateChrome()
    {
        dockBorder.BorderThickness = DockOutlineThickness;
        dockBorder.CornerRadius = new CornerRadius(UseRoundedDockCorners ? 8 : 0);
        var defaultBorder = accessibility.HighContrast ? uiSettings.GetColorValue(UIColorType.Foreground)
            : AppearanceColor.DefaultBorder(root.RequestedTheme == ElementTheme.Dark);
        popupBorderBrush.Color = defaultBorder;
        dockBorder.BorderBrush = new SolidColorBrush(AppearanceColor.Resolve(Settings.DockBorderColor, defaultBorder, Settings.DockBorderOpacity));
        rowOutline.BorderBrush = dockBorder.BorderBrush;
        rowOutline.BorderThickness = DockOutlineThickness;
        UpdateOpenPopupBorders();
        // The transparent input envelope must never receive window chrome.
        var corner = 1;
        Native.DwmSetWindowAttribute(Handle, 33, ref corner, 4);
        var rendering = 1; Native.DwmSetWindowAttribute(Handle, 2, ref rendering, 4);
    }
    public void Save()
    {
        foreach (var item in Settings.Items) ItemPreferences.Remember(Settings, item, SpecialItemKey(item));
        TryAction(() => store.Save(Settings)); App.ScheduleDockSync(this);
    }
    private static string? SpecialItemKey(DockItem item) => item.Kind == ItemKind.StartMenu ? "start" : item.Kind == ItemKind.RecycleBin ? "recyclebin"
        : item.Kind == ItemKind.TaskView ? "taskview" : item.Kind == ItemKind.Search ? "search" : IsDownloads(item) ? "downloads"
        : ShellService.IsSystemSettings(item) ? "settings" : ShellService.IsTaskManager(item) ? "taskmanager" : ShellService.IsMicrosoftStore(item) ? "store"
        : IsFileExplorer(item) ? "fileexplorer" : ShellService.IsTerminal(item) ? "terminal" : null;
    private void UnpinItem(DockItem item)
    {
        pendingModuleBounces.Remove(item.Id);
        ItemPreferences.Remember(Settings, item, SpecialItemKey(item));
        Settings.Items.Remove(item); Save(); lastSignature = ""; Reconcile();
    }
    internal void RefreshMonitorLayout()
    {
        var display = ResolveDisplay();
        if (display.Identity != DisplayIdentity || Math.Abs(display.Scale - scale) > .001)
        {
            ApplySettings(persist: false);
            return;
        }
        // An appbar changing its work area needs layout, not another material
        // initialization or a forced rebuild of every icon on every display.
        Reposition(true);
        settingsWindow?.SyncDockPosition();
        EvaluateVisibility();
    }
    public void ApplySettings() => ApplySettings(persist: true);
    internal void ApplySettings(bool persist)
    {
        if (closing) return;
        monitor = ResolveDisplay();
        tray.Visible = MonitorOverride == null && Settings.ShowTrayIcon;
        Settings.Validate();
        if (previousHideMode != DisplayHideMode)
        {
            edgeToggleHidden = hidden; edgeActivation.Reset(); previousHideMode = DisplayHideMode;
            if (DisplayHideMode == HideMode.AlwaysVisible) { startupWaiting = false; manuallyHidden = false; }
        }
        SyncWindowsTaskbar();
        StopIndicatorAnimations();
        if (visibilitySliding && (!Motion || Settings.AnimationMs == 0)) FinishVisibilityAnimation();
        ShowItemsFromAllVirtualDesktops = Settings.ShowItemsFromAllVirtualDesktops;
        settingsWindow?.SyncDockPosition();
        // Capture the visible frame before changing geometry. In particular,
        // retarget an in-flight slider resize rather than finishing its old tween.
        bool animateSize = StartupComplete && displayRequestedSize > 0 && displayRequestedSize != Settings.IconSize
            && Motion && Settings.AnimationMs > 0 && !hidden && !visibilitySliding && !dragging && popupCount == 0 && !dockOverflow;
        if (animateSize)
        {
            pendingLayoutScene = CaptureRowScene();
            StopRowTransition();
            rowStaging = true;
        }
        try
        {
            ResetMagnification();
            if (persist) Save();
            UpdateTheme(reconcile: false); ApplyHotkey(); lastSignature = "";
            Reposition(true); EvaluateVisibility();
        }
        finally { if (animateSize) rowStaging = false; }
    }
    private void ApplyHotkey()
    {
        if (MonitorOverride != null || Program.SeparatorAudit) return;
        Native.UnregisterHotKey(Handle, 1);
        if (Settings.HotkeyEnabled && !Native.RegisterHotKey(Handle, 1, 0x4000 | Settings.HotkeyModifiers, (uint)Settings.HotkeyKey))
        {
            Settings.HotkeyEnabled = false; Save(); _ = Message("Shortcut unavailable", DockHotkey.Label(Settings.HotkeyModifiers, Settings.HotkeyKey) + " could not be registered. It may already be used by another application. Choose another shortcut in Settings.");
        }
    }
    public void AddItem(DockItem item, int? at = null)
    {
        string? special = SpecialItemKey(item);
        if (ItemPreferences.Restore(Settings, item, special) is { } remembered) item = remembered;
        else if (special != null && special is not ("taskview" or "search")) BetaDefaults.ApplyProperties(item, DefaultSpecialItem(special));
        if (item.Kind == ItemKind.StartMenu) at = 0;
        if (special is "start" or "search" or "taskview" or "downloads" or "recyclebin") pendingModuleBounces.Add(item.Id);
        if (item.Kind is ItemKind.Search or ItemKind.TaskView) ModulePlacement.Pin(Settings.Items, item);
        else if (at.HasValue) Settings.Items.Insert(Math.Clamp(at.Value, 0, Settings.Items.Count), item); else Settings.Items.Add(item);
        Save(); lastSignature = ""; Reconcile();
    }
    private RowScene? pendingLayoutScene;
    internal void SetIconSpacing(int value)
    {
        if (Settings.IconSpacing == value) return;
        if (Motion && Settings.AnimationMs > 0 && !hidden && !dragging && popupCount == 0)
        {
            pendingLayoutScene = CaptureRowScene();
            StopRowTransition();
        }
        Settings.IconSpacing = value;
        Save(); Reconcile();
    }
    internal void PinFromFolder(DockItem item)
    {
        if (popupCount == 0) { AddItem(item); return; }
        item = ItemPreferences.Restore(Settings, item, SpecialItemKey(item)) ?? item;
        var before = Motion && Settings.AnimationMs > 0 && !dockOverflow ? CaptureRowScene() : null;
        // Keep existing buttons attached: the Downloads button anchors the
        // open flyout. Rebuild only the magnification surface around them.
        var growth = hoverSlots.Select((slot, index) => (slot.Element, Growth: hoverWave != null && index < hoverWave.Count ? hoverWave.Growth[index] : 0d)).ToDictionary(x => x.Element, x => x.Growth);
        bool engaged = hoverEngaged;
        double peak = HoverOverflow;
        hoverRebuilding = true;
        rowStaging = before != null;
        try
        {
            ResetMagnification();
            Settings.Items.Add(item); Save();
            var firstTask = buttons.FirstOrDefault(b => !b.Entry.IsPinned).Button;
            int insertion = firstTask == null ? strip.Children.Count : strip.Children.IndexOf(firstTask);
            if (insertion > 0 && strip.Children[insertion - 1] is Border { Tag: null }) insertion--;
            AddButton(new DockEntry(item, null, true));
            var added = buttons[^1].Button;
            strip.Children.Remove(added); strip.Children.Insert(insertion, added);
            Reposition(true);
            if (Motion && !dockOverflow)
            {
                PrepareHoverSurface();
                hoverWave!.RestoreGrowth(hoverSlots.Select(slot => growth.GetValueOrDefault(slot.Element) * (peak > 0 ? HoverOverflow / peak : 0)).ToArray(), hoverAvailableExpansion, HoverOverflow);
                hoverEngaged = engaged;
                PaintHoverFrame();
            }
            lastSignature = "";
        }
        finally
        {
            hoverRebuilding = false;
            rowStaging = false;
            if (before != null) BeginRowTransition(before, allowOpenFlyout: true);
        }
    }
    private int PinInsertionIndex()
    {
        int index = Settings.Items.Count;
        while (index > 0 && Settings.Items[index - 1].Kind == ItemKind.Separator) index--;
        return index;
    }
    private void Reconcile()
    {
        if (initialized && Native.IsIconic(Handle) && HasExternalFullscreenForeground()) return;
        if (closing || !initialized) return;
        if (reconciling || positioning || hoverPainting || hoverGeometryRepairing || hoverRebuilding || rowPainting || rowFinishing)
        {
            if (!reconcileTimer.IsEnabled) reconcileTimer.Start();
            return;
        }

        RowScene? rowBefore = null;
        RowScene? rowCandidate = pendingLayoutScene;
        pendingLayoutScene = null;
        reconciling = true;
        try
        {
                windows = stressTasks?.ToList() ?? (startupShellReady ? tracker.Read() : []);
            if (startupShellReady && MonitorOverride == null && stressTasks == null && (MinimizedOnly || minimizedTabs.Removed.Count > 0)) minimizedTabs.Sync(Settings, tracker.Read(true));
            // Keep task order stable while foreground/Z-order changes.
            var previous = buttons.SelectMany(x => x.Entry.Windows).Select(w => w.Handle).ToList();
            windows = windows.OrderBy(w => { var i = previous.IndexOf(w.Handle); return i < 0 ? int.MaxValue : i; }).ToList();
            SyncUsbPinning();
            SyncDownloadPinning();
            var orderedPins = DownloadPinLayout.OrderPins(Settings.Items);
            var entries = TaskReconciler.Reconcile(orderedPins, windows, Settings.GroupWindows, Settings.CombinePinnedIconsWithRunningApplications, MinimizedOnly);
            if (StartupComplete && lastSignature.Length > 0 && Motion && Settings.AnimationMs > 0 && !hidden && !dragging && popupCount == 0
                && (rowAnimating || !buttons.Select(b => b.Entry.Item.Id).SequenceEqual(entries.Where(e => e.Item.Kind != ItemKind.Separator).Select(e => e.Item.Id))
                    || buttons.Any(b => entries.FirstOrDefault(e => e.Item.Id == b.Entry.Item.Id)?.Windows.Count != b.Entry.Windows.Count)))
                rowCandidate ??= CaptureRowScene();
            bool hadHover = hoverWave != null && Motion;
            bool wasEngaged = hoverEngaged;
            double oldPeak = HoverOverflow;
            double GrowthFor(Button button)
            {
                int i = Array.FindIndex(hoverSlots, slot => slot.Element == button);
                return hoverWave != null && i >= 0 && i < hoverWave.Count ? hoverWave.Growth[i] : 0;
            }
            var growth = hadHover ? buttons.ToDictionary(b => b.Entry.Item.Id, b => GrowthFor(b.Button)) : null;
            var windowGrowth = hadHover ? buttons.SelectMany(b => b.Entry.Windows.Select(w => (w.Handle, Growth: GrowthFor(b.Button))))
                .GroupBy(x => x.Handle).ToDictionary(g => g.Key, g => g.First().Growth) : null;
            bool fitChanged = UpdateAutoFit(entries);
            var liveTasks = entries.Where(e => !e.IsPinned).Select(e => e.Item.Id).ToHashSet();
            if (startupShellReady) SyncStackWatchers();
            var signature = IconPixels + ":" + ButtonSize + ":" + DisplaySpacing + ":" + dockOverflow + ":" + string.Join("|", entries.Select(x => $"{x.Item.Id}:{string.Join(',', x.Windows.Select(w => w.Handle))}:{(x.IsPinned ? x.Item.Name : "")}:{x.Item.CustomIcon}:{x.Item.DarkModeIcon}:{x.Item.ShowThumbnail}:{x.Item.AttachedDriveKind}:{x.Item.Folder.StackIcon}:{x.Item.Folder.ShowDownloadsIconUnderStack}:{x.Item.Folder.Sort}:{x.Item.Folder.Descending}:{MissingTargetForRow(x)}"));
            bool entriesChanged = signature != lastSignature;
            if (!entriesChanged)
                foreach (var existing in buttons.Where(b => !b.Entry.IsPinned))
                    if (entries.FirstOrDefault(e => e.Item.Id == existing.Entry.Item.Id) is { } current)
                    {
                        existing.Entry.Item.Name = current.Item.Name;
                        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(existing.Button, current.Item.Name);
                        ToolTipService.SetToolTip(existing.Button, Settings.ShowIconLabels ? ItemTooltip.Label(current.Item, Settings.ShowFileExtensionsInTooltips) : null);
                    }
            if (entriesChanged && !dragging && popupCount == 0)
            {
                rowBefore = rowCandidate;
                rowStaging = rowBefore != null;
                bool resumeHover = hadHover && Motion && !dockOverflow;
                hoverRebuilding = true;
                StopIndicatorAnimations(); ResetMagnification();
                // Preserve sources held by the live controls even if thumbnail
                // activity has evicted their entries from the general cache.
                IconService.RetainVisibleArtwork(buttons.SelectMany(b => b.Icon is Grid grid
                    ? grid.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().Select(image => image.Source).OfType<ImageSource>()
                    : Enumerable.Empty<ImageSource>()));
                folderPreviewVersions.Clear();
                DetachShellAnimatedIcons();
                foreach (var id in launchBounces.Keys.Where(id => !entries.Any(e => e.Item.Id == id)).ToArray()) launchBounces.Remove(id);
                lastSignature = signature; strip.Children.Clear(); buttons.Clear(); hoverTargets.Clear(); runningIndicators.Clear(); downloadIndicators.Clear(); groupIndicators.Clear(); taskbarHighlights.Clear(); hoveredButtons.Clear(); pressedButtons.Clear(); dragVisuals.Clear();
                // Saved separators were static. The task boundary is dynamic: pins stay
                // together, then one divider appears only when live, unpinned tasks exist.
                var pinned = entries.Where(entry => entry.IsPinned).ToList();
                var unpinned = entries.Where(entry => !entry.IsPinned).ToList();
                // User-created dividers are static, including a trailing divider.
                // Hiding one when the last task is pinned changes the centered row
                // width even though its icon count has not changed.
                DockEntry? previousEntry = null;
                foreach (var entry in pinned.Concat(unpinned))
                {
                    if (DownloadPinLayout.NeedsSeparator(previousEntry, entry)) AddTaskSeparator();
                    AddButton(entry);
                    previousEntry = entry;
                }
                Reposition(fitChanged);
                if (resumeHover)
                {
                    PrepareHoverSurface();
                    hoverWave!.RestoreGrowth(hoverSlots.Select(s =>
                    {
                        if (s.Element is not Button b) return 0d;
                        var entry = buttons.First(x => x.Button == b).Entry;
                        return (growth!.TryGetValue(entry.Item.Id, out double saved) ? saved : entry.Windows.Select(w => windowGrowth!.GetValueOrDefault(w.Handle)).DefaultIfEmpty().Max()) * (oldPeak > 0 ? HoverOverflow / oldPeak : 0);
                    }).ToArray(), hoverAvailableExpansion, HoverOverflow);
                    hoverEngaged = wasEngaged; PaintHoverFrame(); StartHoverRendering();
                }
                hoverRebuilding = false;
                // Do not briefly commit the destination clip before the row
                // animation restores its starting clip. DWM can present that
                // intermediate native-region change as a leading-edge blink.
                if (hoverWave == null && rowBefore == null) { visibleBounds = resting; SetHoverClip(); PaintMaterialSurface(0); }
                if (rowBefore == null && !resumeHover && CanResumeHoverHere()) UpdateMagnification(0);
            }
            EvaluateVisibility();
            if (startupShellReady) TrackStartupLoad(RefreshExplorerFolderIcons());
            var active = Native.GetForegroundWindow();
            if (active == Handle) active = tracker.LastExternalForeground;
            if (!entriesChanged && active == lastStyledForeground && lastStyledThemeRevision == themeRevision && lastStyledTheme == root.ActualTheme) return;
            lastStyledForeground = active; lastStyledThemeRevision = themeRevision; lastStyledTheme = root.ActualTheme;
            foreach (var (button, _, entry) in buttons)
            {
                ApplyStandardHighlight(button, entry, active);
                UpdateTaskbarHighlight(button, entry);
                if (runningIndicators.TryGetValue(button, out var indicator))
                {
                    bool selected = entry.Windows.Any(w => w.Handle == active);
                    int activeLength = Settings.WindowsTaskbarHighlights ? 14 : 16;
                    bool rotated = Vertical && !Settings.ForceIndicatorsToItemBase;
                    SetIndicatorSize(indicator, rotated ? 3 : selected ? activeLength : 6, rotated ? selected ? activeLength : 6 : 3);
                    bool accent = Settings.IndicatorAccent == IndicatorAccentMode.All || (selected && Settings.IndicatorAccent == IndicatorAccentMode.Focused);
                    indicator.ClearValue(Border.BackgroundProperty);
                    indicator.Style = (Style)Application.Current.Resources[accent ? "DockAccentIndicatorStyle" : "DockNeutralIndicatorStyle"];
                    if (indicator.Background is SolidColorBrush indicatorBrush)
                        indicator.Background = new SolidColorBrush(AppearanceColor.Resolve(Settings.ItemIndicatorColor, indicatorBrush.Color, Settings.ItemIndicatorOpacity));
                    indicator.Opacity = 1;
                    PositionIndicator(indicator);
                }
            }
        }
        finally { FinishShellIconReconcile(); rowStaging = false; reconciling = false; if (rowBefore != null) BeginRowTransition(rowBefore); QueueHoverGeometryCheck(); }
    }
    private bool MissingTargetForRow(DockEntry entry)
    {
        if (!startupShellReady || !entry.IsPinned) return false;

        return DockTarget.IsMissing(entry.Item);
    }
    private void SyncStackWatchers()
    {
        var paths = Settings.Items.Where(x => x.Kind == ItemKind.Folder && (x.Folder.StackIcon || x.ShowThumbnail)).Select(x => x.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in stackWatchers.Keys.Where(p => !paths.Contains(p)).ToArray()) { stackWatchers[path].Dispose(); stackWatchers.Remove(path); }
        foreach (var path in paths.Where(p => !stackWatchers.ContainsKey(p)))
        {
            try
            {
                if (!Directory.Exists(path)) continue;
                var watcher = new FileSystemWatcher(path) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite };
                FileSystemEventHandler change = (_, e) => DispatcherQueue.TryEnqueue(() => { IconService.Invalidate(e.FullPath); RefreshFolderPreviews(path); });
                watcher.Created += change; watcher.Deleted += change; watcher.Changed += change;
                watcher.Renamed += (_, e) => DispatcherQueue.TryEnqueue(() => { IconService.Invalidate(e.OldFullPath); RefreshFolderPreviews(path); });
                watcher.EnableRaisingEvents = true; stackWatchers[path] = watcher;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { Log.Write(ex); }
        }
    }
    private readonly Dictionary<Button, Border> downloadIndicators = [];
    private void AddButton(DockEntry entry)
    {
        if (entry.Item.Kind == ItemKind.Separator) { AddPermanentSeparator(entry); return; }
        var button = new Button { UseLayoutRounding = false, Width = Vertical ? ButtonCrossSize : ButtonSize, Height = Vertical ? ButtonSize : ButtonCrossSize, Padding = new Thickness(6 * FitRatio, 8 * FitRatio, 6 * FitRatio, 4 * FitRatio), VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(Settings.RoundedCorners ? 5 : 0), Tag = entry, Clip = null };
        if (!EffectiveIconHighlights)
            foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" }) button.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        if (PositionEdge == DockEdge.Top) button.Padding = new Thickness(6 * FitRatio, 4 * FitRatio, 6 * FitRatio, 8 * FitRatio);
        if (Vertical) { button.VerticalAlignment = VerticalAlignment.Center; button.Padding = PositionEdge == DockEdge.Left ? new Thickness(4 * FitRatio, 6 * FitRatio, 8 * FitRatio, 6 * FitRatio) : new Thickness(8 * FitRatio, 6 * FitRatio, 4 * FitRatio, 6 * FitRatio); }
        if (Settings.WindowsTaskbarHighlights) button.Padding = new Thickness(DisplayPadding / 2d);
        var dragged = ConfigureItemDrag(button, entry);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, entry.Item.Name);
        var labelText = entry.Item.Kind == ItemKind.File ? ItemTooltip.Label(entry.Item, Settings.ShowFileExtensionsInTooltips) : entry.Windows.Count > 1 ? $"{entry.Item.Name} ({entry.Windows.Count} windows)" : entry.Window?.Title ?? entry.Item.Name;
        var content = new Grid { Width = ButtonSize, Height = ButtonSize,
            Margin = Vertical ? new Thickness(MarginDip(Settings.DockMarginLeft), 0, MarginDip(Settings.DockMarginRight), 0)
                : new Thickness(0, MarginDip(Settings.DockMarginTop), 0, MarginDip(Settings.DockMarginBottom)) };
        if (EffectiveIconHighlights)
        {
            foreach (var key in new[] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed", "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" }) button.Resources[key] = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            // StackPanel spacing is the one authoritative gap between tiles.
            var tile = new Border
            {
                CornerRadius = new CornerRadius(Settings.RoundedCorners ? 4 : 0), BorderThickness = new Thickness(1),
                IsHitTestVisible = false, Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BackgroundTransition = !Motion ? null : new BrushTransition { Duration = TimeSpan.FromMilliseconds(83) }
            };
            content.Children.Add(tile); taskbarHighlights[button] = tile;
        }
        var icon = new Grid { Width = DisplayIconSize, Height = DisplayIconSize, VerticalAlignment = VerticalAlignment.Center };
        icon.Margin = button.Padding; button.Padding = new Thickness(0);
        var fallback = new FontIcon { Visibility = Visibility.Collapsed, FontSize = DisplayIconSize * .8 };
        icon.Children.Add(fallback);
        var image = new Microsoft.UI.Xaml.Controls.Image { Width = DisplayIconSize, Height = DisplayIconSize, Stretch = Stretch.Uniform, UseLayoutRounding = DisplayIconSize == Math.Truncate(DisplayIconSize) };
        if (entry.Item.IsDownloadedPin)
        {
            fallback.Visibility = Visibility.Collapsed;
            if (readyDownloadIcons.TryGetValue(entry.Item.Target, out var readyIcon)) image.Source = readyIcon;
        }
        icon.Children.Add(image);
        content.Children.Add(icon);
        if (entry.Item.IsDownloadedPin)
        {
            var downloadIndicator = new Border
            {
                Width = Math.Max(16, DisplayIconSize * .6), Height = 3, CornerRadius = new CornerRadius(1.5),
                Background = new SolidColorBrush(entry.Item.CapturePin switch
                {
                    CapturePinKind.Screenshot => Windows.UI.Color.FromArgb(255, 0x23, 0xA5, 0xE4),
                    CapturePinKind.Recording => Windows.UI.Color.FromArgb(255, 0xAD, 0x6E, 0xF3),
                    _ => Windows.UI.Color.FromArgb(255, 0x29, 0xC0, 0x94)
                }),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, Math.Max(0, (ButtonSize - DisplayIconSize) / 2d) * (100 - Settings.IndicatorPosition) / 100d),
                IsHitTestVisible = false, Opacity = appearedDownloadIndicators.Contains(entry.Item.Id) ? 1 : 0
            };
            downloadIndicators[button] = downloadIndicator;
            content.Children.Add(downloadIndicator);
        }
        if (entry.Window != null)
        {
            bool rotatedIndicator = Vertical && !Settings.ForceIndicatorsToItemBase;
            var dot = new Border { Width = rotatedIndicator ? 3 : 6, Height = rotatedIndicator ? 6 : 3, CornerRadius = new CornerRadius(1.5), Background = (Brush)Application.Current.Resources["SystemControlHighlightAccentBrush"], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, IsHitTestVisible = false };
            PositionIndicator(dot);
            runningIndicators[button] = dot;
            content.Children.Add(dot);
            if (entry.Windows.Count > 1)
            {
                string count = entry.Windows.Count > 99 ? "99+" : entry.Windows.Count.ToString();
                double side = Math.Min(DisplayIconSize, Math.Max(14, count.Length * 6 + 4));
                double radius = !Settings.RoundedCorners && Settings.SquareGroupIndicatorsWhenUnrounded ? 0
                    : Settings.GroupIndicatorShape switch { GroupIndicatorShape.Circle => side / 2, GroupIndicatorShape.Squircle => 4, _ => 0 };
                bool right = Settings.GroupIndicatorCorner is GroupIndicatorCorner.TopRight or GroupIndicatorCorner.BottomRight;
                bool bottom = Settings.GroupIndicatorCorner is GroupIndicatorCorner.BottomLeft or GroupIndicatorCorner.BottomRight;
                double groupInset = Math.Max(0, -DisplaySpacing) / 2d;
                double insetX = Vertical ? 0 : groupInset, insetY = Vertical ? groupInset : 0;
                var badge = new Border { Width = side, Height = side, CornerRadius = new CornerRadius(radius), IsHitTestVisible = false,
                    Background = (Brush)Application.Current.Resources["SystemControlBackgroundChromeMediumBrush"],
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(right ? ButtonSize - side - insetX : insetX, bottom ? ButtonSize - side - insetY : insetY, 0, 0),
                    Child = new TextBlock { Text = count, TextLineBounds = TextLineBounds.Tight, FontSize = Math.Min(10, side / Math.Max(1.4, count.Length * .65)), TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                groupIndicators[button] = badge; content.Children.Add(badge);
            }
        }
        button.Content = content;
        dragVisuals[button] = icon;
        Microsoft.UI.Xaml.Controls.ToolTipService.SetToolTip(button, Settings.ShowIconLabels ? labelText : null);
        nint? pressedForeground = null;
        button.PointerEntered += (_, _) => { hoveredButtons.Add(button); UpdateTaskbarHighlight(button, entry); ApplyStandardHighlight(button, entry, Native.GetForegroundWindow()); };
        button.PointerExited += (_, _) => { hoveredButtons.Remove(button); UpdateTaskbarHighlight(button, entry); ApplyStandardHighlight(button, entry, Native.GetForegroundWindow()); };
        button.GotFocus += (_, _) => { UpdateTaskbarHighlight(button, entry); ApplyStandardHighlight(button, entry, Native.GetForegroundWindow()); };
        button.LostFocus += (_, _) => { UpdateTaskbarHighlight(button, entry); ApplyStandardHighlight(button, entry, Native.GetForegroundWindow()); };
        button.PointerCaptureLost += (_, _) => { pressedButtons.Remove(button); UpdateTaskbarHighlight(button, entry); ApplyStandardHighlight(button, entry, Native.GetForegroundWindow()); };
        button.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            CaptureClickForeground();
            pressedForeground = foregroundBeforeClick;
            if (e.GetCurrentPoint(button).Properties.IsLeftButtonPressed) { pressedButtons.Add(button); UpdateTaskbarHighlight(button, entry); ApplyStandardHighlight(button, entry, Native.GetForegroundWindow()); }
            if (e.GetCurrentPoint(button).Properties.IsMiddleButtonPressed) e.Handled = true;
        }), true);
        button.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            pressedButtons.Remove(button); UpdateTaskbarHighlight(button, entry); ApplyStandardHighlight(button, entry, Native.GetForegroundWindow());
            if (e.GetCurrentPoint(button).Properties.PointerUpdateKind == Microsoft.UI.Input.PointerUpdateKind.MiddleButtonReleased)
            { e.Handled = true; InvokeClick(entry, DockMouseButton.Middle, button, icon, pressedForeground); pressedForeground = null; }
        }), true);
        button.AddHandler(UIElement.RightTappedEvent, new RightTappedEventHandler((_, e) =>
        { e.Handled = true; InvokeClick(entry, DockMouseButton.Right, button, icon, pressedForeground); pressedForeground = null; }), true);
        button.Click += (_, _) =>
        {
            if (dragged()) return;
            var foreground = pressedForeground; pressedForeground = null;
            InvokeClick(entry, DockMouseButton.Left, button, icon, foreground);
        };
        // Preserve keyboard access to configuration even when right-click is Nothing.
        button.ContextRequested += (sender, e) =>
        {
            e.Handled = true;
            if (Native.GetAsyncKeyState(0x12) < 0) ShowDockMenuAtPointer(preferSeparator: true);
            else if (!e.TryGetPosition(button, out var position)) ShowDockFlyout(ItemMenu(entry), button);
        };
        button.KeyDown += (_, e) =>
        {
            if ((!Vertical && e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Right) || (Vertical && e.Key is Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down))
            {
                var index = buttons.FindIndex(x => x.Button == button); var next = (index + (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Up ? buttons.Count - 1 : 1)) % buttons.Count;
                buttons[next].Button.Focus(FocusState.Keyboard); buttons[next].Button.StartBringIntoView(); e.Handled = true;
            }
        };
        buttons.Add((button, icon, entry)); strip.Children.Add(button);
        if (entry.Item.IsDownloadedPin)
        {
            button.Loaded += (_, _) => QueueDownloadFeedback(entry.Item, image, icon);
            image.ImageOpened += (_, _) => QueueDownloadFeedback(entry.Item, image, icon);
            QueueDownloadFeedback(entry.Item, image, icon);
        }
        if (entry.Item.IsAutoUsbPin || pendingModuleBounces.Contains(entry.Item.Id))
        {
            image.ImageOpened += (_, _) => QueueUsbAppearanceBounce(entry.Item, image, icon);
            button.Loaded += (_, _) => QueueUsbAppearanceBounce(entry.Item, image, icon);
            QueueUsbAppearanceBounce(entry.Item, image, icon);
        }
        if (startupShellReady) TrackStartupLoad(LoadIcon(entry.Item, image, fallback, icon, entry.IsPinned, !entry.IsPinned && IsFileExplorer(entry.Item) ? entry.Window?.Handle ?? 0 : 0));
        if (startupShellReady) TryAddShellAnimatedIcon(button, icon, entry);
    }
    private void PositionIndicator(Border indicator)
    {
        var hoverSlot = hoverSlots.FirstOrDefault(slot => slot.Indicator == indicator && slot.IndicatorCarrier != null);
        if (hoverSlot != null) { SyncHoverIndicator(hoverSlot); return; }
        // The available space between an icon and its dock edge is the travel
        // range. At 0 the mark sits on that edge; at 100 it is nearest the icon.
        double travel = Math.Max(0, (ButtonSize - DisplayIconSize) / 2d);
        double offset = travel * (100 - Settings.IndicatorPosition) / 100d;
        if (!Vertical || Settings.ForceIndicatorsToItemBase)
        {
            indicator.HorizontalAlignment = HorizontalAlignment.Center; indicator.VerticalAlignment = PositionEdge == DockEdge.Top && !Settings.ForceIndicatorsToItemBase ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            indicator.Margin = PositionEdge == DockEdge.Top && !Settings.ForceIndicatorsToItemBase ? new Thickness(0, offset, 0, 0) : new Thickness(0, 0, 0, offset);
        }
        else if (PositionEdge == DockEdge.Left)
        {
            indicator.HorizontalAlignment = HorizontalAlignment.Left; indicator.VerticalAlignment = VerticalAlignment.Center;
            indicator.Margin = new Thickness(offset, 0, 0, 0);
        }
        else
        {
            indicator.HorizontalAlignment = HorizontalAlignment.Right; indicator.VerticalAlignment = VerticalAlignment.Center;
            indicator.Margin = new Thickness(0, 0, offset, 0);
        }
    }
    private void AddTaskSeparator()
    {
        var separator = new Border
        {
            Width = SeparatorWidth,
            Height = CurrentDockThickness * .7,
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            BackgroundSizing = BackgroundSizing.InnerBorderEdge,
            BorderThickness = new Thickness(0),
            Opacity = .45,
            IsHitTestVisible = false
        };
        if (Vertical) { separator.Width = CurrentDockThickness * .7; separator.Height = SeparatorWidth; }
        strip.Children.Add(separator);
        InitializeSeparatorStroke(separator);
    }
    private void AddPermanentSeparator(DockEntry entry)
    {
        var separator = new Border
        {
            Width = SeparatorWidth,
            Height = CurrentDockThickness * .7,
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Gray),
            BackgroundSizing = BackgroundSizing.InnerBorderEdge,
            BorderThickness = new Thickness(0),
            Opacity = .45,
            Tag = entry
        };
        if (Vertical) { separator.Width = CurrentDockThickness * .7; separator.Height = SeparatorWidth; }
        ConfigureItemDrag(separator, entry);
        separator.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            if (Native.GetAsyncKeyState(0x12) < 0) ShowDockMenuAtPointer(preferSeparator: true);
            else ShowDockFlyout(ItemMenu(entry), separator);
        };
        strip.Children.Add(separator);
        InitializeSeparatorStroke(separator);
    }
    private void UpdateTaskbarHighlight(Button button, DockEntry entry)
    {
        if (!taskbarHighlights.TryGetValue(button, out var tile)) return;
        if (!EffectiveIconHighlights) { tile.Visibility = Visibility.Collapsed; return; }
        var active = Native.GetForegroundWindow();
        if (active == Handle) active = tracker.LastExternalForeground;
        bool pressed = pressedButtons.Contains(button);
        bool focused = entry.Windows.Any(w => w.Handle == active);
        bool hovered = hoveredButtons.Contains(button) || button.FocusState == FocusState.Keyboard;
        bool visible = pressed || hovered || focused;
        var fill = Settings.HighlightMode == DockHighlightMode.Border ? Microsoft.UI.Colors.Transparent : ItemHighlight.TaskbarFill(root.ActualTheme, focused, hovered, pressed);
        Brush stroke = ItemHighlight.TaskbarStroke(root.ActualTheme, focused, hovered, pressed);
        bool customBorder = Settings.HighlightMode != DockHighlightMode.Background;
        if (customBorder) stroke = new SolidColorBrush(visible ? ItemHighlight.Appearance(Settings, root.ActualTheme, true, pressed).Stroke : Microsoft.UI.Colors.Transparent);
        if (accessibility.HighContrast)
        {
            fill = visible && Settings.HighlightMode != DockHighlightMode.Border ? uiSettings.UIElementColor(Windows.UI.ViewManagement.UIElementType.Highlight) : Microsoft.UI.Colors.Transparent;
            stroke = new SolidColorBrush(visible ? uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.Foreground) : Microsoft.UI.Colors.Transparent);
        }
        double inset = Math.Max(0, -DisplaySpacing) / 2d;
        tile.Margin = Vertical ? new Thickness(0, inset, 0, inset) : new Thickness(inset, 0, inset, 0);
        // Leave the tile rendered while it fades to transparent on pointer exit.
        tile.Visibility = Visibility.Visible;
        tile.Tag = visible;
        tile.BackgroundTransition = !Motion ? null : tile.BackgroundTransition ?? new BrushTransition { Duration = TimeSpan.FromMilliseconds(83) };
        if (tile.Background is not SolidColorBrush current || current.Color != fill) tile.Background = new SolidColorBrush(fill);
        tile.BorderBrush = stroke;
        tile.BorderThickness = new Thickness(customBorder && !accessibility.HighContrast ? 2 : 1);
        tile.CornerRadius = new CornerRadius(Settings.RoundedCorners ? 4 : 0);
    }
    private void ApplyStandardHighlight(Button button, DockEntry entry, nint active)
    {
        if (taskbarHighlights.ContainsKey(button)) return;
        bool visible = EffectiveIconHighlights && !Settings.WindowsTaskbarHighlights && (hoveredButtons.Contains(button) || pressedButtons.Contains(button) || entry.Windows.Any(w => w.Handle == active) || button.FocusState == FocusState.Keyboard);
        ItemHighlight.Apply(button, Settings, root.ActualTheme, visible, alwaysEnabled: EffectiveIconHighlights);
    }
    private readonly Dictionary<string, WindowClickSequence> windowClickSequences = new();
    private bool CanMoveIcons => !Settings.LockIconPositions || Native.GetAsyncKeyState(0x12) < 0;
    private void KeepTaskViewOpenForDockLaunch()
    {
        if (!Settings.ShowInTaskView || !Native.IsTaskViewOpen()) return;
        // Explorer closes Task View as soon as an application receives focus.
        // The bridge consumes this short-lived request while it receives those
        // close messages, then normal Task View dismissal resumes.
        Native.SetPropW(Handle, "WinMac.Dock.KeepTaskViewOpenUntil", unchecked((nint)(Environment.TickCount64 + 750)));
    }
    private void InvokeClick(DockEntry entry, DockMouseButton mouseButton, FrameworkElement anchor, FrameworkElement icon, nint? pressedForeground = null)
    {
        if (mouseButton == DockMouseButton.Left) ClearClickedAttention(entry);
        
        if (mouseButton == DockMouseButton.Right && Native.GetAsyncKeyState(0x12) < 0)
        {
            ShowDockMenuAtPointer(preferSeparator: true);
            return;
        }
        if (pressedForeground.HasValue) foregroundBeforeClick = pressedForeground.Value;
        else CaptureClickForeground();
        if (mouseButton == DockMouseButton.Left && Native.GetAsyncKeyState(0x10) < 0)
        {
            if (entry.Item.Kind != ItemKind.Separator) TryAction(() =>
            {
                KeepTaskViewOpenForDockLaunch();
                if (Native.GetAsyncKeyState(0x11) < 0) ShellService.LaunchAsAdministrator(entry.Item);
                else ShellService.LaunchWithoutElevation(entry.Item);
                if (!entry.Item.IsDownloadedPin) Bounce(icon);
                UnpinOpenedDownload(entry.Item);
            });
            return;
        }
        if (mouseButton == DockMouseButton.Left && Native.GetAsyncKeyState(0x11) < 0)
        {
            var windows = DockClickPolicy.Similar(entry, tracker.Read());
            if (windows.Count > 0)
            {
                windowClickSequences.Remove(DockClickPolicy.Identity(entry.Item) ?? entry.Item.Id);
                bool minimize = windows.All(w => !Native.IsIconic(w.Handle));
                TryAction(() =>
                {
                    foreach (var window in windows.Reverse())
                        if (minimize) ShellService.Activate(window, true);
                        else if (Native.IsIconic(window.Handle)) Native.ShowWindowAsync(window.Handle, 9);
                    if (!minimize) ShellService.Activate(windows[0], false);
                });
                tracker.ObserveForeground();
                return;
            }
        }
        if (mouseButton == DockMouseButton.Left && (entry.Windows.Count > 0 || entry.Item.Kind is ItemKind.StartMenu or ItemKind.TaskView or ItemKind.Search or ItemKind.Folder)) PulseActivatedItem(icon);
        var action = ClickActionsFor(entry).For(mouseButton);
        ExecuteClickAction(entry, action, anchor, icon, foregroundBeforeClick);
    }
    private void PulseActivatedItem(FrameworkElement element)
    {
        if (!Motion) return;
        if (shellIcons.FirstOrDefault(item => ReferenceEquals(item.Artwork, element)) is { } shell)
        {
            if (shell.Entry.Item.Kind == ItemKind.StartMenu) shell.Animation.PulseClick(Settings.RunningClickAnimationMs);
            return;
        }
        // Animate artwork inside the hover carrier. Replacing RenderTransform
        // while a previous XAML storyboard is running leaves stale animation
        // targets when hover reparents or resets the icon during activation.
        foreach (var artwork in element is Panel panel ? panel.Children.OfType<FrameworkElement>() : new[] { element })
        {
            var visual = ElementCompositionPreview.GetElementVisual(artwork);
            visual.StopAnimation("Scale");
            visual.CenterPoint = new((float)artwork.ActualWidth / 2, (float)artwork.ActualHeight / 2, 0);
            visual.Scale = Vector3.One;
            var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
            animation.InsertKeyFrame(0, Vector3.One);
            animation.InsertKeyFrame(167f / 667, new Vector3(.8f, .8f, 1),
                visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, 0), new Vector2(0, 1)));
            animation.InsertKeyFrame(332f / 667, new Vector3(1.02f, 1.02f, 1),
                visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(.16f, 1), new Vector2(.3f, 1)));
            animation.InsertKeyFrame(1, Vector3.One);
            animation.Duration = TimeSpan.FromMilliseconds(Settings.RunningClickAnimationMs);
            visual.StartAnimation("Scale", animation);
        }
    }
    private void CaptureClickForeground()
    {
        tracker.ObserveForeground();
        var foreground = Native.GetForegroundWindow();
        // WinUI button focus can activate the dock before PointerPressed is
        // delivered. Keep the application that was active before that transfer.
        // Settings is a separate window and must not count as an active task.
        foregroundBeforeClick = foreground == Handle ? tracker.LastExternalForeground : foreground;
        if (!Native.IsWindow(foregroundBeforeClick)) foregroundBeforeClick = 0;
    }
    private void ExecuteClickAction(DockEntry entry, DockClickAction action, FrameworkElement anchor, FrameworkElement icon, nint foreground)
    {
        var current = tracker.Read();
        var primary = DockClickPolicy.Primary(entry, current, foreground);
        void Activate(TaskWindow task, bool minimize)
        {
            ShellService.Activate(task, minimize);
        }
        void Open()
        {
            if (entry.Item.Kind == ItemKind.Separator) return;
            if (pendingLaunches.TryGetValue(entry.Item.Id, out var started) && DateTime.UtcNow - started < TimeSpan.FromSeconds(2)) return;
            pendingLaunches[entry.Item.Id] = DateTime.UtcNow;
            KeepTaskViewOpenForDockLaunch();
            if (ShellService.IsSystemSettings(entry.Item)) LaunchSystemSettings(entry.Item);
            else { ShellService.Launch(entry.Item); if (!entry.Item.IsDownloadedPin) Bounce(icon); UnpinOpenedDownload(entry.Item); }
        }
        TryAction(() =>
        {
            switch (action)
            {
                case DockClickAction.Nothing: break;
                case DockClickAction.Open: Open(); break;
                case DockClickAction.Default:
                    if (entry.Item.Kind == ItemKind.Folder)
                    {
                        OpenFolder(entry.Item, anchor);
                    }
                    else if (entry.IsPinned && entry.Item.Kind == ItemKind.Application && !Settings.CombinePinnedIconsWithRunningApplications) Open();
                    else if (ShellService.SupportsAdministratorLaunch(entry.Item) && entry.Item.RunAsAdministrator && primary != null && ShellService.WindowIsElevated(primary.Handle) == false) Open();
                    else if (entry.Windows.Count > 1 && Settings.GroupedClickBehavior == GroupedClickBehavior.ShowWindowList) OpenWindows(entry, anchor);
                    else if (Settings.GroupedClickBehavior == GroupedClickBehavior.Sequential && DockClickPolicy.CycleTargets(entry, current, Settings.GroupWindows, Settings.CombinePinnedIconsWithRunningApplications).Count > 1)
                    {
                        var key = DockClickPolicy.Identity(entry.Item) ?? entry.Item.Id;
                        if (!windowClickSequences.TryGetValue(key, out var sequence)) windowClickSequences[key] = sequence = new();
                        var members = tracker.MostRecentlyUsed(DockClickPolicy.CycleTargets(entry, current, Settings.GroupWindows, Settings.CombinePinnedIconsWithRunningApplications)).ToArray();
                        if (sequence.Next(members, foreground) is { } next && members.FirstOrDefault(w => w.Handle == next.Handle) is { } target)
                            Activate(target, next.Minimize);
                    }
                    else if (primary != null) Activate(primary, foreground == primary.Handle && !Native.IsIconic(primary.Handle));
                    else if (entry.Windows.Count == 0) Open();
                    break;
                case DockClickAction.OpenTaskMenu: ShowDockFlyout(ItemMenu(entry), anchor); break;
                case DockClickAction.Minimize: if (primary != null) Activate(primary, true); break;
                case DockClickAction.BringToFront: if (primary != null) Activate(primary, false); break;
                case DockClickAction.BringToFrontOrMinimize: if (primary != null) Activate(primary, foreground == primary.Handle && !Native.IsIconic(primary.Handle)); break;
                case DockClickAction.Close: if (primary != null) ShellService.Close(primary); break;
                case DockClickAction.MinimizeSimilar:
                case DockClickAction.RestoreSimilar:
                case DockClickAction.CloseSimilar:
                    foreach (var window in DockClickPolicy.Similar(entry, current))
                    {
                        if (action == DockClickAction.CloseSimilar) ShellService.Close(window);
                        else if (action == DockClickAction.MinimizeSimilar) ShellService.Activate(window, true);
                        else if (Native.IsIconic(window.Handle)) Native.ShowWindowAsync(window.Handle, 9);
                    }
                    break;
            }
        });
        // WinEvent notifications may be delayed or suppressed for transitions
        // initiated by this process. Record the accepted activation before the
        // next click can temporarily focus the dock itself.
        tracker.ObserveForeground();
    }
    private Func<bool> ConfigureItemDrag(FrameworkElement element, DockEntry entry)
    {
        Windows.Foundation.Point? pressed = null;
        bool dragged = false;
        // Buttons consume pointer events, so detect the gesture even when handled.
        // In-dock movement uses pointer capture; external file drops use WinUI DnD.
        element.CanDrag = false; element.AllowDrop = true;
        element.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, e) =>
        {
            dragged = false;
            var point = e.GetCurrentPoint(element);
            pressed = entry.IsPinned && CanMoveIcons && point.Properties.IsLeftButtonPressed ? point.Position : null;
            if (pressed != null) { dragStartRoot = element.TransformToVisual(root).TransformPoint(point.Position); element.CapturePointer(e.Pointer); }
        }), true);
        element.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler((_, e) =>
        {
            if (pressed is not { } origin || !CanMoveIcons) return;
            var point = e.GetCurrentPoint(element);
            if (!point.Properties.IsLeftButtonPressed) return;
            if (!dragged && Math.Abs(point.Position.X - origin.X) < 6 && Math.Abs(point.Position.Y - origin.Y) < 6) return;
            if (!dragged)
            {
                dragged = true;
                BeginItemDrag(element, entry, () => element.CapturePointer(e.Pointer));
            }
            e.Handled = true;
            var along = Along(e.GetCurrentPoint(strip).Position);
            dragDropAllowed = IsPinnedDropTarget(along);
            SetDragCursor(dragDropAllowed);
            if (dragDropAllowed)
            {
                dragTargetSlot = DragInsertionSlot(along);
                PreviewPinnedReorder(element, along);
            }
            else PreviewPinnedReorder(element, double.NaN);
            if (dragged)
            {
                UpdateItemDragPosition(e.GetCurrentPoint(root).Position);
            }
        }), true);
        element.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler((_, e) =>
        {
            pressed = null;
            if (dragged && internalDragId == entry.Item.Id)
            {
                e.Handled = true;
                if (dragDropAllowed && DockOrdering.Move(Settings, entry.Item.Id, dragTargetSlot, Native.GetAsyncKeyState(0x12) < 0))
                { Save(); lastSignature = ""; }
                FinishItemDrag();
            }
            element.ReleasePointerCapture(e.Pointer);
        }), true);
        element.AddHandler(UIElement.PointerCanceledEvent, new PointerEventHandler((_, _) => { pressed = null; if (dragged) FinishItemDrag(); }), true);
        element.AddHandler(UIElement.PointerCaptureLostEvent, new PointerEventHandler((_, _) =>
        {
            if (preparingItemDrag) return;
            pressed = null;
            if (internalDragId != entry.Item.Id) return;
            if (Native.GetAsyncKeyState(0x01) >= 0 && dragDropAllowed && DockOrdering.Move(Settings, entry.Item.Id, dragTargetSlot, Native.GetAsyncKeyState(0x12) < 0))
            { Save(); lastSignature = ""; }
            FinishItemDrag();
        }), true);
        element.DragOver += DockDragOver;
        element.Drop += async (_, e) => { e.Handled = true; await DropOn(entry.Item, e); };
        return () => dragged;
    }
    private void BeginItemDrag(FrameworkElement element, DockEntry entry, Action? capturePointer = null)
    {
        dragging = true; internalDragId = entry.Item.Id; draggedElement = element;
        preparingItemDrag = true;
        ResetMagnification(); root.UpdateLayout();
        CaptureDragRestingLayout();
        dragTargetSlot = Settings.Items.FindIndex(item => item.Id == entry.Item.Id);
        if (element is Button draggedButton)
        {
            foreach (var indicator in new[] { runningIndicators.GetValueOrDefault(draggedButton), groupIndicators.GetValueOrDefault(draggedButton) }.OfType<Border>())
            {
                dragIndicatorVisibility[indicator] = indicator.Visibility;
                indicator.Visibility = Visibility.Collapsed;
            }
        }
        draggedVisual = dragVisuals.TryGetValue(element, out var visualElement) ? visualElement : element;
        dragVisualOrigin = draggedVisual.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        draggedVisualOpacity = draggedVisual.Opacity;
        draggedVisual.Opacity = .68;
        if (draggedVisual != element && draggedVisual.Parent is Grid parent)
        {
            var parentOrigin = parent.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            dragVisualOrigin = new Windows.Foundation.Point(
                parentOrigin.X + (parent.ActualWidth - draggedVisual.Width + draggedVisual.Margin.Left - draggedVisual.Margin.Right) / 2,
                parentOrigin.Y + (parent.ActualHeight - draggedVisual.Height + draggedVisual.Margin.Top - draggedVisual.Margin.Bottom) / 2);
            dragVisualParent = parent;
            dragVisualIndex = parent.Children.IndexOf(draggedVisual);
            dragVisualMargin = draggedVisual.Margin;
            parent.Children.Remove(draggedVisual);
            draggedVisual.Margin = new Thickness(0);
            dragOverlay.Children.Add(draggedVisual);
            Canvas.SetLeft(draggedVisual, dragVisualOrigin.X);
            Canvas.SetTop(draggedVisual, dragVisualOrigin.Y);
        }
        capturePointer?.Invoke();
        preparingItemDrag = false;
    }
    private void UpdateItemDragPosition(Windows.Foundation.Point pointer)
    {
        if (draggedVisual == null) return;
        var panel = MaterialPanel;
        double minimum = (Vertical ? panel.Top - hostBounds.Top : panel.Left - hostBounds.Left) / scale;
        double end = (Vertical ? panel.Bottom - hostBounds.Top : panel.Right - hostBounds.Left) / scale;
        double length = Vertical ? draggedVisual.ActualHeight : draggedVisual.ActualWidth;
        double origin = Along(dragVisualOrigin);
        double position = Math.Clamp(origin + Along(pointer) - Along(dragStartRoot), minimum, Math.Max(minimum, end - length));
        draggedVisual.RenderTransform = new TranslateTransform { X = Vertical ? 0 : position - origin, Y = Vertical ? position - origin : 0 };
    }
    private double DragElementLength(FrameworkElement child) => child is Button ? ButtonSize : SeparatorWidth;
    private void CaptureDragRestingLayout()
    {
        // Use the same layout model as Reposition and magnification. Visual
        // transforms can still expose the previous hover frame during reset.
        dragRestingStarts.Clear();
        var children = strip.Children.OfType<FrameworkElement>().ToArray();
        var starts = DockSpacing.Starts(children.Select(DragElementLength).ToArray(), children.Select(c => c is Button).ToArray(), DisplaySpacing);
        double origin = Vertical ? strip.Padding.Top : strip.Padding.Left;
        for (int i = 0; i < children.Length; i++) dragRestingStarts[children[i]] = origin + starts[i];
    }
    private bool IsPinnedDropTarget(double along)
    {
        var firstUnpinned = strip.Children.OfType<FrameworkElement>().FirstOrDefault(child => child.Tag is DockEntry { IsPinned: false } || child.Tag == null && child is Border);
        if (firstUnpinned == null) return true;
        var start = dragRestingStarts.TryGetValue(firstUnpinned, out var restingStart) ? restingStart : Along(firstUnpinned.TransformToVisual(strip).TransformPoint(new Windows.Foundation.Point()));
        return along < start;
    }
    private int DragInsertionSlot(double along)
    {
        var pins = strip.Children.OfType<FrameworkElement>().Where(child => child.Tag is DockEntry { IsPinned: true }).ToList();
        var next = pins.FindIndex(child => along < DragRestingStart(child) + DragElementLength(child) / 2);
        return next < 0 ? Settings.Items.Count : Settings.Items.FindIndex(item => item.Id == ((DockEntry)pins[next].Tag).Item.Id);
    }
    private double DragRestingStart(FrameworkElement child) => dragRestingStarts.TryGetValue(child, out var start) ? start : Along(child.TransformToVisual(strip).TransformPoint(new Windows.Foundation.Point()));
    private void SetDragCursor(bool allowed)
    {
        root.SetDragCursor(allowed);
        // IDC_ARROW / IDC_NO. Pointer capture keeps this cursor stable while
        // a drag crosses the dynamic, unpinned task section.
        Native.SetCursor(Native.LoadCursor(0, (nint)(allowed ? 32512 : 32648)));
    }
    private void PreviewPinnedReorder(FrameworkElement source, double along)
    {
        var pins = strip.Children.OfType<FrameworkElement>().Where(child => child.Tag is DockEntry { IsPinned: true }).ToList();
        var sourceIndex = pins.IndexOf(source);
        if (sourceIndex < 0) return;
        var next = double.IsNaN(along) ? sourceIndex : pins.FindIndex(child => along < DragRestingStart(child) + DragElementLength(child) / 2);
        var target = next < 0 ? pins.Count : next;
        var orderedPins = pins.ToList(); orderedPins.RemoveAt(sourceIndex);
        orderedPins.Insert(target > sourceIndex ? target - 1 : target, source);
        // Keep automatic section dividers and running items in their row slots.
        int pinSlot = 0;
        var ordered = strip.Children.OfType<FrameworkElement>().Select(child => pins.Contains(child) ? orderedPins[pinSlot++] : child).ToArray();
        var starts = DockSpacing.Starts(ordered.Select(DragElementLength).ToArray(), ordered.Select(child => child is Button).ToArray(), DisplaySpacing);
        double origin = DragRestingStart(pins[0]);
        foreach (var child in ordered.Where(child => child != source))
        {
            double offset = origin + starts[Array.IndexOf(ordered, child)] - DragRestingStart(child);
            if (dragPreviewOffsets.TryGetValue(child, out var previousOffset) && previousOffset == offset) continue;
            dragPreviewOffsets[child] = offset;
            var visual = ElementCompositionPreview.GetElementVisual(child);
            ElementCompositionPreview.SetIsTranslationEnabled(child, true);
            var translation = Vertical ? new Vector3(0, (float)offset, 0) : new Vector3((float)offset, 0, 0);
            if (Motion)
            {
                var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
                animation.InsertKeyFrame(1, translation, visual.Compositor.CreateCubicBezierEasingFunction(new Vector2(DockMotion.RearrangeX1, DockMotion.RearrangeY1), new Vector2(DockMotion.RearrangeX2, DockMotion.RearrangeY2)));
                animation.Duration = TimeSpan.FromMilliseconds(DockMotion.RearrangeMs);
                visual.StartAnimation("Translation", animation);
            }
            else visual.Properties.InsertVector3("Translation", translation);
            dragReorderElements.Add(child);
        }
    }
    private void ClearPinnedReorderPreview()
    {
        foreach (var element in dragReorderElements)
        {
            var visual = ElementCompositionPreview.GetElementVisual(element);
            visual.StopAnimation("Translation");
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
        dragReorderElements.Clear();
        dragPreviewOffsets.Clear();
    }
    private void FinishItemDrag()
    {
        foreach (var pair in dragIndicatorVisibility) pair.Key.Visibility = pair.Value;
        dragIndicatorVisibility.Clear();
        ClearPinnedReorderPreview();
        SetDragCursor(true);
        if (draggedVisual != null)
        {
            draggedVisual.RenderTransform = null;
            draggedVisual.Opacity = draggedVisualOpacity;
            if (dragVisualParent != null)
            {
                dragOverlay.Children.Remove(draggedVisual);
                Canvas.SetLeft(draggedVisual, double.NaN); Canvas.SetTop(draggedVisual, double.NaN);
                draggedVisual.Margin = dragVisualMargin;
                dragVisualParent.Children.Insert(dragVisualIndex, draggedVisual);
                dragVisualParent = null;
            }
            draggedVisual = null;
        }
        draggedElement = null; dragDropAllowed = true; dragRestingStarts.Clear();
        internalDragId = null; dragging = false; insertionMarker.Visibility = Visibility.Collapsed;
        DispatcherQueue.TryEnqueue(Reconcile);
    }
    private async Task LoadIcon(DockItem item, Microsoft.UI.Xaml.Controls.Image image, FontIcon fallback, Grid host, bool pinned, nint explorerWindow = 0)
    {
        var revision = themeRevision;
        var request = new object(); image.Tag = request;
        if (pinned && DockTarget.IsMissing(item))
        {
            fallback.Visibility = Visibility.Collapsed;
            folderPreviewCache.Remove(item.Id);
            return;
        }
        if (item.Kind == ItemKind.Folder && (item.ShowThumbnail || item.Folder.StackIcon) && string.IsNullOrEmpty(IconService.CustomPathFor(item))
            && folderPreviewCache.TryGetValue(item.Id, out var cached) && cached.Theme == revision && cached.Pixels == IconPixels)
        {
            foreach (var preview in cached.Sources)
                host.Children.Add(new Microsoft.UI.Xaml.Controls.Image { Source = preview, Width = DisplayIconSize * .72, Height = DisplayIconSize * .72, Margin = new Thickness((host.Children.Count - 2) * 5, 0, 0, (host.Children.Count - 2) * 5) });
            if (cached.Sources.Count > 0) { image.Visibility = Visibility.Collapsed; fallback.Visibility = Visibility.Collapsed; }
            ApplyDownloadsStackBase(item, image, fallback);
        }
        var path = explorerWindow != 0 && explorerFolderTargets.TryGetValue(explorerWindow, out var folderTarget) ? folderTarget : IconService.PathFor(item);
        bool custom = !string.IsNullOrEmpty(IconService.CustomPathFor(item));
        bool thumbnail = item.ShowThumbnail && !custom;
        bool imageFile = custom || item.IsAutoUsbPin && item.AttachedDriveKind != AttachedDriveKind.ExternalDisk;
        bool associatedApp = item.Kind == ItemKind.File && !item.IsDownloadedPin && !item.ShowThumbnail && !custom;
        var retained = IconService.TryGetLoaded(path, thumbnail, imageFile, associatedApp, IconPixels);
        if (retained == null && item.Kind == ItemKind.Application && !custom && path != item.Target)
            retained = IconService.TryGetLoaded(item.Target, requestedPixels: IconPixels);
        if (retained != null) { image.Source = retained; fallback.Visibility = Visibility.Collapsed; }
        var source = await IconService.Load(path, thumbnail, imageFile, associatedApp, IconPixels);
        if (source == null && item.Kind == ItemKind.Application && string.IsNullOrEmpty(IconService.CustomPathFor(item)) && path != item.Target)
            source = await IconService.Load(item.Target, requestedPixels: IconPixels);
        if (revision != themeRevision || !ReferenceEquals(image.Tag, request)) return;
        if (source != null) { image.Source = source; fallback.Visibility = Visibility.Collapsed; QueueUsbAppearanceBounce(item, image, host); if (item.IsDownloadedPin) { readyDownloadIcons[item.Target] = source; QueueDownloadFeedback(item, image, host); } }
        else if (image.Source == null) fallback.Visibility = Visibility.Collapsed;
        if (item.Kind == ItemKind.Folder && (item.ShowThumbnail || item.Folder.StackIcon) && string.IsNullOrEmpty(IconService.CustomPathFor(item)))
        {
            ApplyDownloadsStackBase(item, image, fallback);
            await RefreshFolderPreview(item, host, image, fallback);
        }
    }
    private void ApplyDownloadsStackBase(DockItem item, Microsoft.UI.Xaml.Controls.Image image, FontIcon fallback)
    {
        if (!IsDownloads(item) || !item.Folder.ShowDownloadsIconUnderStack || !(item.ShowThumbnail || item.Folder.StackIcon)
            || !string.IsNullOrEmpty(IconService.CustomPathFor(item))) return;
        if (image.Source is not Microsoft.UI.Xaml.Media.Imaging.SvgImageSource)
        {
            var vector = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource { RasterizePixelWidth = IconPixels * 2, RasterizePixelHeight = IconPixels * 2 };
            image.Source = vector;
            _ = LoadBadge();
            async Task LoadBadge()
            {
                try { await IconResources.LoadDownloadsBadge(vector); }
                catch (Exception ex) { Log.Write(ex); }
            }
        }
        image.Visibility = Visibility.Visible;
        fallback.Visibility = Visibility.Collapsed;
    }
    private void RefreshFolderPreviews(string path)
    {
        foreach (var entry in buttons.Where(b => b.Entry.Item.Kind == ItemKind.Folder && string.Equals(b.Entry.Item.Target, path, StringComparison.OrdinalIgnoreCase)).ToArray())
            if (entry.Icon is Grid host && host.Children.OfType<FontIcon>().FirstOrDefault() is { } fallback && host.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().FirstOrDefault() is { } image)
                _ = RefreshFolderPreview(entry.Entry.Item, host, image, fallback);
    }
    private async Task RefreshFolderPreview(DockItem item, Grid host, Microsoft.UI.Xaml.Controls.Image image, FontIcon fallback)
    {
        int version = folderPreviewVersions.GetValueOrDefault(host) + 1;
        folderPreviewVersions[host] = version;
        try
        {
            var revision = themeRevision;
            // Downloads' stack is its own icon treatment. Keeping the folder
            // image beneath it makes the stack look like a folder badge.
            var stackOptions = FolderListing.StackOptions(item.Folder, IsDownloads(item));
            var rows = await FolderListing.ReadAsync(item.Target, stackOptions, default);
            var previews = new List<Microsoft.UI.Xaml.Controls.Image>();
            foreach (var row in rows.Entries.Take(3).Reverse())
            {
                var preview = await IconService.Load(row.Path, !row.IsFolder, requestedPixels: IconRaster.Pixels(DockGeometry.HoverSizePercent(DisplayIconSize, Settings.HoverMagnificationPercent, 1) * .72, scale));
                if (revision != themeRevision || folderPreviewVersions.GetValueOrDefault(host) != version || closing) return;
                if (preview != null) previews.Add(new Microsoft.UI.Xaml.Controls.Image { Source = preview, Width = DisplayIconSize * .72, Height = DisplayIconSize * .72, Margin = new Thickness(previews.Count * 5, 0, 0, previews.Count * 5) });
            }
            if (revision != themeRevision || folderPreviewVersions.GetValueOrDefault(host) != version || closing) return;
            foreach (var old in host.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().Where(child => child != image).ToArray()) host.Children.Remove(old);
            foreach (var preview in previews) host.Children.Add(preview);
            folderPreviewCache[item.Id] = (revision, IconPixels, previews.Select(p => p.Source).ToList());
            image.Visibility = previews.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            fallback.Visibility = previews.Count > 0 || image.Source != null ? Visibility.Collapsed : Visibility.Visible;
            ApplyDownloadsStackBase(item, image, fallback);
            foreach (var id in folderPreviewCache.Keys.Where(id => !Settings.Items.Any(i => i.Id == id)).ToArray()) folderPreviewCache.Remove(id);
        }
        catch (Exception ex) { Log.Write(ex); }
    }
    private void PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (hoverTestInput != null || dragging || popupCount > 0 || !Motion) return;
        if (KeepHoverClickHold(e.GetCurrentPoint(root).Position)) return;
        UpdateMagnification(Along(e.GetCurrentPoint(strip).Position));
    }
    private void ShowHoverLabel(Button button, string text)
    {
        hoverLabel.Tag = button;
        ((TextBlock)hoverLabel.Child).Text = text;
        hoverLabel.Visibility = Visibility.Visible;
        hoverLabel.UpdateLayout();
        var point = button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        var x = point.X + (button.ActualWidth - hoverLabel.ActualWidth) / 2;
        double y;
        if (!Vertical)
        {
            y = PositionEdge == DockEdge.Top ? point.Y + button.ActualHeight + 4 : point.Y - hoverLabel.ActualHeight - 4;
        }
        else if (PositionEdge == DockEdge.Left)
        {
            x = point.X + button.ActualWidth + 6;
            y = point.Y + (button.ActualHeight - hoverLabel.ActualHeight) / 2;
        }
        else
        {
            x = point.X - hoverLabel.ActualWidth - 6;
            y = point.Y + (button.ActualHeight - hoverLabel.ActualHeight) / 2;
        }
        hoverLabel.Margin = new Thickness(Math.Max(0, x), Math.Max(0, y), 0, 0);
    }
    private ScreenEdgeGapInput? gapInput;
    private PixelRect ScreenEdgeGap => PositionEdge switch
        {
            DockEdge.Top => new(visibleBounds.Left, visibleBounds.Top - Settings.FloatingGap, visibleBounds.Right, visibleBounds.Top),
            DockEdge.Left => new(visibleBounds.Left - Settings.FloatingGap, visibleBounds.Top, visibleBounds.Left, visibleBounds.Bottom),
            DockEdge.Right => new(visibleBounds.Right, visibleBounds.Top, visibleBounds.Right + Settings.FloatingGap, visibleBounds.Bottom),
            _ => new(visibleBounds.Left, visibleBounds.Bottom, visibleBounds.Right, visibleBounds.Bottom + Settings.FloatingGap)
    };
    private bool AcceptGapRightClick(Native.POINT point) => !closing && initialized
        && Settings.ActivateThroughScreenEdgeGap && !hidden && Settings.FloatingGap > 0 && ScreenEdgeGap.Contains(point.X, point.Y);
    private void GapRightClick(Native.POINT point)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (closing || hidden || popupCount > 0) return;
            CaptureClickForeground();
            double along = (Vertical ? point.Y - hostBounds.Top : point.X - hostBounds.Left) / scale;
            var target = buttons.FirstOrDefault(item =>
            {
                double start = Along(item.Button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()));
                return along >= start && along < start + (Vertical ? item.Button.ActualHeight : item.Button.ActualWidth);
            });
            if (target.Button != null) InvokeClick(target.Entry, DockMouseButton.Right, target.Button, target.Icon);
            else ShowDockMenuAtPointer(point, preferSeparator: Native.GetAsyncKeyState(0x12) < 0);
        });
    }
    private bool UpdateGapInteraction(Native.POINT cursor)
    {
        bool active = Settings.ActivateThroughScreenEdgeGap && !hidden && Settings.FloatingGap > 0;
        var gap = ScreenEdgeGap;
        if (!active || !gap.Contains(cursor.X, cursor.Y))
        {
            if (gapHoveredButton != null) { hoveredButtons.Remove(gapHoveredButton); UpdateTaskbarHighlight(gapHoveredButton, (DockEntry)gapHoveredButton.Tag); gapHoveredButton = null; }
            gapLeftPressed = gapRightPressed = gapMiddlePressed = false;
            return false;
        }
        double rootAlong = (Vertical ? cursor.Y - hostBounds.Top : cursor.X - hostBounds.Left) / scale;
        var stripOrigin = strip.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        double stripAlong = rootAlong - Along(stripOrigin);
        var target = buttons.FirstOrDefault(item =>
        {
            var start = Along(item.Button.TransformToVisual(strip).TransformPoint(new Windows.Foundation.Point()));
            return stripAlong >= start && stripAlong < start + (Vertical ? item.Button.ActualHeight : item.Button.ActualWidth);
        });
        if (gapHoveredButton != target.Button)
        {
            if (gapHoveredButton != null) { hoveredButtons.Remove(gapHoveredButton); UpdateTaskbarHighlight(gapHoveredButton, (DockEntry)gapHoveredButton.Tag); }
            gapHoveredButton = target.Button;
            if (gapHoveredButton != null) { hoveredButtons.Add(gapHoveredButton); UpdateTaskbarHighlight(gapHoveredButton, target.Entry); }
        }
        UpdateMagnification(stripAlong);
        bool left = Native.GetAsyncKeyState(0x01) < 0, right = Native.GetAsyncKeyState(0x02) < 0, middle = Native.GetAsyncKeyState(0x04) < 0;
        if (target.Button != null)
        {
            CaptureClickForeground();
            if (left && !gapLeftPressed) InvokeClick(target.Entry, DockMouseButton.Left, target.Button, target.Icon);
            // Right-click is handled by gapInput so Explorer never sees it.
            if (middle && !gapMiddlePressed) InvokeClick(target.Entry, DockMouseButton.Middle, target.Button, target.Icon);
        }
        gapLeftPressed = left; gapRightPressed = right; gapMiddlePressed = middle;
        return true;
    }
    private void Bounce(FrameworkElement element, bool appearance = false)
    {
        if (!Motion || dockOverflow) return;
        var item = buttons.FirstOrDefault(b => b.Icon == element);
        if (item.Button == null) return;
        if (!appearance && item.Entry.Item.Kind is (ItemKind.StartMenu or ItemKind.TaskView or ItemKind.Search)) return;
        if (hoverWave == null) PrepareHoverSurface();
        launchBounces[item.Entry.Item.Id] = System.Diagnostics.Stopwatch.GetTimestamp();
        StartHoverRendering();
    }
    private void UpdateRecycle()
    {
        if (!startupShellReady) return;

        var iconIdentity = IconService.ReadThemeIdentity();
        var count = ShellService.RecycleCount(); bool changed = count != recycleCount || iconIdentity != recycleIconIdentity; recycleCount = count; recycleIconIdentity = iconIdentity;
        if (changed) IconService.Invalidate("shell:RecycleBinFolder");
        foreach (var (button, icon, entry) in buttons.Where(x => x.Entry.Item.Kind == ItemKind.RecycleBin))
        {
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, count == 0 ? "Recycle Bin (empty)" : $"Recycle Bin ({count} {(count == 1 ? "item" : "items")})");
            button.Opacity = 1;
            if (changed && icon is Grid grid && grid.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().FirstOrDefault() is { } image)
                TrackStartupLoad(RefreshRecycleImage(image, entry.Item));
        }
    }
    private async Task RefreshRecycleImage(Microsoft.UI.Xaml.Controls.Image image, DockItem item) { var revision = themeRevision; var count = recycleCount; var identity = recycleIconIdentity; var source = await IconService.Load(IconService.PathFor(item), imageFile: !string.IsNullOrWhiteSpace(IconService.CustomPathFor(item)), requestedPixels: IconPixels); if (source != null && revision == themeRevision && count == recycleCount && identity == recycleIconIdentity && !closing) image.Source = source; }
    private void Reposition(bool reservation)
    {
        if (initialized && Native.IsIconic(Handle) && HasExternalFullscreenForeground()) return;
        if (closing || positioning) return;
        if (rowAnimating && !rowFinishing) FinishRowTransition();
        if (hoverPainting || hoverGeometryRepairing)
        {
            deferredReposition = true; deferredReservation |= reservation;
            return;
        }
        if (hoverTestInput != null) hoverUnexpectedMoves.Add(Environment.StackTrace);
        positioning = true;
        var previousScale = scale;
        try
        {
            monitor = ResolveDisplay(); scale = monitor.Scale;
            var baseHeight = CurrentDockThickness;
            var expandedHeight = baseHeight + (HoverEnvelope ? HoverEnvelopeOverflow : 0);
            strip.Spacing = Math.Max(0, DisplaySpacing);
            var layoutChildren = strip.Children.OfType<FrameworkElement>().ToArray();
            for (int i = 0; i < layoutChildren.Length; i++)
            {
                if (layoutChildren[i] is Border separator) SynchronizeSeparatorGeometry(separator);
                double inset = i > 0 && DisplaySpacing < 0 && layoutChildren[i - 1] is Button && layoutChildren[i] is Button ? DisplaySpacing : 0;
                layoutChildren[i].Margin = layoutChildren[i] is Border
                    ? Vertical ? new Thickness(MarginDip(Settings.DockMarginLeft), inset, MarginDip(Settings.DockMarginRight), 0)
                        : new Thickness(inset, MarginDip(Settings.DockMarginTop), 0, MarginDip(Settings.DockMarginBottom))
                    : Vertical ? new Thickness(0, inset, 0, 0) : new Thickness(inset, 0, 0, 0);
            }
            var visibleHeight = expandedHeight;
            double canvasThickness = Settings.IconSize + (Settings.WindowsTaskbarHighlights ? 20 : 12)
                + MarginDip(Vertical ? Settings.DockMarginLeft + Settings.DockMarginRight : Settings.DockMarginTop + Settings.DockMarginBottom)
                + Math.Max(0, DockGeometry.HoverSizePercent(Settings.IconSize, Settings.HoverMagnificationPercent, 1) - Settings.IconSize)
                + Math.Min(24, Settings.IconSize * .5);
            var h = DockGeometry.Pixels(HoverEnvelope ? Math.Max(expandedHeight, canvasThickness) : expandedHeight, scale);
            strip.Orientation = Vertical ? Orientation.Vertical : Orientation.Horizontal;
            strip.VerticalAlignment = Vertical ? VerticalAlignment.Center : PositionEdge == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            strip.HorizontalAlignment = Vertical ? (PositionEdge == DockEdge.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left) : HorizontalAlignment.Center;
            // The four physical-pixel settings are converted at the monitor DPI.
            // Along-axis padding remains background. Cross-axis padding belongs to
            // each button so clicks at the screen edge reach that item.
            strip.Padding = Vertical ? new Thickness(0, MarginDip(Settings.DockMarginTop), 0, MarginDip(Settings.DockMarginBottom))
                : new Thickness(MarginDip(Settings.DockMarginLeft), 0, MarginDip(Settings.DockMarginRight), 0);
            scroll.Height = Vertical ? double.NaN : visibleHeight; scroll.Width = Vertical ? visibleHeight : double.NaN;
            scroll.HorizontalAlignment = Vertical ? (PositionEdge == DockEdge.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left) : HorizontalAlignment.Center; scroll.VerticalAlignment = Vertical ? VerticalAlignment.Center : PositionEdge == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            scroll.HorizontalContentAlignment = dockOverflow && !Vertical ? HorizontalAlignment.Left : Vertical ? (PositionEdge == DockEdge.Right ? HorizontalAlignment.Right : HorizontalAlignment.Left)
                : PositionAlignment switch { DockAlignment.Start => HorizontalAlignment.Left, DockAlignment.End => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
            scroll.VerticalContentAlignment = dockOverflow && Vertical ? VerticalAlignment.Top : !Vertical ? (PositionEdge == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom)
                : PositionAlignment switch { DockAlignment.Start => VerticalAlignment.Top, DockAlignment.End => VerticalAlignment.Bottom, _ => VerticalAlignment.Center };
            scroll.HorizontalScrollBarVisibility = dockOverflow && !Vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            scroll.VerticalScrollBarVisibility = dockOverflow && Vertical ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden;
            scroll.HorizontalScrollMode = Vertical ? ScrollMode.Disabled : ScrollMode.Enabled; scroll.VerticalScrollMode = Vertical ? ScrollMode.Enabled : ScrollMode.Disabled;
            var reserve = VisibilityPolicy.Reserve(Settings, manuallyHidden, DisplayHideMode);
            if (reservation)
            {
                appbar.Update(monitor, DockGeometry.Pixels(baseHeight, scale) + Settings.FloatingGap, reserve, PositionEdge);
                // Removing the appbar changes the work area synchronously. Do
                // not position against the stale work area read before removal.
                if (!reserve) monitor = Native.Monitors().FirstOrDefault(m => m.Device == monitor.Device) ?? monitor;
            }
            var area = appbar.IsRegistered ? appbar.Area : monitor.Work;
            bool spanDockLength = SpanDockLengthActive();
            laidOutSpanning = spanDockLength;
            var placementArea = Vertical
                ? new PixelRect(area.Left, Math.Max(area.Top, monitor.Work.Top), area.Right, Math.Min(area.Bottom, monitor.Work.Bottom))
                : new PixelRect(Math.Max(area.Left, monitor.Work.Left), area.Top, Math.Min(area.Right, monitor.Work.Right), area.Bottom);
            var rowStart = Vertical ? MarginDip(Settings.DockMarginTop) : MarginDip(Settings.DockMarginLeft);
            var rowEnd = Vertical ? MarginDip(Settings.DockMarginBottom) : MarginDip(Settings.DockMarginRight);
            var widthDips = DockSpacing.Length(layoutChildren.Select(e => e is Button ? ButtonSize : SeparatorWidth).ToArray(), layoutChildren.Select(e => e is Button).ToArray(), DisplaySpacing) + rowStart + rowEnd;
            var width = spanDockLength ? (Vertical ? placementArea.Height : placementArea.Width) : Math.Min(DockGeometry.Pixels(widthDips, scale), Vertical ? placementArea.Height : placementArea.Width);
            resting = DockGeometry.PlaceEdge(placementArea, width, DockGeometry.Pixels(baseHeight, scale), PositionEdge, Settings.FloatingGap, PositionAlignment);
            if (!hoverLayoutExpanded || hoverWave == null) visibleBounds = resting;
            dockBorder.Width = Vertical ? baseHeight : double.NaN; dockBorder.Height = Vertical ? double.NaN : baseHeight;
            dockBorder.HorizontalAlignment = Vertical && PositionEdge == DockEdge.Right ? HorizontalAlignment.Right : Vertical ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            dockBorder.VerticalAlignment = Vertical ? VerticalAlignment.Stretch : PositionEdge == DockEdge.Top ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            // The hidden edge detector uses the global cursor timer, so an
            // off-screen one-pixel host is enough when the strip is invisible.
            // Hidden docks retain their layout and native client dimensions.
            // Only the input region shrinks to the screen-edge trigger.
            var bounds = resting;
            if (HoverEnvelope)
            {
                // Membership changes (including desktop switches) must only
                // change the visible/input clips. Resizing a native compositor
                // surface first exposes a blank strip at its leading edge.
                // A stable transparent canvas also leaves room for hover.
                // Use the same transparent viewport in both span modes. A
                // narrower normal host shifts its native origin at span entry
                // before WinUI presents the compensated artwork (8 px when
                // centered), briefly moving every icon along the screen edge.
                // The separate material window still follows the visible dock.
                var hostLength = Vertical ? placementArea.Height : placementArea.Width;
                bounds = DockGeometry.PlaceEdge(placementArea, hostLength, h, PositionEdge, Settings.FloatingGap, PositionAlignment);
                if (Vertical) scroll.Height = hostLength / scale; else scroll.Width = hostLength / scale;
                strip.HorizontalAlignment = Vertical ? strip.HorizontalAlignment : PositionAlignment switch { DockAlignment.Start => HorizontalAlignment.Left, DockAlignment.End => HorizontalAlignment.Right, _ => HorizontalAlignment.Center };
                strip.VerticalAlignment = !Vertical ? strip.VerticalAlignment : PositionAlignment switch { DockAlignment.Start => VerticalAlignment.Top, DockAlignment.End => VerticalAlignment.Bottom, _ => VerticalAlignment.Center };
                dockBorder.Width = Vertical ? baseHeight : width / scale;
                dockBorder.Height = Vertical ? width / scale : baseHeight;
                if (Vertical) dockBorder.VerticalAlignment = strip.VerticalAlignment; else dockBorder.HorizontalAlignment = strip.HorizontalAlignment;
            }
            // Keep the larger native surface while staging a span contraction;
            // only its visible material region should shrink during the tween.
            if (spanTransitionHost is { } previousSpanHost) bounds = Union(previousSpanHost, bounds);
            bounds = RetainRowHost(bounds);
            if (HoverEnvelope) { if (Vertical) scroll.Height = bounds.Height / scale; else scroll.Width = bounds.Width / scale; }
            // Native HWND resizing and WinUI's client-size notification can
            // complete in different dispatch turns. Lay out against the new
            // logical viewport immediately instead of centering in the old one.
            root.HorizontalAlignment = HorizontalAlignment.Left;
            root.VerticalAlignment = VerticalAlignment.Top;
            root.Width = bounds.Width / scale;
            root.Height = bounds.Height / scale;
            if (hostBounds != bounds || (!visibilitySliding && Native.Bounds(Handle) != bounds))
            {
                hostBounds = bounds;
                hoverWindowMoves++;
                Native.ClearDockRegion(Handle);
                hoverClip = null;
                Native.SetWindowPos(Handle, -1, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x10u | (StartupComplete ? 0x40u : 0u));
            }
            if (!hoverRebuilding)
            {
                if (hoverWave == null) { visibleBounds = resting; SetHoverClip(); }
                PaintMaterialSurface(hoverWave == null ? 0 : Math.Min(hoverWave.TotalGrowth, hoverAvailableExpansion));
            }
        }
        finally
        {
            positioning = false;
            KeepTaskbarsAboveDock();
            // Refit before resuming hover when a monitor/work-area/edge change
            // makes the previously rendered row too long for the new viewport.
            if (initialized && !closing && !reconciling && !hoverRebuilding) Reconcile();
            if (hoverWave != null && !hoverRebuilding) RepairHoverGeometry();
            if (initialized && !closing && Math.Abs(previousScale - scale) > .001) { lastSignature = ""; DispatcherQueue.TryEnqueue(Reconcile); }
        }
    }
    private bool laidOutSpanning;
    private bool SpanDockLengthActive() => Settings.SpanFullWidth && (!Settings.SpanFullWidthOnlyWhenMaximized || WindowsFillDockScreen());
    private bool UseRoundedDockCorners => Settings.RoundedCorners && !(SpanDockLengthActive() && Settings.SquareCornersWhenSpanning);
    private void EnsureDockIsNotMinimized()
    {
        if (!initialized || closing || !Native.IsIconic(Handle) || HasExternalFullscreenForeground()) return;
        // SW_RESTORE activates the dock. Background recovery must leave focus
        // with the application the user is working in.
        Native.ShowWindowAsync(Handle, 4);
        Reposition(true); Reconcile();
    }
    private long coverageChecked;
    private bool coverageResult;
    private bool WindowsFillDockScreen()
    {
        // Corner styling is evaluated during construction, before the first layout selects a monitor.
        if (monitor == null || !startupShellReady) return false;
        long now = Environment.TickCount64;
        if (now - coverageChecked < 60) return coverageResult;
        coverageChecked = now;
        var visible = tracker.Read(false).Where(window => !window.Minimized && window.Handle != tracker.OwnTaskWindow
            && window.Bounds.Right > monitor.Work.Left && window.Bounds.Left < monitor.Work.Right
            && window.Bounds.Bottom > monitor.Work.Top && window.Bounds.Top < monitor.Work.Bottom).ToArray();
        coverageResult = visible.Any(window => window.Maximized)
            || WindowCoverage.TouchesDockAcrossScreen(monitor.Work, resting, visible.Select(window => window.Bounds), PositionEdge);
        return coverageResult;
    }
    private bool SyncWindowsTaskbar()
    {
        if (MonitorOverride != null) return false;
        windowsTaskbar.SetSuppressed = value => Native.SetPropW(Handle, "WinMac.Dock.HideWindowsTaskbar", value ? 1 : 0);
        bool changed = windowsTaskbar.Sync(Settings.HideWindowsTaskbar);
        settingsWindow?.SyncTaskbarStatus(windowsTaskbar.Error);
        return changed;
    }
    private void EvaluateVisibility()
    {
        if (!initialized || !StartupComplete || closing || monitor == null) return;
        if (!positioning && laidOutSpanning != SpanDockLengthActive())
        {
            TransitionDockSpan();
        }
        if (SyncWindowsTaskbar() && !positioning) Reposition(true);
        Native.SetPropW(Handle, "WinMac.Dock.TaskViewEnabled", Settings.ShowInTaskView ? 1 : 0);
        taskViewOpen = taskViewTestOverride ?? Native.IsTaskViewOpen();
        // Notification-band HWNDs are not ordinary desktop input surfaces.
        // Use that layer only while Task View actually covers the desktop;
        // leaving it enabled permanently changes activation/click semantics.
        int taskViewBand = Settings.ShowInTaskView && taskViewOpen ? 4 : 1;
        RequestTaskViewBand(taskViewBand);
        if (inspection) return;
        if (taskViewOpen)
        {
            hideSince = revealSince = manualHideSince = null;
            if (!Settings.ShowInTaskView || manuallyHidden) HideDock(immediate: true);
            else if (hidden) ShowDock(restoreManualReservation: false);
            return;
        }
        Native.GetCursorPos(out var cursor);
        var foreground = Native.GetForegroundWindow(); Native.GetWindowThreadProcessId(foreground, out var pid);
        bool own = pid == Environment.ProcessId;
        var active = own ? null : tracker.Describe(foreground) ?? DescribeFullscreenForeground(foreground);
        bool gapInteraction = UpdateGapInteraction(cursor);
        var triggerBounds = EdgeTriggerBounds;
        bool edge = monitor.Bounds.Contains(cursor.X, cursor.Y) && DockGeometry.AtEdge(triggerBounds, PositionEdge, cursor.X, cursor.Y, Settings.FloatingGap);
        bool inDock = !hidden && (HoverContains(cursor) || BouncingArtworkContains(cursor) || gapInteraction || (Settings.FullScreenEdgeTrigger && edge));
        bool fullscreen = active != null && !active.Minimized && active.Bounds.Left <= monitor.Bounds.Left && active.Bounds.Top <= monitor.Bounds.Top && active.Bounds.Right >= monitor.Bounds.Right && active.Bounds.Bottom >= monitor.Bounds.Bottom;
        bool interacting = inDock || popupCount > 0 || dragging || (own && (settingsWindow != null || keyboardFocus));
        bool fullscreenExcluded = fullscreen && FullscreenExclusions.Contains(Settings, active);
        bool attentionHold = AttentionRequiresDock(foreground);
        if (attentionHold && (!fullscreen || fullscreenExcluded))
        {
            hideSince = revealSince = manualHideSince = null;
            if (hidden) ShowDock(restoreManualReservation: false);
            return;
        }
        if (DisplayHideMode == HideMode.EdgeToggle || startupWaiting)
        {
            if (EvaluateEdgeVisibility(cursor, edge, fullscreen, fullscreenExcluded, interacting)) return;
        }
        if (fullscreen && !fullscreenExcluded && Settings.HideImmediatelyForFullscreen && !interacting && !edge)
        {
            hideSince = revealSince = null;
            HideDock(immediate: true);
            return;
        }
        if (manuallyHidden)
        {
            if (hidden && edge)
            {
                revealSince ??= DateTime.UtcNow;
                if ((DateTime.UtcNow - revealSince.Value).TotalMilliseconds >= Settings.RevealDelayMs)
                    ShowDock(Settings.ManualRevealBehavior == ManualRevealBehavior.RestoreReservation);
            }
            else if (!hidden && !interacting && !edge)
            {
                manualHideSince ??= DateTime.UtcNow;
                if ((DateTime.UtcNow - manualHideSince.Value).TotalMilliseconds >= Settings.ManualOverlayHideDelayMs) HideDock();
            }
            else { revealSince = null; manualHideSince = null; }
            return;
        }
        if (hidden && edge)
        {
            revealSince ??= DateTime.UtcNow;
            if ((DateTime.UtcNow - revealSince.Value).TotalMilliseconds >= Settings.RevealDelayMs) ShowDock();
            return;
        }
        revealSince = null;
        bool shouldHide = VisibilityPolicy.ShouldHide(DisplayHideMode, resting, active, interacting, fullscreen, monitor.Bounds, fullscreenExcluded);
        if (own && popupCount > 0) shouldHide = false;
        if (shouldHide)
        {
            hideSince ??= DateTime.UtcNow;
            if ((DateTime.UtcNow - hideSince.Value).TotalMilliseconds >= Settings.HideDelayMs) HideDock();
        }
        else { hideSince = null; if (hidden) ShowDock(); }
    }
    private void HideDock(bool immediate = false)
    {
        manualHideSince = null;
        if (hidden) { if (immediate && visibilitySliding) { visibilityTo = 1; FinishVisibilityAnimation(); } return; }
        ResetMagnification(); hidden = true;
        AnimateVisibility(false, immediate);
    }
    // Edge previews after a shortcut hide can remain in the manual-hidden state.
    // That keeps the appbar released until an explicit keyboard/tray show restores it.
    private void ShowDock(bool restoreManualReservation = true)
    {
        bool wasHidden = hidden;
        if (restoreManualReservation) manuallyHidden = false;
        hideSince = null; manualHideSince = null;
        hidden = false;
        if (wasHidden || visibilitySliding) AnimateVisibility(true); else { root.Opacity = 1; Reposition(true); }
    }
    public void HandleCommand(string command)
    {
        switch (command)
        {
            case "--test-passive-focus": _ = CheckPassiveFocusUpdates(); break;
            case "--inspect-displays": App.WriteDisplayDiagnostics(); break;
            case "--about": ShowAbout(); break;
            case "--reload": RestartDock(false); break;
            case "--test-pointer-desktop-layer": Settings.ShowInTaskView = false; EvaluateVisibility(); HandleCommand("--inspect"); break;
            case "--capture-render":
                using (var capture = new System.Drawing.Bitmap(resting.Width, resting.Height))
                {
                    using var graphics = System.Drawing.Graphics.FromImage(capture);
                    graphics.CopyFromScreen(resting.Left, resting.Top, 0, 0, capture.Size);
                    capture.Save(Path.Combine(Log.DirectoryPath, "dock-render.png"), System.Drawing.Imaging.ImageFormat.Png);
                }
                break;
            case "--elevate": RestartDock(true); break;
            case "--test-window-behavior": _ = CheckWindowBehavior(); break;
            case "--test-autofit": _ = CheckAutoFit(); break;
            case "--exit": Close(); break;
            case "--inspect": inspection = true; AppWindow.IsShownInSwitchers = true; Native.SetWindowLongPtrW(Handle, -20, (nint)(Native.GetWindowLongPtrW(Handle, -20).ToInt64() & ~0x80)); ShowDock(); Activate(); break;
            case "--inspect-off": inspection = false; AppWindow.IsShownInSwitchers = false; Native.SetWindowLongPtrW(Handle, -20, (nint)(Native.GetWindowLongPtrW(Handle, -20).ToInt64() | 0x80)); EvaluateVisibility(); break;
            case "--hide": SetExplicitVisibility(false); break;
            case "--toggle": SetExplicitVisibility(hidden || manuallyHidden); break;
            case "--settings": ShowSettings(); break;
            case "--test-item-transitions": _ = CheckItemTransitions(); break;
            case "--test-task-view": _ = CheckTaskView(); break;
            case "--test-task-view-bridge": _ = CheckTaskViewBridge(); break;
            case "--test-minimized-only": _ = CheckMinimizedOnly(); break;
            case "--test-minimize-stress": _ = CheckMinimizeStress(); break;
            case "--test-real-app-minimize": _ = CheckRealAppMinimize(); break;
            case "--test-click-stability": _ = CheckClickStability(); break;
            case "--test-menu-centering": _ = CheckMenuCentering(); break;
            case "--test-folder-placement": _ = CheckFolderPlacement(); break;
            case "--test-separator-pixels": _ = CheckSeparatorPixels(); break;
            case "--test-hover-handoff": _ = CheckHoverHandoff(); break;
            case "--test-special-item-menus": CheckSpecialItemMenus(); break;
            case "--test-shortcut-menus": CheckSpecialItemMenus(requestedOnly: true); break;
            case "--test-module-actions": _ = CheckModuleActions(); break;
            case "--test-drag-bounds": _ = CheckDragBounds(); break;
            case "--test-start-shell": _ = CheckStartShellAnimation(); break;
            case "--test-passive-focus-recovery": CheckPassiveFocusRecovery(); break;
            case "--test-edge-favorites": CheckEdgeFavorites(); break;
            case "--test-edge-menu-policy": CheckEdgeMenuPolicy(); break;
            case "--test-edge-new-tab": CheckEdgeNewTab(); break;
            case "--test-window-title-menu": CheckWindowTitleMenu(); break;
            case "--test-quoted-menu-states": _ = CheckQuotedMenuStates(); break;
            case "--test-laptop-layout": _ = CheckLaptopLayout(); break;
            case "--test-folder-scrolling": CheckFolderScrolling(); break;
            case "--test-downloads-sliders": CheckDownloadsSliderSpacing(); break;
            case "--test-settings-launch-feedback": _ = CheckSettingsLaunchFeedback(); break;
            case "--prepare-cross-monitor-test": _ = PrepareCrossMonitorTest(); break;
            case "--swap-cross-monitor-test": _ = SwapCrossMonitorTest(); break;
            case "--finish-cross-monitor-test": FinishCrossMonitorTest(); break;
            case "--test-taskbar-settings": CheckTaskbarSettings(); break;
            case "--test-visibility-features": _ = CheckVisibilityFeatures(); break;
            case "--test-attention": _ = CheckAttention(); break;
            case "--test-administrator-launch": _ = CheckAdministratorLaunch(); break;
            case "--test-drag-spacing": CheckDragSpacing(); break;
            case "--test-icon-resources": CheckBundledIconResources(); break;
            case "--test-icon-apply":
                Exception? iconApplyFailure = null; int iconApplyChecks = 0;
                try
                {
                    foreach (var kind in new[] { ItemKind.Application, ItemKind.StartMenu, ItemKind.RecycleBin })
                    {
                        var editor = new ItemPropertiesWindow(this, new DockItem { Kind = kind });
                        try { iconApplyChecks += editor.CheckIconApplyState(); }
                        finally { editor.Close(); }
                    }
                }
                catch (Exception ex) { iconApplyFailure = ex; Log.Write(ex); }
                File.WriteAllText(Path.Combine(Log.DirectoryPath, "icon-apply-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = iconApplyFailure == null, Checks = iconApplyChecks, Error = iconApplyFailure?.ToString() }));
                Close(); break;
            case "--test-properties-editor": _ = CheckPropertiesEditor(); break;
            case "--test-capture-pins": _ = CheckCapturePins(); break;
            case "--test-indicator-closing": _ = CheckIndicatorClosing(); break;
            case "--test-recycle-properties":
                _ = CheckRecycleProperties(); break;
            case "--test-properties-size":
                var propertyWindow = EditItem(Settings.Items.First());
                Native.GetWindowRect(WinRT.Interop.WindowNative.GetWindowHandle(propertyWindow), out var propertyRect);
                var propertyBounds = propertyRect.Pixels;
                File.WriteAllText(Path.Combine(Log.DirectoryPath, "properties-size-test.json"), System.Text.Json.JsonSerializer.Serialize(new { propertyBounds.Width, propertyBounds.Height }));
                break;
            case "--show": SetExplicitVisibility(true); break;
            case "--snapshot":
                File.WriteAllText(Path.Combine(Log.DirectoryPath, "render-state.json"), System.Text.Json.JsonSerializer.Serialize(new { RootOpacity = root.Opacity, StripOpacity = strip.Opacity, ArtworkOpacity = hoverArtwork.Opacity, RootWidth = root.ActualWidth, RootHeight = root.ActualHeight, StripWidth = strip.ActualWidth, StripHeight = strip.ActualHeight, ScrollWidth = scroll.ActualWidth, ScrollHeight = scroll.ActualHeight, StripOrigin = strip.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()), hidden, visibilitySliding, visibilityFraction, ShiftX = visibilityShift.X, ShiftY = visibilityShift.Y, hoverClip, rowAnimating, rowStaging, DockHandle = Handle.ToInt64(), MaterialHandle = materialHandle.ToInt64(), AboveDock = Native.GetWindow(Handle, 3).ToInt64(), AboveMaterial = Native.GetWindow(materialHandle, 3).ToInt64() }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                var snapshot = new { ShellAnimations = new { Failed = shellAnimationFailed, Icons = shellIcons.Select(i => new { Kind = i.Entry.Item.Kind.ToString(), i.Animation.Diagnostics }).ToArray() }, SettingsWindowHandle = tracker.OwnTaskWindow.ToInt64(), SettingsWindowDescription = tracker.Describe(tracker.OwnTaskWindow), DisplayIconSize, RequestedIconSize = Settings.IconSize, DisplayPadding, Overflow = dockOverflow, IsElevated, Topmost = (Native.GetWindowLongPtrW(Handle, -20).ToInt64() & 8) != 0, Backdrop = materialBackdrop?.Diagnostics ?? backdrop.Diagnostics, Hidden = hidden, ManuallyHidden = manuallyHidden, Reserved = appbar.IsRegistered, WorkArea = monitor.Work, WindowBounds = Native.Bounds(Handle), ClientBounds = Native.ClientBounds(Handle), MaterialSurface = new { Window = Native.Bounds(materialHandle), Client = Native.ClientBounds(materialHandle), Style = Native.GetWindowLongPtrW(materialHandle, -16).ToInt64() }, Resting = resting, RasterScale = scale, Edge = PositionEdge, FloatingGap = Settings.FloatingGap, WindowsTaskbarHighlights = Settings.WindowsTaskbarHighlights, TaskSeparatorVisible = strip.Children.OfType<Border>().Any(), AnimationRectRequests = animationRectRequests, LastAnimationTarget = lastAnimationTarget, TaskWindows = tracker.Read().Select(w => new { w.Title, Handle = w.Handle.ToInt64(), w.Minimized, Foreground = w.Handle == Native.GetForegroundWindow() }).ToArray(), Buttons = buttons.Select(b => new { b.Entry.Item.Name, b.Entry.IsPinned, b.Button.Width, ClickActions = ClickActionsFor(b.Entry), Running = runningIndicators.ContainsKey(b.Button), HighlightVisible = taskbarHighlights.TryGetValue(b.Button, out var tile) && tile.Tag is true, HighlightFill = taskbarHighlights.TryGetValue(b.Button, out var tile2) && tile2.Background is SolidColorBrush fill ? fill.Color.ToString() : null, HighlightBorder = taskbarHighlights.TryGetValue(b.Button, out var tile3) && tile3.BorderBrush is SolidColorBrush stroke ? stroke.Color.ToString() : null, IndicatorWidth = runningIndicators.TryGetValue(b.Button, out var indicator) ? indicator.Width : 0, IndicatorHeight = runningIndicators.TryGetValue(b.Button, out var indicator2) ? indicator2.Height : 0, AnimationTarget = b.Entry.Window == null ? (PixelRect?)null : AnimationTarget(b.Entry.Window.Handle), Scale = ElementCompositionPreview.GetElementVisual(b.Icon).Scale.X, Raster = b.Icon is Grid g ? IconService.Describe(g.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().FirstOrDefault()?.Source) : null }).ToArray() };
                File.WriteAllText(Path.Combine(Log.DirectoryPath, "diagnostics.json"), System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                break;
            case "--test-icon-resize": _ = CheckIconResize(); break;
            case "--test-layout-stress": _ = CheckLayoutStress(); break;
            case "--test-magnification": _ = CheckMagnification(); break;
            case "--test-dock-outline": _ = CheckDockOutline(); break;
            case "--test-beta-020": _ = CheckBeta020(); break;
            case "--test-explorer-folder-icons": _ = CheckExplorerFolderIcons(); break;
            case "--inspect-task-view-bridge":
                var bridgeStatus = TaskViewBridgeStatus();
                File.WriteAllText(Path.Combine(Log.DirectoryPath, "task-view-bridge-status.json"), System.Text.Json.JsonSerializer.Serialize(new { bridgeStatus.Text, bridgeStatus.ColorResource }));
                break;
            case "--test-beta-defaults":
                var expectedDefaults = new[] { "Start Menu", "Separator", "System Settings", "Task Manager", "Separator", "Microsoft Store", "File Explorer", "Separator", "Downloads", "Recycle Bin" };
                var defaultNames = Settings.Items.Select(item => item.Name).ToArray();
                var defaultIcons = Settings.Items.SelectMany(item => new[] { item.CustomIcon, item.DarkModeIcon, item.RecycleEmptyIcon, item.RecycleFullIcon, item.RecycleEmptyDarkIcon, item.RecycleFullDarkIcon }).Where(path => !string.IsNullOrEmpty(path)).ToArray();
                _ = CheckBetaDefaultIcons(defaultNames.SequenceEqual(expectedDefaults), defaultIcons);
                break;
            case "--test-spacing": _ = CheckSpacing(); break;
            case "--test-separator-spacing": _ = RunSeparatorAudit(); break;
            case "--test-unpinned-menus": CheckUnpinnedMenus(); break;
            case "--test-edge-clicks": _ = CheckEdgeClicks(); break;
            case "--test-tray-dismiss": _ = CheckTrayDismiss(); break;
            case "--test-tray-menu": ShowTrayMenu(); break;
            case "--test-motion": _ = CheckDockMotion(); break;
            case "--test-virtual-desktops":
                var desktopReport = new {
                    TaskViewOpen = Native.IsTaskViewOpen(), ShowInTaskView = Settings.ShowInTaskView,
                    BridgeVersion = Native.GetPropW(Handle, "WinMac.Dock.TaskViewBridgeVersion").ToInt64(),
                    Preemptions = Native.GetPropW(Handle, "WinMac.Dock.TaskViewPreemptions").ToInt64(),
                    DockBand = Native.WindowBand(Handle), MaterialBand = Native.WindowBand(materialHandle),
                    Current = tracker.Read(false).Select(w => new { w.Title, Handle = w.Handle.ToInt64() }).ToArray(),
                    All = tracker.Read(true).Select(w => new { w.Title, Handle = w.Handle.ToInt64(), CurrentDesktop = Native.IsWindowOnCurrentVirtualDesktop(w.Handle), Desktop = Native.WindowDesktop(w.Handle) }).ToArray(),
                    UnpinnedCount = buttons.Count(b => !b.Entry.IsPinned),
                    DynamicSeparatorCount = strip.Children.OfType<Border>().Count(b => b.Tag == null),
                    TrailingSeparator = strip.Children.LastOrDefault() is Border
                };
                File.WriteAllText(Path.Combine(Log.DirectoryPath, "virtual-desktops-test.json"), System.Text.Json.JsonSerializer.Serialize(desktopReport, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
                break;
            case "--test-theme-regressions": _ = CheckThemeRegressions(); break;
            case "--test-icon-regressions": _ = CheckIconRegressions(); break;
            case "--test-click-actions": _ = CheckClickActions(); break;
        }
    }
    private async Task CheckClickActions()
    {
        var results = new List<string>();
        var originalGrouping = Settings.GroupWindows;
        Exception? failure = null;
        List<TaskWindow> Fixtures() => tracker.Read().Where(w => w.Title.StartsWith("Dock test window ", StringComparison.Ordinal) && Path.GetFileName(w.ExecutablePath).Equals("WinMac.Dock.Fixture.exe", StringComparison.OrdinalIgnoreCase)).ToList();
        async Task Act(DockClickAction action, nint foreground = 0)
        {
            Reconcile();
            var fixture = buttons.First(b => Path.GetFileName(b.Entry.Item.ExecutablePath).Equals("WinMac.Dock.Fixture.exe", StringComparison.OrdinalIgnoreCase));
            ExecuteClickAction(fixture.Entry, action, fixture.Button, fixture.Icon, foreground);
            await Task.Delay(300);
        }
        void Check(bool passed, string name) { if (!passed) throw new InvalidOperationException(name); results.Add(name); }
        try
        {
            var initial = Fixtures();
            if (initial.Count < 2 || initial.Any(w => w.Minimized)) throw new InvalidOperationException("Start at least two restored disposable fixture windows first.");
            await Act(DockClickAction.Nothing);
            Check(Fixtures().Count == initial.Count && Fixtures().All(w => !w.Minimized), "Nothing leaves windows unchanged");
            await Act(DockClickAction.Minimize);
            Check(Fixtures().Count(w => w.Minimized) == 1, "Minimize affects one grouped member");
            await Act(DockClickAction.BringToFront);
            Check(Fixtures().All(w => !w.Minimized) && Fixtures().Any(w => w.Handle == Native.GetForegroundWindow()), "Bring to Front restores and activates");
            await Act(DockClickAction.BringToFront);
            Check(Fixtures().All(w => !w.Minimized), "Bring to Front keeps an active task visible");
            var active = Native.GetForegroundWindow();
            await Act(DockClickAction.BringToFrontOrMinimize, active);
            Check(Fixtures().Single(w => w.Handle == active).Minimized, "Toggle minimizes the active group member");
            await Act(DockClickAction.BringToFrontOrMinimize, active);
            Check(Fixtures().All(w => !w.Minimized), "Toggle restores a minimized task");
            Settings.GroupWindows = false; ApplySettings();
            await Act(DockClickAction.MinimizeSimilar);
            Check(Fixtures().All(w => w.Minimized), "Minimize Similar includes separately displayed tasks");
            await Act(DockClickAction.RestoreSimilar);
            Check(Fixtures().All(w => !w.Minimized), "Restore Similar restores all matching tasks");
            await Act(DockClickAction.Close);
            Check(Fixtures().Count == initial.Count - 1, "Close requests one normal window close");
            await Act(DockClickAction.CloseSimilar);
            Check(Fixtures().Count == 0, "Close Similar closes matching fixture windows only");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            Settings.GroupWindows = originalGrouping; ApplySettings();
            File.WriteAllText(Path.Combine(Log.DirectoryPath, "click-actions-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Checks = results.Count, Error = failure?.ToString(), Results = results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }
    // Native render regression: core-only tests cannot detect XAML's delayed
    // RO_E_CLOSED failure when reattaching cached SoftwareBitmapSources.
    private async Task CheckIconResize()
    {
        if (iconResizeCheckRunning) return;
        iconResizeCheckRunning = true;
        var original = (Settings.IconSize, Settings.MagnifiedSize, Settings.IconSpacing);
        var results = new List<object>();
        Exception? failure = null;
        try
        {
            for (int cycle = 0; cycle < 5; cycle++)
            {
                foreach (var preset in new[] { (56, 80, 10), (40, 64, 8), (32, 48, 6), (56, 80, 10), (32, 48, 6), (40, 64, 8) })
                {
                    if (closing) throw new OperationCanceledException("Dock closed during resize check.");
                    (Settings.IconSize, Settings.MagnifiedSize, Settings.IconSpacing) = preset;
                    ApplySettings();
                    await Task.Delay(250);
                    Reposition(false);
                    await Task.Delay(100);
                    var rasters = buttons.Select(b => b.Icon is Grid grid ? IconService.Describe(grid.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().FirstOrDefault()?.Source) : null).Where(r => r != null).ToArray();
                    if (rasters.Length == 0) throw new InvalidOperationException("No raster icons rendered during resize check.");
                    root.UpdateLayout();
                    var margins = buttons.Select(b =>
                    {
                        var point = b.Button.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point());
                        double leading = Vertical ? point.X : point.Y;
                        double totalThickness = CurrentDockThickness + (HoverEnvelope ? HoverEnvelopeOverflow : 0);
                        double trailing = totalThickness - leading - (Vertical ? b.Button.ActualWidth : b.Button.ActualHeight);
                        double? highlightLeading = null, highlightTrailing = null;
                        if (taskbarHighlights.TryGetValue(b.Button, out var tile))
                        {
                            var visibility = tile.Visibility;
                            tile.Visibility = Visibility.Visible;
                            root.UpdateLayout();
                            var tilePoint = tile.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point());
                            highlightLeading = Vertical ? tilePoint.X : tilePoint.Y;
                            highlightTrailing = totalThickness - highlightLeading - (Vertical ? tile.ActualWidth : tile.ActualHeight);
                            tile.Visibility = visibility;
                        }
                        double overflow = HoverEnvelope ? HoverEnvelopeOverflow : 0; double expectedLeading = Vertical ? MarginDip(Settings.DockMarginLeft) + (PositionEdge == DockEdge.Right ? overflow : 0) : MarginDip(Settings.DockMarginTop) + overflow;
                        double expectedTrailing = Vertical ? MarginDip(Settings.DockMarginRight) + (PositionEdge == DockEdge.Left ? overflow : 0) : MarginDip(Settings.DockMarginBottom);
                        if (Math.Abs(leading - expectedLeading) > 1 || Math.Abs(trailing - expectedTrailing) > 1 || (highlightLeading.HasValue && (Math.Abs(highlightLeading.Value - expectedLeading) > 1 || Math.Abs(highlightTrailing!.Value - expectedTrailing) > 1)))
                            throw new InvalidOperationException($"Incorrect dock margins for {b.Entry.Item.Name}: {leading}/{trailing}, expected {expectedLeading}/{expectedTrailing}; highlight {highlightLeading}/{highlightTrailing}.");
                        return new { b.Entry.Item.Name, Leading = leading, Trailing = trailing, HighlightLeading = highlightLeading, HighlightTrailing = highlightTrailing };
                    }).ToArray();
                    var separators = strip.Children.OfType<Border>().Where(b => b.Tag is DockEntry entry && entry.Item.Kind == ItemKind.Separator).Select(b =>
                    {
                        var point = b.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point());
                        double leading = Vertical ? point.X : point.Y;
                        double length = Vertical ? b.ActualWidth : b.ActualHeight;
                        double totalThickness = CurrentDockThickness + (HoverEnvelope ? HoverEnvelopeOverflow : 0);
                        double trailing = totalThickness - leading - length;
                        double baseMargin = CurrentDockThickness * .15;
                        double overflow = HoverEnvelope ? HoverEnvelopeOverflow : 0; double expectedLeading = baseMargin + (Vertical && PositionEdge == DockEdge.Right ? overflow : !Vertical ? overflow : 0);
                        double expectedTrailing = baseMargin + (Vertical && PositionEdge == DockEdge.Left ? overflow : 0);
                        // XAML rounds length and placement separately to whole pixels.
                        if (Math.Abs(leading - expectedLeading) > 1 || Math.Abs(trailing - expectedTrailing) > 1)
                            throw new InvalidOperationException($"Incorrect separator margins: {leading}/{trailing}, expected {expectedLeading}/{expectedTrailing}.");
                        return new { Leading = leading, Trailing = trailing, Length = length, ExpectedLeading = expectedLeading, ExpectedTrailing = expectedTrailing };
                    }).ToArray();
                    results.Add(new { Cycle = cycle, IconSize = preset.Item1, Margins = margins, Separators = separators, Rasters = rasters });
                }
                // Reuse cached images after collection as well as after resize.
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            }
            await Task.Delay(1000);
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            (Settings.IconSize, Settings.MagnifiedSize, Settings.IconSpacing) = original;
            if (!closing) ApplySettings();
            iconResizeCheckRunning = false;
            File.WriteAllText(Path.Combine(Log.DirectoryPath, "icon-resize-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Transitions = results.Count, Error = failure?.ToString(), Results = results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct DockStyleChange { public uint OldStyle, NewStyle; }
    [StructLayout(LayoutKind.Sequential)]
    private struct DockWindowPosition { public nint Window, InsertAfter; public int X, Y, Width, Height; public uint Flags; }
    private nint WindowMessage(nint hwnd, uint msg, nuint wp, nint lp, nuint id, nuint data)
    {
        if (msg == 0x0084 && HasIconBounces && !hidden && !dragging && !rowAnimating && popupCount == 0)
        {
            var cursor = new Native.POINT { X = (short)(lp.ToInt64() & 0xffff), Y = (short)((lp.ToInt64() >> 16) & 0xffff) };
            if (!HoverContains(cursor) && !BouncingArtworkContains(cursor)) return -1;
        }
        // Reject shell state changes before DWM can minimize the icon surface.
        // Recovering after WM_SIZE is too late and exposes the background alone.
        if (!closing && msg == 0x007C && unchecked((int)wp) == -16 && lp != 0)
        {
            var change = Marshal.PtrToStructure<DockStyleChange>(lp);
            change.NewStyle &= ~(0x20000000u | 0x01000000u);
            Marshal.StructureToPtr(change, lp, false);
        }
        if (initialized && !closing && !positioning && !visibilitySliding && msg == 0x0046 && lp != 0)
        {
            var position = Marshal.PtrToStructure<DockWindowPosition>(lp);
            position.Flags = (position.Flags & ~0x0080u) | 0x0001u | 0x0002u;
            Marshal.StructureToPtr(position, lp, false);
        }
        // The dock is a fixed shell surface. Block every standard route that
        // can minimize, maximize, restore, move, or resize it, including
        // keyboard shortcuts and touchpad/window-manager gestures.
        if (msg == 0x0112 && ((uint)wp & 0xFFF0) is 0xF000 or 0xF010 or 0xF020 or 0xF030 or 0xF120)
        {
            DispatcherQueue.TryEnqueue(() => { if (!closing) Reposition(false); });
            return 0;
        }
        // Shell-wide minimize commands can bypass WM_SYSCOMMAND. Restore the
        // fixed dock immediately instead of leaving an empty compositor host.
        if (msg == 0x0005 && wp == 1)
        {
            DispatcherQueue.TryEnqueue(EnsureDockIsNotMinimized);
            return 0;
        }
        // WM_ACTIVATE supplies the window losing activation before WinUI
        // delivers pointer events. Unlike an out-of-context WinEvent hook,
        // this cannot arrive too late for the click that focused the dock.
        if (msg == 0x0006 && (wp & 0xffff) != 0)
        {
            
            tracker.RecordForeground(lp);
        }
        if (msg == shellMessage && wp == 5 && Settings.RedirectWindowAnimations && initialized && !closing && lp != 0 && Native.ReadShellInfo(lp, out var info))
        {
            var target = App.AnimationDock(info.Window, this).AnimationTarget(info.Window);
            if (target is { } rect)
            {
                var encoded = new Native.RECT(rect);
                // Some Windows 11 shell implementations exchange two packed
                // signed POINTS in the first two RECT fields.
                if (info.Rect.Right == 0 && info.Rect.Bottom == 0)
                { encoded.Left = (rect.Left & 0xffff) | (rect.Top << 16); encoded.Top = (rect.Right & 0xffff) | (rect.Bottom << 16); encoded.Right = 0; encoded.Bottom = 0; }
                if (Native.WriteShellRect(lp, encoded)) { animationRectRequests++; lastAnimationTarget = rect; return 1; }
            }
        }
        if (msg == 0x001C && wp == 0) DispatcherQueue.TryEnqueue(() => trayMenu?.Hide());
        if (msg == 0x0014 && Native.ClearCompositedClient(hwnd, (nint)wp)) return 1;
        if (msg == 0x031E) Native.ConfigureCompositedClient(hwnd);
        // Appbar work-area updates are layout changes, not icon theme changes.
        if (msg == 0x0219) DispatcherQueue.TryEnqueue(() => { if (!closing) _ = ScanUsbPins(); });
        if (msg == 0x001A && wp != 0x002F) DispatcherQueue.TryEnqueue(UpdateTheme);
        if (msg == 0x031A) DispatcherQueue.TryEnqueue(() => { renderedIconTheme = ""; UpdateTheme(); });
        if (msg == shellMessage && wp == 0x8006) DispatcherQueue.TryEnqueue(() => AttentionRequested(lp));
        if (msg == 0x0020 && internalDragId != null) { SetDragCursor(dragDropAllowed); return 1; }
        if (msg == 0x0312) { HandleCommand("--toggle"); return 0; }
        if (msg == 0x0047 && initialized && !closing && !positioning && !visibilitySliding && Native.Bounds(Handle) != hostBounds)
            DispatcherQueue.TryEnqueue(() => { if (!closing && !positioning && !visibilitySliding && Native.Bounds(Handle) != hostBounds) Reposition(false); });
        if (msg == 0x0047 && initialized && !closing && requestedTaskViewBand != orderedSurfaceBand) DispatcherQueue.TryEnqueue(SyncSurfaceBandOrder);
        if (msg == explorerRestart) DispatcherQueue.TryEnqueue(() => { minimizedTabs.ExplorerRestarted(); appbar.Dispose(); Native.DeregisterShellHookWindow(Handle); Native.RegisterShellHookWindow(Handle); Reposition(true); Reconcile(); });
        else if (msg == 0x007E || msg == 0x02E0 || (msg == appbar.Callback && wp == 1)) DispatcherQueue.TryEnqueue(() => { if (!positioning) Reposition(true); });
        if (msg == 0x0021 && popupCount == 0) {  tracker.ObserveForeground(); return ((lp.ToInt64() >> 16) & 0xffff) == 0x0204 ? 1 : 3; } // Activate for a right-click menu; task clicks remain non-activating.
        return Native.DefSubclassProc(hwnd, msg, wp, lp);
    }
    private PixelRect? AnimationTarget(nint window)
    {
        var task = tracker.Describe(window);
        int index = DockAnimationTarget.FindIndex(buttons.Select(b => b.Entry).ToArray(), window, task);
        var match = index >= 0 ? buttons[index] : default;
        if (match.Button == null || match.Button.ActualWidth <= 0) return task != null && MinimizedOnly ? PendingMinimizeTarget(task) : null;
        var point = match.Button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        int x = (hidden ? resting.Left : hostBounds.Left) + DockGeometry.Pixels(point.X, scale), y = (hidden ? resting.Top : hostBounds.Top) + DockGeometry.Pixels(point.Y, scale);
        int size = DockGeometry.Pixels(ButtonSize, scale);
        // Hidden docks keep the same animation destination as their resting icons.
        if (!Vertical) y = resting.Top + Settings.DockMarginTop;
        else x = resting.Left + Settings.DockMarginLeft;
        x = Math.Clamp(x, resting.Left, Math.Max(resting.Left, resting.Right - size));
        y = Math.Clamp(y, resting.Top, Math.Max(resting.Top, resting.Bottom - size));
        return new(x, y, x + size, y + size);
    }
    private void TrackFlyout(FlyoutBase flyout, bool preserveMagnification = false)
    {
        if (flyout is MenuFlyout menu) StyleMenu(menu);
        flyout.Opening += (_, _) =>
        {
            if (openingFlyouts.Add(flyout)) popupCount++; StartFlyoutDismissWatch();
            if (flyout is MenuFlyout || preserveMagnification) { hoverMenuHeld = true; StopHoverRendering(); }
            else ResetMagnification();
        };
        flyout.Opened += (_, _) => UpdateOpenPopupBorders();
        flyout.Closed += (_, _) =>
        {
            if (openingFlyouts.Remove(flyout)) popupCount = Math.Max(0, popupCount - 1);
            if (popupCount == 0)
            {
                hoverMenuHeld = false;
                hoverClickHeld = false;
                if (Native.GetCursorPos(out var cursor) && visibleBounds.Contains(cursor.X, cursor.Y) && hoverWave != null)
                { hoverEngaged = true; StartHoverRendering(); }
                else ResetMagnification(immediate: false);
            }
            DispatcherQueue.TryEnqueue(Reconcile);
        };
    }
    private MenuFlyout BackgroundMenu(bool fromTray = false)
    {
        var menu = new MenuFlyout(); TrackFlyout(menu);
        var addNewItem = new MenuFlyoutSubItem { Text = "Add new item", Icon = Glyph("\uE710") };
        SubMenu(addNewItem, "New file", () => PickItem(false));
        SubMenu(addNewItem, "New folder", () => PickItem(true));
        SubMenu(addNewItem, "New shortcut", ShowNewShortcut);
        SubMenu(addNewItem, "New separator", () => AddItem(new() { Kind = ItemKind.Separator, Name = "Separator" }, backgroundMenuInsertionSlot ?? PinInsertionIndex()));
        addNewItem.Items.Add(new MenuFlyoutSeparator());
        var modules = new MenuFlyoutSubItem { Text = "Modules", Icon = Glyph("\uE950") };
        var hasRecycleBin = Settings.Items.Any(item => item.Kind == ItemKind.RecycleBin);
        var hasDownloads = Settings.Items.Any(IsDownloads);
        var hasStartMenu = Settings.Items.Any(item => item.Kind == ItemKind.StartMenu);
        var downloads = new MenuFlyoutItem { Text = "Downloads", IsEnabled = !hasDownloads, Icon = Glyph("\uE896") };
        downloads.Click += (_, _) => AddItem(new() { Kind = ItemKind.Folder, Name = "Downloads", Target = Native.Downloads(), Folder = new() { Sort = FolderSort.Modified, Descending = true } });
        modules.Items.Add(downloads);
        var recycleBin = new MenuFlyoutItem { Text = "Recycle Bin", IsEnabled = !hasRecycleBin, Icon = Glyph("\uE74D") };
        recycleBin.Click += (_, _) => AddItem(new() { Kind = ItemKind.RecycleBin, Name = "Recycle Bin" });
        modules.Items.Add(recycleBin);
        var startMenu = new MenuFlyoutItem { Text = "Start Menu", IsEnabled = !hasStartMenu, Icon = Glyph("\uE8FC") };
        startMenu.Click += (_, _) => AddItem(new() { Kind = ItemKind.StartMenu, Name = "Start Menu", CustomIcon = BundledIcons.Start });
        modules.Items.Add(startMenu);
        foreach (var module in new[] { (ItemKind.TaskView, "Task View", "\uE7C4"), (ItemKind.Search, "Search", "\uE721") })
        {
            var command = new MenuFlyoutItem { Text = module.Item2, Icon = Glyph(module.Item3), IsEnabled = !Settings.Items.Any(i => i.Kind == module.Item1) };
            command.Click += (_, _) => AddItem(new() { Kind = module.Item1, Name = module.Item2 });
            modules.Items.Add(command);
        }
        addNewItem.Items.Add(modules);
        menu.Items.Add(addNewItem);
        var position = new MenuFlyoutSubItem { Text = "Dock position" };
        var choices = new List<(DockEdge Edge, ToggleMenuFlyoutItem Item)>();
        var alignChoices = new List<(DockAlignment Alignment, ToggleMenuFlyoutItem Item)>();
        DockEdge? pendingPosition = null;
        DockAlignment? pendingAlignment = null;
        var align = new MenuFlyoutSubItem { Text = "Align to" };
        foreach (var edge in PositionChoices)
        {
            var item = new ToggleMenuFlyoutItem { Text = edge.ToString(), IsChecked = PositionEdge == edge };
            item.Click += (_, _) => { pendingPosition = edge; RefreshPositionChoices(); };
            choices.Add((edge, item)); position.Items.Add(item);
        }
        position.Items.Add(new MenuFlyoutSeparator());
        position.Items.Add(align);
        foreach (var alignment in Enum.GetValues<DockAlignment>())
        {
            var item = new ToggleMenuFlyoutItem();
            item.Click += (_, _) => pendingAlignment = alignment;
            alignChoices.Add((alignment, item)); align.Items.Add(item);
        }
        void RefreshPositionChoices()
        {
            var edge = pendingPosition ?? PositionEdge;
            foreach (var choice in choices) choice.Item.IsChecked = choice.Edge == edge;
            foreach (var choice in alignChoices)
            {
                choice.Item.Text = edge is DockEdge.Bottom or DockEdge.Top
                    ? choice.Alignment switch { DockAlignment.Start => "Left", DockAlignment.Center => "Center", _ => "Right" }
                    : choice.Alignment switch { DockAlignment.Start => "Top", DockAlignment.Center => "Center", _ => "Bottom" };
                choice.Item.IsChecked = choice.Alignment == (pendingAlignment ?? PositionAlignment);
            }
        }
        menu.Opening += (_, _) => { pendingPosition = null; pendingAlignment = null; RefreshPositionChoices(); };
        // Apply after dismissal so moving the owner cannot displace an open menu.
        menu.Closed += (_, _) =>
        {
            if (closing || pendingPosition == null && pendingAlignment == null) return;
            if (pendingPosition is { } edge) PositionEdge = edge;
            if (pendingAlignment is { } alignment) PositionAlignment = alignment;
            ApplySettings();
        };
        menu.Items.Add(position);
        menu.Items.Add(new MenuFlyoutSeparator());
        if (!fromTray)
        {
            Menu(menu, DisplayHideMode == HideMode.AlwaysVisible ? "Enable hiding" : "Disable hiding", () =>
            {
                DisplayHideMode = DisplayHideMode == HideMode.AlwaysVisible ? HideMode.AfterDelay : HideMode.AlwaysVisible;
                previousHideMode = DisplayHideMode;
                edgeToggleHidden = hidden;
                edgeActivation.Reset();
                hideSince = revealSince = manualHideSince = null;
                if (DisplayHideMode == HideMode.AlwaysVisible)
                {
                    startupWaiting = false;
                    manuallyHidden = false;
                }
                Save();
                // Keep the current hover scene until TrackFlyout releases it smoothly.
                // Visibility changes use ShowDock/HideDock's normal slide animation.
                Reposition(true);
                EvaluateVisibility();
            });
            Menu(menu, DisplayMagnificationEnabled ? "Disable magnification" : "Enable magnification", () =>
            {
                DisplayMagnificationEnabled = !DisplayMagnificationEnabled;
                Settings.ShowIconHighlights = !DisplayMagnificationEnabled;
                ApplySettings();
            });
            ((MenuFlyoutItem)menu.Items[^1]).IsEnabled = !Settings.ReduceMotion;
            Menu(menu, Settings.LockIconPositions ? "Unlock items" : "Lock items", () =>
            {
                Settings.LockIconPositions = !Settings.LockIconPositions;
                // Drag permission is read live; rebuilding would snap the held hover wave.
                Save();
            });
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        AddDockSettingsMenu(menu);
        if (fromTray || Settings.ShowDockSettingsInItemMenus) Menu(menu, "Exit", () => App.Dock?.Close(), "\uE711");
        return menu;
    }
    private MenuFlyout ItemMenu(DockEntry entry)
    {
        if (entry.Windows.Count > 0)
        {
            var members = entry.Windows.Select(w => stressTasks?.FirstOrDefault(task => task.Handle == w.Handle) ?? tracker.Describe(w.Handle)).OfType<TaskWindow>().ToArray();
            entry = entry with { Window = members.FirstOrDefault(), Group = members };
        }
        if (!entry.IsPinned && entry.Windows.Count == 1 && entry.Window is { } additionalWindow
            && !ShellService.IsSystemSettings(entry.Item) && !ShellService.IsTaskManager(entry.Item)
            && Settings.Items.Any(pin => TaskReconciler.Matches(pin, additionalWindow)))
        {
            var windowMenu = new MenuFlyout();
            TrackFlyout(windowMenu);
            AddQuotedWindowAction(windowMenu, additionalWindow);
            Menu(windowMenu, "Close window", () => ShellService.Close(additionalWindow), "\uE624");
            return windowMenu;
        }
        bool terminal = ShellService.IsTerminal(entry.Item);
        bool singleton = ShellService.OnlyAlwaysOnTopOptions(entry.Item);
        if (entry.IsPinned || terminal || singleton)
        {
            var matching = (stressTasks ?? tracker.Read()).Where(w => TaskReconciler.Matches(entry.Item, w)).ToArray();
            if (entry.IsPinned || matching.Length > 0) entry = entry with { Window = matching.FirstOrDefault(), Group = matching };
        }
        if (IsFileExplorer(entry.Item))
        {
            // Explorer also owns the desktop. Only folder windows count as
            // running instances for this menu, including minimized windows.
            var windows = tracker.Read().Where(w => IsFileExplorer(new DockItem { ExecutablePath = w.ExecutablePath })
                && Native.Class(w.Handle) is "CabinetWClass" or "ExploreWClass").ToArray();
            entry = entry with { Window = windows.FirstOrDefault(), Group = windows };
        }
        var menu = new MenuFlyout(); TrackFlyout(menu);
        if (entry.Item.IsDownloadedPin)
        {
            Menu(menu, "Open", () => LaunchMenuItem(entry.Item), "\uE737");
            var folderLabel = entry.Item.CapturePin switch { CapturePinKind.Recording => "Open Screen recordings folder", CapturePinKind.Screenshot => "Open Screenshots folder", _ => "Open Downloads folder" };
            Menu(menu, folderLabel, () => TryAction(() => ShellService.Open(entry.Item.CapturePin == CapturePinKind.None ? Native.Downloads() : Path.GetDirectoryName(entry.Item.Target)!)), "\uEC50");
            Menu(menu, "Unpin from dock", () => { UnpinItem(entry.Item); }, "\uE77A");
            return menu;
        }
        if (entry.Item.Kind == ItemKind.Separator)
        {
            Menu(menu, "Remove separator", () => { UnpinItem(entry.Item); }, "\uE77A");
            menu.Items.Add(new MenuFlyoutSeparator());
            AddDockSettingsMenu(menu);
            return menu;
        }
        var hasRunningWindow = entry.Window != null;
        bool systemSettings = ShellService.IsSystemSettings(entry.Item);
        bool edgeBrowser = ShellService.IsMicrosoftEdge(entry.Item);
        if (!edgeBrowser) AddAdditionalCommands(menu, entry.Item);
        bool administrator = ShellService.SupportsAdministratorLaunch(entry.Item) && entry.Item.Kind == ItemKind.Application && entry.Item.RunAsAdministrator;
        string openLabel = administrator ? "Open" : hasRunningWindow ? "Open new window" : "Open";
        string openGlyph = administrator ? "\uE7EF" : "\uE737";
        if (entry.Item.Kind == ItemKind.StartMenu) { openLabel = "Show Start Menu"; openGlyph = "\uE8FC"; }
        else if (entry.Item.Kind == ItemKind.Search) { openLabel = "Show Search"; openGlyph = "\uE721"; }
        else if (entry.Item.Kind == ItemKind.TaskView) { openLabel = "Show Task view"; openGlyph = "\uE7C4"; }
        else if (ShellService.IsMicrosoftEdge(entry.Item) && !hasRunningWindow) { openLabel = "Open Edge"; }
        else if (systemSettings) { openLabel = "Open System Settings"; openGlyph = "\uE713"; }
        else if (ShellService.IsTaskManager(entry.Item)) { openLabel = "Open Task Manager"; openGlyph = "\uF120"; }
        else if (ShellService.IsMicrosoftStore(entry.Item)) { openLabel = "Open Microsoft Store"; openGlyph = "\uEA04"; }
        else if (terminal) { openLabel = "Open Terminal"; openGlyph = administrator ? "\uE7EF" : "\uF1FD"; }
        else if (!hasRunningWindow && IsFileExplorer(entry.Item)) { openLabel = "Open File Explorer"; openGlyph = "\uEC50"; }
        if (singleton && entry.Windows.Any(w => !Native.IsIconic(w.Handle))) openLabel = "Minimize " + openLabel[5..];
        bool showWindowList = !systemSettings && !ShellService.IsTaskManager(entry.Item);
        if (showWindowList && !entry.IsPinned && hasRunningWindow)
        {
            foreach (var window in entry.Windows) AddQuotedWindowAction(menu, window);
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        else if (showWindowList && entry.IsPinned && entry.Windows.Count > 0)
        {
            foreach (var window in entry.Windows) MenuWindow(menu, window, () => ShellService.Activate(window, false));
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        if (edgeBrowser) AddAdditionalCommands(menu, entry.Item);
        if (entry.IsPinned || !hasRunningWindow || !showWindowList)
        {
            if (!hasRunningWindow && entry.Item.Kind == ItemKind.Folder) Menu(menu, IsDownloads(entry.Item) ? "Open Downloads folder" : "Open", () => TryAction(() => { ShellService.Open(entry.Item.Target); BounceItem(entry.Item); }), "\uEC50");
            else if (terminal && hasRunningWindow)
            {
                Menu(menu, "Open in new window", () => OpenTerminal(entry.Item, true), "\uE737");
                Menu(menu, "Open in new tab", () => OpenTerminal(entry.Item, false), "\uEC6C");
            }
            else if (ShellService.IsXbox(entry.Item)) AddXboxOpen(menu, entry);
            else if (singleton)
            {
                Menu(menu, openLabel, () => InvokeSingletonMenu(entry), openGlyph);
                if (ShellService.IsMicrosoftStore(entry.Item))
                {
                    var storeIcon = (FontIcon)((MenuFlyoutItem)menu.Items[^1]).Icon;
                    storeIcon.FontFamily = storeFont;
                    storeIcon.FontSize = 20;
                    storeIcon.IsTextScaleFactorEnabled = false;
                    storeIcon.UseLayoutRounding = true;
                }
            }
            else
            {
                Menu(menu, openLabel, () => LaunchMenuItem(entry.Item), openGlyph);
                if (terminal && !administrator && ((MenuFlyoutItem)menu.Items[^1]).Icon is FontIcon terminalIcon)
                {
                    terminalIcon.FontFamily = new FontFamily("ms-appx:///assets/fonts/SettingsIcons.ttf#Settings Fluent Icons");
                    terminalIcon.IsTextScaleFactorEnabled = false;
                }
            }
        }
        if (!entry.IsPinned && terminal && hasRunningWindow)
            Menu(menu, "Open in new tab", () => OpenTerminal(entry.Item, false), "\uEC6C");
        if (hasRunningWindow && IsFileExplorer(entry.Item))
            Menu(menu, "Open new tab", () => OpenExplorerTab(), "\uEC6C");
        if (ShellService.IsMicrosoftEdge(entry.Item))
        {
            if (HasEdgeWindow) Menu(menu, "Open new tab", () => OpenEdgeTab(entry.Item), "\uEC6C");
        }
        else if (entry.IsPinned && entry.Item.Kind == ItemKind.Application && !IsFileExplorer(entry.Item) && ShellService.SupportsAdministratorLaunch(entry.Item))
        {
            if (IsElevated) Menu(menu, "Run as a user", () => TryAction(() => ShellService.LaunchWithoutElevation(entry.Item)), "\uE7EE");
            else if (administrator) Menu(menu, "Open without elevation", () => TryAction(() => ShellService.LaunchWithoutElevation(entry.Item)), terminal ? "\uE756" : "\uE737");
            else Menu(menu, "Run as administrator", () => TryAction(() => ShellService.LaunchAsAdministrator(entry.Item)), "\uE7EF");
        }
        if (entry.Item.Kind == ItemKind.RecycleBin)
        {
            var empty = new MenuFlyoutItem { Text = "Empty Recycle Bin", Icon = Glyph("\uE74D"), IsEnabled = ShellService.RecycleCount() > 0 };
            empty.Click += (_, _) => TryAction(() =>
            {
                var before = ShellService.RecycleCount();
                // Suppress the shell sound so the selected sound plays only once.
                var result = Native.SHEmptyRecycleBin(Handle, null, (entry.Item.ConfirmRecycleBinEmpty ? 0u : 1u) | 4u);
                if (result == 0 && before > ShellService.RecycleCount() && entry.Item.PlayRecycleBinSounds)
                    ShellService.PlayRecycleSound(empty: true, entry.Item.RecycleBinSoundVolumePercent);
                UpdateRecycle();
            });
            menu.Items.Add(empty);
        }
        if (entry.Item.IsAutoUsbPin)
            Menu(menu, "Eject", () => TryAction(() => ShellService.EjectDrive(entry.Item.Target)), "\uF847");
        bool permanentModule = IsFileExplorer(entry.Item);
        if (!entry.IsPinned) Menu(menu, "Pin to dock", () => AddItem(entry.Item with { Id = Guid.NewGuid().ToString("N"), ClickActions = null }, PinInsertionIndex()), "\uE718");
        if (entry.IsPinned)
        {
            if (hasRunningWindow)
                Menu(menu, "Unpin from dock", () => { UnpinItem(entry.Item); }, "\uE77A");
            if (hasRunningWindow)
            {
                if (!permanentModule) Menu(menu, "End task", () => ShellService.EndTask(entry.Window!), "\uF140");
                Menu(menu, "Close window", () => ShellService.Close(entry.Window!), "\uE624");
                menu.Items.Add(new MenuFlyoutSeparator());
                AddWindowOptions(menu, entry);
                menu.Items.Add(new MenuFlyoutSeparator());
            }
            if (entry.Item.Kind == ItemKind.File)
                Menu(menu, entry.Item.ShowThumbnail || entry.Item.Folder.StackIcon ? "Show as icon" : "Show as thumbnail", () => { entry.Item.ShowThumbnail = !(entry.Item.ShowThumbnail || entry.Item.Folder.StackIcon); entry.Item.Folder.StackIcon = false; ApplySettings(); }, "\uE91B");
            if (!hasRunningWindow)
            {
                Menu(menu, "Unpin from dock", () => { UnpinItem(entry.Item); }, "\uE77A");
                menu.Items.Add(new MenuFlyoutSeparator());
                if (entry.Item.Kind == ItemKind.Folder)
                {
                    AddDownloadsOptions(menu, entry.Item);
                    menu.Items.Add(new MenuFlyoutSeparator());
                }
                else if (HasPinnedItemOptions(entry.Item))
                {
                    AddPinnedItemOptions(menu, entry.Item);
                    menu.Items.Add(new MenuFlyoutSeparator());
                }
            }
            if (!entry.Item.IsAutoUsbPin)
                Menu(menu, "Properties", () => EditItem(entry.Item), "\uE90F");
        }
        else if (hasRunningWindow)
        {
            if (!permanentModule) Menu(menu, "End task", () => ShellService.EndTask(entry.Window!), "\uF140");
            Menu(menu, "Close window", () => ShellService.Close(entry.Window!), "\uE624");
            menu.Items.Add(new MenuFlyoutSeparator());
            AddWindowOptions(menu, entry);
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        if (!hasRunningWindow && !entry.IsPinned) menu.Items.Add(new MenuFlyoutSeparator());
        if (Settings.ShowDockSettingsInItemMenus) AddDockSettingsMenu(menu);
        else while (menu.Items.LastOrDefault() is MenuFlyoutSeparator) menu.Items.RemoveAt(menu.Items.Count - 1);
        return menu;
    }
    internal static FontIcon Glyph(string glyph, double fontSize = 16) => new() { Glyph = glyph, FontSize = fontSize };
    private static void Menu(MenuFlyout flyout, string text, Action action, string? glyph = null, double glyphSize = 16) { var item = new MenuFlyoutItem { Text = text, Icon = glyph == null ? null : Glyph(glyph, glyphSize) }; item.Click += (_, _) => action(); flyout.Items.Add(item); }
    private static void SubMenu(MenuFlyoutSubItem flyout, string text, Action action, string? glyph = null) { var item = new MenuFlyoutItem { Text = text, Icon = glyph == null ? null : Glyph(glyph) }; item.Click += (_, _) => action(); flyout.Items.Add(item); }
    private void AddWindowOptions(MenuFlyout menu, DockEntry entry)
    {
        var options = new MenuFlyoutSubItem { Text = "Options" };
        var windows = entry.Windows.Where(w => Native.IsWindow(w.Handle)).ToArray();
        if (ShellService.IsXbox(entry.Item) && windows.Length == 0) return;
        if (entry.IsPinned && !ShellService.OnlyAlwaysOnTopOptions(entry.Item) && !ShellService.IsXbox(entry.Item))
        {
            SubMenu(options, "Restore all windows", () => { foreach (var window in entry.Windows) ShellService.Activate(window, false); });
            SubMenu(options, "Minimize all windows", () => { foreach (var window in entry.Windows) ShellService.Activate(window, true); });
            if (windows.Length > 0)
                SubMenu(options, "Close all windows", () => { foreach (var window in windows.Where(w => Native.IsWindow(w.Handle))) ShellService.Close(window); });
            options.Items.Add(new MenuFlyoutSeparator());
        }
        bool IsTopmost(TaskWindow window) => (Native.GetWindowLongPtrW(window.Handle, -20).ToInt64() & 8) != 0;
        var alwaysOnTop = new ToggleMenuFlyoutItem
        {
            Text = "Always on top",
            IsEnabled = windows.Length > 0,
            IsChecked = windows.Length > 0 && windows.All(IsTopmost)
        };
        alwaysOnTop.Click += (_, _) => TryAction(() =>
        {
            bool enabled = alwaysOnTop.IsChecked;
            foreach (var window in windows.Where(w => Native.IsWindow(w.Handle)))
                Native.SetWindowPos(window.Handle, enabled ? (nint)(-1) : (nint)(-2), 0, 0, 0, 0, 0x13);
            alwaysOnTop.IsChecked = windows.Any(w => Native.IsWindow(w.Handle))
                && windows.Where(w => Native.IsWindow(w.Handle)).All(IsTopmost);
        });
        options.Items.Add(alwaysOnTop);
        if (IsFileExplorer(entry.Item))
            SubMenu(options, "File Explorer options", () => TryAction(() => ShellService.Open("shell:::{6DFD7C5C-2451-11d3-A299-00C04F8EF6AF}")));
        if (!IsFileExplorer(entry.Item) && !ShellService.OnlyAlwaysOnTopOptions(entry.Item) && !ShellService.IsTerminal(entry.Item) && !ShellService.IsXbox(entry.Item))
            SubMenu(options, "Open file location", () => ShellService.ShowInExplorer(entry.Window!));
        menu.Items.Add(options);
    }
    private static void AddPinnedItemOptions(MenuFlyout menu, DockItem item)
    {
        var options = new MenuFlyoutSubItem { Text = "Options" };
        SubMenu(options, "Open file location", () => ShellService.ShowInExplorer(item));
        menu.Items.Add(options);
    }
    private void AddDownloadsOptions(MenuFlyout menu, DockItem item)
    {
        var options = new MenuFlyoutSubItem { Text = "Options" };
        bool stack = item.ShowThumbnail || item.Folder.StackIcon;
        options.Items.Add(new MenuFlyoutItem { Text = "Sort by", IsEnabled = false });
        foreach (var sort in new[] { FolderSort.Name, FolderSort.Size, FolderSort.Modified, FolderSort.Type })
        {
            string label = sort switch { FolderSort.Modified => "Date modified", _ => sort.ToString() };
            var choice = new ToggleMenuFlyoutItem { Text = label, IsChecked = item.Folder.Sort == sort };
            choice.Click += (_, _) => { item.Folder.Sort = sort; Save(); };
            options.Items.Add(choice);
        }
        options.Items.Add(new MenuFlyoutSeparator());
        options.Items.Add(new MenuFlyoutItem { Text = "Show content as", IsEnabled = false });
        var grid = new ToggleMenuFlyoutItem { Text = "Grid", IsChecked = item.Folder.View == FolderView.Grid };
        grid.Click += (_, _) => { item.Folder.View = FolderView.Grid; Save(); PulseItem(item); };
        options.Items.Add(grid);
        var list = new ToggleMenuFlyoutItem { Text = "List", IsChecked = item.Folder.View == FolderView.List };
        list.Click += (_, _) => { item.Folder.View = FolderView.List; Save(); PulseItem(item); };
        options.Items.Add(list);
        var nativeMenu = new ToggleMenuFlyoutItem { Text = "Menu", IsChecked = item.Folder.View == FolderView.Menu };
        nativeMenu.Click += (_, _) => { item.Folder.View = FolderView.Menu; Save(); PulseItem(item); };
        options.Items.Add(nativeMenu);
        if (!item.IsAutoUsbPin)
        {
            options.Items.Add(new MenuFlyoutSeparator());
            options.Items.Add(new MenuFlyoutItem { Text = "Display as", IsEnabled = false });
            var displayFolder = new ToggleMenuFlyoutItem { Text = "Folder", IsChecked = !stack };
            displayFolder.Click += (_, _) => { item.ShowThumbnail = false; item.Folder.StackIcon = false; _ = ApplyDownloadsDisplayAfterDismissal(); };
            var displayStack = new ToggleMenuFlyoutItem { Text = "Stack", IsChecked = stack };
            displayStack.Click += (_, _) => { item.ShowThumbnail = true; item.Folder.StackIcon = false; _ = ApplyDownloadsDisplayAfterDismissal(); };
            options.Items.Add(displayFolder); options.Items.Add(displayStack);
        }
        menu.Items.Add(options);
    }
    private async Task ApplyDownloadsDisplayAfterDismissal()
    {
        Save();
        // Keep the existing artwork alive until menu dismissal and the
        // hover-out transition finish; ApplySettings rebuilds that artwork.
        while (!closing && (popupCount > 0 || hoverWave != null)) await Task.Delay(16);
        if (!closing) ApplySettings();
    }
    private void PulseItem(DockItem item)
    {
        var button = buttons.FirstOrDefault(b => b.Entry.Item.Id == item.Id);
        if (button.Icon != null) PulseActivatedItem(button.Icon);
    }
    private void BounceItem(DockItem item)
    {
        var button = buttons.FirstOrDefault(b => b.Entry.Item.Id == item.Id);
        if (button.Icon != null) Bounce(button.Icon);
    }
    private void OpenExplorerTab()
    {
        var windows = tracker.Read().Where(w =>
            IsFileExplorer(new DockItem { ExecutablePath = w.ExecutablePath })
            && Native.Class(w.Handle) is "CabinetWClass" or "ExploreWClass");
        var target = tracker.MostRecentlyUsed(windows).FirstOrDefault();
        if (target == null) return;
        TryAction(() =>
        {
            var tab = Native.FindWindowExW(target.Handle, 0, "ShellTabWindowClass", null);
            if (tab == 0) throw new InvalidOperationException("This File Explorer window does not support tabs.");
            ShellService.Activate(target, false);
            // Address the selected Explorer tab directly, without sending
            // keyboard input to whichever application currently has focus.
            if (!Native.PostMessage(tab, 0x0111, 0xA21B, 0))
                throw new InvalidOperationException("File Explorer could not open a new tab.");
        });
    }
    private static bool HasPinnedItemOptions(DockItem item)
    {
        if (item.Kind is ItemKind.RecycleBin or ItemKind.StartMenu or ItemKind.TaskView or ItemKind.Search || IsFileExplorer(item) || ShellService.OnlyAlwaysOnTopOptions(item) || ShellService.IsTerminal(item) || ShellService.IsXbox(item)) return false;
        return !IsDownloads(item);
    }
    internal static bool IsFileExplorer(DockItem item) =>
        string.Equals(Path.GetFileName(item.ExecutablePath), "explorer.exe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(Path.GetFileName(item.Target), "explorer.exe", StringComparison.OrdinalIgnoreCase)
        || string.Equals(item.AppUserModelId, "Microsoft.Windows.Explorer", StringComparison.OrdinalIgnoreCase);
    internal static bool IsDownloads(DockItem item)
    {
        try { return string.Equals(Path.GetFullPath(item.Target), Path.GetFullPath(Native.Downloads()), StringComparison.OrdinalIgnoreCase); }
        catch { return string.Equals(item.Name, "Downloads", StringComparison.OrdinalIgnoreCase); }
    }
    private void MenuWindow(MenuFlyout flyout, TaskWindow window, Action action)
    {
        var icon = new ImageIcon();
        var item = new MenuFlyoutItem { Text = window.Title, Icon = icon };
        item.MaxWidth = Math.Min(560, AvailablePopupWidth);
        TextBlock? titleBlock = null;
        static TextBlock? FindTitle(DependencyObject parent, string title)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock text && text.Text == title) return text;
                if (FindTitle(child, title) is { } nested) return nested;
            }
            return null;
        }
        void UpdateTooltip() => ToolTipService.SetToolTip(item, titleBlock?.IsTextTrimmed == true ? window.Title : null);
        item.Loaded += (_, _) =>
        {
            titleBlock = FindTitle(item, window.Title);
            if (titleBlock == null) return;
            titleBlock.TextWrapping = TextWrapping.NoWrap;
            titleBlock.MaxLines = 1;
            titleBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            UpdateTooltip();
        };
        item.LayoutUpdated += (_, _) => UpdateTooltip();
        item.Click += (_, _) => action(); flyout.Items.Add(item);
        LoadMenuWindowIcon(icon, window);
    }
    private async void LoadMenuWindowIcon(ImageIcon icon, TaskWindow window)
    {
        Microsoft.UI.Xaml.Media.ImageSource? source = null;
        if (IsFileExplorer(new DockItem { ExecutablePath = window.ExecutablePath }))
        {
            var locations = await ExplorerMenuFolders();
            if (locations.TryGetValue(window.Handle, out var folder))
                source = await IconService.Load(folder, requestedPixels: IconPixels);
        }
        source ??= await IconService.LoadWindowIcon(window.Handle, IconPixels);
        if (source != null && Settings.ShowMenuGlyphs) icon.Source = source;
    }
    public void StyleMenu(MenuFlyout menu)
    {
        var style = new Style(typeof(MenuFlyoutPresenter));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, Math.Min(560d, AvailablePopupWidth)));
        menu.MenuFlyoutPresenterStyle = style;
        // Keep assigned icons for the next opening so the preference can hide
        // them without discarding future menu-icon assignments.
        var assigned = new Dictionary<MenuFlyoutItem, IconElement?>();
        var assignedSubItems = new Dictionary<MenuFlyoutSubItem, IconElement?>();
        menu.Opening += (_, _) =>
        {
            foreach (var item in MenuItems(menu.Items))
            {
                if (!assigned.ContainsKey(item)) assigned[item] = item.Icon;
                item.Icon = Settings.ShowMenuGlyphs ? assigned[item] : null;
            }
            foreach (var item in MenuSubItems(menu.Items))
            {
                if (!assignedSubItems.ContainsKey(item)) assignedSubItems[item] = item.Icon;
                item.Icon = Settings.ShowMenuGlyphs ? assignedSubItems[item] : null;
            }
            bool hasIcons = MenuItems(menu.Items).Any(i => i.Icon != null) || MenuSubItems(menu.Items).Any(i => i.Icon != null);
            foreach (var item in menu.Items)
            {
                if (!hasIcons)
                {
                    item.Resources["MenuFlyoutItemPlaceholderThemeThickness"] = new Thickness(0);
                    item.Resources["MenuFlyoutItemDoublePlaceholderThemeThickness"] = new Thickness(0);
                }
                else
                {
                    item.Resources.Remove("MenuFlyoutItemPlaceholderThemeThickness");
                    item.Resources.Remove("MenuFlyoutItemDoublePlaceholderThemeThickness");
                }
            }
        };
    }
    private static IEnumerable<MenuFlyoutItem> MenuItems(IList<MenuFlyoutItemBase> items)
    {
        foreach (var item in items)
        {
            if (item is MenuFlyoutItem menuItem) yield return menuItem;
            if (item is MenuFlyoutSubItem subItem)
                foreach (var nested in MenuItems(subItem.Items)) yield return nested;
        }
    }
    private static IEnumerable<MenuFlyoutSubItem> MenuSubItems(IList<MenuFlyoutItemBase> items)
    {
        foreach (var item in items.OfType<MenuFlyoutSubItem>())
        {
            yield return item;
            foreach (var nested in MenuSubItems(item.Items)) yield return nested;
        }
    }
    private void OpenWindows(DockEntry entry, FrameworkElement anchor)
    {
        var menu = new MenuFlyout(); TrackFlyout(menu);
        foreach (var window in entry.Windows.Select(w => stressTasks?.FirstOrDefault(task => task.Handle == w.Handle) ?? tracker.Describe(w.Handle)).OfType<TaskWindow>()) MenuWindow(menu, window, () => ShellService.Activate(window, false));
        ShowDockFlyout(menu, anchor);
    }
    private int folderOpenVersion;
    private async void OpenFolder(DockItem item, FrameworkElement anchor)
    {
        int version = ++folderOpenVersion;
        if (item.Folder.View == FolderView.Menu) { ShowDockFlyout(CreateFolderMenu(item), anchor); return; }
        var flyout = new Flyout { Placement = PopupPlacement, ShouldConstrainToRootBounds = false };
        var pane = new FolderPane(item, this, () => flyout.Hide());
        try { await pane.PrepareAsync(); }
        catch (Exception ex) { pane.Dispose(); Log.Write(ex); return; }
        if (closing || version != folderOpenVersion || anchor.XamlRoot == null) { pane.Dispose(); return; }
        TrackFlyout(flyout, preserveMagnification: true);
        var folderBackdrop = new DockBackdrop();
        folderBackdrop.Apply(Settings, root.RequestedTheme, accessibility.HighContrast, uiSettings.AdvancedEffectsEnabled, uiSettings.GetColorValue(UIColorType.Background));
        flyout.SystemBackdrop = folderBackdrop;
        flyout.Closed += (_, _) => { flyout.SystemBackdrop = null; folderBackdrop.Dispose(); };
        var style = new Style(typeof(FlyoutPresenter)); style.Setters.Add(new Setter(FrameworkElement.MaxWidthProperty, AvailablePopupWidth + 50)); style.Setters.Add(new Setter(FrameworkElement.MaxHeightProperty, AvailableFolderPopupHeight + 32)); style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0))); flyout.FlyoutPresenterStyle = style;
        style.Setters.Add(new Setter(Control.BackgroundProperty, new SolidColorBrush(Microsoft.UI.Colors.Transparent)));
        style.Setters.Add(new Setter(Control.CornerRadiusProperty, new CornerRadius(8)));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, popupBorderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        // Hide presenter artwork until the native backdrop clip is initialized.
        if (Motion) style.Setters.Add(new Setter(UIElement.OpacityProperty, 0d));
        flyout.Content = pane;
        AlignFolderFlyout(flyout, pane, anchor);
        AnimateFolderFlyout(flyout, pane);
        flyout.Closed += (_, _) => pane.Dispose(); ShowDockFlyout(flyout, anchor);
    }
    internal MenuFlyout CreateFolderMenu(DockItem item)
    {
        var menu = new MenuFlyout(); TrackFlyout(menu);
        var openFolder = new MenuFlyoutItem { Text = IsDownloads(item) ? "Open Downloads folder" : "Open folder", Icon = Glyph("\uED25") };
        openFolder.Click += (_, _) => TryAction(() => ShellService.Open(item.Target));
        menu.Items.Add(openFolder);
        menu.Items.Add(new MenuFlyoutSeparator());
        try
        {
            var files = Directory.EnumerateFileSystemEntries(item.Target).Select(p => new FileInfo(p));
            if (item.Folder.FilesOnly) files = files.Where(p => !Directory.Exists(p.FullName));
            files = item.Folder.Sort switch
            {
                FolderSort.Size => files.OrderBy(p => p.Exists ? p.Length : 0),
                FolderSort.Modified => files.OrderBy(p => p.LastWriteTimeUtc),
                FolderSort.Type => files.OrderBy(p => p.Extension, StringComparer.CurrentCultureIgnoreCase),
                _ => files.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            };
            if (item.Folder.Descending) files = files.Reverse();
            var visibleFiles = IsDownloads(item)
                ? item.Folder.Descending ? files.Take(25) : files.TakeLast(25)
                : files;
            foreach (var file in visibleFiles)
            {
                var path = file.FullName;
                var icon = new ImageIcon();
                var row = new FolderEntry(path, file.Name, Directory.Exists(path), default, default, file.Extension);
                string label = row.DisplayName(Settings.ShowFileExtensionsInTooltips);
                if (label.Length > 48) label = label[..45].TrimEnd() + "...";
                var entry = new MenuFlyoutItem { Text = label, Icon = icon };
                entry.Click += (_, _) => TryAction(() => ShellService.Open(path));
                menu.Items.Add(entry);
                ShellTooltips.Attach(entry, path);
                _ = LoadFolderMenuIcon(icon, path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Log.Write(ex); }
        return menu;
    }
    private async Task LoadFolderMenuIcon(ImageIcon icon, string path)
    {
        icon.Source = await IconService.LoadFlyout(path, thumbnail: false, requestedPixels: IconPixels);
    }
    private async void PickItem(bool folder)
    {
        try
        {
            if (folder) { var picker = new Windows.Storage.Pickers.FolderPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle); var result = await picker.PickSingleFolderAsync(); if (result != null) AddItem(ShellService.FromPath(result.Path)); }
            else { var picker = new Windows.Storage.Pickers.FileOpenPicker(); picker.FileTypeFilter.Add("*"); WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle); var result = await picker.PickSingleFileAsync(); if (result != null) AddItem(ShellService.FromPath(result.Path)); }
        }
        catch (Exception ex) { await Message("Cannot add item", ex.Message); }
    }
    private int InsertionSlot(double x, bool showMarker)
    {
        var pins = strip.Children.OfType<FrameworkElement>().Where(child => child.Tag is DockEntry { IsPinned: true }).ToList();
        var next = pins.FindIndex(child => x < Along(child.TransformToVisual(strip).TransformPoint(Vertical ? new Windows.Foundation.Point(0, child.ActualHeight / 2) : new Windows.Foundation.Point(child.ActualWidth / 2, 0))));
        var slot = next < 0 ? Settings.Items.Count : Settings.Items.FindIndex(item => item.Id == ((DockEntry)pins[next].Tag).Item.Id);
        if (showMarker)
        {
            double markerX;
            if (pins.Count == 0) markerX = Vertical ? strip.Padding.Top : strip.Padding.Left;
            else if (next == 0) markerX = Along(pins[0].TransformToVisual(strip).TransformPoint(new Windows.Foundation.Point())) - 3;
            else
            {
                var previous = pins[next < 0 ? pins.Count - 1 : next - 1];
                var right = Along(previous.TransformToVisual(strip).TransformPoint(Vertical ? new Windows.Foundation.Point(0, previous.ActualHeight) : new Windows.Foundation.Point(previous.ActualWidth, 0)));
                markerX = next < 0 ? right + 3 : (right + Along(pins[next].TransformToVisual(strip).TransformPoint(new Windows.Foundation.Point()))) / 2;
            }
            var rootX = Along(strip.TransformToVisual(root).TransformPoint(Vertical ? new Windows.Foundation.Point(0, markerX) : new Windows.Foundation.Point(markerX, 0)));
            insertionMarker.HorizontalAlignment = HorizontalAlignment.Left; insertionMarker.VerticalAlignment = VerticalAlignment.Top;
            insertionMarker.Width = Vertical ? DisplayIconSize + 4 : 3;
            insertionMarker.Height = Vertical ? 3 : DisplayIconSize + 4;
            insertionMarker.Margin = Vertical ? new Thickness(6, Math.Clamp(rootX - 1.5, 0, Math.Max(0, root.ActualHeight - 3)), 0, 0) : new Thickness(Math.Clamp(rootX - 1.5, 0, Math.Max(0, root.ActualWidth - 3)), 6, 0, 0);
            insertionMarker.Visibility = Visibility.Visible;
        }
        return slot;
    }
    private void DockDragOver(object sender, DragEventArgs e)
    {
        e.Handled = true; dragging = true; ResetMagnification();
        bool internalItem = e.DataView.Properties.ContainsKey("WinMacItemId");
        bool recycle = sender is FrameworkElement { Tag: DockEntry entry } && entry.Item.Kind == ItemKind.RecycleBin;
        e.AcceptedOperation = internalItem ? (!CanMoveIcons ? DataPackageOperation.None : DataPackageOperation.Move)
            : e.DataView.Contains(StandardDataFormats.StorageItems) && (!Settings.LockIconPositions || recycle) ? DataPackageOperation.Copy : DataPackageOperation.None;
        if (e.AcceptedOperation != DataPackageOperation.None && (internalItem || !recycle)) { insertionMarker.Visibility = Visibility.Collapsed; InsertionSlot(Along(e.GetPosition(strip)), false); }
        else insertionMarker.Visibility = Visibility.Collapsed;
    }
    private async void DockDrop(object sender, DragEventArgs e) { e.Handled = true; await DropOn(null, e); }
    private async Task DropOn(DockItem? target, DragEventArgs e)
    {
        var deferral = e.GetDeferral();
        try
        {
            if (e.DataView.Properties.TryGetValue("WinMacItemId", out var raw))
            {
                if (DockOrdering.Move(Settings, raw?.ToString() ?? "", InsertionSlot(Along(e.GetPosition(strip)), false), Native.GetAsyncKeyState(0x12) < 0)) Save();
            }
            else if (e.DataView.Contains(StandardDataFormats.StorageItems))
            {
                if (Settings.LockIconPositions && target?.Kind != ItemKind.RecycleBin) return;
                var at = InsertionSlot(Along(e.GetPosition(strip)), false);
                var rows = await e.DataView.GetStorageItemsAsync();
                if (target?.Kind == ItemKind.RecycleBin)
                {
                    bool recycled = rows.Count > 0 && ShellService.Recycle(Handle, rows.Select(x => x.Path));
                    if (recycled && target.PlayRecycleBinSounds)
                        ShellService.PlayRecycleSound(empty: false, target.RecycleBinSoundVolumePercent);
                    UpdateRecycle();
                    if (recycled)
                    {
                        var bin = buttons.FirstOrDefault(b => b.Entry.Item.Id == target.Id);
                        if (bin.Icon != null) PulseActivatedItem(bin.Icon);
                    }
                }
                else { foreach (var row in rows) AddItem(ShellService.FromPath(row.Path), at++); }
            }
        }
        catch (Exception ex) { await Message("Drop could not be completed", ex.Message); }
        finally { insertionMarker.Visibility = Visibility.Collapsed; deferral.Complete(); if (internalDragId == null) { dragging = false; Reconcile(); } }
    }
    public void ShowSettings()
    {
        (App.Dock ?? this).ShowSettingsForDisplay(DisplayIdentity);
    }
    private void ShowSettingsForDisplay(string identity)
    {
        if (settingsWindow != null)
        {
            settingsWindow.SelectPlacementDisplay(identity);
             settingsWindow.Activate();
            return;
        }
        settingsWindow = new SettingsWindow(this, identity);
        tracker.OwnTaskWindow = WinRT.Interop.WindowNative.GetWindowHandle(settingsWindow);
        settingsWindow.Closed += (_, _) => { tracker.OwnTaskWindow = 0; settingsWindow = null; Reconcile(); };
         settingsWindow.Activate(); Reconcile();
    }
    private void ShowAppPicker() { if (MonitorOverride != null) { App.Dock?.ShowAppPicker(); return; } ShowSettings(); settingsWindow!.ShowApps(); }
    internal ItemPropertiesWindow EditItem(DockItem item)
    {
        if (propertiesWindow != null)
        {
            var handle = WinRT.Interop.WindowNative.GetWindowHandle(propertiesWindow);
            if (Native.IsIconic(handle)) Native.ShowWindow(handle, 9);
             propertiesWindow.Activate();
            Native.SetForegroundWindow(handle);
            return propertiesWindow;
        }
        var editor = new ItemPropertiesWindow(this, item);
        propertiesWindow = editor;
        editor.Closed += (_, _) => { if (ReferenceEquals(propertiesWindow, editor)) propertiesWindow = null; };
         editor.Activate();
        return editor;
    }
    public void TryAction(Action action)
    {
        try { action(); }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223) { }
        catch (Exception ex) { Log.Write(ex); _ = Message("WinMac Dock", ex.Message); }
    }
    public async Task Message(string title, string text)
    {
        try
        {
            var window = new Window { Title = title };
            window.AppWindow.Resize(new Windows.Graphics.SizeInt32(520, 260));
            IconResources.SetWindowIcon(window);
            var panel = new StackPanel { Padding = new Thickness(24), Spacing = 20 };
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap });
            var close = new Button { Content = "OK", HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (_, _) => window.Close(); panel.Children.Add(close);
            var chrome = new WindowChrome(window, panel, root.ActualTheme);
            chrome.Root.Loaded += async (_, _) => chrome.Icon.Source = await IconService.Load(IconResources.PathFor(BundledIcons.Application), imageFile: true, requestedPixels: 32);
             window.Activate(); await Task.CompletedTask;
        }
        catch (Exception ex) { Log.Write(ex); }
    }
    private void Cleanup()
    {
        if (closing) return; ClearShellAnimatedIcons(); closing = true;  usbPinTimer.Stop(); StopRowTransition(); StopIndicatorAnimations(); StopHoverRendering(); downloadPinTimer.Stop(); downloadChangeTimer.Stop(); foreach (var watcher in downloadPinWatchers.Values) watcher.Dispose(); downloadPinWatchers.Clear(); downloadTrackers.Clear(); visibilityTimer.Stop(); reconcileTimer.Stop(); safetyTimer.Stop(); themeTimer.Stop(); recycleTimer.Stop();
        Native.SetPropW(Handle, "WinMac.Dock.HideWindowsTaskbar", 0); windowsTaskbar.Dispose();
        gapInput?.Dispose(); gapInput = null;
        minimizedTabs.Dispose();
        StopVisibilityAnimation(); foreach (var watcher in stackWatchers.Values) watcher.Dispose(); Native.UnregisterHotKey(Handle, 1); Native.DeregisterShellHookWindow(Handle); appbar.Dispose(); tracker.Dispose(); materialBackdrop?.Dispose(); materialWindow?.Close(); backdrop.Dispose(); tray.Visible = false; tray.Dispose();
        Native.RemoveWindowSubclass(Handle, subclass, 1); Native.UnregisterPassiveWindow(Handle); settingsWindow?.Close(); if (MonitorOverride == null) { App.CloseSecondaryDocks(); Application.Current.Exit(); }
    }
}

