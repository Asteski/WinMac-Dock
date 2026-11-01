using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private bool dragRemoveArmed;
    private readonly DispatcherTimer dragOutsideTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private bool dragOutsideTimerHooked;
    private LayeredDragIcon? dragPreview;
    private int dragPreviewGeneration;

    private void BeginOutsideItemDrag()
    {
        dragRemoveArmed = false;
        _ = PrepareDragIcon(++dragPreviewGeneration);
        if (!dragOutsideTimerHooked)
        {
            dragOutsideTimer.Tick += (_, _) =>
            {
                if (closing || internalDragId == null) { dragOutsideTimer.Stop(); return; }
                if (Native.GetAsyncKeyState(0x1B) < 0) { CancelOutsideItemDrag(); return; }
                if (Native.GetAsyncKeyState(0x01) >= 0)
                {
                    if (Settings.Items.FirstOrDefault(i => i.Id == internalDragId) is { } item) CommitOutsideItemDrag(item);
                    else FinishItemDrag();
                    return;
                }
                UpdateOutsideItemDrag();
            };
            dragOutsideTimerHooked = true;
        }
        dragOutsideTimer.Start();
    }
    private bool DragRemovalAllowed(Native.POINT cursor) => DockDragRemoval.CanRemove(
        DockDragRemoval.IsOutside(MaterialPanel, cursor.X, cursor.Y, DockGeometry.Pixels(16, scale)),
        Settings.LockIconPositions, Native.GetAsyncKeyState(0x12) < 0,
        Settings.RequireShiftToDragRemove, Native.GetAsyncKeyState(0x10) < 0);

    private void UpdateOutsideItemDrag()
    {
        if (closing || internalDragId == null || draggedElement == null || draggedVisual == null) return;
        Native.GetCursorPos(out var cursor);
        var pointer = new Point((cursor.X - hostBounds.Left) / scale, (cursor.Y - hostBounds.Top) / scale);
        bool inside = MaterialPanel.Contains(cursor.X, cursor.Y);
        var along = Along(root.TransformToVisual(strip).TransformPoint(pointer));
        dragRemoveArmed = DragRemovalAllowed(cursor);
        dragDropAllowed = inside && CanMoveIcons && IsPinnedDropTarget(along);
        SetDragCursor(true);
        if (dragDropAllowed)
        {
            dragTargetSlot = DragInsertionSlot(along);
            PreviewPinnedReorder(draggedElement, along);
        }
        else PreviewPinnedReorder(draggedElement, double.NaN);
        UpdateItemDragPosition(pointer);
        bool canLeaveDock = DockDragRemoval.CanDragOutside(Settings.LockIconPositions, Native.GetAsyncKeyState(0x12) < 0,
            Settings.RequireShiftToDragRemove, Native.GetAsyncKeyState(0x10) < 0);
        if (inside || !canLeaveDock)
        {
            draggedVisual.Opacity = .68;
            dragPreview?.Hide();
        }
        else UpdateDragRemovalPreview(cursor);
    }
    private void CommitOutsideItemDrag(DockItem item)
    {
        if (internalDragId != item.Id) return;
        if (Native.GetAsyncKeyState(0x1B) < 0) { CancelOutsideItemDrag(); return; }
        UpdateOutsideItemDrag(); // Recheck current pointer and modifier states at release.
        bool remove = dragRemoveArmed && dragPreview != null;
        bool reorder = dragDropAllowed;
        int slot = dragTargetSlot;
        FinishItemDrag(remove);
        if (remove)
        {
            // Only neighboring artwork and the panel should close the gap.
            // Do not briefly bring the dragged icon back into the dock to fade it.
            if (Motion && Settings.AnimationMs > 0 && !hidden && !dockOverflow)
            {
                var before = CaptureRowScene();
                pendingLayoutScene = before with { Sprites = before.Sprites.Where(s => !s.Key.StartsWith(item.Id + ":", StringComparison.Ordinal)).ToArray() };
            }
            UnpinItem(item);
        }
        else if (reorder && DockOrdering.Move(Settings, item.Id, slot, Native.GetAsyncKeyState(0x12) < 0))
        { Save(); lastSignature = ""; }
    }
    private void CancelOutsideItemDrag()
    {
        if (internalDragId == null) return;
        var source = draggedElement;
        FinishItemDrag();
        source?.ReleasePointerCaptures();
    }
    private async Task PrepareDragIcon(int generation)
    {
        FrameworkElement? snapshot = null;
        try
        {
            var source = draggedVisual;
            if (source == null) return;
            // Reparenting invalidates ActualWidth/ActualHeight until layout runs.
            root.UpdateLayout();
            double width = source.Width > 0 && double.IsFinite(source.Width) ? source.Width : source.ActualWidth;
            double height = source.Height > 0 && double.IsFinite(source.Height) ? source.Height : source.ActualHeight;
            if (width <= 0 || height <= 0) throw new InvalidOperationException("Dragged artwork has no size.");
            int pixelWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
            int pixelHeight = Math.Max(1, (int)Math.Ceiling(height * scale));
            foreach (var image in DragImages(source).ToArray())
                if (image.Source != null) await IconService.PrepareDragPixels(image.Source, pixelWidth, pixelHeight);
            if (closing || generation != dragPreviewGeneration || internalDragId == null) return;
            // Compose every visible image, including Downloads stacks and
            // multi-layer app artwork, from its existing alpha pixel buffer.
            if (CaptureDragImages(source, pixelWidth, pixelHeight) is { } iconPixels)
            {
                dragPreview = new LayeredDragIcon(iconPixels, pixelWidth, pixelHeight);
                UpdateOutsideItemDrag();
                return;
            }
            // Restore fallback artwork for native shell icons, which cannot be
            // captured from a child HWND by RenderTargetBitmap.
            snapshot = CloneRowVisual(source, restoreShellArtwork: true);
            snapshot.Width = width; snapshot.Height = height;
            snapshot.Margin = new Thickness(0); snapshot.IsHitTestVisible = false;
            Canvas.SetLeft(snapshot, dragVisualOrigin.X); Canvas.SetTop(snapshot, dragVisualOrigin.Y);
            dragOverlay.Children.Add(snapshot);
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap();
            await bitmap.RenderAsync(snapshot, pixelWidth, pixelHeight);
            var pixels = (await bitmap.GetPixelsAsync()).ToArray();
            if (closing || generation != dragPreviewGeneration || internalDragId == null) return;
            dragPreview = new LayeredDragIcon(pixels, bitmap.PixelWidth, bitmap.PixelHeight);
            UpdateOutsideItemDrag();
        }
        catch (Exception ex) { Log.Write(ex); }
        finally { if (snapshot != null) dragOverlay.Children.Remove(snapshot); }
    }
    private static IEnumerable<Image> DragImages(FrameworkElement element)
    {
            if (element.Visibility != Visibility.Visible) yield break;
            if (element is Image image) yield return image;
            if (element is Panel panel)
                foreach (var child in panel.Children.OfType<FrameworkElement>())
                    foreach (var nested in DragImages(child)) yield return nested;
            if (element is Border { Child: FrameworkElement childElement })
                foreach (var nested in DragImages(childElement)) yield return nested;
    }
    private byte[]? CaptureDragImages(FrameworkElement source, int width, int height)
    {
        using var output = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        bool painted = false;
        using (var graphics = System.Drawing.Graphics.FromImage(output))
        {
            graphics.Clear(System.Drawing.Color.Transparent);
            foreach (var image in DragImages(source))
            {
                if (image.Source == null) continue;
                var bounds = image.TransformToVisual(source).TransformBounds(new Windows.Foundation.Rect(0, 0, image.ActualWidth, image.ActualHeight));
                int w = Math.Max(1, (int)Math.Round(bounds.Width * scale));
                int h = Math.Max(1, (int)Math.Round(bounds.Height * scale));
                if (IconService.DragPixels(image.Source, w, h) is not { } pixels) return null;
                using var layer = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                var data = layer.LockBits(new System.Drawing.Rectangle(0, 0, w, h), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
                try { for (int row = 0; row < h; row++) System.Runtime.InteropServices.Marshal.Copy(pixels, row * w * 4, data.Scan0 + row * data.Stride, w * 4); }
                finally { layer.UnlockBits(data); }
                graphics.DrawImageUnscaled(layer, (int)Math.Round(bounds.X * scale), (int)Math.Round(bounds.Y * scale));
                painted = true;
            }
        }
        if (!painted) return null;
        var result = new byte[width * height * 4];
        var outputData = output.LockBits(new System.Drawing.Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try { for (int row = 0; row < height; row++) System.Runtime.InteropServices.Marshal.Copy(outputData.Scan0 + row * outputData.Stride, result, row * width * 4, width * 4); }
        finally { output.UnlockBits(outputData); }
        return result;
    }
    private void UpdateDragRemovalPreview(Native.POINT cursor)
    {
        if (dragPreview == null || draggedVisual == null) return;
        try
        {
            int x = cursor.X - (int)Math.Round((dragStartRoot.X - dragVisualOrigin.X) * scale);
            int y = cursor.Y - (int)Math.Round((dragStartRoot.Y - dragVisualOrigin.Y) * scale);
            dragPreview.Show(x, y);
            draggedVisual.Opacity = 0;
        }
        catch (Exception ex) { Log.Write(ex); }
    }
    private void EndOutsideItemDrag(bool removed)
    {
        dragOutsideTimer.Stop(); dragRemoveArmed = false;
        ++dragPreviewGeneration;
        var preview = dragPreview; dragPreview = null;
        if (preview == null) return;
        if (removed && Motion && !closing) _ = FadeRemovedDragPreview(preview);
        else preview.Dispose();
    }
    private static async Task FadeRemovedDragPreview(LayeredDragIcon preview)
    {
        try
        {
            var start = System.Diagnostics.Stopwatch.StartNew();
            while (start.ElapsedMilliseconds < 140)
            {
                preview.SetOpacity((byte)Math.Clamp(255 * (1 - start.Elapsed.TotalMilliseconds / 140), 0, 255));
                await Task.Delay(16);
            }
        }
        catch (Exception ex) { Log.Write(ex); }
        finally { preview.Dispose(); }
    }
}
