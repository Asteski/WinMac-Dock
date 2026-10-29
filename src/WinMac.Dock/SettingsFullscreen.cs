using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal sealed partial class SettingsWindow
{
    private async Task EditFullscreenExclusions()
    {
        if (dialogOpen) return;
        dialogOpen = true;
        try
        {
            var rules = new System.Collections.ObjectModel.ObservableCollection<string>(dock.Settings.FullscreenExclusions);
            var list = new ListView { ItemsSource = rules, Height = 155, SelectionMode = ListViewSelectionMode.Single };
            var running = dock.RunningEntries.Where(e => e.Windows.Count > 0)
                .Select(e => new { e.Item.Name, Identity = FullscreenExclusions.Identity(e.Item) })
                .Where(e => !string.IsNullOrWhiteSpace(e.Identity)).DistinctBy(e => e.Identity, StringComparer.OrdinalIgnoreCase).OrderBy(e => e.Name).ToArray();
            var apps = new ComboBox { Header = "Running application", ItemsSource = running, DisplayMemberPath = "Name", HorizontalAlignment = HorizontalAlignment.Stretch };
            var addApp = new Button { Content = "Add selected application" };
            void Add(string value) { value = value.Trim().Trim('"'); if (value.Length > 0 && !rules.Contains(value, StringComparer.OrdinalIgnoreCase)) rules.Add(value); }
            addApp.Click += (_, _) => { if (apps.SelectedIndex >= 0) Add(running[apps.SelectedIndex].Identity); };
            var manual = new TextBox { Header = "Executable name, full path, or app ID", PlaceholderText = "Example: vlc.exe", HorizontalAlignment = HorizontalAlignment.Stretch };
            var addManual = new Button { Content = "Add" };
            addManual.Click += (_, _) => { Add(manual.Text); manual.Text = ""; };
            var remove = new Button { Content = "Remove selected", IsEnabled = false };
            list.SelectionChanged += (_, _) => remove.IsEnabled = list.SelectedItem != null;
            remove.Click += (_, _) => { if (list.SelectedItem is string rule) rules.Remove(rule); };
            var body = new StackPanel { Spacing = 10, MinWidth = 380 };
            body.Children.Add(new TextBlock { Text = "Keep the dock visible while these applications are fullscreen. Manual hiding still works. Executable names match any installation; full paths and app IDs match that specific application.", TextWrapping = TextWrapping.Wrap });
            body.Children.Add(list); body.Children.Add(remove); body.Children.Add(apps); body.Children.Add(addApp); body.Children.Add(manual); body.Children.Add(addManual);
            var dialog = new ContentDialog { XamlRoot = ((FrameworkElement)Content).XamlRoot, Title = "Fullscreen exclusions", Content = new ScrollViewer { Content = body, MaxHeight = 520 }, PrimaryButtonText = "Save", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Primary };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                Add(manual.Text);
                dock.Settings.FullscreenExclusions = rules.ToList(); PreviewSettings();
            }
        }
        finally { dialogOpen = false; }
    }
}
