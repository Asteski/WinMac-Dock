namespace WinMac.Dock.Core;

public static class ModulePlacement
{
    public static int InsertionIndex(IReadOnlyList<DockItem> items, ItemKind kind)
    {
        int start = -1, peer = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Kind == ItemKind.StartMenu) start = i;
            if (items[i].Kind == (kind == ItemKind.Search ? ItemKind.TaskView : ItemKind.Search)) peer = i;
        }
        if (start >= 0)
            return start + 1 + (kind == ItemKind.TaskView && start + 1 < items.Count && items[start + 1].Kind == ItemKind.Search ? 1 : 0);
        return peer >= 0 ? peer + (kind == ItemKind.TaskView ? 1 : 0) : 0;
    }

    public static void Pin(List<DockItem> items, DockItem item)
    {
        if (item.Kind is not (ItemKind.Search or ItemKind.TaskView))
            throw new ArgumentException("Only Search and Task View use module placement.", nameof(item));
        // Place only the new pin; never relocate an existing module.
        items.Insert(InsertionIndex(items, item.Kind), item);
    }
}
