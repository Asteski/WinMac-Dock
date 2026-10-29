using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace WinMac.Dock;

internal static class TaskbarModuleTrigger
{
    // UI Automation must run off the WinUI thread: the shell provider can call
    // back into application accessibility while servicing a request.
    private static readonly BlockingCollection<Action<TaskbarButtonClient>> requests = new();
    private sealed record ButtonStates(int Known, int Active);
    private static ButtonStates buttonStates = new(0, 0);
    private static int readingStates;
    private static long lastStateRead;
    static TaskbarModuleTrigger()
    {
        var thread = new Thread(() =>
        {
            using var client = new TaskbarButtonClient();
            foreach (var request in requests.GetConsumingEnumerable())
                try { request(client); }
                catch (Exception ex) { Log.Write(ex); }
        }) { IsBackground = true, Name = "Taskbar shell actions" };
        thread.SetApartmentState(ApartmentState.MTA);
        thread.Start();
    }

    public static void WarmUp() => requests.Add(client => client.WarmUp());
    public static bool? IsActive(ItemKind kind)
    {
        var snapshot = Volatile.Read(ref buttonStates);
        int bit = 1 << (int)kind;
        return (snapshot.Known & bit) == 0 ? null : (snapshot.Active & bit) != 0;
    }
    public static void RefreshStates()
    {
        if (Environment.TickCount64 - Interlocked.Read(ref lastStateRead) < 80 || Interlocked.CompareExchange(ref readingStates, 1, 0) != 0) return;
        requests.Add(client =>
        {
            try
            {
                var result = client.ReadStates();
                Volatile.Write(ref buttonStates, new(result.Known, result.Active));
            }
            finally { Interlocked.Exchange(ref lastStateRead, Environment.TickCount64); Volatile.Write(ref readingStates, 0); }
        });
    }
    public static void Open(ItemKind kind)
    {
        if (kind is not (ItemKind.Search or ItemKind.TaskView)) throw new ArgumentOutOfRangeException(nameof(kind));
        requests.Add(client => client.Toggle(kind));
    }
}

internal sealed class TaskbarButtonClient : IDisposable
{
    private IUIAutomation? automation;
    private IAppVisibility? appVisibility;
    private nint taskbar;
    private uint taskbarProcess;
    private readonly Dictionary<ItemKind, IUIAutomationTogglePattern> buttons = new();

    public void WarmUp()
    {
        // Missing/disabled buttons are allowed at startup. Resolve them again
        // on demand after taskbar settings change or Explorer restarts.
        foreach (var kind in new[] { ItemKind.StartMenu, ItemKind.Search, ItemKind.TaskView })
            try { _ = Resolve(kind); }
            catch (InvalidOperationException) { }
            catch (Exception ex) { Log.Write(ex); }
    }

    public void Toggle(ItemKind kind)
    {
        try
        {
            try { Resolve(kind).Toggle(); }
            catch (COMException ex) when (ex.HResult is unchecked((int)0x80040201) or unchecked((int)0x80010108))
            {
                ClearButtons(); Resolve(kind).Toggle();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // Hidden/disabled taskbar buttons have no UIA element. Invoke the
            // shell action directly instead of requiring the taskbar control.
            ClearButtons();
            OpenShellAction(kind);
        }
    }

    internal static void OpenShellAction(ItemKind kind)
    {
        object? shell = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            if (kind == ItemKind.Search) ((dynamic)shell!).SearchCommand();
            else if (kind == ItemKind.TaskView) ((dynamic)shell!).WindowSwitcher();
            else throw new ArgumentOutOfRangeException(nameof(kind));
        }
        finally { Release(shell); }
    }

    public (int Known, int Active) ReadStates()
    {
        int known = 0, active = 0;
        bool? startVisible = ReadStartVisibility();
        foreach (var kind in new[] { ItemKind.StartMenu, ItemKind.Search, ItemKind.TaskView })
        {
            try
            {
                if (kind == ItemKind.StartMenu && startVisible is bool shown)
                {
                    known |= 1 << (int)kind;
                    if (shown) active |= 1 << (int)kind;
                    continue;
                }
                int state = Resolve(kind).CurrentToggleState;
                if (state is not (0 or 1)) continue;
                known |= 1 << (int)kind;
                if (state == 1 && !(kind == ItemKind.Search && startVisible == true)) active |= 1 << (int)kind;
            }
            catch (COMException) { ClearButtons(); }
            catch (InvalidOperationException)
            {
                if (kind is not (ItemKind.Search or ItemKind.TaskView)) continue;
                known |= 1 << (int)kind;
                bool visible = kind == ItemKind.TaskView ? Native.IsTaskViewOpen() : startVisible != true && IsSearchActive();
                if (visible) active |= 1 << (int)kind;
            }
        }
        return (known, active);
    }

