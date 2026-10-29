using System.Numerics;
using System.Runtime.InteropServices;
using WinRT;
using W = Windows.UI.Composition;

namespace WinMac.Dock;

// The shell sources use Windows.UI.Composition, not WinUI 3's compositor.
// A transparent child HWND owns their desktop target and inherits the dock's
// clipping, visibility and shell band. It never receives mouse input or focus.
internal sealed class ShellAnimatedIcon : IDisposable
{
    private static W.Compositor? compositor;
    private static Windows.System.DispatcherQueueController? queue;
    private static nint library;
    private readonly W.ContainerVisual container;
    private readonly W.ContainerVisual clickVisual;
    private readonly W.Visual visual;
    private readonly NativeSurface surface;
    private readonly Dictionary<string, double> markers = new();
    private readonly nint animatedVisual;
    private readonly double duration;
    private readonly Vector2 naturalSize;
    private string state = "NormalOff";
    private string requestedState = "NormalOff";
    private string? queuedPress;
    private readonly ShellIconInteraction interaction;
    private readonly bool isSearch;
    private long searchClickUntil;
    private W.CompositionScopedBatch? playback;
    private W.CompositionScopedBatch? clickPlayback;
    internal bool IsClickScalePlaying => clickPlayback != null;
    internal string RequestedState => requestedState;
    private bool disposed;
    private string lastTransition = "";
    private PixelRect? bounds;
    public nint Handle => surface.Handle;
    public object Diagnostics => new { Window = Handle.ToInt64(), State = state, RequestedState = requestedState, Transition = lastTransition, Playing = playback != null, Bounds = bounds, TimelineSeconds = duration, MarkerCount = markers.Count };

    public ShellAnimatedIcon(nint parent, ItemKind kind, bool dark)
    {
        EnsureCompositor();
        isSearch = kind == ItemKind.Search;
        interaction = new(kind == ItemKind.StartMenu);
        string name = "AnimatedVisuals." + (kind switch
        {
            ItemKind.StartMenu => "Start", ItemKind.Search => "Search", ItemKind.TaskView => "TaskView",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        }) + (dark ? "Dark" : "Light");
        nint factory = 0, source = 0, sourceInterface = 0, source2 = 0, map = 0, comp = 0, root = 0, diagnostics = 0;
        try
        {
            using var className = new HString(name);
            Check(Marshal.GetDelegateForFunctionPointer<GetFactory>(NativeLibrary.GetExport(library, "DllGetActivationFactory"))(className.Value, out factory));
            Check(Method<GetPointer>(factory, 6)(factory, out source));
            Check(Marshal.QueryInterface(source, in sourceId, out sourceInterface));
            comp = ((IWinRTObject)compositor!).NativeObject.ThisPtr;
            Marshal.AddRef(comp);
            Check(Method<CreateVisual>(sourceInterface, 6)(sourceInterface, comp, out diagnostics, out animatedVisual));
            if (animatedVisual == 0) throw new InvalidOperationException("The Windows shell animation source returned no visual.");
            Check(Method<GetPointer>(animatedVisual, 6)(animatedVisual, out root));
            visual = WinRT.MarshalInspectable<W.Visual>.FromAbi(root);
            Check(Method<GetVector>(animatedVisual, 7)(animatedVisual, out naturalSize));
            Check(Method<GetDuration>(animatedVisual, 8)(animatedVisual, out long ticks));
            duration = ticks / (double)TimeSpan.TicksPerSecond;
            if (naturalSize.X <= 0 || naturalSize.Y <= 0 || duration <= 0) throw new InvalidOperationException("Invalid Windows animation dimensions or duration.");
            Check(Marshal.QueryInterface(source, in source2Id, out source2));
            Check(Method<GetPointer>(source2, 6)(source2, out map));
            foreach (string from in states) foreach (string to in states) foreach (string end in new[] { "Start", "End" })
            {
                string key = from + "To" + to + "_" + end;
                using var text = new HString(key);
                if (Method<Lookup>(map, 6)(map, text.Value, out double progress) >= 0) markers[key] = progress;
            }
            if (!markers.TryGetValue("NormalOnToNormalOff_End", out double initial)) throw new InvalidOperationException("Windows animation state markers are unavailable.");
            // Newer LottieGen sources defer animation construction until the
            // player calls IAnimatedVisual2.CreateAnimations.
            if (Marshal.QueryInterface(animatedVisual, in visual2Id, out var version2) >= 0)
                try { Check(Method<NoArgs>(version2, 6)(version2)); } finally { Release(version2); }
            visual.Properties.InsertScalar("Progress", (float)initial);
            container = compositor!.CreateContainerVisual();
            container.IsVisible = false;
            container.Size = naturalSize;
            clickVisual = compositor.CreateContainerVisual();
            clickVisual.Size = naturalSize;
            clickVisual.CenterPoint = new Vector3(naturalSize / 2, 0);
            clickVisual.Children.InsertAtTop(visual);
            container.Children.InsertAtTop(clickVisual);
            surface = NativeSurface.Acquire(parent);
            surface.Root.Children.InsertAtTop(container);
        }
        catch
        {
            if (surface != null) surface.Release();
            Release(animatedVisual);
            throw;
        }
        finally { Release(root); Release(comp); Release(map); Release(source2); Release(sourceInterface); Release(source); Release(factory); Release(diagnostics); }
    }

