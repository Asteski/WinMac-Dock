using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinMac.Dock;
internal static partial class ShellService
{
    [StructLayout(LayoutKind.Sequential)] private struct PropertyKey { public Guid Format; public uint Id; public PropertyKey(string format, uint id) { Format = new Guid(format); Id = id; } }
    [StructLayout(LayoutKind.Explicit, Size=24)] private struct PropVariant { [FieldOffset(0)] public ushort Type; [FieldOffset(8)] public nint Pointer; }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count); [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value); [PreserveSig] int Commit();
    }
    [DllImport("shell32.dll")] private static extern int SHGetPropertyStoreForWindow(nint hwnd, ref Guid id, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);
    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
    public static string WindowAppId(nint hwnd)
    {
        var iid = typeof(IPropertyStore).GUID;
        if (SHGetPropertyStoreForWindow(hwnd, ref iid, out var store) != 0) return "";
        try
        {
            var key = new PropertyKey("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3", 5);
            if (store.GetValue(ref key, out var value) != 0) return "";
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) ?? "" : ""; }
            finally { PropVariantClear(ref value); }
        }
        catch { return ""; }
        finally { Marshal.ReleaseComObject(store); }
    }

    public static DockItem FromPath(string path)
    {
        var item = new DockItem { Name = Path.GetFileNameWithoutExtension(path), Target = path, Kind = Directory.Exists(path) ? ItemKind.Folder : ItemKind.File };
        var extension = Path.GetExtension(path);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)) { item.Kind = ItemKind.Application; item.ExecutablePath = path; }
        if (extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            object? scripting = null, shortcut = null, shell = null, folder = null, entry = null;
            try
            {
                scripting = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
                shortcut = ((dynamic)scripting!).CreateShortcut(path);
                item.ExecutablePath = ((dynamic)shortcut).TargetPath ?? "";
                item.Arguments = ((dynamic)shortcut).Arguments ?? "";
                item.WorkingDirectory = ((dynamic)shortcut).WorkingDirectory ?? "";
                item.Kind = Directory.Exists(item.ExecutablePath) ? ItemKind.Folder : ItemKind.Application;
                shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
                folder = ((dynamic)shell!).NameSpace(Path.GetDirectoryName(path));
                entry = ((dynamic)folder!).ParseName(Path.GetFileName(path));
                item.AppUserModelId = Convert.ToString(((dynamic)entry!).ExtendedProperty("System.AppUserModel.ID")) ?? "";
                if (item.Kind == ItemKind.Folder) item.Target = item.ExecutablePath;
            }
            catch (Exception ex) { Log.Write(ex); }
            finally { Release(entry); Release(folder); Release(shell); Release(shortcut); Release(scripting); }
        }
        return item;
    }

    public static List<DockItem> InstalledApps()
    {
        object? shell = null, folder = null, items = null;
        var result = new List<DockItem>();
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder = ((dynamic)shell!).NameSpace("shell:AppsFolder"); items = ((dynamic)folder!).Items();
            foreach (var entry in (dynamic)items!)
            {
                try
                {
                    var id = Convert.ToString(entry.ExtendedProperty("System.AppUserModel.ID")) ?? "";
                    var target = Convert.ToString(entry.Path) ?? "";
                    var name = Convert.ToString(entry.Name) ?? target;
                    if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) result.Add(FromPath(target));
                    else result.Add(new DockItem { Kind = ItemKind.Application, Name = name, Target = target, AppUserModelId = id, ExecutablePath = File.Exists(target) ? target : "" });
                }
                finally { Release(entry); }
            }
        }
        finally { Release(items); Release(folder); Release(shell); }
        return result.OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    public static void Launch(DockItem item)
    {
        if (item.Kind == ItemKind.StartMenu) { Native.OpenStartMenu(); return; }
        if (item.Kind is ItemKind.TaskView or ItemKind.Search) { Native.OpenModule(item.Kind); return; }
        using var process = Process.Start(LaunchInfo(item));
    }
    public static void LaunchAsAdministrator(DockItem item)
    {
        if (item.Kind == ItemKind.StartMenu) { Native.OpenStartMenu(); return; }
        if (item.Kind is ItemKind.TaskView or ItemKind.Search) { Native.OpenModule(item.Kind); return; }
        using var process = Process.Start(LaunchInfo(item, true));
    }
    public static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    public static void Activate(TaskWindow task, bool minimize)
    {
        if (!Native.IsWindow(task.Handle)) return;
        
        // Queue state changes on the target's own UI thread. Synchronous
        // ShowWindow pumps cross-process shell callbacks inside a dock click,
        // including animation-rectangle requests that inspect WinUI layout.
        if (minimize)
        {
            if (!Native.IsIconic(task.Handle)) Native.ShowWindowAsync(task.Handle, 6);
        }
        else { if (Native.IsIconic(task.Handle)) Native.ShowWindowAsync(task.Handle, 9); Native.SetForegroundWindow(task.Handle); }
    }
    public static void Close(TaskWindow task) => Native.PostMessage(task.Handle, 0x0010, 0, 0);
    public static void EndTask(TaskWindow task)
    {
        if (!Native.IsWindow(task.Handle)) return;
        Native.GetWindowThreadProcessId(task.Handle, out var processId);
        if (processId == 0 || processId == Environment.ProcessId) return;
        try { Process.GetProcessById((int)processId).Kill(entireProcessTree: true); }
        catch (Exception ex) { Log.Write(ex); }
    }
    public static void ShowInExplorer(TaskWindow task)
    {
        ShowInExplorer(task.ExecutablePath);
    }
    public static void ShowInExplorer(DockItem item)
    {
        ShowInExplorer(File.Exists(item.ExecutablePath) ? item.ExecutablePath : item.Target);
    }
    public static void OpenFileLocation(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try
        {
            var full = Path.GetFullPath(target.Trim().Trim('"'));
            var directory = Directory.Exists(full) ? full : Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) Open(directory);
        }
        catch (Exception ex) { Log.Write(ex); }
    }
    private static void ShowInExplorer(string path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            if (File.Exists(path))
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path.Replace("\"", "\"\"") + "\"") { UseShellExecute = true });
            else if (Directory.Exists(path)) Open(path);
        }
        catch (Exception ex) { Log.Write(ex); }
    }
    public static long RecycleCount()
    {
        var info = new Native.SHQUERYRBINFO { Size = Marshal.SizeOf<Native.SHQUERYRBINFO>() };
        return Native.SHQueryRecycleBin(null, ref info) == 0 ? info.Items : 0;
    }
    public static void PlayRecycleSound(bool empty, int volumePercent)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", empty ? "recycle.wav" : "Windows Recycle.wav");
        _ = Task.Run(() =>
        {
            try
            {
                if (!File.Exists(path)) return;
                using var stream = new MemoryStream(WaveGain.Apply(File.ReadAllBytes(path), volumePercent));
                using var player = new System.Media.SoundPlayer(stream);
                player.PlaySync();
            }
            catch (Exception ex) { Log.Write(ex); }
        });
    }
    public static bool Recycle(nint owner, IEnumerable<string> paths)
    {
        var op = new Native.SHFILEOPSTRUCT { Window = owner, Func = 3, From = string.Join('\0', paths) + "\0\0", Flags = 0x40 };
        var result = Native.SHFileOperation(ref op);
        if (result != 0 && !op.Aborted) throw new IOException($"Windows could not recycle these items (0x{result:X}).");
        return result == 0 && !op.Aborted;
    }
    public static void Transfer(nint owner, IEnumerable<string> paths, string destination, bool move)
    {
        var op = new Native.SHFILEOPSTRUCT { Window = owner, Func = move ? 1u : 2u, From = string.Join('\0', paths) + "\0\0", To = destination + "\0\0", Flags = 0 };
        var result = Native.SHFileOperation(ref op);
        if (result != 0 && !op.Aborted) throw new IOException($"Windows could not transfer these items (0x{result:X}).");
    }
    private static void Release(object? obj) { if (obj != null && Marshal.IsComObject(obj)) Marshal.ReleaseComObject(obj); }
}
