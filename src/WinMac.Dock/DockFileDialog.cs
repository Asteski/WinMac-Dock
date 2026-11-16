using Forms = System.Windows.Forms;

namespace WinMac.Dock;

internal static class DockFileDialog
{
    private sealed class Owner(nint handle) : Forms.IWin32Window
    {
        public nint Handle { get; } = handle;
    }

    internal static string? PickFile(nint owner, bool iconsOnly = false, bool audioOnly = false)
    {
        using var dialog = new Forms.OpenFileDialog
        {
            Filter = audioOnly ? "Audio files (*.wav, *.mp3)|*.wav;*.mp3" : iconsOnly ? "Icon and image files|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp;*.svg;*.dll;*.exe" : "All files|*.*",
            RestoreDirectory = true
        };
        return dialog.ShowDialog(new Owner(owner)) == Forms.DialogResult.OK ? dialog.FileName : null;
    }

    internal static string? PickFolder(nint owner)
    {
        using var dialog = new Forms.FolderBrowserDialog();
        return dialog.ShowDialog(new Owner(owner)) == Forms.DialogResult.OK ? dialog.SelectedPath : null;
    }
}
