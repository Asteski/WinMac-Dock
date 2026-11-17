using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;

internal sealed partial class SettingsWindow
{
    private sealed record SearchResult(string Category, string Label, FrameworkElement? Target = null, Expander? Group = null)
    {
        public override string ToString() => Label;
    }

    private static IEnumerable<(FrameworkElement Row, Expander? Group)> SearchCardRows(Border card, Expander? group) =>
        card.Child is StackPanel body ? body.Children.OfType<FrameworkElement>().Select(row => (row, group)) : [];

    private static StackPanel SettingsGroup(FrameworkElement heading, Border card, string description = "")
    {
        var header = new StackPanel { Spacing = 2 };
        header.Children.Add(heading);
        if (!string.IsNullOrEmpty(description))
            header.Children.Add(new TextBlock { Text = description, TextWrapping = TextWrapping.Wrap, Opacity = .75, FontSize = 13 });
        var group = new StackPanel { Spacing = 8, Tag = heading };
        group.Children.Add(header);
        group.Children.Add(card);
        return group;
    }

    private static IEnumerable<(FrameworkElement Row, Expander? Group)> SearchGroupRows(UIElement element, Expander? group = null) =>
        element switch
        {
            Border card => SearchCardRows(card, group),
            StackPanel { Tag: FrameworkElement heading } section =>
                new[] { (heading, group) }.Concat(section.Children.OfType<Border>().SelectMany(card => SearchCardRows(card, group))),
            Expander { Content: StackPanel cards } expander => cards.Children.SelectMany(child => SearchGroupRows(child, expander)),
            _ => []
        };

    private AutoSuggestBox? settingsSearch;

    private IEnumerable<SearchResult> SearchRows(string category, StackPanel panel) =>
        panel.Children.SelectMany(element => SearchGroupRows(element))
            .Where(entry => entry.Row.Visibility == Visibility.Visible)
            .Select(entry => new SearchResult(category, entry.Row switch
            {
                TextBlock { Tag: string title } => title,
                TextBlock => "",
                Button { Content: string label } => label,
                StackPanel { Tag: "Installed version" } => "Check for updates",
                _ when settingSearchLabels.TryGetValue(entry.Row, out var caption) => caption.Text,
                _ => entry.Row.Tag as string ?? ""
            }, entry.Row, entry.Group));