    public void Place(PixelRect rect, bool visible)
    {
        container.IsVisible = visible;
        if (!visible) return;
        surface.EnsureBounds();
        if (rect == bounds) return;
        // Keep one native HWND at the full client size. Icon movement and
        // scaling are compositor properties, never a window resize/repaint.
        container.Offset = new Vector3(rect.Left, rect.Top, 0);
        container.Scale = new Vector3(rect.Width / naturalSize.X, rect.Height / naturalSize.Y, 1);
        bounds = rect;
    }

    public void NotifyClick()
    {
        // Click is delivered even when a down/up pair fits between frames.
        // Retain the press that caused this activation, not the shell state
        // observed later while the hover segment was still playing.
        if (isSearch) searchClickUntil = Environment.TickCount64 + 600;
        if (interaction.Click(state, Environment.TickCount64) is { } press) queuedPress = press;
    }

    internal void PulseClick(int durationMs)
    {
        if (disposed) return;
        clickPlayback?.Dispose(); clickPlayback = null;
        clickVisual.StopAnimation("Scale"); clickVisual.Scale = Vector3.One;
        var animation = compositor!.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(0, Vector3.One);
        animation.InsertKeyFrame(167f / 667, new Vector3(.8f, .8f, 1), compositor.CreateCubicBezierEasingFunction(new Vector2(.2f, 0), new Vector2(0, 1)));
        animation.InsertKeyFrame(332f / 667, new Vector3(1.02f, 1.02f, 1), compositor.CreateCubicBezierEasingFunction(new Vector2(.16f, 1), new Vector2(.3f, 1)));
        animation.InsertKeyFrame(1, Vector3.One);
        animation.Duration = TimeSpan.FromMilliseconds(durationMs);
        var batch = compositor.CreateScopedBatch(W.CompositionBatchTypes.Animation);
        clickPlayback = batch;
        batch.Completed += (_, _) => { if (ReferenceEquals(clickPlayback, batch)) { clickPlayback = null; batch.Dispose(); } };
        clickVisual.StartAnimation("Scale", animation); batch.End(); animation.Dispose();
    }

    public void SetState(bool hover, bool pressed, bool active, bool motion)
    {
        // UIA observes the shell toggle shortly after mouse release. Keep the
        // pressed pose until that observation arrives instead of routing a
        // release through an unrelated On state in the source's sparse graph.
        // Search responds only to its dock activation, never to shell state
        // (Start can share SearchHost) or incidental pointer movement.
        if (isSearch)
        {
            hover = false; pressed = false;
            active = Environment.TickCount64 < searchClickUntil;
        }
        var input = interaction.Update(pressed, active, Environment.TickCount64);
        active = input.Active;
        string next = (pressed ? "Pressed" : hover ? "PointerOver" : "Normal") + (active ? "On" : "Off");
        requestedState = next;
        if (!motion)
        {
            if (playback != null) { var old = playback; playback = null; visual.Properties.StopAnimation("Progress"); old.Dispose(); }
            queuedPress = null;
            if (next != state)
                foreach (string from in states)
                    if (markers.TryGetValue(from + "To" + next + "_End", out double resting)) { visual.Properties.InsertScalar("Progress", (float)resting); break; }
            state = next;
            return;
        }
        if (playback != null)
        {
            if (input.NewPress && next != state) queuedPress = next;
            return;
        }
        PlayRequestedState();
    }

