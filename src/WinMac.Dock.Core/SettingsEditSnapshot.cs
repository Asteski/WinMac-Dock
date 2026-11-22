using System.Text.Json;

namespace WinMac.Dock.Core;

// Preferences are transactional; the live pin list belongs to the running dock.
public sealed class SettingsEditSnapshot(DockSettings settings)
{
    private readonly string baseline = JsonSerializer.Serialize(settings);
    public bool StartAtLogin => JsonSerializer.Deserialize<DockSettings>(baseline)!.StartAtLogin;

    public DockSettings WithoutPendingEdits(DockSettings current)
    {
        var saved = JsonSerializer.Deserialize<DockSettings>(baseline)!;
        var originalItems = saved.Items.ToDictionary(item => item.Id);
        // Preserve additions, removals and ordering, including automatic pins.
        saved.Items = current.Items.Select(item => !item.IsDownloadedPin && !item.IsAutoUsbPin
            && originalItems.TryGetValue(item.Id, out var original) ? original
            : JsonSerializer.Deserialize<DockItem>(JsonSerializer.Serialize(item))!).ToList();
        return saved;
    }

    public bool HasChanges(DockSettings current) =>
        JsonSerializer.Serialize(current) != JsonSerializer.Serialize(WithoutPendingEdits(current));

    public void Restore(DockSettings current)
    {
        var saved = WithoutPendingEdits(current);
        foreach (var property in typeof(DockSettings).GetProperties().Where(property => property.CanWrite))
            property.SetValue(current, property.GetValue(saved));
    }
}