    private AutoSuggestBox CreateSettingsSearch()
    {
        var search = settingsSearch = new AutoSuggestBox
        {
            PlaceholderText = "Search for settings",
            QueryIcon = new SymbolIcon(Symbol.Find),
            Width = 340,
            Margin = new Thickness(132, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        search.TextChanged += (_, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            string query = search.Text.Trim();
            search.ItemsSource = query.Length == 0 ? null : categories
                .Where(category => !ItemSettingsPages.Contains(category.Key))
                .SelectMany(category => SearchRows(category.Key, category.Value))
                .Concat(ItemSettingsPages.SelectMany(page => ItemSettingsSearchLabels(page)
                    .Select(label => new SearchResult(page, label))))
                .Where(result => result.Label.Contains(query, StringComparison.OrdinalIgnoreCase))
                .DistinctBy(result => (result.Category, result.Label)).Take(10).ToArray();
        };
        search.QuerySubmitted += (_, args) =>
        {
            var result = args.ChosenSuggestion as SearchResult ?? (search.ItemsSource as SearchResult[])?.FirstOrDefault();
            if (result == null || settingsNavigation == null) return;
            var destination = settingsNavigation.MenuItems.OfType<NavigationViewItem>().SelectMany(item => new[] { item }.Concat(item.MenuItems.OfType<NavigationViewItem>()))
                .FirstOrDefault(item => Equals(item.Tag, result.Category));
            if (destination == null) return;
            settingsNavigation.SelectedItem = destination;
            if (result.Target == null)
            {
                EnsureItemSettingsPage(result.Category);
                result = SearchRows(result.Category, categories[result.Category]).FirstOrDefault(row => row.Label == result.Label);
            }
            if (result?.Target is not { } target) return;
            if (result.Group != null) result.Group.IsExpanded = true;
            DispatcherQueue.TryEnqueue(() =>
            {
                categoryScroll.UpdateLayout();
                if (target.XamlRoot == null) return;
                target.StartBringIntoView(new BringIntoViewOptions
                {
                    VerticalAlignmentRatio = 0,
                    VerticalOffset = 12,
                    AnimationDesired = false
                });
            });
        };
        return search;
    }

    private Button CreateTitleNavigation(NavigationView nav)
    {
        var icon = new AnimatedIcon
        {
            Width = 16,
            Height = 16,
            Source = new Microsoft.UI.Xaml.Controls.AnimatedVisuals.AnimatedGlobalNavigationButtonVisualSource(),
            FallbackIconSource = new FontIconSource { Glyph = "\uE700" }
        };
        var pane = new Button
        {
            Content = icon,
            Width = 40,
            Height = 36,
            Padding = new Thickness(12, 4, 12, 4),
            VerticalAlignment = VerticalAlignment.Center,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0)
        };
        AnimatedIcon.SetState(icon, "Normal");
        pane.PointerEntered += (_, _) => AnimatedIcon.SetState(icon, "PointerOver");
        pane.PointerExited += (_, _) => AnimatedIcon.SetState(icon, "Normal");
        pane.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(
            (_, _) => AnimatedIcon.SetState(icon, "Pressed")), true);
        pane.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(
            (_, _) => AnimatedIcon.SetState(icon, "PointerOver")), true);
        pane.PointerCaptureLost += (_, _) => AnimatedIcon.SetState(icon, "Normal");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(pane, "Toggle menu");
        ToolTipService.SetToolTip(pane, "Toggle menu");
        pane.Click += (_, _) => nav.IsPaneOpen = !nav.IsPaneOpen;
        return pane;
    }

    private sealed record AutomaticOrderRow(AutomaticItemKind Kind)
    {
        public override string ToString() => Kind switch
        {
            AutomaticItemKind.ExternalDisks => "External Drives",
            AutomaticItemKind.Recordings => "Screen Recordings",
            _ => Kind.ToString()
        };
    }

    private sealed class AutomaticOrderList : GridView
    {
        private static readonly Microsoft.UI.Input.InputSystemCursor hand =
            Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Hand);
        private static readonly Microsoft.UI.Input.InputSystemCursor resize =
            Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.SizeAll);

        public AutomaticOrderList()
        {
            ProtectedCursor = hand;
            AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) =>
            {
                if (args.GetCurrentPoint(this).Properties.IsLeftButtonPressed) ProtectedCursor = resize;
            }), true);
            AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(
                (_, _) => ProtectedCursor = hand), true);
            PointerCaptureLost += (_, _) => ProtectedCursor = hand;
            DragItemsStarting += (_, _) => ProtectedCursor = resize;
            DragItemsCompleted += (_, _) => ProtectedCursor = hand;
        }
    }

    private void AddAutomaticItemOrder(StackPanel section)
    {
        var order = new ObservableCollection<AutomaticOrderRow>(DownloadPinLayout.NormalizeOrder(dock.Settings.AutomaticItemOrder).Select(kind => new AutomaticOrderRow(kind)));
        var list = new AutomaticOrderList
        {
            ItemsSource = order,
            CanReorderItems = true,
            CanDragItems = true,
            AllowDrop = true,
            MinHeight = 0,
            SelectionMode = ListViewSelectionMode.Single
        };
        var labelMeasure = new TextBlock();
        double tileWidth = 0;
        foreach (var row in order)
        {
            labelMeasure.Text = row.ToString();
            labelMeasure.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
            tileWidth = Math.Max(tileWidth, labelMeasure.DesiredSize.Width);
        }
        string width = Math.Ceiling(tileWidth + 26).ToString(System.Globalization.CultureInfo.InvariantCulture);
        list.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <ItemsPanelTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <ItemsWrapGrid Orientation="Horizontal"/>
            </ItemsPanelTemplate>
            """);
        var tileStyle = new Style(typeof(GridViewItem));
        tileStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        tileStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0)));
        tileStyle.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 0d));
        tileStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        list.ItemContainerStyle = tileStyle;
        ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollMode(list, ScrollMode.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        list.ItemTemplate = (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load($$"""
            <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Border Background="{ThemeResource ControlFillColorDefaultBrush}"
                        BorderBrush="{ThemeResource ControlStrokeColorDefaultBrush}"
                        BorderThickness="1" CornerRadius="4" Padding="12,6" Margin="2"
                        HorizontalAlignment="Left" Width="{{width}}">
                    <TextBlock Text="{Binding}" TextAlignment="Center" VerticalAlignment="Center"/>
                </Border>
            </DataTemplate>
            """);
        list.DragItemsCompleted += (_, _) =>
        {
            dock.Settings.AutomaticItemOrder = order.Select(row => row.Kind).ToList();
            PreviewSettings();
        };
        var frame = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load("""
            <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    Background="{ThemeResource SubtleFillColorSecondaryBrush}"
                    BorderBrush="{ThemeResource CardStrokeColorDefaultBrush}"
                    BorderThickness="1" CornerRadius="8" Padding="4"/>
            """);
        frame.Child = list;

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        foreach (var (label, step) in new[] { ("Move up", -1), ("Move down", 1) })
        {
            var button = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Stretch };
            button.Click += (_, _) =>
            {
                int index = list.SelectedIndex;
                int destination = index + step;
                if (index < 0 || destination < 0 || destination >= order.Count) return;
                order.Move(index, destination);
                list.SelectedIndex = destination;
                dock.Settings.AutomaticItemOrder = order.Select(row => row.Kind).ToList();
                PreviewSettings();
            };
            actions.Children.Add(button);
        }
        var reset = new Button { Content = "Reset", HorizontalAlignment = HorizontalAlignment.Stretch };
        reset.Click += (_, _) =>
        {
            var defaults = DownloadPinLayout.NormalizeOrder(null);
            order.Clear();
            foreach (var kind in defaults) order.Add(new AutomaticOrderRow(kind));
            dock.Settings.AutomaticItemOrder = defaults;
            PreviewSettings();
        };
        actions.Children.Add(reset);
        var layout = new StackPanel { Spacing = 8, Margin = new Thickness(0, 8, 0, 8) };
        layout.Children.Add(frame);
        layout.Children.Add(actions);
        section.Children.Add(layout);
    }
}
