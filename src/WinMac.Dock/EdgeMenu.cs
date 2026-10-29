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
    private void CheckEdgeMenuPolicy()
    {
        Exception? failure = null;
        try
        {
            var sample = new DockItem { Kind = ItemKind.Application, Name = "Microsoft Edge", Target = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", ExecutablePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", RunAsAdministrator = true };
            var menu = ItemMenu(new(sample, null, true));
            var labels = menu.Items.OfType<MenuFlyoutItem>().Select(item => item.Text).ToArray();
            if (labels.Any(label => label is "Run as administrator" or "Open without elevation" or "Open in new tab")) throw new InvalidOperationException("Edge menu retains an administrator command or old tab label.");
            if (labels.Contains("Open new tab") != HasEdgeWindow) throw new InvalidOperationException("Edge tab command does not follow running windows.");
            if (ShellService.SupportsAdministratorLaunch(sample) || ShellService.LaunchInfo(sample, true).Verb == "runas") throw new InvalidOperationException("Edge still supports administrator launch.");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "edge-menu-policy-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString() }));
    }
    private async void CheckEdgeNewTab()
    {
        Exception? failure = null;
        var checks = new List<string>();
        try
        {
            var windows = tracker.Read().Where(window => ShellService.IsMicrosoftEdge(new DockItem { ExecutablePath = window.ExecutablePath, AppUserModelId = window.AppUserModelId })).ToArray();
            var target = windows.FirstOrDefault() ?? throw new InvalidOperationException("No Edge browser window is open.");
            var edge = new DockItem { Kind = ItemKind.Application, Name = "Microsoft Edge", Target = target.ExecutablePath, ExecutablePath = target.ExecutablePath };
            var menu = ItemMenu(new(edge, null, true));
            if (!menu.Items.OfType<MenuFlyoutItem>().Any(row => row.Text == "Open new tab" && row.Icon is FontIcon { Glyph: "\uEC6C" })
                || menu.Items.OfType<MenuFlyoutItem>().Any(row => row.Text == "Run as administrator")) throw new InvalidOperationException("Running Edge menu has incorrect tab/elevation commands.");
            checks.Add("Running Edge replaces Run as administrator with Open new tab and EC6C");
            var plan = ShellService.LaunchInfo(ShellService.EdgeTabLaunchItem(edge with { Arguments = "--new-window --profile-directory=\"Profile 1\"", RunAsAdministrator = true }, target.ExecutablePath));
            if (plan.Verb == "runas" || plan.Arguments.Contains("--new-window") || !plan.Arguments.Contains("--profile-directory=\"Profile 1\"") || !plan.Arguments.EndsWith("edge://newtab/")) throw new InvalidOperationException("Edge tab launch plan is incorrect.");
            checks.Add("Tab launch preserves profile selection and excludes elevation and new-window flags");
            OpenEdgeTab(edge);
            await Task.Delay(1500);
            var after = tracker.Read(true).Where(window => ShellService.IsMicrosoftEdge(new DockItem { ExecutablePath = window.ExecutablePath, AppUserModelId = window.AppUserModelId })).ToArray();
            if (!after.Any(window => windows.Any(before => before.Handle == window.Handle)
                && (window.Title.Contains("New tab", StringComparison.OrdinalIgnoreCase) || window.Title.Contains("Nowa karta", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidOperationException("Could not confirm a new-tab page in an existing Edge window.");
            checks.Add("A real New tab page opened in an existing Edge window");
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "edge-new-tab-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Checks = checks }));
    }
}
