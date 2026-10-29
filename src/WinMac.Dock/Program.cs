using System.IO.Pipes;
using Microsoft.UI.Xaml;

namespace WinMac.Dock;
internal static class Program
{
    public static event Action<string>? CommandReceived;
    internal static bool SeparatorAudit => StartupCommand is "--test-folder-settings" or "--test-flyout-icons" or "--test-separator-spacing" or "--test-unpinned-menus" or "--test-edge-clicks" or "--test-passive-focus" or "--test-icon-apply" or "--test-icon-resources" or "--test-drag-spacing" or "--test-taskbar-settings";
    public static string StartupCommand { get; private set; } = "--startup";
    [STAThread]
    public static void Main(string[] args)
    {
        StartupCommand = args.FirstOrDefault() ?? "--startup";
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Write(e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject.ToString()));
        if (args.Length >= 2 && args[0] == "--wait-for-exit" && int.TryParse(args[1], out int parentId))
        {
            try { using var parent = System.Diagnostics.Process.GetProcessById(parentId); if (!parent.WaitForExit(15000)) { Log.Write($"Restart timed out waiting for PID={parentId}"); return; } }
            catch (ArgumentException) { }
            args = args.Skip(2).ToArray();
        }
        StartupCommand = args.FirstOrDefault() ?? "--startup";
        if (StartupCommand == "--sync-startup")
        {
            try
            {
                var store = new SettingsStore(Path.Combine(Log.DirectoryPath, "settings.json"));
                LoginStartup.Configure(store.Load(() => new DockSettings()).StartAtLogin);
            }
            catch (Exception ex) { Log.Write(ex); Environment.ExitCode = 1; }
            return;
        }
        using var mutex = new Mutex(true, "Local\\WinMac.Dock." + Environment.UserName + (SeparatorAudit ? ".SeparatorAudit" : ""), out var first);
        var pipeName = "WinMac.Dock." + Environment.UserName + (SeparatorAudit ? ".SeparatorAudit" : "");
        if (!first)
        {
            try { using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out); pipe.Connect(3000); using var writer = new StreamWriter(pipe) { AutoFlush = true }; writer.WriteLine(args.FirstOrDefault() ?? "--show"); }
            catch (Exception ex) { Log.Write(ex); }
            return;
        }
        if (DockElevation.RelaunchIfRequested(args)) return;
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await pipe.WaitForConnectionAsync();
                    using var reader = new StreamReader(pipe);
                    var command = await reader.ReadLineAsync();
                    if (command != null) CommandReceived?.Invoke(command);
                }
                catch (Exception ex) { Log.Write(ex); await Task.Delay(1000); }
            }
        });
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(initialization =>
        {
            SynchronizationContext.SetSynchronizationContext(new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _ = new App();
        });
    }
}
