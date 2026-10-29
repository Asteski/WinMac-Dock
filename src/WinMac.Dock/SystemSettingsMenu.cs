using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal static partial class ShellService
{
    internal static bool IsSystemSettings(DockItem item) =>
        item.AppUserModelId.StartsWith("windows.immersivecontrolpanel_", StringComparison.OrdinalIgnoreCase)
        || item.AppUserModelId.Equals("windows.immersivecontrolpanel", StringComparison.OrdinalIgnoreCase)
        || item.Target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(item.ExecutablePath).Equals("SystemSettings.exe", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(item.Target).Equals("SystemSettings.exe", StringComparison.OrdinalIgnoreCase);
}

internal sealed partial class DockWindow
{
    // URI reference: https://learn.microsoft.com/windows/apps/develop/launch/launch-settings
    private static readonly (string Label, string Uri, string Glyph)[] systemSettingsCategories =
    [
        ("Home", "ms-settings:", "\uE80F"),
        ("System", "ms-settings:system", "\uE75B"),
        ("Bluetooth & devices", "ms-settings:bluetooth", "\uE702"),
        ("Network & internet", "ms-settings:network-status", "\uE701"),
        ("Personalization", "ms-settings:personalization", "\uE771"),
        ("Apps & features", "ms-settings:appsfeatures", "\uE74C"),
        ("Accounts", "ms-settings:accounts", "\uE77B"),
        ("Time & language", "ms-settings:dateandtime", "\uE775"),
        ("Gaming", "ms-settings:gaming-gamemode", "\uE7FC"),
        ("Accessibility", "ms-settings:easeofaccess", "\uE776"),
        ("Privacy & security", "ms-settings:privacy", "\uEA18"),
        ("Windows Update", "ms-settings:windowsupdate", "\uF0C5")
    ];
    private void AddSystemSettingsCategories(MenuFlyout menu, DockItem item)
    {
        foreach (var category in systemSettingsCategories)
            Menu(menu, category.Label, () => LaunchSystemSettings(item, category.Uri), category.Glyph);
        menu.Items.Add(new MenuFlyoutSeparator());
    }
    private void LaunchMenuItem(DockItem item)
    {
        if (ShellService.IsSystemSettings(item)) LaunchSystemSettings(item);
        else TryAction(() => { ShellService.Launch(item); UnpinOpenedDownload(item); });
    }
    private async void LaunchSystemSettings(DockItem item, string uri = "ms-settings:")
    {
        try
        {
            ShellService.Open(uri);
            // Menu commands used to bypass launch feedback. Wait for dismissal
            // and any row transition, then locate the current artwork by ID:
            // starting the Settings process may have already rebuilt its pin.
            while (!closing && (popupCount > 0 || rowAnimating || reconciling || positioning)) await Task.Delay(16);
            if (closing) return;
            var current = buttons.FirstOrDefault(b => b.Entry.Item.Id == item.Id);
            if (current.Button != null) Bounce(current.Icon);
        }
        catch (Exception ex) { Log.Write(ex); _ = Message("Cannot open System Settings", ex.Message); }
    }
}
