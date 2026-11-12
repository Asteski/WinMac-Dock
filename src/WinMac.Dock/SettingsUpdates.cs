using System.Net.Http;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;

internal sealed partial class SettingsWindow
{
    private const string DockReleaseApi = "https://api.github.com/repos/Asteski/WinMac-Dock/releases?per_page=20";
    private const string DockReleasePage = "https://github.com/Asteski/WinMac-Dock/releases";
    private static readonly HttpClient updateClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private void AddVersionAndUpdatesRow()
    {
        var check = new Button
        {
            Content = "Check for updates",
            HorizontalAlignment = HorizontalAlignment.Right,
            Padding = new Thickness(12, 5, 12, 5)
        };
        if (Application.Current.Resources.TryGetValue("AccentButtonStyle", out var accentStyle) && accentStyle is Style style)
            check.Style = style;
        else
        {
            check.Background = AccentBrush();
            if (Application.Current.Resources.TryGetValue("TextOnAccentFillColorPrimaryBrush", out var accentText)
                && accentText is Microsoft.UI.Xaml.Media.Brush brush)
                check.Foreground = brush;
        }

        var status = new TextBlock
        {
            Visibility = Visibility.Collapsed,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = HorizontalAlignment.Right,
            MaxWidth = 220,
            Opacity = .75
        };
        var details = new Button { Content = "View release notes", Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Right };
        details.Click += (_, _) => dock.TryAction(() => ShellService.Open(DockReleasePage));
        var actions = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(check);
        actions.Children.Add(status);
        actions.Children.Add(details);

        var latestVersion = new TextBlock { Text = "Latest version: Not checked", Opacity = .75, TextWrapping = TextWrapping.Wrap };
        var versions = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Top };
        versions.Children.Add(new TextBlock { Text = DockWindow.BetaVersion });
        versions.Children.Add(latestVersion);
        var row = new Grid { ColumnSpacing = 20 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(versions);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);

        var controls = new StackPanel { Tag = "Installed version", Spacing = 12 };
        controls.Children.Add(row);
        controls.Children.Add(new CheckBox { Content = "Download and install updates automatically", IsEnabled = false });
        controls.Children.Add(new TextBlock
        {
            Text = "Automatic installation is unavailable in this build.",
            Opacity = .65,
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        var folder = new Button { Content = "Open configuration folder", HorizontalAlignment = HorizontalAlignment.Left };
        folder.Click += (_, _) => dock.TryAction(() =>
        {
            Directory.CreateDirectory(Log.DirectoryPath);
            ShellService.Open(Log.DirectoryPath);
        });
        controls.Children.Add(folder);
        panel.Children.Add(controls);

        check.Click += async (_, _) =>
        {
            check.IsEnabled = false;
            status.Text = "Checking for updates...";
            status.Visibility = Visibility.Visible;
            details.Visibility = Visibility.Collapsed;
            try
            {
                var latest = await ReadLatestDockVersion();
                var installed = typeof(SettingsWindow).Assembly.GetName().Version;
                if (installed == null) throw new InvalidOperationException("The installed version is unavailable.");
                var current = new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
                latestVersion.Text = $"Latest version: {latest}";
                bool available = latest > current;
                status.Text = available ? $"WinMac Dock {latest} is available." : "You're up to date.";
                if (available) details.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log.Write(ex);
                status.Text = "Couldn't check for updates. Try again later.";
            }
            finally { check.IsEnabled = true; }
        };
    }

    private static async Task<Version> ReadLatestDockVersion()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, DockReleaseApi);
        request.Headers.UserAgent.ParseAdd("WinMac-Dock");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await updateClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var releases = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

        Version? latest = null;
        foreach (var release in releases.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            string? tag = release.GetProperty("tag_name").GetString();
            if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V').Split('-', '+')[0], out var parsed)) continue;
            var version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
            if (latest == null || version > latest) latest = version;
        }
        return latest ?? throw new InvalidDataException("GitHub did not return a versioned WinMac Dock release.");
    }
}
