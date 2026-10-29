using System.Runtime.InteropServices;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Windows.Storage.Streams;

namespace WinMac.Dock;
internal static class IconService
{
    private static readonly Dictionary<string, (string Stamp, ImageSource Image)> flyoutCache = new(StringComparer.OrdinalIgnoreCase);
    public static async Task<ImageSource?> LoadFlyout(string path, bool thumbnail, int requestedPixels)
    {
        string stamp;
        try
        {
            var info = new FileInfo(path);
            stamp = $"{revision}|{info.LastWriteTimeUtc.Ticks}|{(info.Exists ? info.Length : 0)}";
        }
        catch { return await Load(path, thumbnail, requestedPixels: requestedPixels); }
        string key = $"{path}|{thumbnail}|{requestedPixels}";
        if (flyoutCache.TryGetValue(key, out var cached) && cached.Stamp == stamp) return cached.Image;
        if (flyoutCache.ContainsKey(key)) Invalidate(path);
        var image = await Load(path, thumbnail, requestedPixels: requestedPixels);
        if (image != null)
        {
            if (flyoutCache.Count >= 2048) flyoutCache.Remove(flyoutCache.Keys.First());
            flyoutCache[key] = (stamp, image);
        }
        return image;
    }
    private static readonly Dictionary<string, ImageSource> cache = [];
    private sealed record LoadedIdentity(string Prefix);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, LoadedIdentity> loadedIdentities = new();
    private static readonly Dictionary<string, ImageSource> visibleArtwork = [];
    public static void RetainVisibleArtwork(IEnumerable<ImageSource> sources)
    {
        visibleArtwork.Clear();
        foreach (var source in sources)
            if (loadedIdentities.TryGetValue(source, out var identity)) visibleArtwork[identity.Prefix] = source;
    }
    private static readonly Dictionary<string, Task<ImageSource?>> pending = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, int> pathVersions = new(StringComparer.OrdinalIgnoreCase);
    private static int revision;
    private static bool darkTheme;
    private static string? themeCacheIdentity;
    public static void SetTheme(bool dark, string? identity = null)
    {
        if (darkTheme == dark && themeCacheIdentity == identity) return;
        darkTheme = dark; themeCacheIdentity = identity; InvalidateAll();
    }
    public static void InvalidateAll() { revision++; cache.Clear(); visibleArtwork.Clear(); flyoutCache.Clear(); }
    private sealed record RasterInfo(int RequestedPixels, int Width, int Height, string Method, int SourceWidth = 0, int SourceHeight = 0);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, RasterInfo> dimensions = new();
    // XAML can read the backing bitmap again when a cached source is attached
    // to a new Image. Closing it after SetBitmapAsync causes a native
    // RO_E_CLOSED crash on that later render, outside managed exception handlers.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ImageSource, SoftwareBitmap> backingBitmaps = new();
    public static object? Describe(ImageSource? source) => source != null && dimensions.TryGetValue(source, out var info) ? info : null;
    public static bool WriteWindowIcon(IReadOnlyList<ImageSource> sources, string path)
    {
        var frames = new List<(int Size, byte[] Png)>();
        foreach (var source in sources)
        {
            if (!backingBitmaps.TryGetValue(source, out var pixels) || !dimensions.TryGetValue(source, out var info)) continue;
            int size = Math.Clamp(info.RequestedPixels, 16, 256);
            if (frames.Any(f => f.Size == size)) continue;
            // SoftwareBitmapSource uses premultiplied BGRA. PNG/ICO stores
            // straight alpha; exporting the premultiplied bytes darkens edges.
            using var straight = SoftwareBitmap.Convert(pixels, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight);
            var bytes = new byte[straight.PixelWidth * straight.PixelHeight * 4];
            straight.CopyToBuffer(System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions.AsBuffer(bytes));
            using var bitmap = new System.Drawing.Bitmap(straight.PixelWidth, straight.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try { for (int row = 0; row < bitmap.Height; row++) Marshal.Copy(bytes, row * bitmap.Width * 4, data.Scan0 + row * data.Stride, bitmap.Width * 4); }
            finally { bitmap.UnlockBits(data); }
            using var square = new System.Drawing.Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(square))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                double ratio = size / (double)Math.Max(bitmap.Width, bitmap.Height);
                int width = Math.Max(1, (int)Math.Round(bitmap.Width * ratio)), height = Math.Max(1, (int)Math.Round(bitmap.Height * ratio));
                if (bitmap.Width == size && bitmap.Height == size) graphics.DrawImageUnscaled(bitmap, 0, 0);
                else graphics.DrawImage(bitmap, (size - width) / 2, (size - height) / 2, width, height);
            }
            using var png = new MemoryStream(); square.Save(png, System.Drawing.Imaging.ImageFormat.Png);
            frames.Add((size, png.ToArray()));
        }
        if (frames.Count == 0) return false;
        using var output = new BinaryWriter(File.Create(path));
        output.Write((ushort)0); output.Write((ushort)1); output.Write((ushort)frames.Count);
        int offset = 6 + 16 * frames.Count;
        foreach (var frame in frames)
        {
            output.Write((byte)(frame.Size == 256 ? 0 : frame.Size)); output.Write((byte)(frame.Size == 256 ? 0 : frame.Size));
            output.Write((byte)0); output.Write((byte)0); output.Write((ushort)1); output.Write((ushort)32);
            output.Write(frame.Png.Length); output.Write(offset); offset += frame.Png.Length;
        }
        foreach (var frame in frames) output.Write(frame.Png);
        return true;
    }
    public static void Invalidate(string path) { foreach (var entry in flyoutCache.Keys.Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToArray()) flyoutCache.Remove(entry); pathVersions[path] = pathVersions.GetValueOrDefault(path) + 1; foreach (var key in cache.Keys.Where(k => k.StartsWith(path + "|", StringComparison.OrdinalIgnoreCase)).ToArray()) cache.Remove(key); }
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int AssocQueryString(uint flags, uint query, string association, string? extra, System.Text.StringBuilder value, ref uint length);
    private static string Association(string path, uint query)
    {
        var extension = Path.GetExtension(path); if (string.IsNullOrEmpty(extension)) return "";
        var value = new System.Text.StringBuilder(2048); uint length = (uint)value.Capacity;
        return AssocQueryString(0, query, extension, null, value, ref length) == 0 ? value.ToString() : "";
    }
    [StructLayout(LayoutKind.Sequential)] private struct SIZE { public int Width, Height; }
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageFactory { [PreserveSig] int GetImage(SIZE size, uint flags, out nint bitmap); }
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IImageFactory item);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo { public uint Size; public int Width, Height; public ushort Planes, Bits; public uint Compression, ImageSize; public int XPixels, YPixels; public uint Used, Important; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapObject { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [DllImport("gdi32.dll")] private static extern int GetObject(nint bitmap, int size, out BitmapObject info);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] bytes, ref BitmapInfo info, uint usage);
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    public static Task<ImageSource?> Load(string path, bool thumbnail = false, bool imageFile = false, bool associatedApp = false, int requestedPixels = 64)
    {
        path = BundledIconPath.Resolve(path, Path.Combine(AppContext.BaseDirectory, "assets"));
        if (string.IsNullOrEmpty(path)) return Task.FromResult<ImageSource?>(null);
        // Bundled icons and native resources need explicit frame selection and alpha decoding
        // as manually assigned icons. Preserve explicit file-association requests.
        if (!associatedApp && (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase) || path.Contains(".dll,", StringComparison.OrdinalIgnoreCase) || path.Contains(".exe,", StringComparison.OrdinalIgnoreCase))) imageFile = true;
        requestedPixels = Math.Clamp(requestedPixels, 16, 512);
        var recycle = path.Equals("shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase) ? RecycleIconLocation() : null;
        var key = RequestKey(path, revision, darkTheme, recycle, thumbnail, imageFile, associatedApp, requestedPixels);
        if (cache.TryGetValue(key, out var image)) return Task.FromResult<ImageSource?>(image);
        if (pending.TryGetValue(key, out var loading)) return loading;
        var task = LoadCore(path, thumbnail, imageFile, associatedApp, requestedPixels);
        pending[key] = task;
        return CompleteSharedLoad(task, key);
    }
    // Reuse completed artwork immediately while a different raster size loads.
    // The key retains theme, path version and rendering options; only size varies.
    public static ImageSource? TryGetLoaded(string path, bool thumbnail = false, bool imageFile = false, bool associatedApp = false, int requestedPixels = 64)
    {
        path = BundledIconPath.Resolve(path, Path.Combine(AppContext.BaseDirectory, "assets"));
        if (string.IsNullOrEmpty(path)) return null;
        if (!associatedApp && (Path.GetExtension(path).Equals(".ico", StringComparison.OrdinalIgnoreCase) || path.Contains(".dll,", StringComparison.OrdinalIgnoreCase) || path.Contains(".exe,", StringComparison.OrdinalIgnoreCase))) imageFile = true;
        var recycle = path.Equals("shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase) ? RecycleIconLocation() : null;
        string key = RequestKey(path, revision, darkTheme, recycle, thumbnail, imageFile, associatedApp, Math.Clamp(requestedPixels, 16, 512));
        if (cache.TryGetValue(key, out var exact)) return exact;
        string prefix = RequestKey(path, revision, darkTheme, recycle, thumbnail, imageFile, associatedApp, 0)[..^1];
        if (visibleArtwork.TryGetValue(prefix, out var retained)) return retained;
        return cache.Where(pair => pair.Key.StartsWith(prefix, StringComparison.Ordinal))
            .OrderByDescending(pair => dimensions.TryGetValue(pair.Value, out var info) ? info.RequestedPixels : 0)
            .Select(pair => pair.Value).FirstOrDefault();
    }
    private static async Task<ImageSource?> CompleteSharedLoad(Task<ImageSource?> task, string key)
    {
        try { return await task; }
        finally { if (pending.TryGetValue(key, out var current) && ReferenceEquals(current, task)) pending.Remove(key); }
    }
    private static string RequestKey(string path, int generation, bool dark, string? recycle, bool thumbnail, bool imageFile, bool associatedApp, int pixels)
        => path + "|" + generation + "|" + pathVersions.GetValueOrDefault(path) + "|" + dark + "|" + recycle + "|" + thumbnail + "|" + imageFile + "|" + associatedApp + "|" + pixels;
    private static async Task<ImageSource?> LoadCore(string path, bool thumbnail = false, bool imageFile = false, bool associatedApp = false, int requestedPixels = 64)
    {
        if (string.IsNullOrEmpty(path)) return null;
        requestedPixels = Math.Clamp(requestedPixels, 16, 512);
        var generation = revision;
        var dark = darkTheme;
        // Read the actual empty/full registration, rather than Explorer's cached
        // namespace bitmap (which can still refer to the previous theme).
        var recycleLocation = path.Equals("shell:RecycleBinFolder", StringComparison.OrdinalIgnoreCase) ? RecycleIconLocation() : null;
        var key = RequestKey(path, generation, dark, recycleLocation, thumbnail, imageFile, associatedApp, requestedPixels);
        if (cache.TryGetValue(key, out var cached)) return cached;
        try
        {
            var themedAsset = !thumbnail && !imageFile ? await Task.Run(() => PackageIconAsset(path, requestedPixels, dark)) : null;
            if (themedAsset != null)
            {
                var file = await StorageFile.GetFileFromPathAsync(themedAsset); using var input = await file.OpenReadAsync();
                var image = await Decode(input, requestedPixels, false, "Package: " + themedAsset);
                if (generation == revision) Remember(key, image);
                return image;
            }
            var extension = Path.GetExtension(path).ToLowerInvariant();
            // StorageFile rejects Shell links (E_ACCESSDENIED). Ask the Shell
            // for the link's own icon, preserving custom artwork and overlays.
            bool shellThumbnail = thumbnail && extension is not (".lnk" or ".url");
            if ((thumbnail || imageFile) && extension == ".svg" && File.Exists(path))
            {
                var vector = new SvgImageSource { RasterizePixelWidth = requestedPixels, RasterizePixelHeight = requestedPixels };
                var file = await StorageFile.GetFileFromPathAsync(path); using var input = await file.OpenReadAsync();
                if (await vector.SetSourceAsync(input) != SvgImageSourceLoadStatus.Success) return null;
                dimensions.Add(vector, new(requestedPixels, requestedPixels, requestedPixels, "SVG"));
                Remember(key, vector); return vector;
            }
            if ((thumbnail || imageFile) && File.Exists(path) && new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".ico" }.Contains(Path.GetExtension(path).ToLowerInvariant()))
            {
                var file = await StorageFile.GetFileFromPathAsync(path); using var source = await file.OpenReadAsync();
                var image = await Decode(source, requestedPixels, extension == ".ico", "Custom image"); Remember(key, image); return image;
            }
            if (shellThumbnail && File.Exists(path))
            {
                try
                {
                    var file = await StorageFile.GetFileFromPathAsync(path);
                    using var thumb = await file.GetThumbnailAsync(ThumbnailMode.SingleItem, (uint)requestedPixels, ThumbnailOptions.None);
                    if (thumb != null) { var image = await Decode(thumb, requestedPixels, false, "Thumbnail"); Remember(key, image); return image; }
                }
                catch
                {
                    // A failed thumbnail provider must not bypass icon fallback.
                    shellThumbnail = false;
                }
            }
            // Shell's image factory understands AppsFolder identities as well as files.
            var bytes = await Task.Run(() =>
            {
                if (!associatedApp && ReadShortcutIcon(path, requestedPixels) is { } shortcutIcon) return shortcutIcon;
                if (imageFile && ReadRegisteredIcon(path, requestedPixels) is { } customResource) return customResource;
                if (recycleLocation != null && ReadRegisteredIcon(recycleLocation, requestedPixels) is { } recycleIcon) return recycleIcon;
                if (associatedApp)
                {
                    // ASSOCSTR_APPID uses the user's default application and
                    // also resolves packaged handlers without an executable.
                    var appId = Association(path, 21);
                    if (!string.IsNullOrEmpty(appId) && ReadShellImage("shell:AppsFolder\\" + appId, false, requestedPixels) is { } packagedIcon) return packagedIcon;
                    var executable = Association(path, 2);
                    if (File.Exists(executable) && ReadShellImage(executable, false, requestedPixels) is { } appIcon) return appIcon;
                }
                return ReadShellImage(path, shellThumbnail, requestedPixels);
            });
            if (bytes == null) return null;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync(); }
            stream.Seek(0); var result = await Decode(stream, requestedPixels, false, recycleLocation == null ? "Shell" : "Registered Recycle Bin: " + recycleLocation);
            if (generation == revision) Remember(key, result); return result;
        }
        catch { return null; }
    }
    internal static string? RecycleIconLocation(bool? full = null)
    {
        const string relative = @"Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\DefaultIcon";
        var state = (full ?? ShellService.RecycleCount() > 0) ? "full" : "empty";
        using var user = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(relative);
        using var machine = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\DefaultIcon");
        return (user?.GetValue(state) ?? machine?.GetValue(state) ?? user?.GetValue("") ?? machine?.GetValue("")) as string;
    }
    public static string ReadThemeIdentity()
    {
        using var user = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\DefaultIcon");
        using var machine = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"CLSID\{645FF040-5081-101B-9F08-00AA002F954E}\DefaultIcon");
        return string.Join("|", new[] { "", "empty", "full" }.SelectMany(name => new[] { user?.GetValue(name)?.ToString(), machine?.GetValue(name)?.ToString() }));
    }
    private static byte[]? ReadRegisteredIcon(string location, int pixels)
    {
        location = Environment.ExpandEnvironmentVariables(location);
        var comma = location.LastIndexOf(',');
        var index = 0;
        if (comma >= 0 && int.TryParse(location[(comma + 1)..], out index)) location = location[..comma];
        location = location.Trim().Trim('"');
        return File.Exists(location) ? ReadExecutableIcon(location, pixels, index) : null;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetPrivateProfileString(string section, string key, string fallback, System.Text.StringBuilder value, uint size, string path);

    private static string InternetShortcutValue(string path, string key)
    {
        var value = new System.Text.StringBuilder(32768);
        GetPrivateProfileString("InternetShortcut", key, "", value, (uint)value.Capacity, path);
        return value.ToString();
    }

    internal static byte[]? ReadShortcutIcon(string path, int pixels)
    {
        object? scripting = null, shortcut = null;
        try
        {
            if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
            {
                // Internet shortcuts can use any protocol (including steam://).
                // Their assigned icon is independent of the URL's file association.
                var file = InternetShortcutValue(path, "IconFile");
                if (string.IsNullOrWhiteSpace(file)) return null;
                _ = int.TryParse(InternetShortcutValue(path, "IconIndex"), out int index);
                return ReadRegisteredIcon(file + "," + index, pixels);
            }
            if (!Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
            scripting = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);
            shortcut = ((dynamic)scripting!).CreateShortcut(path);
            string location = ((dynamic)shortcut).IconLocation ?? "";
            if (ReadRegisteredIcon(location, pixels) is { } assigned) return assigned;
            string target = ((dynamic)shortcut).TargetPath ?? "";
            // Extract native frames rather than Shell's 256px presentation tile,
            // which can frame a small icon inside a large, mostly empty square.
            if (Path.GetExtension(target).Equals(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(target))
                return ReadExecutableIcon(target, pixels);
        }
        catch { /* Keep Shell fallback for unsupported or broken shortcuts. */ }
        finally
        {
            if (shortcut != null) Marshal.ReleaseComObject(shortcut);
            if (scripting != null) Marshal.ReleaseComObject(scripting);
        }
        return null;
    }
    internal static string? PackageIconAsset(string path, int pixels, bool dark)
    {
        const string prefix = "shell:AppsFolder\\";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var identity = path[prefix.Length..]; var separator = identity.IndexOf('!');
        if (separator < 0) return null;
        try
        {
            var package = new Windows.Management.Deployment.PackageManager().FindPackagesForUser("")
                .FirstOrDefault(p => p.Id.FamilyName.Equals(identity[..separator], StringComparison.OrdinalIgnoreCase));
            if (package == null) return null;
            var root = package.InstalledLocation.Path;
            var manifest = System.Xml.Linq.XDocument.Load(Path.Combine(root, "AppxManifest.xml"));
            var application = manifest.Descendants().FirstOrDefault(e => e.Name.LocalName == "Application" && (string?)e.Attribute("Id") == identity[(separator + 1)..]);
            var logo = application?.Elements().FirstOrDefault(e => e.Name.LocalName == "VisualElements")?.Attribute("Square44x44Logo")?.Value;
            if (string.IsNullOrEmpty(logo)) return null;
            var full = Path.Combine(root, logo.Replace('/', '\\'));
            var directory = Path.GetDirectoryName(full)!; var stem = Path.GetFileNameWithoutExtension(full);
            // Windows uses lightunplated (dark glyph) on light surfaces and
            // unplated (light glyph) on dark surfaces. The shell cache does not
            // take this caller's background into account.
            foreach (var form in new[] { dark ? "unplated" : "lightunplated", "unplated", "" })
            {
                var candidates = Directory.EnumerateFiles(directory, stem + ".targetsize-*.png")
                    .Select(f => new { Path = f, Match = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(f), "^" + System.Text.RegularExpressions.Regex.Escape(stem) + @"\.targetsize-(\d+)" + (form.Length == 0 ? "" : "_altform-" + form) + @"\.png$", System.Text.RegularExpressions.RegexOptions.IgnoreCase) })
                    .Where(c => c.Match.Success).Select(c => new { c.Path, Size = int.Parse(c.Match.Groups[1].Value) }).OrderBy(c => c.Size).ToArray();
                if (candidates.Length > 0) return (candidates.FirstOrDefault(c => c.Size >= pixels) ?? candidates[^1]).Path;
            }
            return File.Exists(full) ? full : null;
        }
        catch { return null; }
    }
    public static async Task<ImageSource?> LoadWindowIcon(nint window, int requestedPixels = 32)
    {
        requestedPixels = Math.Clamp(requestedPixels, 16, 128);
        var key = "window:" + window.ToInt64() + "|" + revision + "|" + requestedPixels;
        if (cache.TryGetValue(key, out var cached)) return cached;
        try
        {
            var bytes = await Task.Run(() =>
            {
                var source = Native.WindowIcon(window);
                if (source == 0) return null;
                var copy = Native.CopyIcon(source);
                if (copy == 0) return null;
                try
                {
                    using var icon = System.Drawing.Icon.FromHandle(copy);
                    using var bitmap = icon.ToBitmap();
                    using var stream = new MemoryStream();
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    return stream.ToArray();
                }
                finally { Native.DestroyIcon(copy); }
            });
            if (bytes == null) return null;
            using var stream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync(); }
            stream.Seek(0); var result = await Decode(stream, requestedPixels, false, "Window"); Remember(key, result); return result;
        }
        catch { return null; }
    }
    private static async Task<ImageSource> Decode(IRandomAccessStream stream, int pixels, bool iconFrames, string method)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        uint selected = 0;
        if (iconFrames && decoder.FrameCount > 1)
        {
            var sizes = new List<int>();
            for (uint i = 0; i < decoder.FrameCount; i++) { var row = await decoder.GetFrameAsync(i); sizes.Add((int)Math.Max(row.PixelWidth, row.PixelHeight)); }
            selected = (uint)IconRaster.FrameIndex(sizes, pixels);
        }
        var frame = await decoder.GetFrameAsync(selected);
        double ratio = Math.Min(1, pixels / (double)Math.Max(frame.PixelWidth, frame.PixelHeight));
        var transform = new BitmapTransform { ScaledWidth = Math.Max(1u, (uint)Math.Round(frame.PixelWidth * ratio)), ScaledHeight = Math.Max(1u, (uint)Math.Round(frame.PixelHeight * ratio)), InterpolationMode = BitmapInterpolationMode.Fant };
        var bitmap = await frame.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
        var image = new SoftwareBitmapSource();
        try { await image.SetBitmapAsync(bitmap); }
        catch { bitmap.Dispose(); throw; }
        // Release COM ownership naturally when the source is no longer used;
        // do not Dispose on cache eviction, because displayed Images can own it.
        backingBitmaps.Add(image, bitmap);
        dimensions.Add(image, new(pixels, bitmap.PixelWidth, bitmap.PixelHeight, method, (int)frame.PixelWidth, (int)frame.PixelHeight));
        return image;
    }
    private static void Remember(string key, ImageSource image)
    {
        if (cache.Count >= 256) cache.Remove(cache.Keys.First());
        cache[key] = image;
        loadedIdentities.Remove(image);
        loadedIdentities.Add(image, new LoadedIdentity(key[..(key.LastIndexOf('|') + 1)]));
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHDefExtractIcon(string file, int index, uint flags, out nint large, out nint small, uint size);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string file, int index, nint large, nint small, uint count);
    internal static int ResourceIconCount(string path) => checked((int)ExtractIconEx(path, -1, 0, 0, 0));
    private static byte[]? ReadExecutableIcon(string path, int pixels, int index = 0)
    {
        nint large = 0, small = 0;
        try
        {
            if (SHDefExtractIcon(path, index, 0, out large, out small, (uint)pixels) != 0 || large == 0) return null;
            using var icon = System.Drawing.Icon.FromHandle(large);
            using var bitmap = icon.ToBitmap();
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            return stream.ToArray();
        }
        finally { if (large != 0) Native.DestroyIcon(large); if (small != 0) Native.DestroyIcon(small); }
    }
    private static byte[]? ReadShellImage(string path, bool thumbnail, int pixels)
    {
        // Task Manager's Shell image factory result can be a transparent
        // placeholder on current Windows builds even though the executable
        // exposes a valid application icon. Read its icon resource directly.
        if (string.Equals(Path.GetFileName(path), "taskmgr.exe", StringComparison.OrdinalIgnoreCase))
            return ReadFileIcon(path);
        if (!thumbnail && Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase)
            && File.Exists(path) && ReadExecutableIcon(path, pixels) is { } executableIcon) return executableIcon;
        var iid = typeof(IImageFactory).GUID;
        if (SHCreateItemFromParsingName(path, 0, ref iid, out var factory) == 0)
        {
            try
            {
                // Let WinUI/WIC downsample native artwork. Shell's SCALEUP path
                // stretches small frames with GDI and permanently loses detail.
                var nativePixels = IconRaster.ShellPixels(pixels, thumbnail);
                if (factory.GetImage(new SIZE { Width = nativePixels, Height = nativePixels }, thumbnail ? 1u : IconRaster.NativeIconFlags, out var bitmap) == 0)
                {
                    try { return PreserveAlpha(bitmap); }
                    finally { DeleteObject(bitmap); }
                }
            }
            finally { Marshal.ReleaseComObject(factory); }
        }
        return ReadFileIcon(path);
    }
    private static byte[]? ReadFileIcon(string path)
    {
        var info = new Native.SHFILEINFO { DisplayName = "", TypeName = "" };
        if (Native.SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<Native.SHFILEINFO>(), 0x100) == 0 || info.Icon == 0) return null;
        try { using var icon = System.Drawing.Icon.FromHandle(info.Icon); using var bitmap = icon.ToBitmap(); using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png); return stream.ToArray(); }
        finally { Native.DestroyIcon(info.Icon); }
    }
    private static byte[]? PreserveAlpha(nint handle)
    {
        if (GetObject(handle, Marshal.SizeOf<BitmapObject>(), out var obj) == 0 || obj.Width <= 0 || obj.Height <= 0) return null;
        var info = new BitmapInfo { Size = 40, Width = obj.Width, Height = -obj.Height, Planes = 1, Bits = 32 };
        var bytes = new byte[obj.Width * obj.Height * 4]; var dc = GetDC(0);
        try { if (GetDIBits(dc, handle, 0, (uint)obj.Height, bytes, ref info, 0) == 0) return null; }
        finally { ReleaseDC(0, dc); }
        if (!Enumerable.Range(0, obj.Width * obj.Height).Any(i => bytes[i * 4 + 3] != 0))
            for (var i = 3; i < bytes.Length; i += 4) bytes[i] = 255;
        using var bitmap = new System.Drawing.Bitmap(obj.Width, obj.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, obj.Width, obj.Height), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try { for (var row = 0; row < obj.Height; row++) Marshal.Copy(bytes, row * obj.Width * 4, data.Scan0 + row * data.Stride, obj.Width * 4); }
        finally { bitmap.UnlockBits(data); }
        using var stream = new MemoryStream(); bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png); return stream.ToArray();
    }
    public static string PathFor(DockItem item)
    {
        if (!string.IsNullOrEmpty(CustomPathFor(item))) return CustomPathFor(item);
        if (item.IsAutoUsbPin)
        {
            if (item.AttachedDriveKind == AttachedDriveKind.UsbFlashDrive) return Path.Combine(Environment.SystemDirectory, "DDORes.dll") + ",-2526";
            if (item.AttachedDriveKind == AttachedDriveKind.MemoryCard) return Path.Combine(Environment.SystemDirectory, "imageres.dll") + ",-60";
            if (item.AttachedDriveKind == AttachedDriveKind.Optical) return Path.Combine(Environment.SystemDirectory, "imageres.dll") + ",-30";
        }
        if (item.Kind == ItemKind.RecycleBin) return "shell:RecycleBinFolder";
        if (item.Kind == ItemKind.TaskView) return IconResources.PathFor(darkTheme ? BundledIcons.TaskViewDark : BundledIcons.TaskViewLight);
        if (item.Kind == ItemKind.Search) return IconResources.PathFor(BundledIcons.Search);
        if (item.Kind == ItemKind.StartMenu) return IconResources.PathFor(BundledIcons.Start);
        // Explorer's window AUMID need not identify an AppsFolder entry. The
        // shell can return a successful generic icon instead of failing, so
        // use the actual Windows executable for this known desktop app.
        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (item.Kind == ItemKind.Application && (string.Equals(item.ExecutablePath, explorer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.Target, explorer, StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.GetFileName(item.ExecutablePath), "explorer.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.AppUserModelId, "Microsoft.Windows.Explorer", StringComparison.OrdinalIgnoreCase))) return explorer;
        // Desktop AUMIDs are grouping identities and need not be registered
        // AppsFolder entries. Prefer the executable for unpackaged applications.
        var executable = string.IsNullOrEmpty(item.ExecutablePath) ? item.Target : item.ExecutablePath;
        if (item.Kind == ItemKind.Application && !item.AppUserModelId.Contains('!') && File.Exists(executable)
            && Path.GetExtension(executable).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return executable;
        return item.Kind == ItemKind.Application && !string.IsNullOrEmpty(item.AppUserModelId) ? "shell:AppsFolder\\" + item.AppUserModelId : item.Target;
    }
    public static string CustomPathFor(DockItem item) => item.Kind == ItemKind.RecycleBin
        ? RecycleCustomPath(item, ShellService.RecycleCount() > 0, darkTheme)
        : darkTheme && !string.IsNullOrWhiteSpace(item.DarkModeIcon) ? item.DarkModeIcon : item.CustomIcon;
    internal static string RecycleCustomPath(DockItem item, bool full, bool dark)
    {
        string themed = full ? item.RecycleFullDarkIcon : item.RecycleEmptyDarkIcon;
        string light = full ? item.RecycleFullIcon : item.RecycleEmptyIcon;
        if (dark && !string.IsNullOrWhiteSpace(themed)) return themed;
        if (!string.IsNullOrWhiteSpace(light)) return light;
        return dark && !string.IsNullOrWhiteSpace(item.DarkModeIcon) ? item.DarkModeIcon : item.CustomIcon;
    }
    internal static async Task<ImageSource?> LoadRecycleState(bool full, int pixels)
    {
        string? location = RecycleIconLocation(full);
        var bytes = location == null ? null : await Task.Run(() => ReadRegisteredIcon(location, pixels));
        if (bytes == null) return await Load("shell:RecycleBinFolder", requestedPixels: pixels);
        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0))) { writer.WriteBytes(bytes); await writer.StoreAsync(); await writer.FlushAsync(); }
        stream.Seek(0);
        return await Decode(stream, pixels, false, "Recycle Bin state preview");
    }
}
