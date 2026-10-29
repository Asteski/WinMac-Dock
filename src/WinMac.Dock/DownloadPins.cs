namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private readonly Microsoft.UI.Xaml.DispatcherTimer downloadPinTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, Microsoft.UI.Xaml.Media.ImageSource> readyDownloadIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> knownDownloadPins = [], pendingDownloadBounces = [];
    private readonly HashSet<Microsoft.UI.Xaml.Controls.Border> queuedDownloadFeedback = [];
    private bool downloadPinsObserved;

    private void QueueDownloadFeedback(DockItem item, Microsoft.UI.Xaml.Controls.Image image, Microsoft.UI.Xaml.Controls.Grid icon)
    {
        if (closing || image.Source == null) return;
        var live = buttons.FirstOrDefault(b => b.Entry.Item.Id == item.Id && b.Icon == icon);
        if (live.Button == null || !downloadIndicators.TryGetValue(live.Button, out var indicator) || !queuedDownloadFeedback.Add(indicator)) return;
        _ = RevealDownloadFeedback(item, live.Button, image, icon, indicator);
    }

    private async Task RevealDownloadFeedback(DockItem item, Microsoft.UI.Xaml.Controls.Button button,
        Microsoft.UI.Xaml.Controls.Image image, Microsoft.UI.Xaml.Controls.Grid icon, Microsoft.UI.Xaml.Controls.Border indicator)
    {
        try
        {
            // A cached icon may finish synchronously inside AddButton. Let the
            // rebuild create its row transition before attempting a bounce.
            await Task.Yield();
            bool Current() => !closing && image.Source != null && downloadIndicators.TryGetValue(button, out var current) && ReferenceEquals(current, indicator)
                && Settings.Items.Any(i => i.Id == item.Id);
            while (Current() && (!button.IsLoaded || rowAnimating || rowStaging || rowFinishing || reconciling || positioning || hidden || visibilitySliding || popupCount > 0))
                await Task.Delay(32);
            if (!Current()) return;
            root.UpdateLayout();
            if (pendingDownloadBounces.Remove(item.Id)) Bounce(icon);
            root.UpdateLayout();
            AnimateDownloadIndicator(indicator, item.Id);
        }
        catch (Exception ex) { Log.Write(ex); }
        finally { queuedDownloadFeedback.Remove(indicator); }
    }
    private readonly Dictionary<string, NewDownloadTracker> downloadTrackers = new(StringComparer.OrdinalIgnoreCase);
    private bool scanningDownloads, downloadTimerHooked, scanDownloadsAgain;
    private readonly Dictionary<string, FileSystemWatcher> downloadPinWatchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly Microsoft.UI.Xaml.DispatcherTimer downloadChangeTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };

    private void QueueDownloadScan()
    {
        if (closing) return;
        if (!downloadChangeTimer.IsEnabled) downloadChangeTimer.Start();
    }
    private void SyncDownloadWatchers(IEnumerable<string> folders)
    {
        var wanted = folders.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in downloadPinWatchers.Keys.Where(p => !wanted.Contains(p)).ToArray())
        { downloadPinWatchers[folder].Dispose(); downloadPinWatchers.Remove(folder); }
        foreach (var folder in wanted.Where(p => !downloadPinWatchers.ContainsKey(p)))
        {
            if (!Directory.Exists(folder)) continue;
            try
            {
                var watcher = new FileSystemWatcher(folder) { NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.LastWrite };
                FileSystemEventHandler changed = (_, _) => DispatcherQueue.TryEnqueue(QueueDownloadScan);
                watcher.Created += changed; watcher.Deleted += changed; watcher.Changed += changed;
                watcher.Renamed += (_, _) => DispatcherQueue.TryEnqueue(QueueDownloadScan);
                watcher.Error += (_, _) => DispatcherQueue.TryEnqueue(() =>
                {
                    if (downloadPinWatchers.Remove(folder, out var failed)) failed.Dispose();
                    QueueDownloadScan();
                });
                watcher.EnableRaisingEvents = true; downloadPinWatchers[folder] = watcher;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
        }
    }

    private readonly HashSet<string> openingDownloadPins = [];
    private void UnpinOpenedDownload(DockItem item)
    {
        if (item.IsDownloadedPin && openingDownloadPins.Add(item.Id)) _ = AnimateOpenedDownload(item);
    }
    private async Task AnimateOpenedDownload(DockItem item)
    {
        try
        {
            // Menu launches must wait for dismissal before starting hover artwork.
            while (!closing && popupCount > 0) await Task.Delay(16);
            if (closing || !Settings.Items.Any(i => i.Id == item.Id)) return;
            BounceItem(item);
            if (Motion && !dockOverflow) await Task.Delay(Settings.LaunchBounceMs + 32);
            bool unpin = item.CapturePin != CapturePinKind.None
                ? Settings.Items.Any(i => ShellService.IsSnippingTool(i) && i.Snipping.UnpinAfterOpening)
                : Settings.Items.Any(i => i.Kind == ItemKind.Folder && IsDownloads(i) && i.Folder.UnpinDownloadsAfterOpening);
            if (closing || !unpin) return;
            while (!closing && popupCount > 0) await Task.Delay(16);
            if (closing || !Settings.Items.Remove(item)) return;
            // Reconcile captures the settled row and animates the removed item out.
            Save(); Reconcile();
        }
        catch (Exception ex) { Log.Write(ex); }
        finally { openingDownloadPins.Remove(item.Id); QueueDownloadScan(); }
    }

    private void SyncDownloadPinning()
    {
        var currentPins = Settings.Items.Where(i => i.IsDownloadedPin).Select(i => i.Id).ToHashSet();
        if (downloadPinsObserved)
            foreach (var id in currentPins.Where(id => !knownDownloadPins.Contains(id))) pendingDownloadBounces.Add(id);
        knownDownloadPins.Clear(); knownDownloadPins.UnionWith(currentPins); downloadPinsObserved = true;
        pendingDownloadBounces.RemoveWhere(id => !currentPins.Contains(id));
        appearedDownloadIndicators.RemoveWhere(id => !currentPins.Contains(id));
        foreach (var file in readyDownloadIcons.Keys.Where(p => !Settings.Items.Any(i => i.IsDownloadedPin && string.Equals(i.Target, p, StringComparison.OrdinalIgnoreCase))).ToArray()) readyDownloadIcons.Remove(file);
        if (MonitorOverride != null) return;
        SyncSnippingPinning();
        var downloadsItem = Settings.Items.FirstOrDefault(i => i.Kind == ItemKind.Folder && IsDownloads(i));
        if (downloadsItem != null && DownloadPinLayout.Trim(Settings.Items, downloadsItem.Folder.DownloadPinLimit)) Save();
        var paths = Settings.Items.Where(i => i.Kind == ItemKind.Folder && IsDownloads(i) && (i.Folder.PinNewDownloads || i.Folder.OpenNewDownloadsAutomatically))
            .Select(i => i.Target).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var path in downloadTrackers.Keys.Where(p => !paths.Contains(p)).ToArray()) downloadTrackers.Remove(path);
        bool added = false;
        foreach (var path in paths.Where(p => !downloadTrackers.ContainsKey(p)))
        { downloadTrackers[path] = new(); added = true; }
        SyncDownloadWatchers(paths.Concat(captureFolders.Keys).Concat(Settings.Items.Where(i => i.IsDownloadedPin).Select(i => Path.GetDirectoryName(i.Target)).OfType<string>()));
        if (!downloadTimerHooked)
        {
            downloadPinTimer.Tick += async (_, _) => { SyncDownloadPinning(); await ScanDownloadPins(); };
            downloadChangeTimer.Tick += async (_, _) => { downloadChangeTimer.Stop(); await ScanDownloadPins(); };
            downloadTimerHooked = true;
        }
        if (paths.Count == 0 && captureFolders.Count == 0 && !Settings.Items.Any(i => i.IsDownloadedPin)) downloadPinTimer.Stop();
        else { downloadPinTimer.Start(); if (added || captureScanNeeded) { captureScanNeeded = false; _ = ScanDownloadPins(); } }
    }

    private async Task ScanDownloadPins()
    {
        if (closing) return;
        if (scanningDownloads) { scanDownloadsAgain = true; return; }
        scanningDownloads = true;
        try
        {
            bool changed = false;
            foreach (var folder in Settings.Items.Where(i => i.IsDownloadedPin).Select(i => Path.GetDirectoryName(i.Target)).OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            {
                var existing = await Task.Run(() =>
                {
                    try { return Directory.EnumerateFiles(folder).ToHashSet(StringComparer.OrdinalIgnoreCase); }
                    catch (DirectoryNotFoundException) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }
                    catch (IOException) { return null; }
                    catch (UnauthorizedAccessException) { return null; }
                });
                if (closing) return;
                if (existing != null)
                {
                    // A launched installer may move/delete itself during feedback.
                    existing.UnionWith(Settings.Items.Where(i => openingDownloadPins.Contains(i.Id)).Select(i => i.Target));
                    changed |= DownloadPinLayout.RemoveMissing(Settings.Items, folder, existing);
                }
            }
            foreach (var (path, tracker) in downloadTrackers.ToArray())
            {
                var files = await Task.Run(() =>
                {
                    try
                    {
                        return new DirectoryInfo(path).EnumerateFiles().Where(f => (f.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                            .Select(f => new DownloadFile(f.FullName, f.Length, f.LastWriteTimeUtc)).ToArray();
                    }
                    catch (IOException) { return null; }
                    catch (UnauthorizedAccessException) { return null; }
                });
                if (closing || files == null || !downloadTrackers.TryGetValue(path, out var active) || active != tracker) continue;
                var options = Settings.Items.FirstOrDefault(i => i.Kind == ItemKind.Folder && IsDownloads(i) && string.Equals(i.Target, path, StringComparison.OrdinalIgnoreCase))?.Folder;
                if (options == null) continue;
                var ready = tracker.Observe(files, DateTime.UtcNow);
                var icons = await Task.WhenAll(ready.Select(async file => (Path: file, Source: await IconService.Load(file, requestedPixels: IconPixels))));
                if (closing || !downloadTrackers.TryGetValue(path, out active) || active != tracker) continue;
                foreach (var icon in icons)
                {
                    if (icon.Source != null) readyDownloadIcons[icon.Path] = icon.Source;
                }
                foreach (var file in ready)
                {
                    if (!File.Exists(file)) continue;
                    if (options.OpenNewDownloadsAutomatically) TryAction(() => ShellService.Open(file));
                    if (!options.PinNewDownloads || Settings.Items.Any(i => string.Equals(i.Target, file, StringComparison.OrdinalIgnoreCase))) continue;
                    Settings.Items.Add(new DockItem { Kind = ItemKind.File, Target = file, Name = Path.GetFileNameWithoutExtension(file), IsDownloadedPin = true });
                    changed = true;
                }
            }
            changed |= await ScanSnippingPins();
            if (changed)
            {
                var downloadsItem = Settings.Items.FirstOrDefault(i => i.Kind == ItemKind.Folder && IsDownloads(i));
                if (downloadsItem != null) DownloadPinLayout.Trim(Settings.Items, downloadsItem.Folder.DownloadPinLimit);
                Save(); Reconcile();
            }
        }
        catch (Exception ex) { Log.Write(ex); }
        finally
        {
            scanningDownloads = false;
            if (!closing && (scanDownloadsAgain || downloadTrackers.Values.Any(t => t.HasPending) || captureTrackers.Values.Any(t => t.HasPending)))
            { scanDownloadsAgain = false; QueueDownloadScan(); }
        }
    }
}
