using System.Net.Http;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;

internal sealed partial class SettingsWindow
{
    private NavigationView? settingsNavigation;
    private Func<Task>? checkForUpdates;

    internal void CheckForUpdates()
    {
        if (settingsNavigation is { } nav) nav.SelectedItem = nav.MenuItems[0];
        if (checkForUpdates is { } check) _ = check();
    }

    private const string DockReleaseApi = "https://api.github.com/repos/Asteski/WinMac-Dock/releases?per_page=20";
    private const string DockReleasePage = "https://github.com/Asteski/WinMac-Dock/releases";
    private static readonly HttpClient updateClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    private static Microsoft.UI.Xaml.Media.SolidColorBrush StatusBrush(ElementTheme theme, int light, int dark)
    {
        int rgb = theme == ElementTheme.Dark ? dark : light;
        return new(Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
    }

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
            MaxWidth = 220
        };
        int lightStatus = 0x005FB8, darkStatus = 0x60CDFF;
        void UpdateStatusColor() => status.Foreground = StatusBrush(status.ActualTheme, lightStatus, darkStatus);
        status.ActualThemeChanged += (_, _) => UpdateStatusColor();
        void ShowStatus(string message, int light, int dark)
        {
            lightStatus = light;
            darkStatus = dark;
            status.Text = message;
            UpdateStatusColor();
        }
        var details = new Button { Content = "View release notes", Visibility = Visibility.Collapsed, HorizontalAlignment = HorizontalAlignment.Right };
        details.Click += (_, _) => dock.TryAction(() => ShellService.Open(DockReleasePage));
        var actions = new StackPanel { Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(check);
        actions.Children.Add(status);
        actions.Children.Add(details);

        var latestVersion = new TextBlock { Text = "Latest version: Not checked", Opacity = .75, TextWrapping = TextWrapping.Wrap };
        var versions = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Top };
        versions.Children.Add(new TextBlock { Text = $"Current version: {DockWindow.DisplayVersion}" });
        versions.Children.Add(latestVersion);
        var row = new Grid { ColumnSpacing = 20 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(versions);
        Grid.SetColumn(actions, 1);
        row.Children.Add(actions);

        var controls = new StackPanel { Tag = "Installed version", Spacing = 12, Padding = new Thickness(0, 8, 0, 8) };
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

        async Task Check()
        {
            if (!check.IsEnabled) return;
            check.IsEnabled = false;
            ShowStatus("Checking for updates...", 0x005FB8, 0x60CDFF);
            status.Visibility = Visibility.Visible;
            details.Visibility = Visibility.Collapsed;
            try
            {
                var latest = await ReadLatestDockVersion();
                var installed = typeof(SettingsWindow).Assembly.GetName().Version;
                if (installed == null) throw new InvalidOperationException("The installed version is unavailable.");
                var current = new Version(installed.Major, installed.Minor, Math.Max(0, installed.Build));
                bool prerelease = DockWindow.DisplayVersion.Contains(" RC", StringComparison.OrdinalIgnoreCase)
                    || DockWindow.DisplayVersion.Contains("beta", StringComparison.OrdinalIgnoreCase);
                string label = latest.Number + (latest.Prerelease ? " (prerelease)" : "");
                latestVersion.Text = $"Latest version: {label}";
                bool available = latest.CompareTo(new DockReleaseVersion(current, prerelease)) > 0;
                if (available) ShowStatus($"WinMac Dock {label} is available.", 0x974600, 0xFFB366);
                else ShowStatus("You're up to date.", 0x0F6D32, 0x6CCB80);
                if (available) details.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Log.Write(ex);
                ShowStatus("Couldn't check for updates. Try again later.", 0xA4262C, 0xFF99A4);
            }
            finally { check.IsEnabled = true; }
        }
        checkForUpdates = Check;
        check.Click += async (_, _) => await Check();
    }

    private static async Task<DockReleaseVersion> ReadLatestDockVersion()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, DockReleaseApi);
        request.Headers.UserAgent.ParseAdd("WinMac-Dock");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await updateClient.SendAsync(request).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        using var releases = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));

        DockReleaseVersion? latest = null;
        foreach (var release in releases.RootElement.EnumerateArray())
        {
            if (release.GetProperty("draft").GetBoolean()) continue;
            string? tag = release.GetProperty("tag_name").GetString();
            if (!DockReleaseVersion.TryParse(tag, release.GetProperty("prerelease").GetBoolean(), out var version)) continue;
            if (latest == null || version.CompareTo(latest.Value) > 0) latest = version;
        }
        return latest ?? throw new InvalidDataException("GitHub did not return a versioned WinMac Dock release.");
    }
}
