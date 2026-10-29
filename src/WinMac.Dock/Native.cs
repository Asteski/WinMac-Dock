using System.Runtime.InteropServices;
using System.Text;

namespace WinMac.Dock;
internal static class Native
{
    [ComImport, Guid("A5CD92FF-29BE-454C-8D04-D82879FB3F1B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IVirtualDesktopManager
    {
        [PreserveSig] int IsWindowOnCurrentVirtualDesktop(nint hwnd, [MarshalAs(UnmanagedType.Bool)] out bool current);
        [PreserveSig] int GetWindowDesktopId(nint hwnd, out Guid desktop);
        [PreserveSig] int MoveWindowToDesktop(nint hwnd, ref Guid desktop);
    }
    private static IVirtualDesktopManager? virtualDesktopManager;
    public static bool IsWindowOnCurrentVirtualDesktop(nint hwnd)
    {
        try
        {
            virtualDesktopManager ??= (IVirtualDesktopManager)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("AA509086-5CA9-4C25-8F95-589D3C07B48A"))!)!;
            return virtualDesktopManager.IsWindowOnCurrentVirtualDesktop(hwnd, out var current) >= 0 ? current : true;
        }
        catch { return true; }
    }
    public static Guid? WindowDesktop(nint hwnd)
    {
        try
        {
            _ = IsWindowOnCurrentVirtualDesktop(hwnd);
            if (virtualDesktopManager != null && virtualDesktopManager.GetWindowDesktopId(hwnd, out var desktop) >= 0) return desktop;
            return null;
        }
        catch { return null; }
    }
    public static bool MoveToDesktop(nint hwnd, Guid desktop)
    {
        try
        {
            _ = IsWindowOnCurrentVirtualDesktop(hwnd);
            return virtualDesktopManager?.MoveWindowToDesktop(hwnd, ref desktop) >= 0;
        }
        catch { return false; }
    }
    public static bool IsTaskViewWindow(nint hwnd)
    {
        if (hwnd == 0 || Class(hwnd) is not ("XamlExplorerHostIslandWindow" or "MultitaskingViewFrame")) return false;
        GetWindowThreadProcessId(hwnd, out var pid);
        return Path.GetFileName(ProcessPath(pid)).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
    }
    public static bool IsTaskViewOpen()
    {
        var foreground = GetForegroundWindow();
        if (IsTaskViewWindow(foreground)) taskViewWindow = foreground;
        if (taskViewWindow != 0 && IsWindow(taskViewWindow) && IsWindowVisible(taskViewWindow)
            && (DwmGetInt(taskViewWindow, 14, out int state, 4) != 0 || state == 0)) return true;
        // Keep detecting the shell surface when a dock flyout takes foreground.
        // Explorer retains this HWND after closing Task View, so visibility and
        // DWM cloaking must both be checked, rather than just its existence.
        bool open = false;
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd) || !IsTaskViewWindow(hwnd)) return true;
            if (DwmGetInt(hwnd, 14, out int cloaked, 4) == 0 && cloaked != 0) return true;
            open = true; return false;
        }, 0);
        return open;
    }
    private static nint taskViewWindow;
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetPropW(nint hwnd, string name, nint value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint GetPropW(nint hwnd, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint RemovePropW(nint hwnd, string name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowExW(nint parent, nint after, string className, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowBand(nint hwnd, out uint band);
    public static uint WindowBand(nint hwnd) => GetWindowBand(hwnd, out var band) ? band : 0;
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; public readonly PixelRect Pixels => new(Left, Top, Right, Bottom); public RECT(PixelRect p) { Left=p.Left;Top=p.Top;Right=p.Right;Bottom=p.Bottom; } }
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct MONITORINFOEX { public int Size; public RECT Monitor, Work; public uint Flags; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)] public string Device; }
    [StructLayout(LayoutKind.Sequential)] public struct APPBARDATA { public uint Size; public nint Window; public uint CallbackMessage, Edge; public RECT Rect; public nint Param; }
    [StructLayout(LayoutKind.Sequential)] public struct SHELLHOOKINFO { public nint Window; public RECT Rect; }
    [DllImport("kernel32.dll")] private static extern bool ReadProcessMemory(nint process, nint address, out SHELLHOOKINFO buffer, nuint size, out nuint read);
    [DllImport("kernel32.dll")] private static extern bool WriteProcessMemory(nint process, nint address, ref RECT buffer, nuint size, out nuint written);
    public static bool ReadShellInfo(nint address, out SHELLHOOKINFO info) => ReadProcessMemory(-1, address, out info, (nuint)Marshal.SizeOf<SHELLHOOKINFO>(), out var count) && count == (nuint)Marshal.SizeOf<SHELLHOOKINFO>();
    public static bool WriteShellRect(nint address, RECT rect) => WriteProcessMemory(-1, address + nint.Size, ref rect, (nuint)Marshal.SizeOf<RECT>(), out var count) && count == (nuint)Marshal.SizeOf<RECT>();
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct SHFILEINFO { public nint Icon; public int IconIndex; public uint Attributes; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)] public string DisplayName; [MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)] public string TypeName; }
    [StructLayout(LayoutKind.Sequential)] public struct SHQUERYRBINFO { public int Size; public long Bytes, Items; }
    [StructLayout(LayoutKind.Sequential, CharSet=CharSet.Unicode)] public struct SHFILEOPSTRUCT { public nint Window; public uint Func; public string From; public string? To; public ushort Flags; [MarshalAs(UnmanagedType.Bool)] public bool Aborted; public nint Mappings; public string? Title; }
    public delegate bool EnumWindowsProc(nint hwnd, nint param);
    public delegate bool MonitorProc(nint monitor, nint dc, ref RECT rect, nint data);
    public delegate nint SubclassProc(nint hwnd, uint msg, nuint wparam, nint lparam, nuint id, nuint data);
    public delegate void WinEventProc(nint hook, uint evt, nint hwnd, int obj, int child, uint thread, uint time);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, nint data);
    [DllImport("user32.dll")] public static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(nint hwnd);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint hwnd, uint command);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetWindowText(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(nint hwnd, StringBuilder text, int count);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint LoadCursor(nint instance, nint cursor);
    [DllImport("user32.dll")] public static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("shcore.dll")] public static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] public static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorProc callback, nint data);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern bool GetMonitorInfo(nint monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll")] public static extern nint GetWindowLongPtrW(nint hwnd, int index);
    [DllImport("user32.dll")] public static extern nint SetWindowLongPtrW(nint hwnd, int index, nint value);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<nint, byte> passiveWindows = new();
    internal static void RegisterPassiveWindow(nint window) => passiveWindows[window] = 0;
    internal static void UnregisterPassiveWindow(nint window) => passiveWindows.TryRemove(window, out _);
    [DllImport("user32.dll", EntryPoint = "SetWindowPos")] private static extern bool SetWindowPosNative(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    public static bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags)
    {
        if (passiveWindows.ContainsKey(hwnd))
        {
            flags |= 0x10u;
        }
        return SetWindowPosNative(hwnd, after, x, y, width, height, flags);
    }
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int command);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(nint hwnd, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SystemParametersInfoW(uint action, uint parameter, ref RECT value, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool PostMessage(nint hwnd, uint msg, nuint wparam, nint lparam);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern nint SendMessageTimeout(nint hwnd, uint msg, nuint wparam, nint lparam, uint flags, uint timeout, out nint result);
    [DllImport("user32.dll", CharSet=CharSet.Unicode, EntryPoint="SendMessageTimeoutW")] private static extern nint SendMessageTimeoutText(nint hwnd, uint msg, nuint wparam, string lparam, uint flags, uint timeout, out nint result);
    // Explorer uses this private tray setting message for live edge changes.
    // Validate the resulting position; patched shells may not implement it.
    public static bool SetTaskbarEdge(nint window, uint edge) =>
        SendMessageTimeout(window, 0x0400 + 0x1CA, 6, (nint)edge, 2, 1000, out _) != 0;
    public static void BroadcastWorkAreaChange() => SendMessageTimeoutText(0xffff, 0x001A, 0x002F, "TraySettings", 2, 200, out _);
    [DllImport("user32.dll", EntryPoint="GetClassLongPtrW")] private static extern nint GetClassLongPtr(nint hwnd, int index);
    [DllImport("user32.dll")] public static extern nint CopyIcon(nint icon);
    public static nint WindowIcon(nint hwnd)
    {
        // Query the same per-window artwork Windows uses for the task switcher.
        // A short timeout keeps an unresponsive app from delaying a dock menu.
        const uint wmGetIcon = 0x007F;
        foreach (var type in new nuint[] { 2, 0, 1 })
            if (SendMessageTimeout(hwnd, wmGetIcon, type, 0, 2, 100, out var icon) != 0 && icon != 0) return icon;
        return GetClassLongPtr(hwnd, -34) is var small && small != 0 ? small : GetClassLongPtr(hwnd, -14);
    }
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] public static extern bool RegisterShellHookWindow(nint hwnd);
    [DllImport("user32.dll")] public static extern bool DeregisterShellHookWindow(nint hwnd);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(nint icon);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out RECT value, int size);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] public static extern int DwmGetInt(nint hwnd, uint attribute, out int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref int value, int size);
    [StructLayout(LayoutKind.Sequential)] private struct GlassMargins { public int Left, Right, Top, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct BlurBehind { public uint Flags; [MarshalAs(UnmanagedType.Bool)] public bool Enabled; public nint Region; [MarshalAs(UnmanagedType.Bool)] public bool Transition; }
    [DllImport("dwmapi.dll")] private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref GlassMargins margins);
    [DllImport("dwmapi.dll")] private static extern int DwmEnableBlurBehindWindow(nint hwnd, ref BlurBehind blur);
    [DllImport("gdi32.dll")] private static extern nint CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint value);
    [DllImport("user32.dll")] private static extern int SetWindowRgn(nint window, nint region, bool redraw);
    [DllImport("gdi32.dll")] private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    public static void ClearDockRegion(nint window) => SetWindowRgn(window, 0, true);
    public static void SetDockRegion(nint window, PixelRect bounds, int radius = 0)
    {
        var region = radius > 0 ? CreateRoundRectRgn(bounds.Left, bounds.Top, bounds.Right + 1, bounds.Bottom + 1, radius * 2, radius * 2)
            : CreateRectRgn(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        // Windows owns the region after a successful SetWindowRgn call.
        // The retained composition surface already contains the newly exposed
        // pixels. Avoid a synchronous GDI redraw on every clipping update.
        if (SetWindowRgn(window, region, false) == 0 && region != 0) DeleteObject(region);
    }
    [DllImport("gdi32.dll")] private static extern nint GetStockObject(int kind);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out RECT rect);
    public static PixelRect ClientBounds(nint hwnd) => GetClientRect(hwnd, out var rect) ? rect.Pixels : default;
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
    [DllImport("user32.dll")] private static extern int FillRect(nint dc, ref RECT rect, nint brush);

    public static void ConfigureCompositedClient(nint hwnd)
    {
        // Transparent hover hosts have no native frame. The material window
        // retains WS_THICKFRAME and stays unregioned for its native DWM shadow.
        var rendering = (GetWindowLongPtrW(hwnd, -16).ToInt64() & 0x00040000L) != 0 ? 2 : 1;
        DwmSetWindowAttribute(hwnd, 2, ref rendering, 4);
        var margins = rendering == 2 ? new GlassMargins { Left = 1, Right = 1, Top = 1, Bottom = 1 } : new GlassMargins();
        DwmExtendFrameIntoClientArea(hwnd, ref margins);
        var region = CreateRectRgn(0, 0, 0, 0);
        try
        {
            // An empty blur region enables client alpha without adding a second
            // blur effect. The material controller supplies the actual backdrop.
            var blur = new BlurBehind { Flags = 3, Enabled = true, Region = region };
            Marshal.ThrowExceptionForHR(DwmEnableBlurBehindWindow(hwnd, ref blur));
        }
        finally { if (region != 0) DeleteObject(region); }
        var dc = GetDC(hwnd);
        try { ClearCompositedClient(hwnd, dc); } finally { if (dc != 0) ReleaseDC(hwnd, dc); }
    }
    public static bool ClearCompositedClient(nint hwnd, nint dc)
    {
        // GDI's default white erase paints an opaque native surface underneath
        // WinUI. Black erase leaves zero alpha for composition to blend correctly.
        return dc != 0 && GetClientRect(hwnd, out var rect) && FillRect(dc, ref rect, GetStockObject(4)) != 0;
    }
    [DllImport("comctl32.dll")] public static extern bool SetWindowSubclass(nint hwnd, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] public static extern bool RemoveWindowSubclass(nint hwnd, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] public static extern nint DefSubclassProc(nint hwnd, uint msg, nuint wp, nint lp);
    [StructLayout(LayoutKind.Sequential)] public struct DispatcherQueueOptions { public int Size, ThreadType, ApartmentType; }
    [DllImport("CoreMessaging.dll")] public static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint controller);
    [DllImport("shell32.dll")] public static extern nuint SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern nint SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern int SHQueryRecycleBin(string? root, ref SHQUERYRBINFO info);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern int SHEmptyRecycleBin(nint hwnd, string? root, uint flags);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] public static extern int SHFileOperation(ref SHFILEOPSTRUCT operation);
    public static void OpenModule(ItemKind kind)
    {
        TaskbarModuleTrigger.Open(kind);
    }
    public static void OpenStartMenu()
    {
        const uint wmSysCommand = 0x0112;
        const nuint scTaskList = 0xF130;
        var taskbar = FindWindowExW(0, 0, "Shell_TrayWnd", null);
        if (taskbar == 0) throw new InvalidOperationException("Windows Explorer's taskbar is not available to open Start.");
        if (!PostMessage(taskbar, wmSysCommand, scTaskList, 0))
            throw new InvalidOperationException("Windows Explorer could not receive the Start menu command.");
    }
    [DllImport("shell32.dll")] public static extern int SHGetKnownFolderPath(ref Guid id, uint flags, nint token, out nint path);
    [DllImport("kernel32.dll")] public static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref int length);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] public static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder? id);

    public static string ProcessPath(uint pid)
    {
        var handle = OpenProcess(0x1000, false, pid); if (handle == 0) return "";
        try { var b = new StringBuilder(4096); var size = b.Capacity; return QueryFullProcessImageName(handle, 0, b, ref size) ? b.ToString() : ""; }
        finally { CloseHandle(handle); }
    }
    public static string ProcessAppId(uint pid)
    {
        var h = OpenProcess(0x1000, false, pid); if (h == 0) return "";
        try { uint size = 0; GetApplicationUserModelId(h, ref size, null); if (size == 0 || size > 4096) return ""; var b = new StringBuilder((int)size); return GetApplicationUserModelId(h, ref size, b) == 0 ? b.ToString() : ""; }
        finally { CloseHandle(h); }
    }
    public static PixelRect Bounds(nint hwnd) { if (DwmGetWindowAttribute(hwnd, 9, out var r, 16) != 0) GetWindowRect(hwnd, out r); return r.Pixels; }
    public static string Title(nint hwnd) { var b = new StringBuilder(512); GetWindowText(hwnd, b, b.Capacity); return b.ToString(); }
    public static string Class(nint hwnd) { var b = new StringBuilder(256); GetClassName(hwnd, b, b.Capacity); return b.ToString(); }
    public static string Downloads()
    {
        var id = new Guid("374DE290-123F-4565-9164-39C4925E467B");
        if (SHGetKnownFolderPath(ref id, 0, 0, out var p) != 0) return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        try { return Marshal.PtrToStringUni(p)!; } finally { Marshal.FreeCoTaskMem(p); }
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayDevice
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice display, uint flags);
    private static string MonitorIdentity(string device)
    {
        var display = new DisplayDevice { Size = Marshal.SizeOf<DisplayDevice>(), Name = "", Description = "", Id = "", Key = "" };
        return EnumDisplayDevices(device, 0, ref display, 1) && !string.IsNullOrEmpty(display.Id) ? display.Id : device;
    }
    public static List<MonitorData> Monitors()
    {
        var result = new List<MonitorData>();
        EnumDisplayMonitors(0, 0, (nint h, nint dc, ref RECT r, nint p) => { var info = new MONITORINFOEX { Size = Marshal.SizeOf<MONITORINFOEX>(), Device = "" }; GetMonitorInfo(h, ref info); GetDpiForMonitor(h, 0, out var dpi, out _); result.Add(new(h, info.Device, info.Monitor.Pixels, info.Work.Pixels, (info.Flags & 1) != 0, dpi == 0 ? 1 : dpi / 96d) { Identity = MonitorIdentity(info.Device) }); return true; }, 0);
        return result;
    }
}
internal sealed record MonitorData(nint Handle, string Device, PixelRect Bounds, PixelRect Work, bool Primary, double Scale)
{
    public string Identity { get; init; } = Device;
    public string Label => "Display " + string.Concat(Device.Reverse().TakeWhile(char.IsDigit).Reverse());
}
