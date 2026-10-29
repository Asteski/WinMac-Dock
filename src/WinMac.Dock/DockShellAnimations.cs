using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private sealed record ShellIcon(Button Button, Grid Artwork, DockEntry Entry, bool Dark, ShellAnimatedIcon Animation);
    private readonly List<ShellIcon> shellIcons = [];
    private readonly Dictionary<(ItemKind Kind, bool Dark), ShellAnimatedIcon> reusableShellIcons = new();
    private bool syncingShellIcons;
    private bool shellAnimationFailed;

    private async Task CheckStartShellAnimation()
    {
        var checks = new List<string>(); Exception? failure = null;
        nint previous = Native.GetForegroundWindow();
        try
        {
            if (TaskbarModuleTrigger.IsActive(ItemKind.StartMenu) == true) { Native.OpenStartMenu(); await Task.Delay(300); }
            Native.OpenStartMenu();
            for (int i = 0; i < 30 && TaskbarModuleTrigger.IsActive(ItemKind.StartMenu) != true; i++) { TaskbarModuleTrigger.RefreshStates(); await Task.Delay(50); }
            if (TaskbarModuleTrigger.IsActive(ItemKind.StartMenu) != true) throw new Exception("Windows did not report Start as visible.");
            if (TaskbarModuleTrigger.IsActive(ItemKind.Search) == true) throw new Exception("Opening Start also activated Search icon state.");
            checks.Add("External Start activation sets only Start active, without animating Search");
            var start = shellIcons.First(i => i.Entry.Item.Kind == ItemKind.StartMenu);
            PulseActivatedItem(start.Artwork);
            if (!start.Animation.IsClickScalePlaying) throw new Exception("Native Start artwork did not start its click-scale animation.");
            await Task.Delay(Settings.RunningClickAnimationMs + 150);
            if (start.Animation.IsClickScalePlaying) throw new Exception("Start click-scale animation did not finish.");
            checks.Add("Native Start artwork runs and completes the same 80%-102%-100% click scale as regular items");
            Native.OpenStartMenu(); await Task.Delay(300);
            TaskbarModuleTrigger.Open(ItemKind.Search);
            for (int i = 0; i < 30 && TaskbarModuleTrigger.IsActive(ItemKind.Search) != true; i++) { TaskbarModuleTrigger.RefreshStates(); await Task.Delay(50); }
            if (TaskbarModuleTrigger.IsActive(ItemKind.Search) != true) throw new Exception("Explicit Search activation no longer animates Search.");
            checks.Add("Explicit Search activation still opens Search");
            using var search = new ShellAnimatedIcon(Handle, ItemKind.Search, start.Dark);
            search.SetState(true, true, true, Motion);
            if (search.RequestedState != "NormalOff") throw new Exception("Search reacted without a dock click.");
            search.NotifyClick(); search.SetState(false, false, false, Motion);
            if (search.RequestedState != "NormalOn") throw new Exception("Search dock click did not animate.");
            await Task.Delay(750);
            search.SetState(true, true, true, Motion);
            if (search.RequestedState != "NormalOff") throw new Exception("Search animation did not reset independently of shell state.");
            checks.Add("Search ignores shell/hover/press state and animates only after its dock click, then resets");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { if (Native.IsWindow(previous)) Native.SetForegroundWindow(previous); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "start-shell-test.json"), System.Text.Json.JsonSerializer.Serialize(
            new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    private void TryAddShellAnimatedIcon(Button button, Grid artwork, DockEntry entry)
    {
        if (shellAnimationFailed || accessibility.HighContrast || entry.Item.Kind is not (ItemKind.StartMenu or ItemKind.Search or ItemKind.TaskView)) return;
        string custom = BundledIcons.Portable(IconService.CustomPathFor(entry.Item));
        string expected = entry.Item.Kind switch { ItemKind.StartMenu => BundledIcons.Start, ItemKind.Search => BundledIcons.Search,
            _ => root.RequestedTheme == ElementTheme.Dark ? BundledIcons.TaskViewDark : BundledIcons.TaskViewLight };
        if (!string.IsNullOrEmpty(custom) && !custom.Equals(expected, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            bool dark = root.RequestedTheme == ElementTheme.Dark;
            var animation = reusableShellIcons.Remove((entry.Item.Kind, dark), out var retained) ? retained : new ShellAnimatedIcon(Handle, entry.Item.Kind, dark);
            shellIcons.Add(new(button, artwork, entry, dark, animation));
            // The transparent foreground HWND otherwise loses its native
            // input coverage when its bitmap is suppressed. Retain an alpha-1
            // XAML plate so the original button keeps its full hit rectangle.
            artwork.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(1, 0, 0, 0));
            // Keep the bitmap loaded for row snapshots and a recoverable
            // fallback. Only its paint is suppressed; layout stays identical.
            foreach (var child in artwork.Children) child.Opacity = 0;
            button.Click += (_, _) => animation.NotifyClick();
            button.PointerEntered += (_, _) => SyncShellAnimatedIcons();
            button.PointerExited += (_, _) => SyncShellAnimatedIcons();
            button.PointerCaptureLost += (_, _) => SyncShellAnimatedIcons();
            button.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => SyncShellAnimatedIcons()), true);
            button.AddHandler(UIElement.PointerReleasedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, _) => SyncShellAnimatedIcons()), true);
            if (shellIcons.Count == 1) CompositionTarget.Rendering += ShellIconsRendering;
        }
        catch (Exception ex) { shellAnimationFailed = true; Log.Write(ex); }
    }

    private void ShellIconsRendering(object? sender, object args) => SyncShellAnimatedIcons();

    private void SyncShellAnimatedIcons()
    {
        if (syncingShellIcons || shellIcons.Count == 0 || closing || reconciling || positioning) return;
        syncingShellIcons = true;
        try
        {
            TaskbarModuleTrigger.RefreshStates();
            Native.GetCursorPos(out var cursor);
            Native.GetWindowRect(Handle, out var window);
            double x = cursor.X - window.Left, y = cursor.Y - window.Top;
            bool mousePressed = Native.GetAsyncKeyState(1) < 0;
            var poses = new List<(ShellIcon Item, PixelRect Rect, bool Visible)>();
            foreach (var item in shellIcons)
            {
                // Hover reparenting temporarily detaches the XAML artwork.
                // Keep the last native pose instead of revealing its bitmap.
                if (item.Artwork.XamlRoot == null) continue;
                var slot = hoverSlots.FirstOrDefault(s => ReferenceEquals(s.Icon, item.Artwork));
                Windows.Foundation.Rect rect;
                if (slot != null && hoverWave != null) rect = slot.HitBounds;
                else rect = item.Artwork.TransformToVisual(root).TransformBounds(new(0, 0, item.Artwork.ActualWidth, item.Artwork.ActualHeight));
                if (rowAnimating && rowMoves.FirstOrDefault(move => move.Sprite.Key == item.Entry.Item.Id + ":icon") is { } row)
                {
                    var moving = OffsetRowBounce(row.Current, row.Sprite.Key);
                    rect = new((moving.X - window.Left) / scale, (moving.Y - window.Top) / scale, moving.Width / scale, moving.Height / scale);
                }
                var pixels = new PixelRect((int)Math.Round(rect.Left * scale), (int)Math.Round(rect.Top * scale),
                    (int)Math.Round(rect.Right * scale), (int)Math.Round(rect.Bottom * scale));
                bool show = StartupComplete && !rowStaging && !dragging && !hidden && !manuallyHidden && Native.IsWindowVisible(Handle)
                    && pixels.Width > 0 && pixels.Height > 0;
                item.Animation.Place(pixels, show);
                poses.Add((item, pixels, show));
                // Row transitions use the existing bitmap snapshots; show the
                // original artwork during those transitions and drag previews.
                foreach (var child in item.Artwork.Children) if (child.Opacity != (show ? 0 : 1)) child.Opacity = show ? 0 : 1;
            }
            // XAML's stationary button bounds and pointer exit events are not
            // authoritative while artwork moves through the hover carrier.
            // Resolve one visible icon under the physical pointer instead.
            var hovered = poses.Where(p => p.Visible && x >= p.Rect.Left - 4 * scale && x < p.Rect.Right + 4 * scale
                    && y >= p.Rect.Top - 4 * scale && y < p.Rect.Bottom + 4 * scale)
                .OrderBy(p => Math.Pow(x - (p.Rect.Left + p.Rect.Right) / 2d, 2) + Math.Pow(y - (p.Rect.Top + p.Rect.Bottom) / 2d, 2))
                .Select(p => p.Item).FirstOrDefault();
            foreach (var pose in poses)
                pose.Item.Animation.SetState(ReferenceEquals(hovered, pose.Item), mousePressed && ReferenceEquals(hovered, pose.Item),
                    TaskbarModuleTrigger.IsActive(pose.Item.Entry.Item.Kind) == true, Motion);
        }
        catch (Exception ex)
        {
            Log.Write(ex); shellAnimationFailed = true;
            ClearShellAnimatedIcons();
        }
        finally { syncingShellIcons = false; }
    }

    private void ClearShellAnimatedIcons()
    {
        DetachShellAnimatedIcons();
        FinishShellIconReconcile();
    }

    private void DetachShellAnimatedIcons()
    {
        CompositionTarget.Rendering -= ShellIconsRendering;
        foreach (var item in shellIcons)
        {
            foreach (var child in item.Artwork.Children) child.Opacity = 1;
            item.Artwork.Background = null;
            if (!reusableShellIcons.TryAdd((item.Entry.Item.Kind, item.Dark), item.Animation)) item.Animation.Dispose();
        }
        shellIcons.Clear();
    }

    private void FinishShellIconReconcile()
    {
        foreach (var animation in reusableShellIcons.Values)
            try { animation.Dispose(); } catch (Exception ex) { Log.Write(ex); }
        reusableShellIcons.Clear();
    }
}
