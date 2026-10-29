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
            }
            catch (Exception ex) { Log.Write(ex); if (shortcutWindow == window) { error.Text = ex.Message; error.Visibility = Visibility.Visible; } }
        };
        var cancel = new Button { Content = "Cancel" };
        var add = new Button { Content = "OK", IsEnabled = false };
        command.TextChanged += (_, _) => { add.IsEnabled = !string.IsNullOrWhiteSpace(command.Text); error.Visibility = Visibility.Collapsed; };
        cancel.Click += (_, _) => window.Close();
        add.Click += (_, _) =>
        {
            try
            {
                var shortcut = ShortcutCommand.Create(command.Text, name.Text, keepShellOpen.IsChecked == true);
                if (chosenIcon != null) shortcut.CustomIcon = chosenIcon;
                AddItem(shortcut, insertion); window.Close();
            }
            catch (Exception ex) { error.Text = ex.Message; error.Visibility = Visibility.Visible; }
        };
        actions.Children.Add(cancel); actions.Children.Add(add);
        var footer = new Grid();
        assignIcon.HorizontalAlignment = HorizontalAlignment.Left;
        footer.Children.Add(assignIcon); footer.Children.Add(actions); panel.Children.Add(footer);
        var chrome = new WindowChrome(window, panel, root.ActualTheme);
        bool windowClosed = false;
        window.Closed += (_, _) => windowClosed = true;
        void FitHeight()
        {
            if (windowClosed) return;
            chrome.Root.UpdateLayout();
            var client = Native.ClientBounds(WinRT.Interop.WindowNative.GetWindowHandle(window));
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
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern uint SearchPathW(string? path, string file, string? extension, int length, System.Text.StringBuilder buffer, nint filePart);

    private static string IconTarget(string target)
    {
        if (File.Exists(target)) return Path.GetFullPath(target);
        if (target.IndexOfAny(['\\', '/', ':']) >= 0) return target;
        string extension = Path.GetExtension(target);
        if (extension.Length != 0 && !extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) && !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return target;
        var buffer = new System.Text.StringBuilder(32768);
        uint length = SearchPathW(null, target, extension.Length == 0 ? ".exe" : null, buffer.Capacity, buffer, 0);
        if (length > 0 && length < buffer.Capacity && File.Exists(buffer.ToString())) return buffer.ToString();
        // ShellExecute also recognizes application registrations outside PATH.
        string app = extension.Length == 0 ? target + ".exe" : target;
        foreach (var hive in new[] { Microsoft.Win32.Registry.CurrentUser, Microsoft.Win32.Registry.LocalMachine })
        {
            using var key = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + app);
            string? registered = (key?.GetValue(null) as string)?.Trim('"');
            if (!string.IsNullOrWhiteSpace(registered))
            {
                registered = Environment.ExpandEnvironmentVariables(registered);
                if (File.Exists(registered)) return registered;
            }
        }
        return target;
    }
    internal static string DefaultIcon => Path.Combine(Environment.SystemDirectory, "shell32.dll") + ",-16769";
    internal static DockItem Create(string command, string name, bool keepShellOpen = false)
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
        var item = ShellService.FromPath(target);
        string iconTarget = IconTarget(target);
        item.CustomIcon = File.Exists(iconTarget) && Path.GetExtension(iconTarget).ToLowerInvariant() is ".exe" or ".lnk" ? iconTarget : DefaultIcon;
        if (string.IsNullOrWhiteSpace(item.ExecutablePath) && File.Exists(iconTarget) && Path.GetExtension(iconTarget).Equals(".exe", StringComparison.OrdinalIgnoreCase)) item.ExecutablePath = iconTarget;
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
