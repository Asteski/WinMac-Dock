namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private readonly Dictionary<string, NewDownloadTracker> captureTrackers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<CapturePinKind>> captureFolders = new(StringComparer.OrdinalIgnoreCase);
    private long captureSettingsRead;
    private bool capturePinningEnabled;
    private bool captureScanNeeded;
    private SnippingOptions? ActiveSnippingOptions => Settings.Items.FirstOrDefault(i => ShellService.IsSnippingTool(i) && i.Snipping.PinNewCaptures)?.Snipping;
    internal void InvalidateCapturePaths() => captureSettingsRead = 0;

    private void SyncSnippingPinning()
    {
        bool enabled = ActiveSnippingOptions != null;
        if (enabled == capturePinningEnabled && captureSettingsRead != 0 && Environment.TickCount64 - captureSettingsRead < 5000) return;
        captureSettingsRead = Environment.TickCount64; capturePinningEnabled = enabled;
        captureFolders.Clear();
        if (ActiveSnippingOptions != null)
        {
            try
            {
                var paths = SnippingToolSettings.Read();
                void Add(string path, CapturePinKind kind)
                {
                    if (!captureFolders.TryGetValue(path, out var kinds)) captureFolders[path] = kinds = [];
                    kinds.Add(kind);
                }
                Add(paths.Screenshots, CapturePinKind.Screenshot); Add(paths.Recordings, CapturePinKind.Recording);
            }
            catch (Exception ex) { Log.Write(ex); }
        }
        foreach (var path in captureTrackers.Keys.Where(p => !captureFolders.ContainsKey(p)).ToArray()) captureTrackers.Remove(path);
        foreach (var path in captureFolders.Keys.Where(p => !captureTrackers.ContainsKey(p)))
        {
            var tracker = new NewDownloadTracker();
            if (!Directory.Exists(path)) tracker.Observe([], DateTime.UtcNow);
            captureTrackers[path] = tracker;
            captureScanNeeded = true;
        }
    }

    private async Task<bool> ScanSnippingPins()
    {
        bool changed = false;
        foreach (var (path, tracker) in captureTrackers.ToArray())
        {
            DownloadFile[]? files = await Task.Run(() =>
            {
                try { return new DirectoryInfo(path).EnumerateFiles().Where(f => CapturePins.KindFor(f.FullName) != CapturePinKind.None && CaptureFileClosed(f.FullName))
                    .Select(f => new DownloadFile(f.FullName, f.Length, f.LastWriteTimeUtc)).ToArray(); }
                catch (IOException) { return null; } catch (UnauthorizedAccessException) { return null; }
            });
            if (closing || files == null || !captureTrackers.TryGetValue(path, out var current) || current != tracker) continue;
            var ready = tracker.Observe(files, DateTime.UtcNow);
            foreach (var file in ready)
            {
                var options = ActiveSnippingOptions;
                var kind = CapturePins.KindFor(file);
                if (options == null || !captureFolders.TryGetValue(path, out var kinds) || !kinds.Contains(kind) || !File.Exists(file)
                    || Settings.Items.Any(i => string.Equals(i.Target, file, StringComparison.OrdinalIgnoreCase))) continue;
                var thumbnail = await IconService.Load(file, thumbnail: true, requestedPixels: IconPixels);
                if (closing || ActiveSnippingOptions == null || !File.Exists(file)) continue;
                if (thumbnail != null) readyDownloadIcons[file] = thumbnail;
                Settings.Items.Add(new DockItem { Kind = ItemKind.File, Target = file, Name = Path.GetFileNameWithoutExtension(file),
                    ShowThumbnail = true, IsDownloadedPin = true, CapturePin = kind });
                changed = true;
            }
        }
        if (ActiveSnippingOptions is { } active) changed |= CapturePins.Trim(Settings.Items, active.PinLimit);
        return changed;
    }

    private static bool CaptureFileClosed(string path)
    {
        try { using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None); return file.Length > 0; }
        catch (IOException) { return false; } catch (UnauthorizedAccessException) { return false; }
    }
}
