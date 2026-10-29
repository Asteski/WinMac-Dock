namespace WinMac.Dock.Core;

public readonly record struct SeparatorStroke(double X, double Y, double Width, double Height);

public static class SeparatorGeometry
{
    public static int NearestHit(IReadOnlyList<SeparatorStroke> strokes, double x, double y, double radius)
    {
        int nearest = -1;
        double best = radius * radius;
        for (int i = 0; i < strokes.Count; i++)
        {
            var stroke = strokes[i];
            double dx = Math.Max(0, Math.Max(stroke.X - x, x - stroke.X - stroke.Width));
            double dy = Math.Max(0, Math.Max(stroke.Y - y, y - stroke.Y - stroke.Height));
            double distance = dx * dx + dy * dy;
            if (distance <= best && (nearest < 0 || distance < best)) { nearest = i; best = distance; }
        }
        return nearest;
    }
    public static SeparatorStroke CenteredStroke(double originX, double originY, double width, double height, bool verticalDock, double scale)
    {
        double inset = Math.Max(0, ((verticalDock ? height : width) - 1 / scale) / 2);
        return Stroke(originX, originY, width, height, verticalDock ? 0 : inset, verticalDock ? inset : 0, verticalDock, scale);
    }

    public static SeparatorStroke Stroke(double originX, double originY, double width, double height,
        double insetX, double insetY, bool verticalDock, double scale)
    {
        double thin = 1 / scale;
        double x = Math.Round((originX + insetX) * scale) / scale - originX;
        double y = Math.Round((originY + insetY) * scale) / scale - originY;
        return new(x, y, verticalDock ? Math.Max(1, Math.Round(width * scale)) / scale : thin,
            verticalDock ? thin : Math.Max(1, Math.Round(height * scale)) / scale);
    }
}
