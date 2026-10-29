using Microsoft.UI.Xaml.Controls;
using System.Runtime.InteropServices;

namespace WinMac.Dock;

internal static partial class ShellService
{
    internal static bool IsSnippingTool(DockItem item) =>
        item.AppUserModelId.StartsWith("Microsoft.ScreenSketch_", StringComparison.OrdinalIgnoreCase)
        || item.Target.StartsWith(@"shell:AppsFolder\Microsoft.ScreenSketch_", StringComparison.OrdinalIgnoreCase)
        || item.Target.StartsWith("ms-screensketch:", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(item.Target).Equals("SnippingTool.exe", StringComparison.OrdinalIgnoreCase)
        || Path.GetFileName(item.ExecutablePath).Equals("SnippingTool.exe", StringComparison.OrdinalIgnoreCase);

    [ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId, [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    }

    internal static void LaunchSnippingTask(string arguments)
    {
        object? manager = null;
        try
        {
            manager = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"))!);
            Marshal.ThrowExceptionForHR(((IApplicationActivationManager)manager!).ActivateApplication("Microsoft.ScreenSketch_8wekyb3d8bbwe!App", arguments, 0, out _));
        }
        finally { Release(manager); }
    }

    internal static void EjectDrive(string path)
    {
        var root = Path.GetPathRoot(Path.GetFullPath(path));
        if (root == null || !string.Equals(root.TrimEnd('\\'), path.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Only a drive root can be ejected.", nameof(path));
        object? shell = null, folder = null, drive = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder = ((dynamic)shell!).NameSpace(17);
            drive = ((dynamic)folder!).ParseName(root);
            if (drive == null) throw new IOException("The drive is no longer connected.");
            ((dynamic)drive).InvokeVerb("Eject");
        }
        finally { Release(drive); Release(folder); Release(shell); }
    }
}

internal sealed partial class DockWindow
{
    // The newer capture API requires MSIX identity and a callback; use the
    // installed app's interactive launch commands for this unpackaged dock.
    private static readonly (string Label, string Target, string? Glyph)[] snippingToolCommands =
    [
        ("Open Screenshots folder", "open-screenshots-folder", null),
        ("Open Screen recordings folder", "open-recordings-folder", null),
        ("New screenshot", "new-snip", "\uE722"),
        ("Screenshot in 3 seconds", "new-snip-short", "\uE916"),
        ("Screenshot in 5 seconds", "new-snip-medium", "\uE916"),
        ("Screenshot in 10 seconds", "new-snip-long", "\uE916"),
        ("Record screen", "ms-screenclip:?type=recording", "\uE714"),
        ("Extract text", "ms-screenclip:?type=textExtractor", "\uE8D2")
    ];

    private void AddSnippingToolCommands(MenuFlyout menu)
    {
        foreach (var command in snippingToolCommands)
        {
            if (command.Target == "new-snip") menu.Items.Add(new MenuFlyoutSeparator());
            Menu(menu, command.Label, () => TryAction(() =>
            {
                if (command.Target is "open-screenshots-folder" or "open-recordings-folder")
                {
                    var paths = SnippingToolSettings.Read();
                    string folder = command.Target == "open-screenshots-folder" ? paths.Screenshots : paths.Recordings;
                    Directory.CreateDirectory(folder);
                    ShellService.Open(folder);
                }
                else if (command.Target.StartsWith("new-snip", StringComparison.Ordinal)) ShellService.LaunchSnippingTask(command.Target);
                else ShellService.Open(command.Target);
            }), command.Glyph);
        }
    }
}