    private void PlayRequestedState()
    {
        string wanted = queuedPress ?? requestedState;
        // Apply the release grace period here as well as on input updates:
        // a segment completion can otherwise bypass it between UIA polls.
        if (interaction.HoldRelease(state, wanted, queuedPress != null, Environment.TickCount64)) return;
        queuedPress = null;
        if (wanted == state) { if (wanted != requestedState) PlayRequestedState(); return; }
        // The sources omit release/cancel edges that keep the toggle value.
        // Reverse the matching press segment; never invent a route through On
        // merely to get back to an inactive hover/normal pose.
        string reverse = wanted + "To" + state;
        bool reverseRelease = state.StartsWith("Pressed", StringComparison.Ordinal)
            && state.EndsWith("On", StringComparison.Ordinal) == wanted.EndsWith("On", StringComparison.Ordinal)
            && !markers.ContainsKey(state + "To" + wanted + "_End")
            && markers.ContainsKey(reverse + "_End");
        // Play only genuine edges in the source's state graph. Finishing the
        // current segment before changing markers avoids jumping between
        // unrelated intermediate vector frames on quick pointer changes.
        var paths = new Queue<string[]>();
        var visited = new HashSet<string> { state };
        paths.Enqueue([state]);
        string? next = reverseRelease ? wanted : null;
        while (paths.Count > 0 && next == null)
        {
            var path = paths.Dequeue();
            foreach (string candidate in states)
            {
                if (!markers.ContainsKey(path[^1] + "To" + candidate + "_End") || !visited.Add(candidate)) continue;
                var route = path.Concat(new[] { candidate }).ToArray();
                if (candidate == wanted) { next = route[1]; break; }
                paths.Enqueue(route);
            }
        }
        if (next == null) return;
        if (next != wanted && wanted.StartsWith("Pressed", StringComparison.Ordinal)) queuedPress = wanted;
        string transition = reverseRelease ? reverse : state + "To" + next;
        double start = markers[transition + (reverseRelease ? "_End" : "_Start")], end = markers[transition + (reverseRelease ? "_Start" : "_End")];
        visual.Properties.InsertScalar("Progress", (float)start);
        var animation = compositor!.CreateScalarKeyFrameAnimation();
        animation.InsertKeyFrame(0, (float)start, compositor.CreateLinearEasingFunction());
        animation.InsertKeyFrame(1, (float)end, compositor.CreateLinearEasingFunction());
        animation.Duration = TimeSpan.FromSeconds(Math.Max(.001, Math.Abs(end - start) * duration));
        var batch = compositor.CreateScopedBatch(W.CompositionBatchTypes.Animation);
        playback = batch;
        state = next;
        lastTransition = transition;
        batch.Completed += (_, _) =>
        {
            if (disposed || !ReferenceEquals(playback, batch)) return;
            playback = null; batch.Dispose(); PlayRequestedState();
        };
        visual.Properties.StartAnimation("Progress", animation);
        batch.End(); animation.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        playback?.Dispose(); playback = null;
        clickPlayback?.Dispose(); clickPlayback = null;
        surface.Root.Children.Remove(container);
        container.Children.RemoveAll(); clickVisual.Children.RemoveAll(); clickVisual.Dispose();
        container.Dispose(); visual.Dispose();
        if (Marshal.QueryInterface(animatedVisual, in closableId, out var closable) >= 0)
            try { _ = Method<NoArgs>(closable, 6)(closable); } finally { Release(closable); }
        Release(animatedVisual);
        surface.Release();
    }

