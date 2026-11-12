using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private Window? shortcutWindow;

    private void ShowNewShortcut()
    {
        if (shortcutWindow != null) { shortcutWindow.Activate(); return; }
        int insertion = backgroundMenuInsertionSlot ?? PinInsertionIndex();
        var window = new Window { Title = "Add new shortcut", SystemBackdrop = new MicaBackdrop() };
        shortcutWindow = window;
        window.Closed += (_, _) => shortcutWindow = null;
        IconResources.SetWindowIcon(window);
        double dpi = monitor.Scale;
        var panel = new StackPanel { Padding = new Thickness(16, 8, 16, 12), Spacing = 8 };
        var command = new TextBox { Header = "Command or location", PlaceholderText = "notepad.exe, ms-settings:, a URL, or a quoted path with arguments" };
        var name = new TextBox { Header = "Name (optional)", PlaceholderText = "Shortcut name" };
        var keepShellOpen = new CheckBox { Content = "Keep shell open" };
        ToolTipService.SetToolTip(keepShellOpen, "Keep PowerShell or Command Prompt open after the command finishes, including when launched through Windows Terminal.");
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed), Visibility = Visibility.Collapsed };
        panel.Children.Add(command); panel.Children.Add(keepShellOpen); panel.Children.Add(name); panel.Children.Add(error);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        string? chosenIcon = null;
        var assignIcon = new Button { Content = "Assign icon" };
        var iconPreview = new Image { Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center };
        var iconFilename = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1, VerticalAlignment = VerticalAlignment.Center };
        var iconDetails = new Grid { ColumnSpacing = 8, Margin = new Thickness(8, 0, 8, 0), Visibility = Visibility.Collapsed };
        iconDetails.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        iconDetails.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        iconDetails.Children.Add(iconPreview);
        Grid.SetColumn(iconFilename, 1);
        iconDetails.Children.Add(iconFilename);
        assignIcon.Click += async (_, _) =>
        {
            try
            {
                var picker = new Windows.Storage.Pickers.FileOpenPicker();
                foreach (var extension in new[] { ".ico", ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".svg", ".dll", ".exe" }) picker.FileTypeFilter.Add(extension);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));
                var file = await picker.PickSingleFileAsync();
                if (file == null || shortcutWindow != window) return;
                string? selected = await ShortcutIconPicker.Choose(file.Path, root.ActualTheme);
                if (selected == null || shortcutWindow != window) return;
                chosenIcon = selected;
                iconFilename.Text = file.Name;
                ToolTipService.SetToolTip(iconFilename, file.Path);
                ToolTipService.SetToolTip(iconPreview, file.Path);
                iconPreview.Source = null;
                iconDetails.Visibility = Visibility.Visible;
                ImageSource? preview;
                if (file.FileType.Equals(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    // Keep the SVG's intrinsic viewport and let the Image fit it into the preview.
                    var vector = new Microsoft.UI.Xaml.Media.Imaging.SvgImageSource();
                    using var stream = await file.OpenReadAsync();
                    var status = await vector.SetSourceAsync(stream);
                    preview = status == Microsoft.UI.Xaml.Media.Imaging.SvgImageSourceLoadStatus.Success ? vector : null;
                }
                else
                {
                    preview = await IconService.Load(selected, imageFile: true, requestedPixels: (int)Math.Ceiling(16 * dpi));
                }
                if (shortcutWindow == window && chosenIcon == selected) iconPreview.Source = preview;
            }
            catch (Exception ex) { Log.Write(ex); if (shortcutWindow == window) { error.Text = ex.Message; error.Visibility = Visibility.Visible; } }
        };
        var cancel = new Button { Content = "Cancel" };
        var add = new Button { Content = "OK", IsEnabled = false };
        int previewRevision = 0;
        command.TextChanged += async (_, _) =>
        {
            add.IsEnabled = !string.IsNullOrWhiteSpace(command.Text);
            error.Visibility = Visibility.Collapsed;
            if (chosenIcon != null) return;
            int revision = ++previewRevision;
            string text = command.Text;
            iconDetails.Visibility = Visibility.Collapsed;
            iconPreview.Source = null;
            if (!add.IsEnabled) return;
            try
            {
                string? path = await Task.Run(() => ShortcutCommand.DetectIcon(text));
                if (revision != previewRevision || chosenIcon != null || shortcutWindow != window || path == null) return;
                iconFilename.Text = Path.GetFileName(path);
                ToolTipService.SetToolTip(iconFilename, path);
                ToolTipService.SetToolTip(iconPreview, path);
                iconDetails.Visibility = Visibility.Visible;
                var preview = await IconService.Load(path, imageFile: true, requestedPixels: (int)Math.Ceiling(16 * dpi));
                if (revision == previewRevision && chosenIcon == null && shortcutWindow == window) iconPreview.Source = preview;
            }
            catch (ArgumentException) { /* The command may still be incomplete. */ }
            catch (Exception ex) { Log.Write(ex); }
        };
        cancel.Click += (_, _) => window.Close();
        add.Click += (_, _) =>
        {
            try
            {
                var shortcut = ShortcutCommand.Create(command.Text, name.Text, keepShellOpen.IsChecked == true);
                if (chosenIcon != null) shortcut.CustomIcon = chosenIcon;
                AddItem(shortcut, insertion, useNewIcon: true); window.Close();
            }
            catch (Exception ex) { error.Text = ex.Message; error.Visibility = Visibility.Visible; }
        };
        actions.Children.Add(cancel); actions.Children.Add(add);
        var footer = new Grid();
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.Children.Add(assignIcon);
        Grid.SetColumn(iconDetails, 1); footer.Children.Add(iconDetails);
        Grid.SetColumn(actions, 2); footer.Children.Add(actions);
        panel.Children.Add(footer);
        var chrome = new WindowChrome(window, panel, root.ActualTheme);
        bool windowClosed = false;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(window);
        if (window.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.IsMaximizable = false;
        Native.SetWindowSubclass(handle, Native.FixedHeightSubclass, 104, 0);
        window.Closed += (_, _) =>
        {
            windowClosed = true;
            Native.RemoveWindowSubclass(handle, Native.FixedHeightSubclass, 104);
        };
        void FitHeight()
        {
            if (windowClosed) return;
            chrome.Root.UpdateLayout();
            var client = Native.ClientBounds(handle);
            int frameHeight = window.AppWindow.Size.Height - client.Height;
            panel.Measure(new Windows.Foundation.Size(client.Width / dpi, double.PositiveInfinity));
            int height = (int)Math.Ceiling((32 + panel.DesiredSize.Height) * dpi) + frameHeight;
            if (panel.DesiredSize.Height < 150) return;
            int width = window.AppWindow.Size.Width;
            window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                monitor.Work.Left + (monitor.Work.Width - width) / 2,
                monitor.Work.Top + (monitor.Work.Height - height) / 2, width, height));
        }
        chrome.Root.Loaded += async (_, _) =>
        {
            try
            {
                FitHeight();
                var icon = await IconService.Load(IconResources.PathFor(BundledIcons.Application), imageFile: true, requestedPixels: 32);
                if (windowClosed) return;
                chrome.Icon.Source = icon;
                command.Focus(FocusState.Programmatic);
            }
            catch (Exception ex) { Log.Write(ex); }
        };
        window.Activate();
        int width = (int)Math.Round(600 * dpi), height = (int)Math.Round(295 * dpi);
        window.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            monitor.Work.Left + (monitor.Work.Width - width) / 2,
            monitor.Work.Top + (monitor.Work.Height - height) / 2, width, height));
        window.DispatcherQueue.TryEnqueue(FitHeight);
    }
}

