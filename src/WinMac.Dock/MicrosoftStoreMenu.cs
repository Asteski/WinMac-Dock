using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private static readonly FontFamily storeFont = new("ms-appx:///assets/fonts/StoreIcons.ttf#Store Fluent Icons");
    private static readonly (string Label, string Uri, string Glyph)[] storeCategories =
    [
        ("Home", "ms-windows-store://home", "\uE80F"),
        ("Apps", "ms-windows-store://navigatetopage/?id=productivity&ocid=jumplist", "\uE74C"),
        ("Gaming", "ms-windows-store://navigatetopage/?id=gaming&ocid=jumplist", "\uE42D"),
        ("Library", "ms-windows-store://mylibrary?ocid=jumplist", "\uE6AA"),
        ("Downloads", "ms-windows-store://downloadsAndUpdates?ocid=jumplist", "\uEBD3")
    ];
    private void AddStoreCategories(MenuFlyout menu)
    {
        foreach (var category in storeCategories)
        {
            var icon = Glyph(category.Glyph);
            if (category.Label is "Gaming" or "Library") icon.FontFamily = xboxFont;
            var command = new MenuFlyoutItem { Text = category.Label, Icon = icon };
            command.Click += (_, _) => TryAction(() => ShellService.Open(category.Uri));
            menu.Items.Add(command);
        }
        menu.Items.Add(new MenuFlyoutSeparator());
    }
    private void InvokeSingletonMenu(DockEntry entry)
    {
        TryAction(() =>
        {
            var windows = entry.Windows.Select(w => tracker.Describe(w.Handle)).OfType<TaskWindow>().ToArray();
            var target = windows.FirstOrDefault(w => !Native.IsIconic(w.Handle)) ?? windows.FirstOrDefault();
            if (target == null) { LaunchMenuItem(entry.Item); return; }
            ShellService.Activate(target, !Native.IsIconic(target.Handle));
            var current = buttons.FirstOrDefault(b => b.Entry.Item.Id == entry.Item.Id);
            if (current.Button != null) PulseActivatedItem(current.Icon);
        });
    }
}
