using System.ComponentModel;
using System.Runtime.InteropServices;

namespace WinMac.Dock;

// A per-pixel alpha surface: no XAML island, backdrop, border, or input region.
internal sealed class LayeredDragIcon : IDisposable
{
    private nint handle, dc, bitmap, previous;
    private readonly int width, height;
    private POINT position;
    public LayeredDragIcon(byte[] pixels, int width, int height)
    {
        this.width = width; this.height = height;
        try
        {
            if (width <= 0 || height <= 0 || pixels.Length != checked(width * height * 4))
                throw new ArgumentException("Invalid icon pixel buffer.");
            handle = CreateWindowExW(0x080800A8, "Static", "", 0x88000000, 0, 0, width, height, 0, 0, 0, 0);
            if (handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            Native.RegisterPassiveWindow(handle);
            dc = CreateCompatibleDC(0);
            if (dc == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            var info = new BITMAPINFO { Size = 40, Width = width, Height = -height, Planes = 1, Bits = 32 };
            bitmap = CreateDIBSection(dc, ref info, 0, out var bits, 0, 0);
            if (bitmap == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
            previous = SelectObject(dc, bitmap);
            // RenderTargetBitmap supplies premultiplied BGRA, as required by ULW_ALPHA.
            Marshal.Copy(pixels, 0, bits, pixels.Length);
        }
        catch { Dispose(); throw; }
    }
    public void Show(int x, int y)
    {
        position = new POINT { X = x, Y = y };
        SetOpacity(255);
        Native.SetWindowPos(handle, -1, x, y, width, height, 0x10 | 0x40);
    }
    public void Hide() => Native.ShowWindow(handle, 0);
    public void SetOpacity(byte opacity)
    {
        var size = new POINT { X = width, Y = height };
        var origin = new POINT();
        var blend = new BLEND { Alpha = opacity, Format = 1 };
        if (!UpdateLayeredWindow(handle, 0, ref position, ref size, dc, ref origin, 0, ref blend, 2))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void Dispose()
    {
        if (dc != 0 && previous != 0) SelectObject(dc, previous);
        if (bitmap != 0) DeleteObject(bitmap);
        if (dc != 0) DeleteDC(dc);
        if (handle != 0) { Native.UnregisterPassiveWindow(handle); DestroyWindow(handle); }
        handle = dc = bitmap = previous = 0;
    }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct BLEND { public byte Operation, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] private struct BITMAPINFO
    {
        public uint Size; public int Width, Height; public ushort Planes, Bits;
        public uint Compression, ImageSize; public int XPels, YPels; public uint Colors, Important, Palette;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(nint hwnd, nint destinationDC, ref POINT position, ref POINT size, nint sourceDC, ref POINT source, uint colorKey, ref BLEND blend, uint flags);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll", SetLastError = true)] private static extern nint CreateDIBSection(nint dc, ref BITMAPINFO info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
}
