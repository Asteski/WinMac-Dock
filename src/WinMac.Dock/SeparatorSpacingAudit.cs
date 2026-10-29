using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async Task RunSeparatorAudit()
    {
        if (!Program.SeparatorAudit) return;
        int cases = 0, observations = 0, failures = 0, separatorHits = 0;
        double maximumError = 0, maximumTileInset = 0, maximumEdgeError = 0;
        var errors = new List<object>();
        bool quick = Environment.GetEnvironmentVariable("WINMAC_SEPARATOR_QUICK") == "1";
        string scenario = "";
        var items = Settings.Items;
        var report = Path.Combine(Log.DirectoryPath, "separator-spacing-matrix.json");
        void Write(bool complete) => File.WriteAllText(report, JsonSerializer.Serialize(new { Complete = complete,
            Passed = failures == 0, Cases = cases, Observations = observations, Failures = failures,
            SeparatorHitChecks = separatorHits, MaximumErrorDip = maximumError, MaximumTileInsetDip = maximumTileInset, MaximumEdgeErrorDip = maximumEdgeError, Scenario = scenario, Errors = errors }, new JsonSerializerOptions { WriteIndented = true }));
        async Task SettleFrame()
        {
            root.UpdateLayout();
            var ready = new TaskCompletionSource<bool>();
            int frames = 0;
            EventHandler<object>? rendered = null;
            rendered = (_, _) => { if (++frames >= 4) ready.TrySetResult(true); };
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += rendered;
            try
            {
                if (await Task.WhenAny(ready.Task, Task.Delay(2000)) != ready.Task)
                    throw new TimeoutException("No settled rendering frame for separator audit");
                await Task.Delay(16);
            }
            finally { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= rendered; }
        }
        void Measure(string stage)
        {
            root.UpdateLayout(); SnapRestingSeparators();
            var children = strip.Children.OfType<FrameworkElement>().ToArray();
            foreach (var divider in children.OfType<Border>())
            {
                var stroke = SeparatorScreenStroke(divider);
                bool removable = divider.Tag is DockEntry { Item.Kind: ItemKind.Separator };
                foreach (int offset in removable ? new[] { -4, 0, 4 } : new[] { 0 })
                {
                    var point = new Native.POINT { X = (int)Math.Round(stroke.X + stroke.Width / 2 + (Vertical ? 0 : offset)),
                        Y = (int)Math.Round(stroke.Y + stroke.Height / 2 + (Vertical ? offset : 0)) };
                    var hit = RemovableSeparatorAt(point);
                    separatorHits++;
                    if (hit != (removable ? divider : null))
                    {
                        failures++;
                        if (errors.Count < 30) errors.Add(new { scenario, stage, Error = "Wrong separator Alt-context target", offset, removable });
                    }
                }
            }
            if (stage == "rest")
            {
                foreach (var (button, tile) in taskbarHighlights)
                {
                    var bp = button.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    var tp = tile.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    double inset = Math.Max(0, -DisplaySpacing) / 2;
                    double error = Math.Max(Math.Abs(tp.X - bp.X - (Vertical ? MarginDip(Settings.DockMarginLeft) : inset)), Math.Abs(tp.Y - bp.Y - (Vertical ? inset : MarginDip(Settings.DockMarginTop))));
                    error = Math.Max(error, Math.Max(Math.Abs(tile.ActualWidth - button.ActualWidth + (Vertical ? MarginDip(Settings.DockMarginLeft + Settings.DockMarginRight) : inset * 2)),
                        Math.Abs(tile.ActualHeight - button.ActualHeight + (Vertical ? inset * 2 : MarginDip(Settings.DockMarginTop + Settings.DockMarginBottom)))));
                    maximumTileInset = Math.Max(maximumTileInset, error);
                    if (error > .001) { failures++; if (errors.Count < 30) errors.Add(new { scenario, stage, Error = "Highlight inset differs from its item", error }); }
                }
                if (!dockOverflow)
                {
                    var first = children.First(); var last = children.Last();
                    var fp = first.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    var lp = last.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    double leading = Vertical ? fp.Y - (resting.Top - hostBounds.Top) / scale : fp.X - (resting.Left - hostBounds.Left) / scale;
                    double trailing = Vertical ? (resting.Bottom - hostBounds.Top) / scale - lp.Y - last.ActualHeight : (resting.Right - hostBounds.Left) / scale - lp.X - last.ActualWidth;
                    double error = Math.Max(Math.Abs(leading - MarginDip(Vertical ? Settings.DockMarginTop : Settings.DockMarginLeft)),
                        Math.Abs(trailing - MarginDip(Vertical ? Settings.DockMarginBottom : Settings.DockMarginRight)));
                    var firstTile = taskbarHighlights[(Button)first];
                    var cp = firstTile.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    double crossStart = Vertical ? cp.X - (resting.Left - hostBounds.Left) / scale : cp.Y - (resting.Top - hostBounds.Top) / scale;
                    double crossEnd = Vertical ? (resting.Right - hostBounds.Left) / scale - cp.X - firstTile.ActualWidth : (resting.Bottom - hostBounds.Top) / scale - cp.Y - firstTile.ActualHeight;
                    error = Math.Max(error, Math.Max(Math.Abs(crossStart - MarginDip(Vertical ? Settings.DockMarginLeft : Settings.DockMarginTop)),
                        Math.Abs(crossEnd - MarginDip(Vertical ? Settings.DockMarginRight : Settings.DockMarginBottom))));
                    maximumEdgeError = Math.Max(maximumEdgeError, error);
                    if (error > .001) { failures++; if (errors.Count < 30) errors.Add(new { scenario, stage, Error = "Dock margins differ from configured pixels", error, leading, trailing, crossStart, crossEnd }); }
                }
            }
            Windows.Foundation.Rect Artwork(Button button)
            {
                var slot = hoverSlots.FirstOrDefault(slot => slot.Element == button);
                if (hoverWave != null && slot != null) return slot.HitBounds;
                var image = buttons.First(entry => entry.Button == button).Icon;
                var origin = image.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                return new(origin.X, origin.Y, image.ActualWidth, image.ActualHeight);
            }
            for (int i = 1; i < children.Length - 1; i++)
            {
                if (children[i] is not Border separator || children[i - 1] is not Button before || children[i + 1] is not Button after
                    || !separatorStrokes.TryGetValue(separator, out var stroke)) continue;
                if (stage == "rest")
                {
                    var snapshot = CaptureRowScene().Sprites.Single(sprite => sprite.Key == "divider:" + children.Take(i).OfType<Border>().Count());
                    if (Math.Abs(snapshot.Bounds.Width - stroke.Size.X * scale) > .00001
                        || Math.Abs(snapshot.Bounds.Height - stroke.Size.Y * scale) > .00001)
                    {
                        failures++;
                        if (errors.Count < 30) errors.Add(new { scenario, stage, Error = "Animated separator differs from its resting stroke" });
                    }
                }
                var origin = separator.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                var a = Artwork(before); var b = Artwork(after);
                double start = Vertical ? origin.Y + stroke.Offset.Y : origin.X + stroke.Offset.X;
                double thin = Vertical ? stroke.Size.Y : stroke.Size.X;
                double left = start - (Vertical ? a.Bottom : a.Right);
                double right = (Vertical ? b.Top : b.Left) - start - thin;
                double expected = (ButtonSize - DisplayIconSize) / 2 + Math.Max(0, DisplaySpacing);
                if (stage == "rest" && taskbarHighlights.TryGetValue(before, out var beforeTile) && taskbarHighlights.TryGetValue(after, out var afterTile))
                {
                    var p = beforeTile.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    var q = afterTile.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                    double expectedGap = Math.Max(0, DisplaySpacing) + Math.Max(0, -DisplaySpacing) / 2;
                    double leadingGap = start - (Vertical ? p.Y + beforeTile.ActualHeight : p.X + beforeTile.ActualWidth);
                    double trailingGap = (Vertical ? q.Y : q.X) - start - thin;
                    double gapError = Math.Max(Math.Abs(leadingGap - expectedGap), Math.Abs(trailingGap - expectedGap));
                    if (gapError > .001) { failures++; if (errors.Count < 30) errors.Add(new { scenario, stage, Error = "Highlight-to-separator gap differs", leadingGap, trailingGap, expectedGap }); }
                }
                double error = Math.Max(Math.Abs(left - expected), Math.Abs(right - expected));
                maximumError = Math.Max(maximumError, error); observations++;
                if (error > 1.05 / scale + .01)
                {
                    failures++;
                    if (errors.Count < 30) errors.Add(new { scenario, stage, Before = ((DockEntry)before.Tag).Item.Name,
                        After = ((DockEntry)after.Tag).Item.Name, left, right, expected, error, DisplayIconSize, DisplaySpacing,
                        SeparatorOrigin = origin, StrokeOffset = stroke.Offset, BeforeBounds = a, AfterBounds = b,
                        SeparatorShift = hoverSlots.FirstOrDefault(slot => slot.Element == separator)?.Shift.X,
                        SeparatorBase = hoverSlots.FirstOrDefault(slot => slot.Element == separator)?.ElementOriginX });
                }
            }
        }
        try
        {
            safetyTimer.Stop(); reconcileTimer.Stop(); themeTimer.Stop(); visibilityTimer.Stop();
            Settings.ReserveSpace = false; Settings.StartAtLogin = false; Settings.ShowTrayIcon = false; Settings.ShowIconHighlights = true;
            Settings.AnimationMs = 0; Settings.ReduceMotion = false; Settings.HoverMagnificationPercent = 50;
            Settings.GroupWindows = true; Settings.CombinePinnedIconsWithRunningApplications = true;
            inspection = true; root.IsHitTestVisible = false; DisplayMagnificationEnabled = true;
            string sample = IconResources.PathFor(BundledIcons.Application);
            var app = new DockItem { Id = "audit-app", Kind = ItemKind.Application, Name = "Application", Target = Environment.ProcessPath!, AppUserModelId = "audit.app", PinUsbDrives = false };
            var types = new DockItem[] {
                new() { Id = "audit-start", Kind = ItemKind.StartMenu, Name = "Start Menu", PinUsbDrives = false }, app,
                new() { Id = "audit-file", Kind = ItemKind.File, Name = "File", Target = sample, PinUsbDrives = false },
                new() { Id = "audit-folder", Kind = ItemKind.Folder, Name = "Folder stack", Target = Path.Combine(AppContext.BaseDirectory, "assets"), Folder = new() { StackIcon = true }, PinUsbDrives = false },
                new() { Id = "audit-recycle", Kind = ItemKind.RecycleBin, Name = "Recycle Bin", PinUsbDrives = false },
                new() { Id = "audit-explorer", Kind = ItemKind.Application, Name = "File Explorer", Target = "explorer.exe", PinUsbDrives = false },
                new() { Id = "audit-drive", Kind = ItemKind.Folder, Name = "Attached drive", Target = Path.GetPathRoot(AppContext.BaseDirectory)!, IsAutoUsbPin = true, AttachedDriveKind = AttachedDriveKind.UsbFlashDrive, PinUsbDrives = false },
                new() { Id = "audit-download", Kind = ItemKind.File, Name = "Downloaded file", Target = sample, IsDownloadedPin = true, PinUsbDrives = false }
            };
            Settings.Items = new();
            for (int i = 0; i < types.Length; i++)
            {
                if (i > 0 && !types[i].IsAutoUsbPin && !types[i].IsDownloadedPin) Settings.Items.Add(new() { Id = "audit-divider-" + i, Kind = ItemKind.Separator });
                Settings.Items.Add(types[i]);
            }
            stressTasks = [new(0x7fff0010, "Grouped one", Environment.ProcessPath!, "audit.app", default, false, false),
                new(0x7fff0020, "Grouped two", Environment.ProcessPath!, "audit.app", default, false, false),
                new(0x7fff0030, "Running application", Environment.ProcessPath!, "audit.running", default, false, false)];
            var baseItems = Settings.Items.ToList();
            foreach (bool crowded in new[] { false, true })
            foreach (var edge in Enum.GetValues<DockEdge>())
            foreach (var alignment in Enum.GetValues<DockAlignment>())
            foreach (int size in (quick ? new[] { 40 } : crowded ? new[] { 16, 40, 64 } : new[] { 16, 20, 24, 28, 32, 40, 48, 56, 64 }))
            foreach (int spacing in (quick ? new[] { -16, 0, 16 } : crowded ? new[] { -16, 0, 16 } : new[] { -16, -8, -1, 0, 1, 4, 8, 16 }))
            foreach (bool highlights in new[] { true })
            {
                scenario = $"{edge}/{alignment}/size={size}/gap={spacing}/crowded={crowded}";
                Settings.Items = baseItems.ToList();
                if (crowded) Settings.Items.InsertRange(0, Enumerable.Range(0, 32).Select(i => app with { Id = "audit-extra-" + i, AppUserModelId = "audit.extra." + i }));
                ResetMagnification(); PositionEdge = edge; PositionAlignment = alignment;
                Settings.IconSize = size; Settings.IconSpacing = spacing; Settings.WindowsTaskbarHighlights = highlights;
                ApplySettings(persist: false); Reconcile(); ShowDock(); root.UpdateLayout();
                await Task.Delay(16);
                Measure("rest");
                if (!dockOverflow)
                {
                    hoverTestInput = () => ((hoverWave?.Length ?? 1) / 2, true);
                    PrepareHoverSurface(); StopHoverRendering();
                    hoverWave!.RestoreGrowth(hoverSlots.Select((slot, index) => slot.Icon == null ? 0d : HoverOverflow * ((index % 3 + 1) / 3d)).ToArray(), hoverAvailableExpansion, HoverOverflow);
                    PaintHoverFrame(); await SettleFrame(); Measure("magnified");
                    hoverWave.RestoreGrowth(new double[hoverSlots.Length], hoverAvailableExpansion, HoverOverflow);
                    PaintHoverFrame(); await SettleFrame(); Measure("contracted");
                    hoverTestInput = null; ResetMagnification(); await SettleFrame(); Measure("reset");
                }
                cases++;
                if (cases % 48 == 0) Write(false);
            }
        }
        catch (Exception ex) { failures++; errors.Add(new { scenario, Error = ex.ToString() }); }
        finally
        {
            hoverTestInput = null; ResetMagnification(); stressTasks = null; Settings.Items = items;
            Write(true); Close();
        }
    }
}
