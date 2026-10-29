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
    public WindowChrome(Window window, FrameworkElement content, ElementTheme theme)
    {
        Root.RequestedTheme = theme;
        Root.RowDefinitions.Add(new() { Height = new GridLength(32) });
        Root.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        var title = new Grid { Padding = new Thickness(12, 0, 144, 0), ColumnSpacing = 8 };
        title.ColumnDefinitions.Add(new() { Width = new GridLength(16) }); title.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        title.Children.Add(Icon);
        var text = new TextBlock { Text = window.Title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        titleText = text;
        Grid.SetColumn(text, 1); title.Children.Add(text);
        Root.Children.Add(title); Grid.SetRow(content, 1); Root.Children.Add(content);
        window.Content = Root; window.ExtendsContentIntoTitleBar = true; window.SetTitleBar(title);
        // The title-bar image is drawn explicitly: WinUI's default title bar can omit
        // the icon even when AppWindow.SetIcon supplies taskbar/task-switcher artwork.
        window.AppWindow.TitleBar.IconShowOptions = Microsoft.UI.Windowing.IconShowOptions.HideIconAndSystemMenu;
        bool closed = false;
        window.Closed += (_, _) => closed = true;
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
