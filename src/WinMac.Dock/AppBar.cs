using System.Runtime.InteropServices;

namespace WinMac.Dock;
internal sealed class AppBar(nint window) : IDisposable
{
    public readonly uint Callback = Native.RegisterWindowMessage("WinMac.Dock.AppBar");
    private bool registered;
    public bool IsRegistered => registered;
    private PixelRect lastRect;
    public PixelRect Area { get; private set; }
    public void Update(MonitorData monitor, int thickness, bool reserve, DockEdge edge)
    {
        if (!reserve) { Dispose(); Area = monitor.Work; return; }
        var data = new Native.APPBARDATA { Size = (uint)Marshal.SizeOf<Native.APPBARDATA>(), Window = window, CallbackMessage = Callback, Edge = edge == DockEdge.Left ? 0u : edge == DockEdge.Right ? 2u : edge == DockEdge.Top ? 1u : 3u, Rect = new(monitor.Bounds) };
        if (!registered)
        {
            if (Native.SHAppBarMessage(0, ref data) == 0) { Area = monitor.Work; return; }
            registered = true;
        }
        SetThickness(ref data.Rect, thickness, edge);
        Native.SHAppBarMessage(2, ref data);
        SetThickness(ref data.Rect, thickness, edge);
        if (data.Rect.Pixels != lastRect) { Native.SHAppBarMessage(3, ref data); lastRect = data.Rect.Pixels; }
        Area = data.Rect.Pixels;
    }
    private static void SetThickness(ref Native.RECT rect, int thickness, DockEdge edge)
    {
        if (edge == DockEdge.Top) rect.Bottom = rect.Top + thickness;
        else if (edge == DockEdge.Left) rect.Right = rect.Left + thickness;
        else if (edge == DockEdge.Right) rect.Left = rect.Right - thickness;
        else rect.Top = rect.Bottom - thickness;
    }
    public void Dispose()
    {
        if (!registered) return;
        registered = false;
        lastRect = default;
        var data = new Native.APPBARDATA { Size = (uint)Marshal.SizeOf<Native.APPBARDATA>(), Window = window };
        Native.SHAppBarMessage(1, ref data);
    }
}
