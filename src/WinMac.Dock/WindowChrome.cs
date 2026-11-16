using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;
internal sealed class WindowChrome
{
    public Grid Root { get; } = new();
    private TextBlock? titleText;
    public void SetTitle(string title) { if (titleText != null) titleText.Text = title; }
    public Image Icon { get; } = new() { Width = 16, Height = 16, Stretch = Stretch.Uniform };
    internal void ReleaseBody() => Root.Children.RemoveAt(1);
    public WindowChrome(Window window, FrameworkElement content, ElementTheme theme, FrameworkElement? center = null, FrameworkElement? navigation = null)
    {
        Root.RequestedTheme = theme;
        Root.RowDefinitions.Add(new() { Height = new GridLength(center == null ? 32 : 48) });
        Root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var title = new Grid { Padding = new Thickness(navigation == null ? 12 : 3, 0, 144, 0), ColumnSpacing = 8 };
        title.ColumnDefinitions.Add(new() { Width = new GridLength(16) }); title.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        title.Children.Add(Icon);
        var text = new TextBlock { Text = window.Title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        titleText = text;
        Grid.SetColumn(text, 1); title.Children.Add(text);
        if (navigation != null)
        {
            void UpdateIconColumn() => title.ColumnDefinitions[0].Width = new GridLength(
                navigation.Visibility == Visibility.Visible ? 64 : 16);
            navigation.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) => UpdateIconColumn());
            UpdateIconColumn();
            Icon.HorizontalAlignment = HorizontalAlignment.Right;
            navigation.HorizontalAlignment = HorizontalAlignment.Left;
            title.Children.Add(navigation);
        }
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        if (center != null)
        {
            Grid.SetColumnSpan(center, 2);
            title.Children.Add(center);
            var input = Microsoft.UI.Input.InputNonClientPointerSource.GetForWindowId(window.AppWindow.Id);
            Windows.Graphics.RectInt32[] previous = [];
            void UpdateSearchHitArea()
            {
                if (closed || center.XamlRoot == null || center.ActualWidth <= 0) return;
                double dpi = center.XamlRoot.RasterizationScale;
                Windows.Graphics.RectInt32 Bounds(FrameworkElement element)
                {
                    var point = element.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
                    return new((int)Math.Floor(point.X * dpi), (int)Math.Floor(point.Y * dpi),
                        (int)Math.Ceiling(element.ActualWidth * dpi), (int)Math.Ceiling(element.ActualHeight * dpi));
                }
                Windows.Graphics.RectInt32[] regions = navigation == null || navigation.Visibility != Visibility.Visible ? [Bounds(center)] : [Bounds(center), Bounds(navigation)];
                if (previous.SequenceEqual(regions)) return;
                try
                {
                    input.SetRegionRects(Microsoft.UI.Input.NonClientRegionKind.Passthrough, regions);
                    previous = regions;
                }
                catch (ObjectDisposedException)
                {
                    closed = true;
                }
            }
            EventHandler<object> layout = (_, _) => UpdateSearchHitArea();
            center.LayoutUpdated += layout;
            window.Closed += (_, _) => center.LayoutUpdated -= layout;
            title.SizeChanged += (_, args) =>
            {
                center.Width = Math.Clamp(args.NewSize.Width - 430, 120, 516);
                text.HorizontalAlignment = HorizontalAlignment.Left;
                text.MaxWidth = Math.Max(0, (args.NewSize.Width - center.Width) / 2 - (navigation == null ? 44 : 98));
            };
        }
        Root.Children.Add(title); Grid.SetRow(content, 1); Root.Children.Add(content);
        window.Content = Root; window.ExtendsContentIntoTitleBar = true; window.SetTitleBar(title);
        // The title-bar image is drawn explicitly: WinUI's default title bar can omit
        // the icon even when AppWindow.SetIcon supplies taskbar/task-switcher artwork.
        if (center != null)
            window.AppWindow.TitleBar.PreferredHeightOption = Microsoft.UI.Windowing.TitleBarHeightOption.Tall;
        window.AppWindow.TitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;
        void Theme()
        {
            if (closed || window.AppWindow is not { } appWindow) return;
            bool dark = Root.ActualTheme == ElementTheme.Dark;
            Root.Background = new SolidColorBrush(dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 247, 247, 249));
            appWindow.TitleBar.ButtonForegroundColor = dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
            appWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
        }
        Root.ActualThemeChanged += (_, _) => Theme(); Theme();
    }
}
