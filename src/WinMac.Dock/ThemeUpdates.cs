using Microsoft.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private string trayIconReference = BundledIcons.Tray;

    private void UpdateTrayIcon()
    {
        if (closing || MonitorOverride != null) return;
        bool dark = TaskbarMaterialTheme() == ElementTheme.Dark;
        var style = Settings.TrayIconStyle;
        string reference = style != TrayIconStyle.Default
            ? (dark ? BundledIcons.MenuDark : BundledIcons.MenuLight)
            : BundledIcons.Tray;
        var colour = uiSettings.GetColorValue(accessibility.HighContrast ? UIColorType.Foreground : dark ? UIColorType.AccentLight2 : UIColorType.Accent);
        int pixels = style != TrayIconStyle.Default ? System.Windows.Forms.SystemInformation.SmallIconSize.Width : 32;
        string identity = $"{reference}|{pixels}|{style}";
        if (style == TrayIconStyle.AccentColour) identity += $"|{colour.R},{colour.G},{colour.B}";
        if (trayIconReference == identity) return;
        var icon = style == TrayIconStyle.AccentColour ? IconResources.Tint(reference, pixels, colour) : IconResources.Load(reference, pixels);
        var previous = tray.Icon;
        tray.Icon = icon;
        trayIconReference = identity;
        previous?.Dispose();
    }

    internal static ElementTheme TaskbarMaterialTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0 ? ElementTheme.Light : ElementTheme.Dark;
    }
    private bool WindowsUsesDarkApps()
    {
        if (accessibility.HighContrast)
        {
            var background = uiSettings.GetColorValue(UIColorType.Background);
            return background.R + background.G + background.B <= 380;
        }
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value ? value == 0 : uiSettings.GetColorValue(UIColorType.Background).R < 128;
    }
    private static string ReadThemeIdentity()
    {
        using var theme = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        using var accent = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
        using var dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        return $"{theme?.GetValue("AppsUseLightTheme")}|{theme?.GetValue("SystemUsesLightTheme")}|{dwm?.GetValue("ColorizationColor")}|{dwm?.GetValue("AccentColor")}|{Convert.ToHexString(accent?.GetValue("AccentPalette") as byte[] ?? [])}";
    }
}