    private bool? ReadStartVisibility()
    {
        try
        {
            appVisibility ??= (IAppVisibility)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("7E5FE3D9-985F-4908-91F9-EE19F9FD1514"))!)!;
            return appVisibility.IsLauncherVisible(out bool visible) >= 0 ? visible : null;
        }
        catch (COMException) { Release(appVisibility); appVisibility = null; return null; }
    }

    internal static bool IsSearchActive()
    {
        Native.GetWindowThreadProcessId(Native.GetForegroundWindow(), out var process);
        return Path.GetFileName(Native.ProcessPath(process)).Equals("SearchHost.exe", StringComparison.OrdinalIgnoreCase);
    }

    private IUIAutomationTogglePattern Resolve(ItemKind kind)
    {
        var window = Native.FindWindowExW(0, 0, "Shell_TrayWnd", null);
        if (window == 0) throw new InvalidOperationException("Windows Explorer's taskbar is not available.");
        Native.GetWindowThreadProcessId(window, out var process);
        if (window != taskbar || process != taskbarProcess)
        {
            ClearButtons();
            taskbar = window; taskbarProcess = process;
        }
        if (buttons.TryGetValue(kind, out var cached)) return cached;

        automation ??= (IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(
            new Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E"))!)!;
        IUIAutomationElement? root = null, button = null;
        object? condition = null, pattern = null;
        try
        {
            root = automation.ElementFromHandle(window);
            string id = kind switch
            {
                ItemKind.StartMenu => "StartButton",
                ItemKind.Search => "SearchButton",
                ItemKind.TaskView => "TaskViewButton",
                _ => throw new ArgumentOutOfRangeException(nameof(kind))
            };
            condition = automation.CreatePropertyCondition(30011, id); // UIA_AutomationIdPropertyId
            button = root.FindFirst(4, condition); // TreeScope_Descendants, scoped to the taskbar
            if (button == null) throw new InvalidOperationException($"Enable the Windows taskbar's {kind} button to use this action.");
            // REA verified both Windows 11 taskbar controls expose Toggle,
            // not Invoke. This runs the button's own shell activation path.
            pattern = button.GetCurrentPattern(10015); // UIA_TogglePatternId
            if (pattern is not IUIAutomationTogglePattern toggle)
                throw new InvalidOperationException($"The Windows taskbar's {kind} button has no Toggle action.");
            buttons.Add(kind, toggle);
            pattern = null; // cached pattern owns this COM reference
            return toggle;
        }
        finally { Release(pattern); Release(button); Release(condition); Release(root); }
    }

    private void ClearButtons()
    {
        foreach (var button in buttons.Values) Release(button);
        buttons.Clear();
    }
    private static void Release(object? value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    public void Dispose() { ClearButtons(); Release(automation); automation = null; Release(appVisibility); appVisibility = null; }

    [ComImport, Guid("2246EA2D-CAEA-4444-A3C4-6DE827E44313"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAppVisibility
    {
        [PreserveSig] int GetAppVisibilityOnMonitor(nint monitor, out int mode);
        [PreserveSig] int IsLauncherVisible([MarshalAs(UnmanagedType.Bool)] out bool visible);
    }

    // Only the vtable prefix used here is declared; unused slots retain their
    // SDK order. No dependency on WPF or an injected Explorer module is needed.
    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomation
    {
        void CompareElements(); void CompareRuntimeIds(); void GetRootElement();
        IUIAutomationElement ElementFromHandle(nint hwnd);
        void ElementFromPoint(); void GetFocusedElement(); void GetRootElementBuildCache();
        void ElementFromHandleBuildCache(); void ElementFromPointBuildCache(); void GetFocusedElementBuildCache();
        void CreateTreeWalker(); void GetControlViewWalker(); void GetContentViewWalker(); void GetRawViewWalker();
        void GetRawViewCondition(); void GetControlViewCondition(); void GetContentViewCondition();
        void CreateCacheRequest(); void CreateTrueCondition(); void CreateFalseCondition();
        [return: MarshalAs(UnmanagedType.Interface)] object CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value);
    }

    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationElement
    {
        void SetFocus(); void GetRuntimeId();
        IUIAutomationElement? FindFirst(int scope, [MarshalAs(UnmanagedType.Interface)] object condition);
        void FindAll(); void FindFirstBuildCache(); void FindAllBuildCache(); void BuildUpdatedCache();
        void GetCurrentPropertyValue(); void GetCurrentPropertyValueEx(); void GetCachedPropertyValue(); void GetCachedPropertyValueEx();
        void GetCurrentPatternAs(); void GetCachedPatternAs();
        [return: MarshalAs(UnmanagedType.IUnknown)] object? GetCurrentPattern(int patternId);
    }

    [ComImport, Guid("94CF8058-9B8D-4AB9-8BFD-4CD0A33C8C70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IUIAutomationTogglePattern { void Toggle(); int CurrentToggleState { get; } }
}

internal sealed partial class DockWindow
{
    private async Task CheckModuleActions()
    {
        var checks = new List<string>(); Exception? failure = null;
        nint previous = Native.GetForegroundWindow();
        try
        {
            foreach (var kind in new[] { ItemKind.Search, ItemKind.TaskView })
            {
                TaskbarModuleTrigger.Open(kind);
                bool IsOpen() => kind == ItemKind.Search ? TaskbarButtonClient.IsSearchActive() : Native.IsTaskViewOpen();
                for (int attempt = 0; attempt < 40 && !IsOpen(); attempt++) await Task.Delay(100);
                if (!IsOpen()) throw new InvalidOperationException(kind + " did not open from the dock launch path.");
                checks.Add(kind + " opened through the dock launch path");
            }
            TaskbarButtonClient.OpenShellAction(ItemKind.TaskView);
            await Task.Delay(300);
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { if (Native.IsWindow(previous)) Native.SetForegroundWindow(previous); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "module-actions-test.json"), System.Text.Json.JsonSerializer.Serialize(
            new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
