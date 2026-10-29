using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;
internal static class ItemHighlight
{
    // TaskListButtonResources + TaskbarThemeLight/Dark, extracted from the installed taskbar.
    internal static Windows.UI.Color TaskbarFill(ElementTheme theme, bool active, bool hovered, bool pressed)
    {
        bool dark = theme == ElementTheme.Dark;
        byte alpha = pressed ? (dark ? (byte)0x0A : (byte)0x4C)
            : active && hovered ? (dark ? (byte)0x15 : (byte)0xB3)
            : active || hovered ? (dark ? (byte)0x0F : (byte)0x80) : (byte)0;
        return Windows.UI.Color.FromArgb(alpha, 255, 255, 255);
    }

    internal static Brush TaskbarStroke(ElementTheme theme, bool active, bool hovered, bool pressed)
    {
        bool dark = theme == ElementTheme.Dark;
        if (pressed) return new SolidColorBrush(Windows.UI.Color.FromArgb(dark ? (byte)0x0A : (byte)0x05, dark ? (byte)255 : (byte)0, dark ? (byte)255 : (byte)0, dark ? (byte)255 : (byte)0));
        if (!active && !hovered) return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        byte rgb = dark ? (byte)255 : (byte)0;
        bool primary = active && !hovered;
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 3)
        };
        if (!dark) brush.RelativeTransform = new ScaleTransform { CenterY = .5, ScaleY = -1 };
        brush.GradientStops.Add(new GradientStop { Offset = .33, Color = Windows.UI.Color.FromArgb(dark ? (byte)0x1A : primary ? (byte)0x17 : (byte)0x0F, rgb, rgb, rgb) });
        brush.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(dark ? (byte)0x0F : primary ? (byte)0x07 : (byte)0x05, rgb, rgb, rgb) });
        return brush;
    }

    internal static (Windows.UI.Color Fill, Windows.UI.Color Stroke, Thickness Thickness, CornerRadius Radius) Appearance(DockSettings settings, ElementTheme theme, bool visible, bool pressed = false, bool alwaysEnabled = false)
    {
        bool dark = theme == ElementTheme.Dark;
        bool taskbar = settings.WindowsTaskbarHighlights;
        visible &= alwaysEnabled || settings.ShowIconHighlights;
        bool background = visible && (settings.HighlightMode is DockHighlightMode.Background or DockHighlightMode.BackgroundAndBorder || taskbar && pressed);
        bool border = visible && settings.HighlightMode is DockHighlightMode.Border or DockHighlightMode.BackgroundAndBorder;
        // Taskbar tiles are translucent raised surfaces: light themes use
        // a white wash rather than darkening the material underneath.
        var fill = Windows.UI.Color.FromArgb(dark ? (pressed ? (byte)12 : (byte)20) : (pressed ? (byte)55 : (byte)85), 255, 255, 255);
        var stroke = dark ? Windows.UI.Color.FromArgb(taskbar ? (byte)235 : (byte)150, 255, 255, 255) : Windows.UI.Color.FromArgb(taskbar ? (byte)210 : (byte)120, 0, 0, 0);
        return (background ? fill : Microsoft.UI.Colors.Transparent, stroke, new Thickness(border ? (taskbar ? 2 : 1) : background ? 1 : 0), new CornerRadius(settings.RoundedCorners ? 4 : 0));
    }
    internal static Brush StrokeBrush(DockSettings settings, ElementTheme theme, Windows.UI.Color explicitStroke)
    {
        if (settings.HighlightMode != DockHighlightMode.Background) return new SolidColorBrush(explicitStroke);
        bool dark = theme == ElementTheme.Dark;
        var stroke = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0), EndPoint = new Windows.Foundation.Point(0, 1) };
        stroke.GradientStops.Add(new GradientStop { Offset = 0, Color = Windows.UI.Color.FromArgb(dark ? (byte)24 : (byte)120, 255, 255, 255) });
        stroke.GradientStops.Add(new GradientStop { Offset = .25, Color = Windows.UI.Color.FromArgb(dark ? (byte)12 : (byte)35, 255, 255, 255) });
        stroke.GradientStops.Add(new GradientStop { Offset = 1, Color = Windows.UI.Color.FromArgb(dark ? (byte)10 : (byte)32, 255, 255, 255) });
        return stroke;
    }
    internal static void Apply(Control control, DockSettings settings, ElementTheme theme, bool visible, bool pressed = false, bool alwaysEnabled = false)
    {
        var appearance = Appearance(settings, theme, visible, pressed, alwaysEnabled);
        control.Background = new SolidColorBrush(appearance.Fill);
        control.BorderBrush = StrokeBrush(settings, theme, appearance.Stroke);
        control.BorderThickness = appearance.Thickness;
        control.CornerRadius = appearance.Radius;
    }
}
