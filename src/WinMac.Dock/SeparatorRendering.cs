using System.Numerics;
using System.Runtime.CompilerServices;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private static readonly ConditionalWeakTable<Border, SpriteVisual> separatorStrokes = new();

    private void InitializeSeparatorStroke(Border separator)
    {
        SynchronizeSeparatorGeometry(separator);
        separator.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        var compositor = ElementCompositionPreview.GetElementVisual(separator).Compositor;
        var stroke = compositor.CreateSpriteVisual();
        // XAML parents can have fractional origins after crowded-dock fitting.
        // Snap in the compositor too, and prevent inherited soft-edge sampling
        // from spreading a single physical pixel over adjacent rows/columns.
        stroke.IsPixelSnappingEnabled = true;
        stroke.BorderMode = CompositionBorderMode.Hard;
        stroke.Brush = compositor.CreateColorBrush(Microsoft.UI.Colors.Gray);
        separatorStrokes.Add(separator, stroke);
        ElementCompositionPreview.SetElementChildVisual(separator, stroke);
        separator.Loaded += (_, _) => UpdateSeparatorStroke(separator);
        separator.SizeChanged += (_, _) => UpdateSeparatorStroke(separator);
    }

    private void SynchronizeSeparatorGeometry(Border separator)
    {
        separator.UseLayoutRounding = false;
        separator.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
        separator.Width = Vertical ? CurrentDockThickness * .7 : SeparatorWidth;
        separator.Height = Vertical ? SeparatorWidth : CurrentDockThickness * .7;
    }

    private void UpdateSeparatorStroke(Border separator)
    {
        if (!separator.IsLoaded || scale <= 0) return;
        var origin = separator.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        SnapSeparator(separator, origin.X, origin.Y);
    }

    private void SnapSeparator(Border separator, double originX, double originY)
    {
        if (!separatorStrokes.TryGetValue(separator, out var stroke) || scale <= 0) return;
        var bounds = SeparatorGeometry.CenteredStroke(originX, originY, separator.ActualWidth, separator.ActualHeight, Vertical, scale);
        // Draw a solid rectangle without resampling the XAML Border background.
        // Opacity remains on the existing separator host.
        stroke.Offset = new Vector3((float)bounds.X, (float)bounds.Y, 0);
        stroke.Size = new Vector2((float)bounds.Width, (float)bounds.Height);
    }

    private SeparatorStroke SeparatorScreenStroke(Border separator)
    {
        var origin = separator.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        var stroke = separatorStrokes.GetValue(separator, _ => throw new InvalidOperationException("Missing separator stroke"));
        return new(hostBounds.Left + (origin.X + stroke.Offset.X) * scale,
            hostBounds.Top + (origin.Y + stroke.Offset.Y) * scale, stroke.Size.X * scale, stroke.Size.Y * scale);
    }
    private Border? RemovableSeparatorAt(Native.POINT point)
    {
        var separators = strip.Children.OfType<Border>()
            .Where(separator => separator.Tag is DockEntry { Item.Kind: ItemKind.Separator } && separatorStrokes.TryGetValue(separator, out _)).ToArray();
        int index = SeparatorGeometry.NearestHit(separators.Select(SeparatorScreenStroke).ToArray(), point.X, point.Y, 6);
        return index < 0 ? null : separators[index];
    }
    private readonly Microsoft.UI.Xaml.Media.TranslateTransform stripPixelShift = new();
    private void AlignRestingStrip()
    {
        if (!strip.IsLoaded || strip.ActualWidth <= 0 || scale <= 0) return;
        strip.RenderTransform = stripPixelShift;
        var origin = strip.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        double x = origin.X - stripPixelShift.X, y = origin.Y - stripPixelShift.Y;
        double targetX = (resting.Left - hostBounds.Left) / scale;
        double targetY = (resting.Top - hostBounds.Top) / scale;
        // Full-width and scrolling rows retain their chosen content alignment.
        // Compact rows share the native material's origin, not XAML's half-pixel center.
        stripPixelShift.X = !laidOutSpanning && !dockOverflow ? targetX - x : Math.Round(x * scale) / scale - x;
        stripPixelShift.Y = !laidOutSpanning && !dockOverflow ? targetY - y : Math.Round(y * scale) / scale - y;
    }
    private void SnapRestingSeparators()
    {
        if (closing || scale <= 0 || hoverWave != null || hoverRebuilding || rowAnimating || rowStaging || rowFinishing) return;
        AlignRestingStrip();
        foreach (var separator in strip.Children.OfType<Border>()) UpdateSeparatorStroke(separator);
    }
}
