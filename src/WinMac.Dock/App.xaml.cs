using Microsoft.UI.Xaml;

namespace WinMac.Dock;
public partial class App : Application
{
    internal static DockWindow? Dock { get; private set; }
    private static readonly List<DockWindow> secondaryDocks = [];
    private static bool syncing, syncQueued;
    private DispatcherTimer? monitorTimer;
    private static string displayTopology = "";
    private static bool startupRegistrationChecked;
    internal static void ScheduleDockSync(DockWindow source)
    {
        if (syncing || syncQueued || Dock == null) return;
        syncQueued = true;
        Dock.DispatcherQueue.TryEnqueue(() => { syncQueued = false; SyncDocks(source); });
    }
    private static void SyncDocks(DockWindow? source = null)
    {
        if (Dock == null || syncing) return;
        syncing = true;
        try
        {
            var monitors = Native.Monitors();
            if (monitors.Count == 0) return;
            foreach (var display in monitors) Dock.Settings.LayoutForDisplay(display.Identity);
            bool firstTopology = displayTopology.Length == 0;
            var createdDocks = new HashSet<DockWindow>();
            var topology = string.Join("|", monitors.Select(m => $"{m.Identity}:{m.Device}:{m.Bounds}:{m.Work}:{m.Scale}:{m.Primary}"));
            bool topologyChanged = displayTopology != topology;
            displayTopology = topology;
            var home = monitors.FirstOrDefault(m => m.Identity == Dock.Settings.Monitor || m.Device == Dock.Settings.Monitor) ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors[0];
            if (Dock.Settings.Monitor == home.Device) Dock.Settings.Monitor = home.Identity;
            var wanted = Dock.Settings.ShowOnAllMonitors ? monitors.Where(m => m.Identity != home.Identity).Select(m => m.Identity).ToHashSet() : [];
            foreach (var dock in secondaryDocks.Where(d => !wanted.Contains(d.MonitorOverride!)).ToArray())
            { secondaryDocks.Remove(dock); dock.Close(); }
            foreach (var device in wanted.Where(device => !secondaryDocks.Any(d => d.MonitorOverride == device)))
            {
                DockWindow dock;
                dock = new DockWindow(Dock.Settings, device);
                secondaryDocks.Add(dock); createdDocks.Add(dock); dock.StartupReady += CheckStartupRegistration; dock.Initialize();
            }
            if (source != null || topologyChanged && !firstTopology)
                foreach (var dock in secondaryDocks.Prepend(Dock).Where(d => !createdDocks.Contains(d) && d != source))
                {
                    if (source != null) dock.ApplySettings(persist: false);
                    else dock.RefreshMonitorLayout();
                }
            if (topologyChanged) Dock.Save();
        }
        finally { syncing = false; }
    }
    internal static DockWindow AnimationDock(nint window, DockWindow fallback)
    {
        if (Dock == null || !Dock.Settings.ShowOnAllMonitors) return fallback;
        var device = Native.Monitors().FirstOrDefault(m => m.Handle == Native.MonitorFromWindow(window, 2))?.Identity;
        return secondaryDocks.FirstOrDefault(d => d.MonitorOverride == device) ?? Dock;
    }
    private static void CheckStartupRegistration()
    {
        if (startupRegistrationChecked || Dock == null || !Dock.StartupComplete || secondaryDocks.Any(d => !d.StartupComplete)) return;
        startupRegistrationChecked = true;
        _ = Task.Run(() =>
        {
            try
            {
                LoginStartup.EnsureConfigured(Dock.Settings.StartAtLogin);
            }
            catch (Exception ex) { Log.Write(ex); }
        });
    }
    internal static void SetDockVisibility(bool show, DockWindow source)
    {
        if (Dock == null) return;
        foreach (var dock in secondaryDocks.Prepend(Dock).Where(d => d != source)) dock.SetExplicitVisibility(show, false);
    }
    internal static void CloseSecondaryDocks()
    {
        syncing = true;
        foreach (var dock in secondaryDocks.ToArray()) dock.Close();
        secondaryDocks.Clear();
    }
    public App()
    {
        UnhandledException += (_, e) => { Log.Write(e.Exception); };
        InitializeComponent();
    }
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Dock = new DockWindow();
        Program.CommandReceived += command => Dock.DispatcherQueue.TryEnqueue(() => Dock.HandleCommand(command));
        Dock.StartupReady += () => Dock.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            SyncDocks();
            Dock.HandleCommand(Program.StartupCommand);
            monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            monitorTimer.Tick += (_, _) => SyncDocks(); monitorTimer.Start();
            CheckStartupRegistration();
        });
        Dock.Initialize();
        // InitializeFully shows the dock only after startup artwork has loaded.
    }
}

internal static class Log
{
    public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WinMac", "Dock");
    private static readonly RotatingLineLog file = new(Path.Combine(DirectoryPath, "WinMac.Dock.log"), legacyPath: Path.Combine(DirectoryPath, "dock.log"));
    public static void Write(Exception ex) => Write(ex.ToString());
    public static void Write(string message) { try { file.Write(DateTimeOffset.Now + " " + message); } catch { } }
}
