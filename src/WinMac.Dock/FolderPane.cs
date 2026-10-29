using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Windows.ApplicationModel.DataTransfer;
using Windows.ApplicationModel.DataTransfer.DragDrop;
using Windows.Storage;

namespace WinMac.Dock;
internal sealed class FolderPane : UserControl, IDisposable
{
    private readonly DockItem item;
    private readonly DockWindow owner;
    private readonly Action dismiss;
    private readonly StackPanel root = new() { Spacing = 10 };
    private readonly TextBlock heading = new() { FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Opacity = .7 };
    private readonly ContentControl body = new();
    internal ScrollViewer Scroller { get; private set; } = null!;
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer scrollbarIdle = new() { Interval = TimeSpan.FromSeconds(3) };
    private ScrollBar? gridScrollbar;
    private FileSystemWatcher? watcher;
    private CancellationTokenSource? cancellation;
    private readonly string path;
    private string? listingError;
    private bool ShowHeading => item.Folder.View != FolderView.Grid || !(item.Folder.HideFolderNameInGrid ?? false);
    private double HeadingHeight => ShowHeading ? 58 : 0; // 32-DIP header + equal 13-DIP top/bottom insets.
    private readonly Grid header = new();
    private readonly Border headerSurface = new();
    private readonly Border surface = new() { CornerRadius = new CornerRadius(8) };
    private bool disposed;
    private bool initialGridScrollApplied;
    private List<FolderEntry> entries = [];
    private int loaded;
    private Task? initialLoad;
    internal Task PrepareAsync() => initialLoad ??= Reload();
    private readonly int iconSize;
    private readonly int gridCellHeight;
    private readonly double gridCellWidth;
    private readonly int gridColumns;
    private readonly double gridWidth;
    private ListViewBase? list;
    private readonly HashSet<GridViewItem> highlightedContainers = [];

