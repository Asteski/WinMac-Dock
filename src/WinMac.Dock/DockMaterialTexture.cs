using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private Image? materialGrain;
    private (int Width, int Height) materialGrainSize;
    private void UpdateMaterialGrain(PixelRect panel)
    {
        if (materialRoot == null) return;
        materialGrain ??= new Image
        {
            IsHitTestVisible = false, Stretch = Stretch.Fill, Opacity = .5,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top
        };
        if (materialGrain.Parent == null) materialRoot.Children.Add(materialGrain);
        bool show = Settings.Material == DockMaterial.Default && !accessibility.HighContrast
            && uiSettings.AdvancedEffectsEnabled && materialBackdrop?.HasActiveAcrylic == true;
        materialGrain.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) return;
        // Cache a strip long enough for this display: magnification changes only
        // the native viewport, avoiding texture regeneration during hover frames.
        int width = Math.Max(1, Vertical ? panel.Width : monitor.Bounds.Width);
        int height = Math.Max(1, Vertical ? monitor.Bounds.Height : panel.Height);
        if (materialGrainSize != (width, height))
        {
            var bitmap = new WriteableBitmap(width, height);
            var pixels = new byte[checked(width * height * 4)];
            uint noise = 0x6D2B79F5;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                noise ^= noise << 13; noise ^= noise >> 17; noise ^= noise << 5;
                byte alpha = (byte)(noise % 5);
                byte tint = (noise & 0x100) != 0 ? alpha : (byte)0;
                pixels[i] = pixels[i + 1] = pixels[i + 2] = tint; // Premultiplied monochrome noise.
                pixels[i + 3] = alpha;
            }
            using (var stream = bitmap.PixelBuffer.AsStream()) stream.Write(pixels, 0, pixels.Length);
            bitmap.Invalidate();
            materialGrain.Source = bitmap;
            materialGrainSize = (width, height);
        }
        materialGrain.Width = width / scale;
        materialGrain.Height = height / scale;
    }
}
