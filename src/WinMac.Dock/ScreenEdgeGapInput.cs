using System.Runtime.InteropServices;

namespace WinMac.Dock;

// Polling can mirror a click but cannot stop Explorer from receiving it. Consume
// just the right-button pair in the enabled dock gap; all other input passes on.
internal sealed class ScreenEdgeGapInput : IDisposable
{
    private delegate nint MouseHook(int code, nint message, nint data);
    private readonly MouseHook callback;
    private nint hook;
    private bool captured;
    public ScreenEdgeGapInput(Func<Native.POINT, bool> accepts, Action<Native.POINT> clicked)
    {
        callback = (code, message, data) =>
        {
            if (code >= 0)
            {
                if (message == 0x0205)
                {
                    bool consume = captured || accepts(Marshal.PtrToStructure<Native.POINT>(data));
                    captured = false;
                    if (consume) return 1;
                }
                if (message == 0x0204)
                {
                    var point = Marshal.PtrToStructure<Native.POINT>(data);
                    if (accepts(point))
                    {
                        captured = true;
                        clicked(point);
                        return 1;
                    }
                }
            }
            return CallNextHookEx(hook, code, message, data);
        };
        hook = SetWindowsHookExW(14, callback, GetModuleHandleW(null), 0);
        if (hook == 0) Log.Write(new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()));
    }
    public void Dispose()
    {
        if (hook != 0) UnhookWindowsHookEx(hook);
        hook = 0;
        GC.KeepAlive(callback);
    }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookExW(int id, MouseHook callback, nint module, uint thread);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandleW(string? name);
}
