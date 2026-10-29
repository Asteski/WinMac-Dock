using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private Window? materialWindow;
    private Grid? materialRoot;
    private DockBackdrop? materialBackdrop;
    private nint materialHandle;
    private Native.SubclassProc? materialSubclass;
    private PixelRect materialHostBounds;
    private bool materialVisible;
    private long materialWindowMoves;
    private uint orderedSurfaceBand;
    private readonly Canvas hoverArtwork = new() { IsHitTestVisible = false };
    private bool HoverEnvelope => Motion && !dockOverflow;
    private double HoverEnvelopeOverflow => HoverOverflow + BounceEnvelopeHeight;
    private Thickness DockOutlineThickness => !Settings.ShowDockBorder ? new Thickness(0) : !SpanDockLengthActive() ? new Thickness(1)
        : PositionEdge switch
        {
            DockEdge.Bottom => new Thickness(0, 1, 0, 0),
            DockEdge.Top => new Thickness(0, 0, 0, 1),
            DockEdge.Left => new Thickness(0, 0, 1, 0),
            _ => new Thickness(1, 0, 0, 0)
        };

    private void SyncSurfaceBandOrder()
    {
        if (!materialVisible || materialHandle == 0) return;
        uint foregroundBand = Native.WindowBand(Handle), backgroundBand = Native.WindowBand(materialHandle);
        if (foregroundBand == 0 || foregroundBand != backgroundBand || foregroundBand == orderedSurfaceBand) return;
        // Explorer moves each HWND to its shell band independently, placing
        // the later material HWND above the icons. Ordinary GetWindow traversal
        // does not reliably expose that ordering for shell-band windows. Once
        // both have arrived, explicitly restore the material behind the icons.
        if (Native.SetWindowPos(materialHandle, Handle, 0, 0, 0, 0, 0x13))
            orderedSurfaceBand = foregroundBand;
    }

    private void EnsureMaterialSurface()
    {
        if (materialWindow != null) return;
        materialRoot = new Grid { Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent) };
        materialWindow = new Window { Title = "WinMac Dock material surface", Content = materialRoot };
        materialHandle = WinRT.Interop.WindowNative.GetWindowHandle(materialWindow);
        Native.RegisterPassiveWindow(materialHandle);
        var presenter = (Microsoft.UI.Windowing.OverlappedPresenter)materialWindow.AppWindow.Presenter;
        presenter.SetBorderAndTitleBar(false, false); presenter.IsResizable = true;
        presenter.IsMaximizable = false; presenter.IsMinimizable = false; presenter.IsAlwaysOnTop = true;
        materialWindow.AppWindow.IsShownInSwitchers = false;
        var style = Native.GetWindowLongPtrW(materialHandle, -20).ToInt64();
        Native.SetWindowLongPtrW(materialHandle, -20, (nint)((style & ~(0x100L | 0x200L | 0x20000L)) | 0x8 | 0x80 | 0x20 | 0x08000000)); // Topmost tool, click-through, no activation.
        var nativeStyle = Native.GetWindowLongPtrW(materialHandle, -16).ToInt64();
        // Keep the composited DWM frame while NCCALCSIZE makes the entire
        // panel client area. Leave it unregioned so DWM supplies its shadow.
        Native.SetWindowLongPtrW(materialHandle, -16, (nint)((nativeStyle & ~(0x00800000L | 0x00400000L)) | 0x80000000L | 0x00040000L));
        Native.SetWindowPos(materialHandle, 0, 0, 0, 0, 0, 0x20 | 0x1 | 0x2 | 0x4 | 0x10); // Recalculate client area after removing the frame.
        materialSubclass = (hwnd, msg, wp, lp, id, data) =>
        {
            if (msg == 0x0083) return 0;
            if (msg == 0x0014 && Native.ClearCompositedClient(hwnd, (nint)wp)) return 1;
            if (msg == 0x031E) Native.ConfigureCompositedClient(hwnd);
            if (msg == 0x0047 && initialized && !closing && requestedTaskViewBand != orderedSurfaceBand) DispatcherQueue.TryEnqueue(SyncSurfaceBandOrder);
            return Native.DefSubclassProc(hwnd, msg, wp, lp);
        };
        Native.SetWindowSubclass(materialHandle, materialSubclass, 1, 0);
        materialWindow.Closed += (_, _) => { Native.RemoveWindowSubclass(materialHandle, materialSubclass, 1); Native.UnregisterPassiveWindow(materialHandle); };
        Native.SetWindowPos(materialHandle, 0, 0, 0, 0, 0, 0x20 | 0x1 | 0x2 | 0x4 | 0x10);
        var borderColor = unchecked((int)0xFFFFFFFE); Native.DwmSetWindowAttribute(materialHandle, 34, ref borderColor, 4);
        materialBackdrop = new DockBackdrop(materialWindow);
    }

    private void ApplyMaterialSurface(Windows.UI.Color contrastColor)
    {
        EnsureMaterialSurface();
        // The interactive window is always transparent. Native Mica/Acrylic
        // stays on the same backing surface at rest, entry, hover, and exit.
        backdrop.DetachForOverlay();
        dockBorder.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        materialRoot!.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        materialBackdrop!.Apply(Settings, root.RequestedTheme, accessibility.HighContrast, uiSettings.AdvancedEffectsEnabled, contrastColor);
    }

    private void PaintMaterialSurface(double expansion)
    {
        if (rowStaging || !StartupComplete) return;
        if (materialWindow == null) return;
        if ((hidden && !visibilitySliding) || closing)
        {
            if (materialVisible) Native.ShowWindow(materialHandle, 0);
            materialVisible = false; return;
        }
        var panel = ExpandedMaterialPanel(expansion);
        if (rowAnimating) panel = rowDisplayPanel;
        UpdateMaterialGrain(panel);
        // Draw the outline at the exact material bounds. Scaling the resting
        // border stretches its stroke/corners and drifts from pixel-rounded clips.
        dockBorder.RenderTransform = null;
        dockBorder.BorderThickness = DockOutlineThickness;
        if (rowAnimating) rowOutline.BorderThickness = DockOutlineThickness;
        dockBorder.HorizontalAlignment = HorizontalAlignment.Left;
        dockBorder.VerticalAlignment = VerticalAlignment.Top;
        dockBorder.Margin = new Thickness((panel.Left - hostBounds.Left) / scale, (panel.Top - hostBounds.Top) / scale, 0, 0);
        dockBorder.Width = panel.Width / scale;
        dockBorder.Height = panel.Height / scale;
        // Width/Margin changes otherwise wait until a later XAML layout pass,
        // while the icon composition transforms are submitted immediately.
        dockBorder.UpdateLayout();
        // The material HWND follows the visible panel. DWM owns its rounded
        // corners and shadow; a window region would disable that shadow.
        var surface = panel;
        if (visibilitySliding)
        {
            var slide = DockGeometry.Slide(resting, PositionEdge, visibilityFraction, Settings.FloatingGap, Settings.KeepHiddenEdgeTrigger);
            int dx = slide.Left - resting.Left, dy = slide.Top - resting.Top;
            surface = new(panel.Left + dx, panel.Top + dy, panel.Right + dx, panel.Bottom + dy);
        }
        int corners = UseRoundedDockCorners ? 2 : 1;
        Native.DwmSetWindowAttribute(materialHandle, 33, ref corners, 4);
        if (materialHostBounds != surface || !materialVisible)
        {
            materialHostBounds = surface;
            materialWindowMoves++;
            // Match the new viewport now rather than waiting for the WinUI
            // window-size notification after SetWindowPos.
            materialRoot!.Width = surface.Width / scale;
            materialRoot.Height = surface.Height / scale;
            // Geometry changes do not require reordering the two surfaces or
            // copying pixels from the old-sized material backing store.
            Native.SetWindowPos(materialHandle, Handle, surface.Left, surface.Top, surface.Width, surface.Height,
                0x10u | 0x100u | 0x400u | (materialVisible ? 0x4u : 0x40u));
            materialRoot.UpdateLayout();
            materialVisible = true;
        }
    }
    private PixelRect ExpandedMaterialPanel(double expansion)
    {
        if (laidOutSpanning) return resting;
        int before = DockGeometry.Pixels(expansion * HoverAnchor, scale), after = DockGeometry.Pixels(expansion * (1 - HoverAnchor), scale);
        return Vertical ? new(resting.Left, resting.Top - before, resting.Right, resting.Bottom + after)
            : new(resting.Left - before, resting.Top, resting.Right + after, resting.Bottom);
    }
}