    public FolderPane(DockItem item, DockWindow owner, Action dismiss)
    {
        this.item = item; this.owner = owner; this.dismiss = dismiss; path = item.Target;
        bool gridView = item.Folder.View == FolderView.Grid;
        double rasterScale = Native.GetDpiForWindow(owner.Handle) / 96d;
        iconSize = owner.Settings.FolderFlyoutIconSize;
        gridCellHeight = iconSize + 52;
        gridCellWidth = Math.Max(94, iconSize + 12);
        gridColumns = Math.Clamp((int)Math.Floor((owner.AvailablePopupWidth - 34 - 1 / rasterScale) / gridCellWidth), 1, Math.Clamp(item.Folder.GridColumns, 1, 7));
        // Include one physical pixel so float layout storage cannot round
        // an exact N-cell width below the ItemsWrapGrid wrapping threshold.
        gridWidth = Math.Ceiling(gridColumns * gridCellWidth * rasterScale) / rasterScale + 1 / rasterScale;
        int gridRows = Math.Clamp(item.Folder.GridRows, 1, 7);
        // The scrollbar track supplies the right inset; do not also reserve
        // another full padding inset beside it.
        Width = gridView ? gridWidth + 16 + 18 : Math.Min(490, owner.AvailablePopupWidth) + 32;
        double paneHeight = gridView ? Math.Min(owner.AvailableFolderPopupHeight, gridRows * gridCellHeight + 32 + HeadingHeight) : owner.AvailablePopupHeight;
        MaxHeight = paneHeight;
        if (gridView) Height = paneHeight;
        root.Padding = gridView ? new Thickness(16, 16, 0, 16) : new Thickness(16);
        Scroller = new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Enabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            MaxHeight = paneHeight
        };
        if (gridView)
        {
            var viewer = Scroller;
            scrollbarIdle.Tick += (_, _) =>
            {
                scrollbarIdle.Stop();
                if (gridScrollbar != null) { gridScrollbar.Opacity = 0; gridScrollbar.IsHitTestVisible = false; }
            };
            viewer.Loaded += (_, _) =>
            {
                gridScrollbar = FindVerticalScrollbar(viewer);
                if (gridScrollbar != null) { gridScrollbar.Opacity = 0; gridScrollbar.IsHitTestVisible = false; }
            };
            double previousOffset = 0;
            viewer.ViewChanged += (_, _) =>
            {
                if (Math.Abs(viewer.VerticalOffset - previousOffset) > .01) ShowScrollbar();
                previousOffset = viewer.VerticalOffset;
            };
            viewer.PointerMoved += (_, e) => { if (e.GetCurrentPoint(viewer).Position.X >= viewer.ActualWidth - 20) ShowScrollbar(); };
            viewer.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler((_, _) => ShowScrollbar()), true);
        }
        if (gridView)
        {
            // Fixed insets must not contribute to the scroll extent.
            root.Padding = new Thickness(0);
            Scroller.Margin = new Thickness(16, 16, 0, 16);
        }
        heading.VerticalAlignment = VerticalAlignment.Center; heading.Height = 24; heading.Margin = new Thickness(0, 0, 18, 10);
        heading.Visibility = ShowHeading ? Visibility.Visible : Visibility.Collapsed;
        header.Visibility = heading.Visibility;
        root.Padding = gridView ? new Thickness(0) : new Thickness(16, 0, 16, 16);
        heading.Margin = new Thickness(0, 0, 8, 0);
        header.Height = 32; header.Margin = new Thickness(16, 13, 16, 13); header.ColumnSpacing = 4;
        header.Visibility = ShowHeading ? Visibility.Visible : Visibility.Collapsed;
        header.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        header.Children.Add(heading);
        var openFolder = new Button
        {
            Content = new FontIcon { Glyph = "\uED25", FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Segoe Fluent Icons"), FontSize = 16 },
            VerticalAlignment = VerticalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Width = 32, Height = 32, Padding = new Thickness(8), BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(4),
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent)
        };
        foreach (var key in new[] { "ButtonBackground", "ButtonBorderBrush", "ButtonBorderBrushPointerOver", "ButtonBorderBrushPressed" })
            openFolder.Resources[key] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        openFolder.Resources["ButtonBackgroundPointerOver"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(24, 128, 128, 128));
        openFolder.Resources["ButtonBackgroundPressed"] = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(40, 128, 128, 128));
        string openFolderLabel = DockWindow.IsDownloads(item) ? "Open Downloads folder" : "Open folder";
        ToolTipService.SetToolTip(openFolder, openFolderLabel);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(openFolder, openFolderLabel);
        openFolder.Click += (_, _) => { dismiss(); owner.TryAction(() => ShellService.Open(path)); };
        openFolder.RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform { X = 3 };
        Grid.SetColumn(openFolder, 1); header.Children.Add(openFolder);
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        headerSurface.Child = header;
        headerSurface.Visibility = header.Visibility;
        layout.Children.Add(headerSurface);
        Grid.SetRow(Scroller, 1); layout.Children.Add(Scroller);
        surface.Child = layout;
        Content = surface;
        ApplyGridColors();
        ActualThemeChanged += (_, _) => ApplyGridColors();
        root.Children.Add(status); root.Children.Add(body);
        KeyDown += (_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { dismiss(); e.Handled = true; } };
        AllowDrop = true;
        DragOver += (_, e) => { if (e.DataView.Contains(StandardDataFormats.StorageItems)) e.AcceptedOperation = (e.Modifiers & DragDropModifiers.Shift) != 0 ? DataPackageOperation.Move : DataPackageOperation.Copy; };
        Drop += async (_, e) =>
        {
            if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
            var deferral = e.GetDeferral();
            try
            {
                var files = await e.DataView.GetStorageItemsAsync();
                var paths = files.Select(f => f.Path).ToArray();
                var move = TransferPolicy.Move(paths, path, (e.Modifiers & DragDropModifiers.Control) != 0, (e.Modifiers & DragDropModifiers.Shift) != 0);
                owner.TryAction(() => ShellService.Transfer(owner.Handle, paths, path, move)); await Reload();
            }
            catch (Exception ex) { await owner.Message("Drop could not be completed", ex.Message); }
            finally { deferral.Complete(); }
        };
        refresh.Tick += async (_, _) => { refresh.Stop(); await Reload(); };
        Loaded += async (_, _) => await PrepareAsync();
    }
    private async Task Reload()
    {
        if (disposed) return;
        cancellation?.Cancel(); cancellation?.Dispose(); cancellation = new(); var token = cancellation.Token;
        heading.Text = item.Name; status.Text = "Loading…";
        watcher?.Dispose(); watcher = null;
        try
        {
            var result = await FolderListing.ReadAsync(path, item.Folder, token);
            if (token.IsCancellationRequested || disposed) return;
            entries = result.Entries.Select(row => row with { ShowExtensions = owner.Settings.ShowFileExtensionsInTooltips }).ToList(); listingError = result.Error; status.Text = result.Error ?? (entries.Count == 0 ? "This folder is empty." : $"{entries.Count} items"); Render();
            if (Directory.Exists(path))
            {
                watcher = new(path) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite, EnableRaisingEvents = true };
                FileSystemEventHandler changed = (_, e) => DispatcherQueue.TryEnqueue(() => { IconService.Invalidate(e.FullPath); refresh.Stop(); refresh.Start(); });
                watcher.Created += changed; watcher.Deleted += changed; watcher.Changed += changed;
                watcher.Renamed += (_, _) => DispatcherQueue.TryEnqueue(() => { refresh.Stop(); refresh.Start(); });
                watcher.Error += (_, _) => DispatcherQueue.TryEnqueue(() => { listingError = status.Text = "Folder notifications interrupted. Reopen to refresh."; Render(); });
            }
        }
        catch (OperationCanceledException) { }
    }
    private void Render()
    {
        bool compactGrid = item.Folder.View == FolderView.Grid;
        if (compactGrid)
        {
            root.Padding = new Thickness(0);
            int columns = gridColumns;
            int rows = Math.Max(1, (int)Math.Ceiling(entries.Count / (double)columns));
            const double topInset = 16;
            const double bottomInset = 16;
            double reservedHeight = HeadingHeight + topInset + bottomInset;
            int visibleRows = FolderGridLayout.VisibleRows(rows, item.Folder.GridRows,
                owner.AvailableFolderPopupHeight, reservedHeight, gridCellHeight);
            // The viewport contains exactly N cells; its insets stay outside
            // scrolling, so top and bottom offsets both show complete rows.
            Height = visibleRows * gridCellHeight + reservedHeight;
            MaxHeight = Height;
            Scroller.Margin = new Thickness(16, topInset, 0, bottomInset);
            Scroller.MaxHeight = visibleRows * gridCellHeight;
        }
        root.Spacing = compactGrid ? 0 : 10;
        heading.Visibility = ShowHeading ? Visibility.Visible : Visibility.Collapsed;
        header.Visibility = heading.Visibility;
        headerSurface.Visibility = header.Visibility;
        ApplyGridColors();
        status.Visibility = entries.Count == 0 || listingError != null ? Visibility.Visible : Visibility.Collapsed;
        bool grid = item.Folder.View == FolderView.Grid;
        highlightedContainers.Clear();
        list = grid ? new GridView() : new ListView();
        // Reveal the prepared popup as one surface, without a second entrance
        // animation on its rows after the header has already appeared.
        list.ItemContainerTransitions = new Microsoft.UI.Xaml.Media.Animation.TransitionCollection();
        if (grid)
        {
            // A GridViewItem adds its own spacing around the 94-DIP template.
            // Define the whole cell explicitly so N columns occupy N * 94 DIP.
            list.UseLayoutRounding = false;
            list.Padding = new Thickness(0);
            // Bound the grid to its occupied cells; the default items presenter
            // otherwise contributes trailing space to the outer scroll extent.
            list.Height = Math.Ceiling(entries.Count / (double)gridColumns) * gridCellHeight;
            var cellStyle = new Style(typeof(GridViewItem));
            cellStyle.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, false));
            cellStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, gridCellWidth));
            cellStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, (double)gridCellHeight));
            cellStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
            cellStyle.Setters.Add(new Setter(Control.TemplateProperty, (ControlTemplate)XamlReader.Load("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='GridViewItem'><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='{TemplateBinding CornerRadius}'><ContentPresenter Content='{TemplateBinding Content}' ContentTemplate='{TemplateBinding ContentTemplate}' HorizontalContentAlignment='Center' VerticalContentAlignment='Center'/></Border></ControlTemplate>")));
            list.ItemContainerStyle = cellStyle;
        }
        list.Width = compactGrid ? gridWidth : Width - 32;
        ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        // Only the outer ScrollViewer scrolls a grid. Its content owns the
        // padding, so its scrollbar stays against the flyout's right edge.
        ScrollViewer.SetVerticalScrollMode(list, grid ? ScrollMode.Disabled : ScrollMode.Enabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, grid ? ScrollBarVisibility.Disabled : ScrollBarVisibility.Auto);
        if (grid) list.ItemsPanel = (ItemsPanelTemplate)XamlReader.Load($"<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid UseLayoutRounding='False' Orientation='Horizontal' ItemWidth='{gridCellWidth.ToString(System.Globalization.CultureInfo.InvariantCulture)}' ItemHeight='{gridCellHeight}' MaximumRowsOrColumns='{gridColumns}'/></ItemsPanelTemplate>");
        list.MaxHeight = grid ? double.PositiveInfinity : Math.Max(100, owner.AvailablePopupHeight - (compactGrid ? 20 : 160)); list.SelectionMode = ListViewSelectionMode.Single; list.IsItemClickEnabled = true;
        list.CanDragItems = true;
        var template = grid
            ? $"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Width='{gridCellWidth}' Height='{gridCellHeight}' Padding='6' Spacing='5'><Image Width='{iconSize}' Height='{iconSize}'/><TextBlock Text='{{Binding DisplayLabel}}' FontSize='12' Height='34' TextAlignment='Center' TextTrimming='CharacterEllipsis' MaxLines='2' TextWrapping='Wrap'/></StackPanel></DataTemplate>"
            : $"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><StackPanel Orientation='Horizontal' Spacing='12' Padding='4'><Image Width='{iconSize}' Height='{iconSize}'/><TextBlock Text='{{Binding DisplayLabel}}' VerticalAlignment='Center' TextTrimming='CharacterEllipsis' MaxWidth='380'/></StackPanel></DataTemplate>";
        list.ItemTemplate = (DataTemplate)XamlReader.Load(template);
        list.ItemClick += async (_, e) => { if (e.ClickedItem is FolderEntry row) await Open(row); };
        list.ContainerContentChanging += (sender, e) =>
        {
            if (!e.InRecycleQueue && grid)
            {
                bool realItem = e.Item is FolderEntry;
                e.ItemContainer.IsEnabled = realItem;
                e.ItemContainer.IsTabStop = realItem;
                e.ItemContainer.Opacity = realItem ? 1 : 0;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(e.ItemContainer, realItem
                    ? Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Content : Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
            }
            if (!e.InRecycleQueue && e.Item is FolderEntry row && e.ItemContainer.ContentTemplateRoot is StackPanel panel)
            {
                if (e.ItemContainer is GridViewItem cell && highlightedContainers.Add(cell)) ConfigureHighlight(cell);
                if (panel.Children[0] is Microsoft.UI.Xaml.Controls.Image image) { image.Tag = row.Path; _ = SetImage(image, row); }
                e.ItemContainer.ContextFlyout = MakeMenu(row);
                ShellTooltips.Attach(e.ItemContainer, row.Path);
            }
        };
        list.DragItemsStarting += (_, e) =>
        {
            var rows = e.Items.Cast<FolderEntry>().ToArray();
            e.Data.RequestedOperation = DataPackageOperation.Copy | DataPackageOperation.Move;
            e.Data.SetDataProvider(StandardDataFormats.StorageItems, async request =>
            {
                var deferral = request.GetDeferral();
                try { var items = new List<IStorageItem>(); foreach (var row in rows) items.Add(row.IsFolder ? await StorageFolder.GetFolderFromPathAsync(row.Path) : await StorageFile.GetFileFromPathAsync(row.Path)); request.SetData(items); }
                catch (Exception ex) { Log.Write(ex); } finally { deferral.Complete(); }
            });
        };
        var container = new StackPanel(); container.Children.Add(list); loaded = 0;
        if (grid)
        {
            // Ascending Downloads opens at the bottom. Put unused cells in the
            // first row so the last row fills without changing the sort order.
            int leadingEmptyCells = FolderGridLayout.LeadingEmptyCells(entries.Count, gridColumns,
                DockWindow.IsDownloads(item) && !item.Folder.Descending);
            for (int i = 0; i < leadingEmptyCells; i++) list.Items.Add(new { DisplayLabel = "" });
            foreach (var row in entries) list.Items.Add(row);
            loaded = entries.Count;
        }
        else AddPage();
        var more = new Button { Content = "Load more", HorizontalAlignment = HorizontalAlignment.Stretch, Visibility = loaded < entries.Count ? Visibility.Visible : Visibility.Collapsed };
        more.Click += (_, _) => { AddPage(); more.Visibility = loaded < entries.Count ? Visibility.Visible : Visibility.Collapsed; };
        container.Children.Add(more); body.Content = container;
        if (grid && !initialGridScrollApplied && DockWindow.IsDownloads(item) && !item.Folder.Descending && entries.Count > 0)
        {
            var initialList = list;
            initialList.Loaded += (_, _) => DispatcherQueue.TryEnqueue(() =>
            {
                if (disposed || initialGridScrollApplied || !ReferenceEquals(list, initialList)) return;
                Scroller.UpdateLayout();
                Scroller.ChangeView(null, Scroller.ScrollableHeight, null, true);
                initialGridScrollApplied = true;
            });
        }
    }
    private void ApplyGridColors()
    {
        if (item.Folder.View != FolderView.Grid)
        {
            surface.Background = null;
            headerSurface.Background = null;
            return;
        }
        bool dark = ActualTheme == ElementTheme.Dark;
        byte headerShade = dark ? (byte)0x1c : (byte)0xee;
        byte bodyShade = dark ? (byte)0x24 : (byte)0xf2;
        headerSurface.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, headerShade, headerShade, headerShade));
        headerSurface.CornerRadius = new CornerRadius(8, 8, 0, 0);
        surface.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Windows.UI.Color.FromArgb(255, bodyShade, bodyShade, bodyShade));
    }
    private void AddPage() { if (list == null) return; foreach (var row in entries.Skip(loaded).Take(100)) list.Items.Add(row); loaded = Math.Min(entries.Count, loaded + 100); }
    private void ConfigureHighlight(GridViewItem cell)
    {
        bool hovered = false, pressed = false;
        void Update() => ItemHighlight.Apply(cell, owner.Settings, ActualTheme, hovered || pressed || cell.FocusState == FocusState.Keyboard, pressed, alwaysEnabled: true);
        cell.PointerEntered += (_, _) => { hovered = true; Update(); };
        cell.PointerExited += (_, _) => { hovered = false; Update(); };
        cell.PointerPressed += (_, e) => { pressed = e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed; Update(); };
        cell.PointerReleased += (_, _) => { pressed = false; Update(); };
        cell.PointerCanceled += (_, _) => { pressed = false; Update(); };
        cell.PointerCaptureLost += (_, _) => { pressed = false; Update(); };
        cell.GotFocus += (_, _) => Update();
        cell.LostFocus += (_, _) => Update();
        cell.ActualThemeChanged += (_, _) => Update();
        Update();
    }
    private Task Open(FolderEntry row)
    {
        owner.TryAction(() => ShellService.Open(row.Path));
        dismiss();
        return Task.CompletedTask;
    }
    private MenuFlyout MakeMenu(FolderEntry row)
    {
        var menu = new MenuFlyout();
        owner.StyleMenu(menu);
        var open = new MenuFlyoutItem { Text = "Open", Icon = DockWindow.Glyph(row.IsFolder ? "\uE8DA" : "\uE8E5") }; open.Click += (_, _) => owner.TryAction(() => ShellService.Open(row.Path)); menu.Items.Add(open);
        var downloads = new MenuFlyoutItem { Text = DockWindow.IsDownloads(item) ? "Open Downloads folder" : "Open containing folder", Icon = DockWindow.Glyph("\uED25") };
        downloads.Click += (_, _) => owner.TryAction(() => ShellService.Open(item.Target));
        menu.Items.Add(downloads);
        var pin = new MenuFlyoutItem { Text = "Pin to dock", Icon = DockWindow.Glyph("\uE718") }; pin.Click += (_, _) => owner.PinFromFolder(ShellService.FromPath(row.Path)); menu.Items.Add(pin); return menu;
    }
    private async Task SetImage(Microsoft.UI.Xaml.Controls.Image image, FolderEntry row)
    {
        image.Tag = row.Path;
        // A cache hit completes synchronously. Finish WinUI's container-change
        // callback before attaching artwork, and ignore detached popup work.
        await Task.Yield();
        if (disposed || !Equals(image.Tag, row.Path)) return;
        var icon = await IconService.LoadFlyout(row.Path, !row.IsFolder,
            requestedPixels: IconRaster.Pixels(image.Width, image.XamlRoot?.RasterizationScale ?? owner.RasterScale));
        if (!disposed && Equals(image.Tag, row.Path)) image.Source = icon;
    }
    private static ScrollBar? FindVerticalScrollbar(DependencyObject parent)
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is ScrollBar { Orientation: Orientation.Vertical } bar) return bar;
            if (FindVerticalScrollbar(child) is { } nested) return nested;
        }
        return null;
    }
    private void ShowScrollbar()
    {
        if (gridScrollbar == null || Scroller.ScrollableHeight <= 0) return;
        gridScrollbar.Opacity = 1; gridScrollbar.IsHitTestVisible = true;
        scrollbarIdle.Stop(); scrollbarIdle.Start();
    }
    public void Dispose() { disposed = true; cancellation?.Cancel(); cancellation?.Dispose(); watcher?.Dispose(); refresh.Stop(); scrollbarIdle.Stop(); }
}
