using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private void AddQuotedWindowAction(MenuFlyout menu, TaskWindow window, bool allowMinimize = true)
    {
        bool minimize = allowMinimize && !Native.IsIconic(window.Handle);
        string verb = minimize ? "Minimize" : "Open";
        string fullLabel = verb + " \"" + window.Title + "\"";
        var item = new MenuFlyoutItem { Text = fullLabel, MaxWidth = Math.Min(560, AvailablePopupWidth) };
        TextBlock? titleBlock = null;
        bool composed = false;
        static TextBlock? FindLabel(DependencyObject parent, string label)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBlock text && text.Text == label) return text;
                if (FindLabel(child, label) is { } nested) return nested;
            }
            return null;
        }
        item.Loaded += (_, _) =>
        {
            if (composed) return;
            titleBlock = FindLabel(item, fullLabel);
            if (titleBlock == null || VisualTreeHelper.GetParent(titleBlock) is not Panel parent) return;
            int index = parent.Children.IndexOf(titleBlock);
            // Let the native menu measure the entire label, as with Edge window
            // titles. Only the middle column can trim; both quotes stay visible.
            var label = new Grid { Margin = titleBlock.Margin, VerticalAlignment = titleBlock.VerticalAlignment,
                HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 0 };
            Grid.SetRow(label, Grid.GetRow(titleBlock)); Grid.SetColumn(label, Grid.GetColumn(titleBlock));
            Grid.SetRowSpan(label, Grid.GetRowSpan(titleBlock)); Grid.SetColumnSpan(label, Grid.GetColumnSpan(titleBlock));
            label.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            label.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
            label.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
            TextBlock FixedText(string text) => new() { Text = text, FontFamily = titleBlock.FontFamily,
                FontSize = titleBlock.FontSize, FontWeight = titleBlock.FontWeight, FontStyle = titleBlock.FontStyle,
                CharacterSpacing = titleBlock.CharacterSpacing, Foreground = titleBlock.Foreground,
                IsTextScaleFactorEnabled = titleBlock.IsTextScaleFactorEnabled, VerticalAlignment = VerticalAlignment.Center };
            // Keep the named template part in its original namescope. WinUI
            // targets it from hover/pressed/disabled visual-state setters;
            // removing it can cause a deferred, fatal XAML setter error.
            var templateLabel = titleBlock;
            templateLabel.Visibility = Visibility.Collapsed;
            // Use a fresh middle label so native placeholder margins cannot
            // appear inside the quotes.
            titleBlock = FixedText(window.Title);
            titleBlock.Margin = new Thickness(0);
            titleBlock.MinWidth = 0;
            titleBlock.HorizontalAlignment = HorizontalAlignment.Left;
            titleBlock.TextAlignment = TextAlignment.Left;
            titleBlock.TextWrapping = TextWrapping.NoWrap;
            titleBlock.MaxLines = 1;
            titleBlock.TextTrimming = TextTrimming.CharacterEllipsis;
            Grid.SetRow(titleBlock, 0); Grid.SetColumn(titleBlock, 1);
            Grid.SetRowSpan(titleBlock, 1); Grid.SetColumnSpan(titleBlock, 1);
            label.Children.Add(FixedText(verb + " \""));
            label.Children.Add(titleBlock);
            var quote = FixedText("\""); Grid.SetColumn(quote, 2); label.Children.Add(quote);
            foreach (var text in label.Children.OfType<TextBlock>())
                text.SetBinding(TextBlock.ForegroundProperty, new Microsoft.UI.Xaml.Data.Binding
                { Source = templateLabel, Path = new PropertyPath("Foreground") });
            parent.Children.Insert(index + 1, label);
            composed = true;
        };
        item.LayoutUpdated += (_, _) => ToolTipService.SetToolTip(item, titleBlock?.IsTextTrimmed == true ? window.Title : null);
        item.Click += (_, _) => ShellService.Activate(window, minimize);
        menu.Items.Add(item);
    }
}
