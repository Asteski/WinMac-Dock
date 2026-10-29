using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    internal const string BetaVersion = "Beta v0.7.0";
    private Window? aboutWindow;
    private void ShowAbout()
    {
        if (aboutWindow != null) { PositionAbout(aboutWindow); aboutWindow.Activate(); return; }
        var window = new Window { Title = "About WinMac Dock", SystemBackdrop = new MicaBackdrop() };
        aboutWindow = window;
        var iconPath = IconResources.PathFor(BundledIcons.Application);
        IconResources.SetWindowIcon(window);
        var panel = new StackPanel { Padding = new Thickness(32, 24, 32, 16), Spacing = 14, HorizontalAlignment = HorizontalAlignment.Stretch };
        var logo = new Image { Width = 64, Height = 64, Stretch = Stretch.Uniform };
        panel.Children.Add(logo);
        panel.Children.Add(new TextBlock { Text = "WinMac Dock", FontSize = 24, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        var details = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
        details.Children.Add(new TextBlock { Text = $"Version: {BetaVersion}", HorizontalAlignment = HorizontalAlignment.Center });
        details.Children.Add(new TextBlock { Text = "Created by: Asteski", HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(details);
        panel.Children.Add(new TextBlock { Text = "© 2026 Adam Kamienski", Opacity = .7, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new HyperlinkButton { Content = "GitHub page", NavigateUri = new Uri("https://github.com/Asteski"), HorizontalAlignment = HorizontalAlignment.Center, Padding = new Thickness(8, 4, 8, 4) });
        var close = new Button { Content = "Close", Width = 90, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        close.Click += (_, _) => window.Close(); panel.Children.Add(close);
        var chrome = new WindowChrome(window, panel, Settings.Theme switch { DockTheme.Light => ElementTheme.Light, DockTheme.Dark => ElementTheme.Dark, _ => ElementTheme.Default });
        chrome.Root.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, e) => { if (e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; window.Close(); } }), true);
        chrome.Root.Loaded += async (_, _) =>
        {
            logo.Source = await IconService.Load(iconPath, imageFile: true, requestedPixels: 128);
            chrome.Icon.Source = await IconService.Load(iconPath, imageFile: true, requestedPixels: 32);
            PositionAbout(window);
        };
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) { presenter.IsResizable = false; presenter.IsMaximizable = false; presenter.IsMinimizable = false; }
        window.Closed += (_, _) => aboutWindow = null;
        PositionAbout(window);
        window.Activate();
    }
    private void PositionAbout(Window window)
    {
        // The primary taskbar owns the notification area, even when the dock
        // lives on another display. Use the tray bounds to choose its corner.
        var taskbar = Native.FindWindowExW(0, 0, "Shell_TrayWnd", null);
        var displays = Native.Monitors();
        var display = displays.FirstOrDefault(m => taskbar != 0 && m.Handle == Native.MonitorFromWindow(taskbar, 2))
            ?? displays.FirstOrDefault(m => m.Primary) ?? monitor;
        var screen = display.Bounds;
        var area = display.Work;
        var bar = taskbar != 0 ? Native.Bounds(taskbar) : default;
        var tray = taskbar != 0 ? Native.FindWindowExW(taskbar, 0, "TrayNotifyWnd", null) : 0;
        var trayBounds = tray != 0 ? Native.Bounds(tray) : bar;
        bool horizontal = bar.Width >= bar.Height;
        bool right = horizontal ? tray == 0 || trayBounds.Left + trayBounds.Width / 2 >= screen.Left + screen.Width / 2
            : bar.Left + bar.Width / 2 >= screen.Left + screen.Width / 2;
        bool bottom = horizontal ? taskbar == 0 || bar.Top + bar.Height / 2 >= screen.Top + screen.Height / 2
            : tray == 0 || trayBounds.Top + trayBounds.Height / 2 >= screen.Top + screen.Height / 2;
        int width = Math.Min((int)Math.Round(340 * display.Scale), Math.Max(1, area.Width - 24));
        // Fit the content so the Close button keeps the same 16-DIP bottom
        // padding as the Settings action row, without spare fixed-height space.
        var content = (FrameworkElement)window.Content;
        content.Measure(new Windows.Foundation.Size(width / display.Scale, double.PositiveInfinity));
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        int frameHeight = Math.Max(0, window.AppWindow.Size.Height - Native.ClientBounds(handle).Height);
        int height = Math.Min((int)Math.Ceiling(content.DesiredSize.Height * display.Scale) + frameHeight, Math.Max(1, area.Height - 24));
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            right ? area.Right - width - 12 : area.Left + 12,
            bottom ? area.Bottom - height - 12 : area.Top + 12, width, height));
    }

}
