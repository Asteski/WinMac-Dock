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
    private sealed class WindowThumbnail(uint process, bool minimized, bool explorer)
    {
        public uint Process { get; } = process;
        public bool Explorer { get; } = explorer;
        public bool Minimized { get; set; } = minimized;
        public long RetryAfter { get; set; }
        public WriteableBitmap? Image { get; set; }
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
    }

    private bool UsesWindowThumbnail(DockEntry entry) => Settings.ShowUngroupedWindowThumbnails
        && !entry.IsPinned && entry.Window != null;

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
        foreach (var window in windows)
            if (!window.Minimized && (MinimizedOnly || Settings.GroupWindows || IsExplorerWindow(window)))
                RequestWindowSnapshot(window, captureOnRestore: true);
    }

    private static bool IsExplorerWindow(TaskWindow window) =>
        string.Equals(Path.GetFileName(window.ExecutablePath), "explorer.exe", StringComparison.OrdinalIgnoreCase)
        || window.ClassName is "CabinetWClass" or "ExploreWClass";

    private void RetryForegroundWindowThumbnail()
    {
        if (!Settings.ShowUngroupedWindowThumbnails) return;
        nint handle = Native.GetForegroundWindow();
        if (handle == 0 || !windowThumbnails.TryGetValue(handle, out var state)
            || state.Image != null || state.Capture is not { IsCompleted: true }
            || Environment.TickCount64 < state.RetryAfter) return;

        var window = tracker.Describe(handle);
        if (window == null || window.Minimized) return;
        RequestWindowSnapshot(window);
        if (!MinimizedOnly && buttons.Any(button => !button.Entry.IsPinned && button.Entry.Window?.Handle == handle))
        {
            lastSignature = "";
            Reconcile();
        }
    }

    private WindowThumbnail? RequestWindowSnapshot(TaskWindow window, bool captureOnRestore = false)
    {
        nint handle = window.Handle;
        Native.GetWindowThreadProcessId(handle, out uint process);
        if (process == 0) return null;

        bool captureNeeded;
        if (!windowThumbnails.TryGetValue(handle, out var state) || state.Process != process)
        {
            state = new WindowThumbnail(process, window.Minimized, IsExplorerWindow(window));
            windowThumbnails[handle] = state;
            captureNeeded = true;
        }
        else
        {
            captureNeeded = window.Minimized != state.Minimized && (window.Minimized || captureOnRestore);
            state.Minimized = window.Minimized;
            captureNeeded |= !window.Minimized && state.Image == null && Environment.TickCount64 >= state.RetryAfter;
        }

        // Explorer can paint its folder or drive icon instead of the window
        // when iconic. Preserve the last restored snapshot in that state.
        if (captureNeeded && !(state.Explorer && window.Minimized)
            && (state.Capture == null || state.Capture.IsCompleted))
        {
            state.RetryAfter = Environment.TickCount64 + 1500;
            state.Capture = CacheWindowSnapshot(handle, state);
        }
        return state;
    }

    private async Task<WriteableBitmap?> CacheWindowSnapshot(nint handle, WindowThumbnail state)
    {
        try
        {
            var snapshot = await LoadWindowSnapshot(handle, state.Process, state.Explorer);
            Native.GetWindowThreadProcessId(handle, out uint process);
            if (snapshot != null && !closing && process == state.Process
                && windowThumbnails.TryGetValue(handle, out var current) && current == state)
                state.Image = snapshot;
        }
        catch (Exception ex) { Log.Write(ex); }
        return state.Image;
    }

    private async Task ApplyWindowThumbnail(TaskWindow window, Microsoft.UI.Xaml.Controls.Image appIcon, Grid host)
    {
        var state = RequestWindowSnapshot(window);
        if (state == null) return;

        Microsoft.UI.Xaml.Controls.Border? preview = null;
        var brush = new Microsoft.UI.Xaml.Media.ImageBrush { Stretch = Microsoft.UI.Xaml.Media.Stretch.Fill };
        void ShowSnapshot(WriteableBitmap source)
        {
            if (preview == null)
            {
                preview = new Microsoft.UI.Xaml.Controls.Border
                {
                    Background = brush,
                    HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
                    VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
                    UseLayoutRounding = false,
                    IsHitTestVisible = false
                };
                host.Children.Insert(host.Children.IndexOf(appIcon), preview);
                appIcon.Width = appIcon.Height = Math.Max(10, Math.Round(appIcon.Width * 0.36));
                appIcon.UseLayoutRounding = false;
                appIcon.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right;
                appIcon.VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom;
            }
            double fit = host.Width / Math.Max(source.PixelWidth, source.PixelHeight);
            preview.Width = source.PixelWidth * fit;
            preview.Height = source.PixelHeight * fit;
            bool rounded = Settings.ThumbnailCorners == ThumbnailCornerStyle.Rounded
                || (Settings.ThumbnailCorners == ThumbnailCornerStyle.Dynamic && Settings.RoundedCorners);
            preview.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(rounded ? 4 : 0);
            brush.ImageSource = source;
        }

        try
        {
            var previous = state.Image;
            if (previous != null) ShowSnapshot(previous);
            if (state.Capture is not { IsCompleted: false } capture) return;
            var snapshot = await capture;
            if (!closing && snapshot != null && snapshot != previous
                && windowThumbnails.TryGetValue(window.Handle, out var current) && current == state)
                ShowSnapshot(snapshot);
        }
        catch (Exception ex) { Log.Write(ex); }
    }

    private static async Task<WriteableBitmap?> LoadWindowSnapshot(nint handle, uint process, bool explorer)
    {
        byte[]? png = await Task.Run(async () =>
        {
            // PrintWindow calls into the target application synchronously.
            // Bound the number of calls and keep them off the UI thread.
            if (!await windowCaptureSlots.WaitAsync(TimeSpan.FromSeconds(2))) return null;
            try
            {
                // Explorer can briefly return a blank frame during a window
                // transition. Retry without replacing any usable cached image.
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var frame = CaptureWindowThumbnail(handle, process, !explorer && attempt == 1 ? 0u : 2u, explorer);
                    if (frame != null || !Native.IsWindow(handle)) return frame;
                    if (attempt < 2) await Task.Delay(attempt == 0 ? 60 : 150);
                }
                return null;
            }
            finally { windowCaptureSlots.Release(); }
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

    private static byte[]? CaptureWindowThumbnail(nint handle, uint process, uint flags, bool explorer)
    {
        Native.GetWindowThreadProcessId(handle, out uint actualProcess);
        if (actualProcess != process || !Native.IsWindowVisible(handle)
            || !Native.GetWindowRect(handle, out var bounds)) return null;
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
            var dc = graphics.GetHdc();
            try { if (!PrintWindow(handle, dc, flags)) return null; }
            finally { graphics.ReleaseHdc(dc); }
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
        for (int row = 1; row < 12; row++)
        {
            for (int column = 1; column < 12; column++)
            {
                Color pixel = thumbnail.GetPixel(column * thumbnail.Width / 12, row * thumbnail.Height / 12);
                if (Math.Max(pixel.R, Math.Max(pixel.G, pixel.B)) > 8) nonBlack++;
            }
        }
        if (nonBlack < 6) return null;

        using var stream = new MemoryStream();
        thumbnail.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