internal static class ShortcutCommand
{
    private static (string Target, string Arguments) Parse(string command)
    {
        string value = Environment.ExpandEnvironmentVariables(command.Trim());
        if (value.Length == 0) throw new ArgumentException("Enter a command or location.");
        string target = value, arguments = "";
        if (value[0] == '"')
        {
            int end = value.IndexOf('"', 1);
            if (end < 0) throw new ArgumentException("Close the quotation mark around the path.");
            target = value[1..end]; arguments = value[(end + 1)..].TrimStart();
        }
        else if (!File.Exists(value) && !Directory.Exists(value) && !(Uri.TryCreate(value, UriKind.Absolute, out var location) && !location.IsFile))
        {
            int end = value.IndexOfAny([' ', '\t']);
            if (end >= 0) { target = value[..end]; arguments = value[(end + 1)..].TrimStart(); }
        }
        if (string.IsNullOrWhiteSpace(target)) throw new ArgumentException("Enter a command or location.");
        return (target, arguments);
    }

    internal static string? DetectIcon(string command) => ResolveIcon(Parse(command).Target);

    private static string? ResolveIcon(string target)
    {
        string path = ShortcutIconTarget.Resolve(target);
        return File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".lnk" ? path : null;
    }

    internal static DockItem Create(string command, string name, bool keepShellOpen = false)
    {
        var (target, arguments) = Parse(command);
        var item = ShellService.FromPath(target);
        string? iconTarget = ResolveIcon(target);
        item.CustomIcon = iconTarget ?? ShortcutIconTarget.DefaultIcon;
        if (iconTarget != null && Path.GetExtension(iconTarget).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            item.ExecutablePath = iconTarget;
        // Command aliases and URI handlers use the same Shell launch path as existing pins.
        if (!File.Exists(target) && !Directory.Exists(target)) item.Kind = ItemKind.Application;
        if (arguments.Length > 0) item.Arguments = arguments;
        if (!string.IsNullOrWhiteSpace(name)) item.Name = name.Trim();
        else if (Uri.TryCreate(target, UriKind.Absolute, out var uri) && !uri.IsFile) item.Name = target;
        if (string.IsNullOrWhiteSpace(item.Name)) item.Name = target;
        if (keepShellOpen)
        {
            item.Arguments = ShellKeepOpen.Arguments(string.IsNullOrWhiteSpace(item.ExecutablePath) ? item.Target : item.ExecutablePath, item.Arguments);
            // Editing a resolved .lnk's arguments must launch its executable,
            // rather than letting the original link discard the new flags.
            if (Path.GetExtension(item.Target).Equals(".lnk", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(item.ExecutablePath)) item.Target = item.ExecutablePath;
        }
        return item;
    }
}
