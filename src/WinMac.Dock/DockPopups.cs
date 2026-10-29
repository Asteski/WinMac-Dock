using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private void AlignFolderFlyout(Flyout flyout, FrameworkElement content, FrameworkElement anchor)
    {
        bool aligning = false, opened = false;
        void Align()
        {
            if (aligning || !opened || content.XamlRoot == null) return;
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(content.XamlRoot)
                .FirstOrDefault(p => p.IsOpen && p.Child is FlyoutPresenter presenter && ReferenceEquals(presenter.Content, content));
            if (popup?.Child is not FrameworkElement surface || surface.ActualWidth <= 0 || surface.ActualHeight <= 0) return;
            aligning = true;
            try
            {
                var origin = surface.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                var target = anchor.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                // WinUI can flip an edge-adjacent flyout to the far side of its
                // target. Center it along the dock instead, clamped to a 12
                // physical-pixel work-area inset. Keep the dock gap/slide axis.
                double minimum = ((Vertical ? monitor.Work.Top - hostBounds.Top : monitor.Work.Left - hostBounds.Left) + 12) / scale;
                double extent = Vertical ? surface.ActualHeight : surface.ActualWidth;
                double maximum = ((Vertical ? monitor.Work.Bottom - hostBounds.Top : monitor.Work.Right - hostBounds.Left) - 12) / scale - extent;
                double center = Vertical ? target.Y + anchor.ActualHeight / 2 : target.X + anchor.ActualWidth / 2;
                double desired = Math.Clamp(center - extent / 2, minimum, Math.Max(minimum, maximum));
                double delta = desired - (Vertical ? origin.Y : origin.X);
                if (Math.Abs(delta * scale) < .5) return;
                if (Vertical) popup.VerticalOffset += delta;
                else popup.HorizontalOffset += delta;
            }
            finally { aligning = false; }
        }
        // Popup offsets invalidate layout. Never adjust them from LayoutUpdated:
        // WinUI's placement pass may still report the previous screen position.
        content.SizeChanged += (_, _) => { if (opened) DispatcherQueue.TryEnqueue(() => Align()); };
        flyout.Opened += (_, _) => { opened = true; Align(); };
        flyout.Closed += (_, _) => opened = false;
    }

    private void AnimateFolderFlyout(Flyout flyout, FrameworkElement content)
    {
        // Keep WinUI's popup HWND stationary. Its placement manager owns native
        // coordinates; moving that HWND causes a full-size flash and repositioning.
        flyout.AreOpenCloseAnimationsEnabled = false;
        FlyoutPresenter? presenter = null;
        TranslateTransform? slide = null;
        Transform? originalTransform = null;
        RectangleGeometry? originalClip = null;
        var contentClip = new RectangleGeometry();
        nint popupWindow = 0;
        nint detachedOwner = 0;
        PixelRect settled = default;
        var existingWindows = new HashSet<nint>();
        Native.EnumWindows((window, _) => { existingWindows.Add(window); return true; }, 0);
        double travel = 0, rasterScale = 1;
        bool rendering = false;
        double offset = 0, from = 0, to = 0;
        double dx = 0, dy = 0;
        long started = 0;
        const int folderFlyoutDurationMs = 320;
        int duration = folderFlyoutDurationMs;
        bool exiting = false, allowClose = false;
        void PutBehindDock()
        {
            // Detach ownership during each animation so lowering the popup
            // cannot pull the dock's icon HWND below its material HWND.
            if (popupWindow == 0 || materialHandle == 0) return;
            if (detachedOwner == 0 && (Native.GetWindowLongPtrW(popupWindow, -16).ToInt64() & 0x40000000L) == 0)
            {
                detachedOwner = Native.GetWindowLongPtrW(popupWindow, -8);
                if (detachedOwner != 0) Native.SetWindowLongPtrW(popupWindow, -8, 0);
            }
            Native.SetWindowPos(materialHandle, Handle, 0, 0, 0, 0, 0x13);
            Native.SetWindowPos(popupWindow, materialHandle, 0, 0, 0, 0, 0x13);
        }
        void RestorePopupOwner()
        {
            if (popupWindow != 0 && Native.IsWindow(popupWindow) && detachedOwner != 0 && Native.IsWindow(detachedOwner))
                Native.SetWindowLongPtrW(popupWindow, -8, detachedOwner);
            detachedOwner = 0;
            if (materialHandle != 0 && Native.IsWindow(materialHandle))
                Native.SetWindowPos(materialHandle, Handle, 0, 0, 0, 0, 0x13);
        }
        void FindSurface()
        {
            if (!Motion || slide != null) return;
            for (DependencyObject? parent = content; parent != null; parent = VisualTreeHelper.GetParent(parent))
                if (parent is FlyoutPresenter found) { presenter = found; break; }
            if (presenter == null) return;
            rasterScale = content.XamlRoot.RasterizationScale;
            Native.EnumWindows((window, _) =>
            {
                Native.GetWindowThreadProcessId(window, out var pid);
                if (pid == Environment.ProcessId && !existingWindows.Contains(window)
                    && Native.Class(window).Contains("Popup", StringComparison.OrdinalIgnoreCase))
                    popupWindow = window;
                return true;
            }, 0);
            if (popupWindow == 0) return;
            Native.GetWindowRect(popupWindow, out var bounds);
            settled = bounds.Pixels;
            if (settled.Width <= 0 || settled.Height <= 0) return;
            // Mask the native backdrop before presenting the first frame.
            Native.SetDockRegion(popupWindow, new PixelRect(0, 0, 0, 0));
            // The XAML outline moves with the presenter; suppress a second,
            // stationary DWM outline on the native popup window.
            int noBorder = unchecked((int)0xFFFFFFFE);
            Native.DwmSetWindowAttribute(popupWindow, 34, ref noBorder, 4);
            originalTransform = presenter.RenderTransform;
            originalClip = presenter.Clip;
            slide = new TranslateTransform();
            presenter.RenderTransform = slide;
            travel = Vertical ? presenter.ActualWidth : presenter.ActualHeight;
            (dx, dy) = PositionEdge switch
            {
                DockEdge.Top => (0d, -1d), DockEdge.Left => (-1d, 0d),
                DockEdge.Right => (1d, 0d), _ => (0d, 1d)
            };
        }
        void RefreshSurfaceBounds()
        {
            if (presenter == null || popupWindow == 0 || !Native.IsWindow(popupWindow)) return;
            presenter.UpdateLayout();
            Native.GetWindowRect(popupWindow, out var bounds);
            if (bounds.Pixels.Width <= 0 || bounds.Pixels.Height <= 0) return;
            settled = bounds.Pixels;
            travel = Vertical ? presenter.ActualWidth : presenter.ActualHeight;
        }
        void Paint(double value)
        {
            offset = value;
            if (slide == null || popupWindow == 0 || !Native.IsWindow(popupWindow)) return;
            int x = (int)Math.Round(dx * value * rasterScale), y = (int)Math.Round(dy * value * rasterScale);
            // Clip the stationary native material to the translated presenter.
            // The popup cannot draw over the dock or move off its final anchor.
            var visible = FolderFlyoutClip.NativeRegion(settled.Width, settled.Height, x, y);
            Native.SetDockRegion(popupWindow, visible);
            // HWND regions clip the material but do not reliably bound WinUI's
            // redirected popup visuals. Clip the moving artwork in its own
            // coordinates too, so it cannot show through transparent dock fill.
            var local = FolderFlyoutClip.ContentRegion((int)Math.Round(presenter!.ActualWidth * rasterScale),
                (int)Math.Round(presenter.ActualHeight * rasterScale), x, y);
            contentClip.Rect = new Windows.Foundation.Rect(local.Left / rasterScale, local.Top / rasterScale,
                local.Width / rasterScale, local.Height / rasterScale);
            if (presenter != null) presenter.Clip = contentClip;
            // Quantize both the presenter and native clip to identical pixels.
            slide.X = x / rasterScale;
            slide.Y = y / rasterScale;
            if (presenter != null) presenter.Opacity = 1;
            if (value == 0)
            {
                Native.ClearDockRegion(popupWindow);
                if (presenter != null) presenter.Clip = originalClip;
            }
        }
        content.Loaded += (_, _) => FindSurface();
        void Begin(double target, int milliseconds)
        {
            from = offset; to = target; duration = milliseconds;
            // Opened can fire before a frame is presented. Starting elapsed time
            // there can consume the whole entrance while folder layout completes.
            started = 0;
            if (!rendering) { rendering = true; CompositionTarget.Rendering += Render; }
        }
        void StopRendering()
        {
            if (!rendering) return;
            CompositionTarget.Rendering -= Render;
            rendering = false;
        }
        void Render(object? sender, object args)
        {
            if (started == 0)
            {
                if (!exiting) { RefreshSurfaceBounds(); from = travel; }
                started = System.Diagnostics.Stopwatch.GetTimestamp();
                Paint(from);
                return;
            }
            double progress = Math.Clamp(System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds / duration, 0, 1);
            // Reference GIF: a gentle start followed by a decelerating reveal;
            // dismissal accelerates back behind the dock edge.
            double x1 = exiting ? .42 : .25, x2 = exiting ? 1 : .25;
            double low = 0, high = 1;
            for (int i = 0; i < 20; i++)
            {
                double mid = (low + high) / 2, inv = 1 - mid;
                double x = 3 * inv * inv * mid * x1 + 3 * inv * mid * mid * x2 + mid * mid * mid;
                if (x < progress) low = mid; else high = mid;
            }
            double t = (low + high) / 2;
            double eased = progress == 0 ? 0 : progress == 1 ? 1 : 3 * (1 - t) * t * t + t * t * t;
            Paint(from + (to - from) * eased);
            if (progress < 1) return;
            StopRendering();
            if (exiting) { allowClose = true; flyout.Hide(); }
            else RestorePopupOwner();
        }
        flyout.Opened += (_, _) =>
        {
            if (!Motion) return;
            if (slide == null) FindSurface();
            if (slide == null)
            {
                if (presenter != null) presenter.Opacity = 1;
                if (popupWindow != 0) Native.ClearDockRegion(popupWindow);
                Log.Write("Folder reveal: no visual popup surface found; opening without animation.");
                return;
            }
            RefreshSurfaceBounds();
            Paint(travel);
            PutBehindDock();
            Begin(0, folderFlyoutDurationMs);
        };
        flyout.Closing += (_, args) =>
        {
            if (allowClose || !Motion || closing || slide == null) return;
            args.Cancel = true;
            if (exiting) return;
            exiting = true;
            PutBehindDock();
            if (presenter != null) presenter.IsHitTestVisible = false;
            Begin(travel, folderFlyoutDurationMs);
        };
        flyout.Closed += (_, _) =>
        {
            StopRendering();
            if (popupWindow != 0 && Native.IsWindow(popupWindow))
            {
                Native.ClearDockRegion(popupWindow);

            }
            RestorePopupOwner();
            if (presenter != null) { presenter.RenderTransform = originalTransform; presenter.Clip = originalClip; }
            popupWindow = 0; detachedOwner = 0; slide = null; presenter = null;
        };
    }
    private readonly HashSet<FlyoutBase> openingFlyouts = [];
    private readonly SolidColorBrush popupBorderBrush = new();
    private void UpdateOpenPopupBorders()
    {
        if (root.XamlRoot == null) return;
        foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot))
            if (popup.Child is FlyoutPresenter presenter)
            {
                presenter.BorderBrush = popupBorderBrush;
                presenter.BorderThickness = new Thickness(1);
                presenter.RequestedTheme = root.RequestedTheme;
            }
    }
    // Non-activating dock windows can miss native light dismissal. Watch only
    // while a flyout is open, and keep clicks inside nested popups untouched.
    private DispatcherTimer? flyoutDismissTimer;
    private void StartFlyoutDismissWatch()
    {
        Native.GetAsyncKeyState(1); Native.GetAsyncKeyState(2);
        flyoutDismissTimer ??= CreateFlyoutDismissTimer();
        flyoutDismissTimer.Start();
    }
    private DispatcherTimer CreateFlyoutDismissTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        timer.Tick += (_, _) =>
        {
            if (openingFlyouts.Count == 0) { timer.Stop(); return; }
            UpdateOpenPopupBorders();
            bool pressed = (Native.GetAsyncKeyState(1) & 1) != 0;
            pressed |= (Native.GetAsyncKeyState(2) & 1) != 0;
            if (!pressed || !Native.GetCursorPos(out var point)) return;
            if (visibleBounds.Contains(point.X, point.Y)) return;
            var position = new Windows.Foundation.Point((point.X - hostBounds.Left) / scale, (point.Y - hostBounds.Top) / scale);
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot))
            {
                if (!popup.IsOpen || popup.Child is not FrameworkElement child) continue;
                var origin = child.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
                if (new Windows.Foundation.Rect(origin.X, origin.Y, child.ActualWidth, child.ActualHeight).Contains(position)) return;
            }
            foreach (var flyout in openingFlyouts.ToArray()) flyout.Hide();
        };
        return timer;
    }
    private FlyoutShowOptions DockPopupOptions(FrameworkElement anchor, Windows.Foundation.Point? requestedPosition = null, double gap = 12)
    {
        var origin = anchor.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
        // visibleBounds includes magnified icon overflow. Use the material edge
        // for a constant visual gap in both magnification modes.
        var panel = MaterialPanel;
        double left = (panel.Left - hostBounds.Left) / scale;
        double top = (panel.Top - hostBounds.Top) / scale;
        double right = (panel.Right - hostBounds.Left) / scale;
        double bottom = (panel.Bottom - hostBounds.Top) / scale;
        var position = PositionEdge switch
        {
            DockEdge.Top => new Windows.Foundation.Point(requestedPosition?.X ?? origin.X + anchor.ActualWidth / 2, bottom + gap),
            DockEdge.Left => new Windows.Foundation.Point(right + gap, requestedPosition?.Y ?? origin.Y + anchor.ActualHeight / 2),
            DockEdge.Right => new Windows.Foundation.Point(left - gap, requestedPosition?.Y ?? origin.Y + anchor.ActualHeight / 2),
            _ => new Windows.Foundation.Point(requestedPosition?.X ?? origin.X + anchor.ActualWidth / 2, top - gap)
        };
        return new FlyoutShowOptions { Placement = PopupPlacement, ShowMode = FlyoutShowMode.Standard, Position = position,
            ExclusionRect = new Windows.Foundation.Rect(left, top, right - left, bottom - top) };
    }
    private void ShowDockMenuAtPointer(Native.POINT? requestedPoint = null, bool preferSeparator = false)
    {
        Native.POINT point;
        if (requestedPoint.HasValue) point = requestedPoint.Value;
        else if (!Native.GetCursorPos(out point)) return;
        var separator = preferSeparator ? RemovableSeparatorAt(point) : null;
        // Finishing a resize can move the native host. Convert the captured
        // screen position only after its bounds have settled.
        FinishRowTransition();
        if (visibilitySliding && !hidden) FinishVisibilityAnimation();
        var position = new Windows.Foundation.Point((point.X - hostBounds.Left) / scale, (point.Y - hostBounds.Top) / scale);
        if (separator?.Tag is DockEntry entry) ShowDockFlyout(ItemMenu(entry), separator, position);
        else ShowDockFlyout(BackgroundMenu(), dockBorder, position);
    }
    private void ShowDockFlyout(FlyoutBase flyout, FrameworkElement anchor, Windows.Foundation.Point? requestedPosition = null)
    {
        string? id = (anchor.Tag as DockEntry)?.Item.Id;
        FinishRowTransition();
        if (visibilitySliding && !hidden) FinishVisibilityAnimation();
        if (id != null) anchor = buttons.FirstOrDefault(b => b.Entry.Item.Id == id).Button
            ?? strip.Children.OfType<FrameworkElement>().FirstOrDefault(e => (e.Tag as DockEntry)?.Item.Id == id) ?? dockBorder;
        flyout.ShouldConstrainToRootBounds = false;
        if (flyout is MenuFlyout)
        {
            AlignMenuDockGap(flyout);
            if (id != null) CenterItemMenu(flyout, anchor);
        }
        // Light dismissal requires a foreground owner. This runs only for an
        // explicitly opened popup, never from passive startup or maintenance.
        tracker.ObserveForeground();
        Activate();
        Native.SetForegroundWindow(Handle);
        flyout.ShowAt(root, DockPopupOptions(anchor, requestedPosition));
    }
    private void AlignMenuDockGap(FlyoutBase flyout)
    {
        void Opened(object? sender, object args)
        {
            flyout.Opened -= Opened;
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
                .FirstOrDefault(p => p.IsOpen && p.Child is MenuFlyoutPresenter);
            if (popup?.Child is not FrameworkElement presenter) return;
            presenter.UpdateLayout();
            var origin = presenter.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            var panel = MaterialPanel;
            // MenuFlyout can choose its own pointer-based placement. Correct its
            // cross-axis position after layout; retain along-axis item centering.
            if (Vertical)
            {
                double desired = PositionEdge == DockEdge.Left ? (panel.Right - hostBounds.Left) / scale + 12
                    : (panel.Left - hostBounds.Left) / scale - 12 - presenter.ActualWidth;
                popup.HorizontalOffset += desired - origin.X;
            }
            else
            {
                double desired = PositionEdge == DockEdge.Top ? (panel.Bottom - hostBounds.Top) / scale + 12
                    : (panel.Top - hostBounds.Top) / scale - 12 - presenter.ActualHeight;
                popup.VerticalOffset += desired - origin.Y;
            }
        }
        flyout.Opened += Opened;
    }
    private void DismissFlyoutsOnExternalFocus()
    {
        if (openingFlyouts.Count == 0) return;
        var foreground = Native.GetForegroundWindow();
        if (foreground == 0) return;
        Native.GetWindowThreadProcessId(foreground, out var pid);
        if (pid == Environment.ProcessId) return;
        foreach (var flyout in openingFlyouts.ToArray()) flyout.Hide();
    }
    // Keep item-menu centering separate from background/tray and folder placement.
    private void CenterItemMenu(FlyoutBase flyout, FrameworkElement anchor)
    {
        void Opened(object? sender, object args)
        {
            flyout.Opened -= Opened;
            var popup = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot)
                .FirstOrDefault(p => p.Child is MenuFlyoutPresenter && p.IsOpen);
            if (popup?.Child is not FrameworkElement presenter) return;
            presenter.UpdateLayout();
            var menuOrigin = presenter.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            var itemOrigin = anchor.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point());
            if (Vertical)
            {
                double minimum = (monitor.Work.Top - hostBounds.Top) / scale;
                double maximum = (monitor.Work.Bottom - hostBounds.Top) / scale - presenter.ActualHeight;
                double desired = Math.Clamp(itemOrigin.Y + anchor.ActualHeight / 2 - presenter.ActualHeight / 2, minimum, Math.Max(minimum, maximum));
                popup.VerticalOffset += desired - menuOrigin.Y;
            }
            else
            {
                double minimum = (monitor.Work.Left - hostBounds.Left) / scale;
                double maximum = (monitor.Work.Right - hostBounds.Left) / scale - presenter.ActualWidth;
                double desired = Math.Clamp(itemOrigin.X + anchor.ActualWidth / 2 - presenter.ActualWidth / 2, minimum, Math.Max(minimum, maximum));
                popup.HorizontalOffset += desired - menuOrigin.X;
            }
        }
        flyout.Opened += Opened;
    }
}
