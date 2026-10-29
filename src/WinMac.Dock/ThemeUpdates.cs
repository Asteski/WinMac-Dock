using Microsoft.Win32;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI.ViewManagement;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
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
        if (Program.SeparatorAudit) return "separator-audit";
        using var theme = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        using var accent = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent");
        using var dwm = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
        return $"{theme?.GetValue("AppsUseLightTheme")}|{theme?.GetValue("SystemUsesLightTheme")}|{dwm?.GetValue("ColorizationColor")}|{dwm?.GetValue("AccentColor")}|{Convert.ToHexString(accent?.GetValue("AccentPalette") as byte[] ?? [])}";
    }
    private async Task CheckThemeRegressions()
    {
        var originalTheme = Settings.Theme;
        Exception? failure = null;
        var results = new List<object>();
        var probe = new Border { Width = 1, Height = 1, IsHitTestVisible = false, Style = (Style)Application.Current.Resources["DockAccentIndicatorStyle"] };
        root.Children.Add(probe);
        try
        {
            foreach (var theme in new[] { DockTheme.Light, DockTheme.Dark, DockTheme.Light, DockTheme.Dark })
            {
                Settings.Theme = theme;
                UpdateTheme();
                await Task.Delay(250);
                var actual = (probe.Background as SolidColorBrush)?.Color;
                var expected = uiSettings.GetColorValue(theme == DockTheme.Dark ? UIColorType.AccentLight2 : UIColorType.Accent);
                if (actual != expected) throw new InvalidOperationException($"{theme}: indicator {actual} differs from Windows shade {expected}");
                var chat = buttons.FirstOrDefault(b => b.Entry.Item.Name.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase)
                    || b.Entry.Item.AppUserModelId.StartsWith("OpenAI.ChatGPT-Desktop_", StringComparison.OrdinalIgnoreCase));
                Microsoft.UI.Xaml.Controls.Image? image = null;
                if (chat.Button != null && string.IsNullOrEmpty(chat.Entry.Item.CustomIcon))
                {
                    var expectedAsset = IconService.PackageIconAsset(IconService.PathFor(chat.Entry.Item), IconPixels, theme == DockTheme.Dark);
                    for (int attempt = 0; attempt < 50; attempt++)
                    {
                        var liveChat = buttons.FirstOrDefault(b => b.Entry.Item.Id == chat.Entry.Item.Id);
                        image = (liveChat.Icon as Grid)?.Children.OfType<Microsoft.UI.Xaml.Controls.Image>().FirstOrDefault();
                        if (image?.Source != null && (expectedAsset == null || IconService.Describe(image.Source)?.ToString()?.Contains(expectedAsset, StringComparison.OrdinalIgnoreCase) == true)) break;
                        await Task.Delay(100);
                    }
                    if (expectedAsset != null && IconService.Describe(image?.Source)?.ToString()?.Contains(expectedAsset, StringComparison.OrdinalIgnoreCase) != true)
                        throw new InvalidOperationException("Displayed ChatGPT icon did not change to " + expectedAsset + "; actual: " + IconService.Describe(image?.Source));
                }
                results.Add(new { Theme = theme.ToString(), Color = actual.ToString(), Expected = expected.ToString(), ChatGptRaster = IconService.Describe(image?.Source), RecycleIcon = IconService.RecycleIconLocation() });
            }
        }
        catch (Exception ex) { failure = ex; Log.Write(ex); }
        finally { root.Children.Remove(probe); Settings.Theme = originalTheme; UpdateTheme(); }
        File.WriteAllText(Path.Combine(Log.DirectoryPath, "theme-regressions-test.json"), System.Text.Json.JsonSerializer.Serialize(new { Passed = failure == null, Error = failure?.ToString(), Results = results }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }
}
