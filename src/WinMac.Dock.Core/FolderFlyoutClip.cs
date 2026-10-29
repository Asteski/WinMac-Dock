namespace WinMac.Dock.Core;

public static class FolderFlyoutClip
{
    // Native material stays stationary; XAML content moves. Their clips are
    // related by the translation, but their surface sizes may differ.
    public static PixelRect NativeRegion(int width, int height, int x, int y)
    {
        int left = Math.Clamp(x, 0, width), top = Math.Clamp(y, 0, height);
        return new(left, top, Math.Clamp(width + x, left, width), Math.Clamp(height + y, top, height));
    }

    public static PixelRect ContentRegion(int width, int height, int x, int y) =>
        NativeRegion(width, height, -x, -y);
}
