using System.IO.Pipes;
using Microsoft.UI.Xaml;

namespace WinMac.Dock;
internal static class Program
{
    private static readonly StartupCommandQueue commands = new();
    internal static void ReadyForCommands(Action<string> dispatch) => commands.Ready(dispatch);
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
        if (StartupCommand is not ("--startup" or "--sync-startup" or "--show" or "--hide" or "--toggle" or "--settings" or "--about" or "--reload" or "--elevate" or "--exit")) return;
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
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        string user = identity.User?.Value ?? throw new InvalidOperationException("The current Windows user could not be identified.");
        using var mutex = new Mutex(true, "Local\\WinMac.Dock." + user, out var first);
        // Match the session-local singleton: this does not permit a second
        // instance in this session, and cannot route commands to another one.
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var pipeName = "WinMac.Dock." + user + "." + process.SessionId;
        if (!first)
        {
            // Launching the executable again opens the existing Settings
            // window. Explicit startup and command-line actions retain their meaning.
            try { using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.Out); pipe.Connect(3000); using var writer = new StreamWriter(pipe) { AutoFlush = true }; writer.WriteLine(args.FirstOrDefault() ?? "--settings"); }
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
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var command = await reader.ReadLineAsync(timeout.Token);
                    if (command is { Length: <= 64 }) commands.Receive(command);
                }
                catch (OperationCanceledException) { }
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
