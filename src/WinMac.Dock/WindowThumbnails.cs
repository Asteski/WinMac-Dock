using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private sealed class WindowThumbnail(uint process, bool minimized, bool explorer, bool preferForegroundCapture, bool console)
    {
        public uint Process { get; } = process;
        public bool Explorer { get; } = explorer;
        public bool PreferForegroundCapture { get; } = preferForegroundCapture;
        public bool Console { get; } = console;
        public bool Minimized { get; set; } = minimized;
        public long RetryAfter { get; set; }
        public WriteableBitmap? Image { get; set; }
        public WriteableBitmap? ForegroundFrame { get; set; }
        public Task<WriteableBitmap?>? Capture { get; set; }
    }

    private readonly Dictionary<nint, WindowThumbnail> windowThumbnails = [];
    private static readonly SemaphoreSlim windowCaptureSlots = new(2);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PrintWindow(nint window, nint deviceContext, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowPlacement
    {
        public int Length, Flags, ShowCommand;
        public Native.POINT MinPosition, MaxPosition;
        public Native.RECT NormalPosition;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint window, ref WindowPlacement placement);

    private readonly Dictionary<nint, long> minimizedWindowOrder = [];
    private long minimizedWindowSerial;

    private void RecordWindowMinimize(nint handle)
    {
        minimizedWindowOrder[handle] = ++minimizedWindowSerial;
        if (Settings.ShowUngroupedWindowThumbnails && tracker.Describe(handle) is { } window)
            RequestWindowSnapshot(window, refresh: !window.Minimized);
    }

    private bool UsesWindowThumbnail(DockEntry entry) => Settings.ShowUngroupedWindowThumbnails
        && !entry.IsPinned && entry.Window != null;

    private async Task MinimizeWithFreshThumbnail(TaskWindow window)
    {
        if (Native.IsIconic(window.Handle)) return;
        var state = Settings.ShowUngroupedWindowThumbnails ? RequestWindowSnapshot(window, refresh: true) : null;
        if (state?.Capture is { IsCompleted: false } capture)
            await Task.WhenAny(capture, Task.Delay(150));
        if (closing) return;
        if (state != null)
        {
            Native.GetWindowThreadProcessId(window.Handle, out uint process);
            if (process != state.Process) return;
        }
        ShellService.Activate(window, true);
    }

    private TaskWindow ThumbnailWindowFor(DockEntry entry) => Settings.GroupWindows
        ? entry.Windows.MaxBy(window => minimizedWindowOrder.GetValueOrDefault(window.Handle)) ?? entry.Window!
        : entry.Window!;

    private void PruneWindowThumbnails(IReadOnlyList<TaskWindow> windows)
    {
        var tracked = windows.ToDictionary(window => window.Handle);
        foreach (var handle in minimizedWindowOrder.Keys.ToArray())
            if (!tracked.ContainsKey(handle)) minimizedWindowOrder.Remove(handle);

        if (!Settings.ShowUngroupedWindowThumbnails)
        {
            windowThumbnails.Clear();
            return;
        }
        foreach (var handle in windowThumbnails.Keys.ToArray())
        {
            if (!tracked.TryGetValue(handle, out var window))
                windowThumbnails.Remove(handle);
            else if (!window.Minimized && !MinimizedOnly && !Settings.GroupWindows && !windowThumbnails[handle].Explorer)
                windowThumbnails[handle].Minimized = false;
        }

        // In minimized-only mode there is no tile to capture until the window
        // has already minimized. Keep a snapshot from when it can still paint.
        nint foreground = Native.GetForegroundWindow();
        foreach (var window in windows.OrderByDescending(window => window.Handle == foreground))
            if (!window.Minimized || windowThumbnails.ContainsKey(window.Handle))
                RequestWindowSnapshot(window, captureOnRestore: true);
    }

    private static bool IsExplorerWindow(TaskWindow window) =>
        string.Equals(Path.GetFileName(window.ExecutablePath), "explorer.exe", StringComparison.OrdinalIgnoreCase)
        || window.ClassName is "CabinetWClass" or "ExploreWClass";

    private void RetryForegroundWindowThumbnail()
    {
        if (fullscreenIdleActive) return;
        if (!Settings.ShowUngroupedWindowThumbnails) return;
        nint handle = Native.GetForegroundWindow();
        if (handle == 0 || !windowThumbnails.TryGetValue(handle, out var state)
            || state.Image != null || state.Capture is not { IsCompleted: true }
            || Environment.TickCount64 < state.RetryAfter) return;

        var window = tracker.Describe(handle);
        if (window == null || window.Minimized) return;
        RequestWindowSnapshot(window);
    }

    private void CheckForegroundThumbnail()
    {
        if (fullscreenIdleActive) return;
        if (!Settings.ShowUngroupedWindowThumbnails) return;
        var handle = Native.GetForegroundWindow();
        if (!MinimizedOnly && windowThumbnails.TryGetValue(handle, out var state) && state.Image != null
            && !Native.IsIconic(handle) && Environment.TickCount64 >= state.RetryAfter
            && (state.Capture == null || state.Capture.IsCompleted)
            && buttons.Any(tile => UsesWindowThumbnail(tile.Entry) && ThumbnailWindowFor(tile.Entry).Handle == handle))
        {
            state.RetryAfter = Environment.TickCount64 + 1000;
            state.Capture = CacheWindowSnapshot(handle, state, keepUntilMinimize: true);
            return;
        }
        RetryForegroundWindowThumbnail();
    }

    private WindowThumbnail? RequestWindowSnapshot(TaskWindow window, bool captureOnRestore = false, bool refresh = false)
    {
        if (fullscreenIdleActive) return null;
        nint handle = window.Handle;
        Native.GetWindowThreadProcessId(handle, out uint process);
        if (process == 0) return null;

        bool captureNeeded;
        if (!windowThumbnails.TryGetValue(handle, out var state) || state.Process != process)
        {
            var executable = Path.GetFileName(window.ExecutablePath);
            bool console = window.ClassName is "ConsoleWindowClass" or "CASCADIA_HOSTING_WINDOW_CLASS"
                || executable.Equals("WindowsTerminal.exe", StringComparison.OrdinalIgnoreCase)
                || executable.Equals("WindowsTerminalPreview.exe", StringComparison.OrdinalIgnoreCase)
                || executable.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase)
                || executable.Equals("pwsh.exe", StringComparison.OrdinalIgnoreCase);
            state = new WindowThumbnail(process, window.Minimized, IsExplorerWindow(window),
                executable.Equals("Code.exe", StringComparison.OrdinalIgnoreCase)
                || executable.Equals("Code - Insiders.exe", StringComparison.OrdinalIgnoreCase) || console, console);
            windowThumbnails[handle] = state;
            captureNeeded = true;
        }
        else
        {
            captureNeeded = window.Minimized != state.Minimized && (window.Minimized || captureOnRestore);
            state.Minimized = window.Minimized;
            if (captureNeeded && window.Minimized && state.ForegroundFrame is { } frame)
            {
                state.ForegroundFrame = null;
                PublishWindowSnapshot(handle, state, frame);
                captureNeeded = false;
            }
            else if (captureNeeded && !window.Minimized) state.ForegroundFrame = null;
            captureNeeded |= !window.Minimized && state.Image == null && Environment.TickCount64 >= state.RetryAfter;
        }

        // Explorer can paint its folder or drive icon instead of the window
        // when iconic. Preserve the last restored snapshot in that state.
        if ((captureNeeded || refresh) && !(window.Minimized && (state.Explorer || state.Console))
            && (state.Capture == null || state.Capture.IsCompleted))
        {
            state.RetryAfter = Environment.TickCount64 + 1500;
            state.Capture = CacheWindowSnapshot(handle, state);
        }
        return state;
    }

    private async Task<WriteableBitmap?> CacheWindowSnapshot(nint handle, WindowThumbnail state, bool keepUntilMinimize = false)
    {
        try
        {
            var snapshot = await LoadWindowSnapshot(handle, state.Process, state.Explorer, state.PreferForegroundCapture, () => fullscreenIdleActive || closing);
            Native.GetWindowThreadProcessId(handle, out uint process);
            if (snapshot != null && !closing && !fullscreenIdleActive && process == state.Process
                && windowThumbnails.TryGetValue(handle, out var current) && current == state)
            {
                if (keepUntilMinimize && !state.Minimized) state.ForegroundFrame = snapshot;
                else PublishWindowSnapshot(handle, state, snapshot);
            }
        }
        catch (Exception ex) { Log.Write(ex); }
        return state.Image;
    }

    private void PublishWindowSnapshot(nint handle, WindowThumbnail state, WriteableBitmap snapshot)
    {
        state.Image = snapshot;
        foreach (var tile in buttons.ToArray())
            if (UsesWindowThumbnail(tile.Entry) && ThumbnailWindowFor(tile.Entry).Handle == handle
                && tile.Icon is Grid host && host.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().FirstOrDefault() is { } badge)
                ShowWindowSnapshot(host, badge, snapshot, animateAppearance: true);
    }

    private void ConfigureThumbnailBadge(Microsoft.UI.Xaml.Controls.Image badge)
    {
        badge.Width = badge.Height = Math.Min(16, DisplayIconSize / 2);
        badge.UseLayoutRounding = true;
        badge.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right;
        badge.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom;
    }

    private void ApplyWindowThumbnail(TaskWindow window, Microsoft.UI.Xaml.Controls.Image appIcon, Grid host)
    {
        var state = RequestWindowSnapshot(window);
        if (state?.Image is { } snapshot) ShowWindowSnapshot(host, appIcon, snapshot);
    }

    private static readonly object windowPreviewTag = new();

    private void ShowWindowSnapshot(Grid host, Microsoft.UI.Xaml.Controls.Image appIcon, WriteableBitmap source, bool animateAppearance = false)
    {
        appIcon.Visibility = Settings.ShowThumbnailAppBadges ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        var preview = host.Children.OfType<Microsoft.UI.Xaml.Controls.Border>().FirstOrDefault(child => ReferenceEquals(child.Tag, windowPreviewTag));
        bool appearing = preview == null;
        if (preview == null)
        {
            preview = new Microsoft.UI.Xaml.Controls.Border
            {
                Tag = windowPreviewTag,
                Background = new Microsoft.UI.Xaml.Media.ImageBrush { Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill },
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                UseLayoutRounding = false,
                IsHitTestVisible = false
            };
            host.Children.Insert(host.Children.IndexOf(appIcon), preview);
        }
        double fit = host.Width / Math.Max(source.PixelWidth, source.PixelHeight);
        preview.Width = source.PixelWidth * fit;
        preview.Height = source.PixelHeight * fit;
        bool rounded = Settings.ThumbnailCorners == ThumbnailCornerStyle.Rounded
            || (Settings.ThumbnailCorners == ThumbnailCornerStyle.Dynamic && Settings.RoundedCorners);
        preview.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(rounded ? 4 : 0);
        ((Microsoft.UI.Xaml.Media.ImageBrush)preview.Background).ImageSource = source;
        if (appearing && animateAppearance && Motion)
        {
            var visual = Microsoft.UI.Xaml.Hosting.ElementCompositionPreview.GetElementVisual(preview);
            visual.CenterPoint = new((float)preview.Width / 2, (float)preview.Height / 2, 0);
            using var animation = visual.Compositor.CreateVector3KeyFrameAnimation();
            animation.InsertKeyFrame(0, new System.Numerics.Vector3(PressedArtworkScale, PressedArtworkScale, 1));
            animation.InsertKeyFrame(1, System.Numerics.Vector3.One,
                visual.Compositor.CreateCubicBezierEasingFunction(new(.2f, 0), new(0, 1)));
            animation.Duration = TimeSpan.FromMilliseconds(150);
            visual.StartAnimation("Scale", animation);
        }
    }

    private static async Task<WriteableBitmap?> LoadWindowSnapshot(nint handle, uint process, bool explorer, bool preferForegroundCapture, Func<bool> shouldStop)
    {
        byte[]? png = await Task.Run(async () =>
        {
            // PrintWindow calls into the target application synchronously.
            // Bound the number of calls and keep them off the UI thread.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (shouldStop()) return null;
                if (!await windowCaptureSlots.WaitAsync(TimeSpan.FromSeconds(2))) return null;
                byte[]? frame;
                uint flags = explorer ? 2u : attempt switch { 1 => 3u, 2 => 0u, _ => 2u };
                try
                {
                    if (shouldStop()) return null;
                    frame = null;
                    // Console hosts can stall PrintWindow or paint a blank GPU surface.
                    // Capture the visible foreground frame before asking them to repaint.
                    if (preferForegroundCapture && attempt == 0)
                        frame = CaptureWindowThumbnail(handle, process, flags, explorer, captureForegroundPixels: true);
                    frame ??= CaptureWindowThumbnail(handle, process, flags, explorer);
                }
                finally { windowCaptureSlots.Release(); }
                if (frame != null || !Native.IsWindow(handle)) return frame;
                if (attempt < 2) await Task.Delay(attempt == 0 ? 60 : 150);
            }
            return null;
        });
        if (png == null) return null;

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(png);
            await writer.StoreAsync();
            await writer.FlushAsync();
        }
        stream.Seek(0);
        // Keep decoded pixels with the snapshot. Hover magnification moves
        // the visual between parents, so it can reuse the same pixel buffer.
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var data = await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, new Windows.Graphics.Imaging.BitmapTransform(),
            Windows.Graphics.Imaging.ExifOrientationMode.IgnoreExifOrientation,
            Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        var thumbnail = new WriteableBitmap((int)decoder.PixelWidth, (int)decoder.PixelHeight);
        using (var pixels = thumbnail.PixelBuffer.AsStream()) pixels.Write(data.DetachPixelData());
        thumbnail.Invalidate();
        return thumbnail;
    }

    private static byte[]? CaptureWindowThumbnail(nint handle, uint process, uint flags, bool explorer, bool captureForegroundPixels = false)
    {
        Native.GetWindowThreadProcessId(handle, out uint actualProcess);
        if (actualProcess != process || !Native.IsWindowVisible(handle)
            || !Native.GetWindowRect(handle, out var bounds)) return null;
        if (captureForegroundPixels && (Native.GetForegroundWindow() != handle || Native.IsIconic(handle))) return null;
        if (explorer && Native.IsIconic(handle)) return null;
        if (Native.IsIconic(handle) && bounds.Left <= -30000)
        {
            var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
            if (!GetWindowPlacement(handle, ref placement)) return null;
            bounds = placement.NormalPosition;
        }
        int width = bounds.Right - bounds.Left;
        int height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0 || (long)width * height > 20_000_000) return null;

        using var frame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(frame))
        {
            if (captureForegroundPixels)
            {
                graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, frame.Size);
                if (Native.GetForegroundWindow() != handle || Native.IsIconic(handle)) return null;
            }
            else
            {
            var dc = graphics.GetHdc();
            try { if (!PrintWindow(handle, dc, flags)) return null; }
            finally { graphics.ReleaseHdc(dc); }
            }
        }

        if (explorer && Native.IsIconic(handle)) return null;

        const int maxSide = 256;
        double scale = Math.Min(1d, (double)maxSide / Math.Max(width, height));
        using var thumbnail = new Bitmap(Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)), PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(thumbnail))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(frame, 0, 0, thumbnail.Width, thumbnail.Height);
        }

        // A failed capture can still contain a title bar or border. Sample
        // the interior so those pixels cannot make a blank client look valid.
        int nonBlack = 0;
        int varied = 0;
        Color center = thumbnail.GetPixel(thumbnail.Width / 2, thumbnail.Height / 2);
        for (int row = 2; row < 11; row++)
        {
            for (int column = 2; column < 11; column++)
            {
                Color pixel = thumbnail.GetPixel(column * thumbnail.Width / 12, row * thumbnail.Height / 12);
                if (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) > 8) nonBlack++;
                if (Math.Abs(pixel.R - center.R) + Math.Abs(pixel.G - center.G) + Math.Abs(pixel.B - center.B) > 24)
                    varied++;
            }
        }
        if (nonBlack < 6) return null;
        if (varied < 2)
        {
            // An empty Explorer folder can have a uniform client area.
            // Require visible toolbar detail before accepting that frame.
            if (!explorer) return null;
            int toolbarDetail = 0;
            for (int row = 1; row <= 5; row++)
            for (int column = 2; column < 11; column++)
            {
                var pixel = thumbnail.GetPixel(column * thumbnail.Width / 12, row * thumbnail.Height / 32);
                if (Math.Abs(pixel.R - center.R) + Math.Abs(pixel.G - center.G) + Math.Abs(pixel.B - center.B) > 24)
                    toolbarDetail++;
            }
            if (toolbarDetail < 2) return null;
        }

        using var stream = new MemoryStream();
        thumbnail.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
