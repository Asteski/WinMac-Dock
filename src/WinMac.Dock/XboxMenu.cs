using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;

internal static partial class ShellService
{
    internal static bool IsXbox(DockItem item) =>
        item.AppUserModelId.StartsWith("Microsoft.GamingApp_", StringComparison.OrdinalIgnoreCase)
        || item.Target.StartsWith(@"shell:AppsFolder\Microsoft.GamingApp_", StringComparison.OrdinalIgnoreCase)
        || IsXboxExecutable(item.Target) || IsXboxExecutable(item.ExecutablePath);

    private static bool IsXboxExecutable(string path) =>
        Path.GetFileName(path).Equals("XboxPcApp.exe", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(path).Equals("XboxPcAppFT.exe", StringComparison.OrdinalIgnoreCase);
}

internal sealed partial class DockWindow
{
    private static readonly FontFamily xboxFont = new("ms-appx:///assets/fonts/XboxFluent.ttf#Xbox Fluent");
    private static readonly FontFamily xboxFont2 = new("ms-appx:///assets/fonts/XboxFluent2.ttf#Xbox Fluent v2");
    private static readonly (string Label, string Target, string Glyph, FontFamily Font)[] xboxCommands =
    [
        ("Home", "xbox://home/", "\uE35A", xboxFont),
        ("View profile", "xbox://profile/", "\uE301", xboxFont),
        ("Game Pass", "xbox://gamepass/", "\uE636", xboxFont),
        ("Library", "xbox://library", "\uE6AA", xboxFont),
        ("Cloud Gaming", "xbox://gamestreaming/", "\uE34A", xboxFont),
        ("Store", "xbox://store/", "\uE3A0", xboxFont),
        ("Redeem a code", "xbox://redeem/", "\uEB71", xboxFont2),
        ("Install queue", "xbox://installs/", "\uE47D", xboxFont)
    ];

    private void AddXboxCommands(MenuFlyout menu)
    {
        foreach (var command in xboxCommands)
        {
            var entry = new MenuFlyoutItem { Text = command.Label, Icon = new FontIcon { Glyph = command.Glyph, FontFamily = command.Font, FontSize = 16 } };
            entry.Click += (_, _) => TryAction(() => ShellService.Open(command.Target));
            menu.Items.Add(entry);
        }
    }

    private void AddXboxOpen(MenuFlyout menu, DockEntry entry)
    {
        bool minimize = entry.Windows.Any(w => Native.IsWindow(w.Handle) && !Native.IsIconic(w.Handle));
        var open = new MenuFlyoutItem { Text = minimize ? "Minimize Xbox" : "Open Xbox", Icon = new FontIcon { Glyph = "\uE9A1", FontFamily = xboxFont2, FontSize = 16 } };
        open.Click += (_, _) => InvokeSingletonMenu(entry);
        menu.Items.Add(open);
    }
}
