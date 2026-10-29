using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private async void CheckDragSpacing()
    {
        var observations = new List<object>(); Exception? failure = null;
        async Task Settle()
        {
            root.UpdateLayout();
            var ready = new TaskCompletionSource<bool>(); int frames = 0;
            EventHandler<object> rendered = (_, _) => { if (++frames >= 4) ready.TrySetResult(true); };
            Microsoft.UI.Xaml.Media.CompositionTarget.Rendering += rendered;
            try { if (await Task.WhenAny(ready.Task, Task.Delay(2000)) != ready.Task) throw new TimeoutException("No rendering frames"); await Task.Delay(16); }
            finally { Microsoft.UI.Xaml.Media.CompositionTarget.Rendering -= rendered; }
        }
        try
        {
            safetyTimer.Stop(); reconcileTimer.Stop(); themeTimer.Stop(); visibilityTimer.Stop();
            Settings.ReserveSpace = false; Settings.ShowTrayIcon = false; Settings.AnimationMs = 0; Settings.ReduceMotion = false;
            Settings.ShowIconHighlights = true; inspection = true; stressTasks = [];
            foreach (bool highlights in new[] { false, true })
            foreach (bool automaticSections in new[] { false, true })
            foreach (var edge in Enum.GetValues<DockEdge>())
            foreach (int count in new[] { 38 })
            foreach (int spacing in new[] { -16, 0, 16 })
            {
                ClearPinnedReorderPreview(); dragging = false; dragRestingStarts.Clear(); ResetMagnification();
                root.IsHitTestVisible = false; Settings.ShowIconHighlights = highlights; DisplayMagnificationEnabled = true;
                PositionEdge = edge; Settings.IconSize = 40; Settings.IconSpacing = spacing;
                Settings.Items = Enumerable.Range(0, count).Select(i => i == 3 || i == count - 2
                    ? new DockItem { Id = "drag-separator-" + i, Kind = ItemKind.Separator }
                    : new DockItem { Id = "drag-item-" + i, Kind = ItemKind.Application, Name = "Drag " + i, Target = Environment.ProcessPath!, CustomIcon = BundledIcons.Application }).ToList();
                if (automaticSections)
                {
                    Settings.Items[count - 4].Kind = ItemKind.Folder; Settings.Items[count - 4].Target = Path.GetPathRoot(AppContext.BaseDirectory)!;
                    Settings.Items[count - 4].IsAutoUsbPin = true;
                    Settings.Items[count - 3].Kind = ItemKind.File; Settings.Items[count - 3].IsDownloadedPin = true;
                    stressTasks = [new(0x7fff0030, "Running fixture", @"C:\drag-running-fixture.exe", "drag.running", default, false, false)];
                }
                else stressTasks = [];
                lastSignature = ""; ApplySettings(persist: false); Reconcile(); ShowDock(); await Settle();
                var children = strip.Children.OfType<FrameworkElement>().ToArray();
                foreach (var child in children) dragRestingStarts[child] = Along(child.TransformToVisual(strip).TransformPoint(new Windows.Foundation.Point()));
                var source = children.First(c => c is Button && ((DockEntry)c.Tag).Item.Id == "drag-item-5");
                var before = children.Select(c => new { Id = (c.Tag as DockEntry)?.Item.Id, Start = DragRestingStart(c), Width = Vertical ? c.ActualHeight : c.ActualWidth }).ToArray();
                hoverTestInput = () => ((hoverWave?.Length ?? 1) / 2, true);
                PrepareHoverSurface(); StopHoverRendering();
                hoverWave!.RestoreGrowth(hoverSlots.Select(s => s.Icon == null ? 0d : HoverOverflow / 2).ToArray(), hoverAvailableExpansion, HoverOverflow);
                PaintHoverFrame(); await Settle();
                BeginItemDrag(source, (DockEntry)source.Tag);
                PreviewPinnedReorder(source, DragRestingStart(source) + (Vertical ? source.ActualHeight : source.ActualWidth) / 2 - .01);
                if (dragPreviewOffsets.Count != children.Length - 1) throw new Exception("Not all drag preview elements were measured: " + dragPreviewOffsets.Count);
                double startError = before.Select((original, index) => Math.Abs(original.Start - DragRestingStart(children[index]))).DefaultIfEmpty().Max();
                double error = dragPreviewOffsets.Values.Select(Math.Abs).DefaultIfEmpty().Max();
                PreviewPinnedReorder(source, double.NaN);
                double invalidError = dragPreviewOffsets.Values.Select(Math.Abs).DefaultIfEmpty().Max();
                var lastChild = children[^1];
                double originalEnd = DragRestingStart(lastChild) + DragElementLength(lastChild);
                var targetChild = children.First(c => c.Tag is DockEntry { Item.Id: "drag-item-1" });
                PreviewPinnedReorder(source, DragRestingStart(targetChild) + DragElementLength(targetChild) / 2 - .01);
                double endError = Math.Abs(DragRestingStart(lastChild) + DragElementLength(lastChild) + dragPreviewOffsets.GetValueOrDefault(lastChild) - originalEnd);
                if (startError > .001 || error > .001 || invalidError > .001 || endError > .001)
                    failure ??= new Exception($"Drag geometry changed for {edge}, highlights={highlights}, automaticSections={automaticSections}, spacing={spacing}: start={startError}, rest={error}, invalid={invalidError}, end={endError}");
                observations.Add(new { StartError = startError, AutomaticSections = automaticSections, InvalidError = invalidError, EndError = endError, Highlights = highlights, Before = before, Edge = edge.ToString(), Count = count, Spacing = spacing, DisplaySpacing, ButtonSize, Error = error,
                    Children = children.Select(c => new { Kind = c.GetType().Name, Start = DragRestingStart(c), Width = Vertical ? c.ActualHeight : c.ActualWidth, Offset = dragPreviewOffsets.GetValueOrDefault(c), Margin = c.Margin.ToString() }).ToArray() });
                FinishItemDrag(); hoverTestInput = null;
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { FinishItemDrag(); hoverTestInput = null; }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "drag-spacing-test.json"), JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Observations = observations }, new JsonSerializerOptions { WriteIndented = true }));
        Close();
    }
}
