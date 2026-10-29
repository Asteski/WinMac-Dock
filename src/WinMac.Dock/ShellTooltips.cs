using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal static class ShellTooltips
{
    public static void Attach(FrameworkElement element, string path)
    {
        var tip = new ToolTip { Content = System.IO.Path.GetFileName(path) };
        tip.Opened += async (_, _) =>
        {
            string text = await Task.Run(() => Read(path));
            tip.Content = text;
        };
        ToolTipService.SetToolTip(element, tip);
    }
    internal static string Read(string path)
    {
        IShellItem? item = null;
        object? handler = null;
        IntPtr text = IntPtr.Zero;
        try
        {
            var iid = typeof(IShellItem).GUID;
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item));
            var bhid = new Guid("3981E225-F559-11D3-8E3A-00C04F6837D5");
            iid = typeof(IQueryInfo).GUID;
            item.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out handler);
            ((IQueryInfo)handler).GetInfoTip(0, out text);
            return Marshal.PtrToStringUni(text) is { Length: > 0 } value ? value : System.IO.Path.GetFileName(path);
        }
        catch (Exception) { return System.IO.Path.GetFileName(path); }
        finally
        {
            if (text != IntPtr.Zero) Marshal.FreeCoTaskMem(text);
            if (handler != null && Marshal.IsComObject(handler)) Marshal.ReleaseComObject(handler);
            if (item != null && Marshal.IsComObject(item)) Marshal.ReleaseComObject(item);
        }
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid bhid, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object handler);
    }
    [ComImport, Guid("00021500-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IQueryInfo
    {
        void GetInfoTip(uint flags, out IntPtr text);
        void GetInfoFlags(out uint flags);
    }
}
