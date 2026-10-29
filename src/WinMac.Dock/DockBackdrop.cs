using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;

// Use Windows' material controllers, rather than blurring a captured desktop.
// A separate Windows compositor is required by ICompositionSupportsSystemBackdrop.
internal sealed class DockBackdrop(Window? window = null) : SystemBackdrop, IDisposable
{
    private ICompositionSupportsSystemBackdrop? target;
    private DockSettings? lastSettings;
    private ElementTheme lastTheme;
    private bool lastHighContrast, lastEffects;
    private Windows.UI.Color lastContrastColor;
    private DesktopAcrylicController? acrylic;
    private MicaController? mica;
    private SystemBackdropConfiguration? configuration;
    private Windows.UI.Composition.Compositor? compositor;
    private Windows.System.DispatcherQueueController? systemQueue;
    private Windows.UI.Composition.CompositionColorBrush? plain;
    private (DockMaterial Material, bool Defaults, bool HighContrast, bool Effects, int Tint, int Luminosity, ElementTheme Theme)? current;
    public string Status { get; private set; } = "";
    public bool HasActiveAcrylic => acrylic?.State == SystemBackdropState.Active;
    public object Diagnostics => new { Status, SurfaceConnected = target != null && (window == null || ReferenceEquals(window.SystemBackdrop, this)), DispatcherReady = Windows.System.DispatcherQueue.GetForCurrentThread() != null, State = acrylic?.State.ToString() ?? mica?.State.ToString(), TintColor = acrylic?.TintColor.ToString() ?? mica?.TintColor.ToString(), Material = current?.Material.ToString(), NativeDefaults = current?.Defaults, TintOpacity = acrylic?.TintOpacity ?? mica?.TintOpacity, LuminosityOpacity = acrylic?.LuminosityOpacity ?? mica?.LuminosityOpacity, PlainAlpha = plain?.Color.A };
    public void DetachForOverlay()
    {
        // Keep the custom backdrop connected, but make its window surface
        // alpha-zero. This avoids the black/opaque band around hover content.
        if (window != null && !ReferenceEquals(window.SystemBackdrop, this)) window.SystemBackdrop = this;
        ReleaseMaterial(); current = null;
        if (target == null) return;
        EnsureSystemDispatcher();
        if (compositor == null) compositor = new Windows.UI.Composition.Compositor();
        plain = compositor.CreateColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0));
        target.SystemBackdrop = plain;
        Status = "Transparent hover overlay";
    }

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop connectedTarget, XamlRoot xamlRoot)
    {
        if (window != null) Native.ConfigureCompositedClient(WinRT.Interop.WindowNative.GetWindowHandle(window));
        base.OnTargetConnected(connectedTarget, xamlRoot);
        target = connectedTarget;
        if (lastSettings != null) Apply(lastSettings, lastTheme, lastHighContrast, lastEffects, lastContrastColor);
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop disconnectedTarget)
    {
        ReleaseMaterial(); target = null; current = null;
        base.OnTargetDisconnected(disconnectedTarget);
    }

    public void Apply(DockSettings settings, ElementTheme theme, bool highContrast, bool effectsEnabled, Windows.UI.Color highContrastColor)
    {
        lastSettings = settings; lastTheme = theme; lastHighContrast = highContrast; lastEffects = effectsEnabled; lastContrastColor = highContrastColor;
        // Window.SystemBackdrop tells WinUI to remove its opaque XAML island
        // background. Setting only the composition target brush leaves that
        // background in front of the material in current WinUI builds.
        if (window != null && !ReferenceEquals(window.SystemBackdrop, this)) window.SystemBackdrop = this;
        if (target == null) return;
        EnsureSystemDispatcher();
        bool defaults = settings.Material == DockMaterial.Default;
        if (defaults) theme = DockWindow.TaskbarMaterialTheme();
        var key = (settings.Material, defaults, highContrast, effectsEnabled, defaults ? 0 : settings.BackgroundStrength, defaults ? 0 : settings.MaterialLuminosity, theme);
        bool reconnect = key != current;
        var tint = defaults
            ? (theme == ElementTheme.Light ? Windows.UI.Color.FromArgb(255, 238, 238, 238) : Windows.UI.Color.FromArgb(255, 28, 28, 28)) // Base acrylic solid fallbacks.
            : (theme == ElementTheme.Light ? Windows.UI.Color.FromArgb(255, 245, 245, 248) : Windows.UI.Color.FromArgb(255, 24, 24, 28));
        if (reconnect)
        {
            ReleaseMaterial(); current = key;
            if (!highContrast && effectsEnabled && settings.Material != DockMaterial.None)
            {
                configuration = new SystemBackdropConfiguration { IsInputActive = true };
                if ((defaults || settings.Material == DockMaterial.Acrylic) && DesktopAcrylicController.IsSupported())
                {
                    acrylic = new DesktopAcrylicController { Kind = DesktopAcrylicKind.Base };
                    acrylic.SetSystemBackdropConfiguration(configuration);
                }
                else if (settings.Material == DockMaterial.Mica && MicaController.IsSupported())
                {
                    mica = new MicaController { Kind = MicaKind.Base };
                    mica.SetSystemBackdropConfiguration(configuration);
                }
            }
            if (acrylic == null && mica == null)
            {
                if (compositor == null)
                {
                    // WinUI's Microsoft dispatcher is distinct from the Windows
                    // dispatcher needed by the system-backdrop color brush.
                    compositor = new Windows.UI.Composition.Compositor();
                }
                plain = compositor.CreateColorBrush(); target.SystemBackdrop = plain;
            }
        }
        // Establish policy/theme before supplying custom colors and attaching
        // the surface. Native mode leaves Windows' material values untouched.
        if (configuration != null) configuration.Theme = theme == ElementTheme.Light ? SystemBackdropTheme.Light : SystemBackdropTheme.Dark;
        if (acrylic != null)
        {
            // The taskbar references AcrylicInAppFillColorBaseBrush. Base acrylic
            // supplies the native recipe; Default must never set custom properties.
            if (!defaults)
            {
                acrylic.TintColor = tint;
                acrylic.TintOpacity = settings.BackgroundStrength / 100f;
                acrylic.LuminosityOpacity = settings.MaterialLuminosity / 100f;
                acrylic.FallbackColor = tint;
            }
            Status = defaults ? "Default · Windows taskbar acrylic" : "Acrylic · Custom background";
            if (reconnect) acrylic.AddSystemBackdropTarget(target);
        }
        else if (mica != null)
        {
            mica.TintColor = tint;
            mica.TintOpacity = settings.BackgroundStrength / 100f;
            mica.LuminosityOpacity = settings.MaterialLuminosity / 100f;
            mica.FallbackColor = tint;
            Status = "Mica · Custom background";
            if (reconnect) mica.AddSystemBackdropTarget(target);
        }
        else if (plain != null)
        {
            bool fallback = highContrast || !effectsEnabled || settings.Material != DockMaterial.None;
            var color = highContrast ? highContrastColor : tint;
            color.A = fallback ? (byte)255 : (byte)Math.Round(settings.BackgroundStrength * 255d / 100);
            plain.Color = color;
            Status = highContrast ? "Solid background · Windows high contrast" : !effectsEnabled ? "Solid background · Windows transparency effects are off"
                : settings.Material != DockMaterial.None ? "Solid background · Selected material unavailable"
                : $"No blur · Background opacity {settings.BackgroundStrength}%";
        }
    }
    private void EnsureSystemDispatcher()
    {
        if (Windows.System.DispatcherQueue.GetForCurrentThread() != null) return;
        var options = new Native.DispatcherQueueOptions { Size = System.Runtime.InteropServices.Marshal.SizeOf<Native.DispatcherQueueOptions>(), ThreadType = 2, ApartmentType = 0 };
        System.Runtime.InteropServices.Marshal.ThrowExceptionForHR(Native.CreateDispatcherQueueController(options, out var queue));
        try { systemQueue = WinRT.MarshalInterface<Windows.System.DispatcherQueueController>.FromAbi(queue); }
        finally { System.Runtime.InteropServices.Marshal.Release(queue); }
    }
    private void ReleaseMaterial()
    {
        acrylic?.Dispose(); acrylic = null; mica?.Dispose(); mica = null;
        if (target != null) target.SystemBackdrop = null;
        plain?.Dispose(); plain = null; configuration = null;
    }
    public void Dispose()
    {
        if (window != null) window.SystemBackdrop = null;
        ReleaseMaterial(); compositor?.Dispose(); compositor = null;
        if (systemQueue != null) { _ = systemQueue.ShutdownQueueAsync(); systemQueue = null; }
    }
}
