using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task CheckSeparatorPixels()
    {
        bool savedInspection = inspection;
        var results = new List<object>();
        var separators = strip.Children.OfType<Border>().ToArray();
        string? error = null;
        try
        {
            inspection = true; ResetMagnification(); ShowDock(); root.UpdateLayout(); SnapRestingSeparators();
            await Task.Delay(200);
            var screen = Native.Bounds(Handle);
            using var before = new System.Drawing.Bitmap(screen.Width, screen.Height);
            using (var graphics = System.Drawing.Graphics.FromImage(before))
                graphics.CopyFromScreen(screen.Left, screen.Top, 0, 0, before.Size);
            foreach (var separator in separators)
                if (separatorStrokes.TryGetValue(separator, out var stroke)) stroke.Opacity = 0;
            await Task.Delay(100);
            using var after = new System.Drawing.Bitmap(screen.Width, screen.Height);
            using (var graphics = System.Drawing.Graphics.FromImage(after))
                graphics.CopyFromScreen(screen.Left, screen.Top, 0, 0, after.Size);
            foreach (var separator in separators)
            {
                if (!separatorStrokes.TryGetValue(separator, out var stroke)) continue;
                var origin = separator.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                double x = (origin.X + stroke.Offset.X) * scale, y = (origin.Y + stroke.Offset.Y) * scale;
                var bands = new List<object>();
                int changedBands = 0;
                for (int band = -2; band <= 3; band++)
                {
                    int difference = 0, samples = 0;
                    for (int along = 3; along < (Vertical ? stroke.Size.X : stroke.Size.Y) * scale - 3; along++)
                    {
                        int px = (int)Math.Floor(x) + (Vertical ? along : band);
                        int py = (int)Math.Floor(y) + (Vertical ? band : along);
                        if (px < 0 || py < 0 || px >= screen.Width || py >= screen.Height) continue;
                        var a = before.GetPixel(px, py); var b = after.GetPixel(px, py);
                        difference += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B); samples++;
                    }
                    double mean = samples == 0 ? 0 : difference / (samples * 3d);
                    if (mean > 3) changedBands++;
                    bands.Add(new { Band = band, MeanChannelDifference = mean });
                }
                results.Add(new { OriginX = origin.X, OriginY = origin.Y, PaintedX = x, PaintedY = y,
                    WidthPixels = stroke.Size.X * scale, HeightPixels = stroke.Size.Y * scale, ChangedBands = changedBands, Bands = bands });
            }
        }
        catch (Exception ex) { error = ex.ToString(); }
        finally
        {
            foreach (var separator in separators)
                if (separatorStrokes.TryGetValue(separator, out var stroke)) stroke.Opacity = 1;
            inspection = savedInspection;
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "separator-pixels.json"), System.Text.Json.JsonSerializer.Serialize(
            new { Edge = PositionEdge.ToString(), Scale = scale, DisplayIconSize, Error = error, Results = results },
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