    private sealed class NativeSurface
    {
        private static readonly Dictionary<nint, NativeSurface> surfaces = new();
        private readonly nint parent;
        private readonly Native.SubclassProc subclass;
        private readonly Windows.UI.Composition.Desktop.DesktopWindowTarget target;
        private int references;
        private (int Width, int Height) size;
        public nint Handle { get; }
        public W.ContainerVisual Root { get; }
        private NativeSurface(nint parent)
        {
            this.parent = parent;
            Handle = CreateWindowExW(0x00200000 | 0x08000000 | 0x20, "Static", "WinMac shell animation surface",
                0x40000000 | 0x04000000 | 0x08000000, 0, 0, 1, 1, parent, 0, 0, 0);
            if (Handle == 0) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            subclass = (h, m, w, l, id, data) =>
            {
                if (m == 0x0084) return -1;
                if (m == 0x0021) return 3;
                if (m == 0x0014) return 1;
                if (m == 0x000F) { ValidateRect(h, 0); return 0; }
                return Native.DefSubclassProc(h, m, w, l);
            };
            Native.SetWindowSubclass(Handle, subclass, 1, 0);
            try
            {
                compositor!.As<ICompositorDesktopInterop>().CreateDesktopWindowTarget(Handle, true, out var raw);
                try { target = WinRT.MarshalInspectable<Windows.UI.Composition.Desktop.DesktopWindowTarget>.FromAbi(raw); } finally { ShellAnimatedIcon.Release(raw); }
                Root = compositor!.CreateContainerVisual();
                if (target == null) throw new InvalidOperationException("The shell composition target is unavailable.");
                target.Root = Root;
            }
            catch { Native.RemoveWindowSubclass(Handle, subclass, 1); DestroyWindow(Handle); throw; }
        }
        public static NativeSurface Acquire(nint parent)
        {
            if (!surfaces.TryGetValue(parent, out var surface)) surfaces.Add(parent, surface = new(parent));
            surface.references++; return surface;
        }
        public void EnsureBounds()
        {
            if (!GetClientRect(parent, out var rect)) return;
            var next = (Width: rect.Right - rect.Left, Height: rect.Bottom - rect.Top);
            if (size == next) return;
            Root.Size = new Vector2(next.Width, next.Height);
            Native.SetWindowPos(Handle, 0, 0, 0, next.Width, next.Height, 0x10 | 0x40);
            size = next;
        }
        public void Release()
        {
            if (--references > 0) return;
            surfaces.Remove(parent);
            target.Root = null; target.Dispose(); Root.Dispose();
            Native.RemoveWindowSubclass(Handle, subclass, 1); DestroyWindow(Handle);
        }
    }

    private static void EnsureCompositor()
    {
        if (compositor != null) return;
        if (Windows.System.DispatcherQueue.GetForCurrentThread() == null)
        {
            var options = new Native.DispatcherQueueOptions { Size = Marshal.SizeOf<Native.DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 0 };
            Check(Native.CreateDispatcherQueueController(options, out var raw));
            try { queue = WinRT.MarshalInterface<Windows.System.DispatcherQueueController>.FromAbi(raw); } finally { Release(raw); }
        }
        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        foreach (string package in new[] { "MicrosoftWindows.Client.Core_cw5n1h2txyewy", "MicrosoftWindows.Client.CBS_cw5n1h2txyewy" })
        {
            string path = Path.Combine(windows, "SystemApps", package, "Taskbar.View.dll");
            if (File.Exists(path)) { library = LoadLibraryExW(path, 0, 0x1100); if (library != 0) break; }
        }
        if (library == 0) throw new InvalidOperationException("The installed Windows taskbar animation library is unavailable.");
        compositor = new W.Compositor();
    }

    private static readonly string[] states = ["NormalOff", "PointerOverOff", "PressedOff", "NormalOn", "PointerOverOn", "PressedOn"];
    private static readonly Guid sourceId = new("57AF690E-B331-5462-8CC6-C529A5DA4123"), source2Id = new("1A3B53A7-A8FE-59A1-B544-43A4D9C81EF2"),
        visual2Id = new("763A3F0B-95CC-59EF-9890-147868599DE9"), closableId = new("30D5A829-7FA4-4026-83BB-D75BAE4EA99E");
    private static T Method<T>(nint value, int slot) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(value), slot * IntPtr.Size));
    private static void Check(int hr) => Marshal.ThrowExceptionForHR(hr);
    private static void Release(nint value) { if (value != 0) Marshal.Release(value); }
    private sealed class HString : IDisposable
    {
        public nint Value { get; }
        public HString(string value) { Check(WindowsCreateString(value, (uint)value.Length, out var text)); Value = text; }
        public void Dispose() => WindowsDeleteString(Value);
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetFactory(nint name, out nint factory);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetPointer(nint self, out nint value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateVisual(nint self, nint compositor, out nint diagnostics, out nint visual);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetVector(nint self, out Vector2 size);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDuration(nint self, out long ticks);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Lookup(nint self, nint name, out double value);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int NoArgs(nint self);
    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop { void CreateDesktopWindowTarget(nint hwnd, [MarshalAs(UnmanagedType.Bool)] bool topmost, out nint target); }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint LoadLibraryExW(string path, nint file, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateWindowExW(uint exStyle, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint hwnd, out Native.RECT rect);
    [DllImport("user32.dll")] private static extern bool ValidateRect(nint hwnd, nint rect);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, uint length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
}
