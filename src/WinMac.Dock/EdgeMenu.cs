using Microsoft.UI.Xaml.Controls;
using System.Text.RegularExpressions;

namespace WinMac.Dock;
internal static partial class ShellService
{
    internal static bool IsMicrosoftEdge(DockItem item) =>
        (Path.GetFileName(item.ExecutablePath).Equals("msedge.exe", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(item.Target).Equals("msedge.exe", StringComparison.OrdinalIgnoreCase))
        && !DockApplicationIdentity.IsBrowserHostedApp(item)
        && !item.Arguments.Contains("--app", StringComparison.OrdinalIgnoreCase)
        && !item.AppUserModelId.Contains("_crx_", StringComparison.OrdinalIgnoreCase);

    internal static DockItem EdgeTabLaunchItem(DockItem item, string executable)
    {
        // Retain explicit profile selection, but never inherit new-window,
        // app-mode or elevation options from the pinned launcher.
        var profile = Regex.Matches(item.Arguments, """--(?:profile-directory|user-data-dir)=(?:"[^"]*"|\S+)""", RegexOptions.IgnoreCase);
        return new DockItem { Kind = ItemKind.Application, Target = executable, ExecutablePath = executable,
            Arguments = string.Join(" ", profile.Select(match => match.Value).Append("edge://newtab/")) };
    }
}
internal sealed partial class DockWindow
{
    private bool HasEdgeWindow => tracker.Read().Any(window => ShellService.IsMicrosoftEdge(new DockItem { ExecutablePath = window.ExecutablePath, AppUserModelId = window.AppUserModelId }));
    private void OpenEdgeTab(DockItem item) => TryAction(() =>
    {
        var windows = tracker.Read().Where(window => ShellService.IsMicrosoftEdge(new DockItem { ExecutablePath = window.ExecutablePath, AppUserModelId = window.AppUserModelId }));
        var target = tracker.MostRecentlyUsed(windows).FirstOrDefault();
        if (target == null) return;
        ShellService.Activate(target, false);
        ShellService.LaunchWithoutElevation(ShellService.EdgeTabLaunchItem(item, target.ExecutablePath));
    });
}
