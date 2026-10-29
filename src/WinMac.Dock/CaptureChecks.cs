using System.Text.Json;
using Windows.Management.Core;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private async Task CheckCapturePins()
    {
        var checks = new List<string>(); Exception? failure = null;
        string fixture = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "capture-fixtures", "recording.mp4"));
        string folder = Path.Combine(AppContext.BaseDirectory, "capture-check-" + Guid.NewGuid().ToString("N"));
        var data = ApplicationDataManager.CreateForPackageFamily(SnippingToolSettings.PackageFamily);
        var values = data.LocalSettings.Values;
        bool hadPictures = values.TryGetValue(SnippingToolSettings.ScreenshotsKey, out var oldPictures), hadVideos = values.TryGetValue(SnippingToolSettings.RecordingsKey, out var oldVideos);
        var originalItems = Settings.Items.ToList();
        var originalOptions = originalItems.Where(ShellService.IsSnippingTool).ToDictionary(i => i.Id, i => i.Snipping with { });
        try
        {
            foreach (var snip in originalItems.Where(ShellService.IsSnippingTool)) snip.Snipping.PinNewCaptures = false;
            var item = new DockItem { Kind = ItemKind.Application, Target = "ms-screensketch:", Name = "Capture test", Snipping = new() { PinNewCaptures = true } };
            Settings.Items.Add(item);
            string pictures = Path.Combine(folder, "Pictures"), videos = Path.Combine(folder, "Videos");
            SnippingToolSettings.Apply(pictures, videos);
            var editor = new ItemPropertiesWindow(this, item);
            try { editor.CheckCaptureControls(); checks.Add("Snipping Tool Properties loads native folders and tracks unsaved capture edits"); }
            finally { editor.Close(); }
            var paths = SnippingToolSettings.Read();
            if (paths.Screenshots != pictures || paths.Recordings != videos) throw new Exception("Snipping Tool save paths did not round-trip through ApplicationData.");
            checks.Add("Both save folders update Snipping Tool's own settings");
            InvalidateCapturePaths(); SyncSnippingPinning(); await ScanSnippingPins();
            using (var bitmap = new System.Drawing.Bitmap(160, 90))
            {
                using var g = System.Drawing.Graphics.FromImage(bitmap); g.Clear(System.Drawing.Color.CornflowerBlue);
                bitmap.Save(Path.Combine(pictures, "screenshot.png"), System.Drawing.Imaging.ImageFormat.Png);
            }
            File.Copy(fixture, Path.Combine(videos, "recording.mp4"));
            for (int n = 0; n < 30 && Settings.Items.Count(i => i.Target.StartsWith(folder) && i.IsDownloadedPin) < 2; n++)
            { await ScanSnippingPins(); await Task.Delay(100); }
            var captures = Settings.Items.Where(i => i.Target.StartsWith(folder) && i.IsDownloadedPin).ToArray();
            if (captures.Length != 2 || captures.Any(i => !i.ShowThumbnail) || captures.Select(i => i.CapturePin).Distinct().Count() != 2)
                throw new Exception("Screenshot and recording did not become separate thumbnail pins.");
            if (captures.Any(i => !readyDownloadIcons.ContainsKey(i.Target))) throw new Exception("A capture thumbnail was not loaded.");
            checks.Add("New screenshot and recording load thumbnails and pin independently");
            Reconcile(); await Task.Delay(700);
            foreach (var capture in captures)
            {
                var button = buttons.First(b => b.Entry.Item.Id == capture.Id).Button;
                var brush = (Microsoft.UI.Xaml.Media.SolidColorBrush)downloadIndicators[button].Background;
                string expected = capture.CapturePin == CapturePinKind.Screenshot ? "#FF23A5E4" : "#FFAD6EF3";
                if (!brush.Color.ToString().Equals(expected, StringComparison.OrdinalIgnoreCase)) throw new Exception("Wrong capture indicator colour.");
            }
            checks.Add("Screenshot and recording indicators use #23A5E4 and #AD6EF3");
            foreach (var capture in captures) UnpinOpenedDownload(capture);
            for (int n = 0; n < 40 && captures.Any(c => Settings.Items.Contains(c)); n++) await Task.Delay(100);
            if (captures.Any(c => Settings.Items.Contains(c))) throw new Exception("Opening a capture did not unpin it.");
            checks.Add("Opening capture pins unpins them without a Downloads module");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally
        {
            if (hadPictures) values[SnippingToolSettings.ScreenshotsKey] = oldPictures; else values.Remove(SnippingToolSettings.ScreenshotsKey);
            if (hadVideos) values[SnippingToolSettings.RecordingsKey] = oldVideos; else values.Remove(SnippingToolSettings.RecordingsKey);
            data.SignalDataChanged();
            Settings.Items = originalItems;
            foreach (var item in originalItems.Where(i => originalOptions.ContainsKey(i.Id))) item.Snipping = originalOptions[item.Id];
            captureTrackers.Clear(); captureFolders.Clear(); InvalidateCapturePaths();
            Save(); Reconcile();
        }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "capture-pins-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
