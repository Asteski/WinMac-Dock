namespace WinMac.Dock.Core;

public enum AttachedDriveKind { ExternalDisk, UsbFlashDrive, MemoryCard, Optical }

public static class AttachedDriveDetection
{
    public static AttachedDriveKind? Classify(int busType, int deviceType, bool removableMedia, string model)
    {
        if (busType is 12 or 13) return AttachedDriveKind.MemoryCard; // SD / MMC
        if (busType != 7) return null; // Only USB devices otherwise.
        if (deviceType == 5) return AttachedDriveKind.Optical; // SCSI CD-ROM device
        if (deviceType != 0) return null; // Exclude non-disk peripheral devices.
        if (new[] { "card reader", "cardreader", "sd/mmc", "sd card", "microsd", "compactflash" }
            .Any(hint => model.Contains(hint, StringComparison.OrdinalIgnoreCase))) return AttachedDriveKind.MemoryCard;
        return removableMedia ? AttachedDriveKind.UsbFlashDrive : AttachedDriveKind.ExternalDisk;
    }
}

public sealed record UsbVolume(string Root, string Name, AttachedDriveKind Kind = AttachedDriveKind.ExternalDisk);

// Remember connections so a manually unpinned automatic item stays unpinned
// until that drive is reconnected. Never take ownership of existing pins.
public sealed class UsbPinLayout
{
    private readonly HashSet<string> observed = new(StringComparer.OrdinalIgnoreCase);
    public bool Update(List<DockItem> items, IReadOnlyList<UsbVolume> usb, IReadOnlySet<string> present, bool enabled)
    {
        bool changed = items.RemoveAll(i => i.IsAutoUsbPin && (!enabled || !present.Contains(i.Target))) > 0;
        observed.RemoveWhere(root => !present.Contains(root));
        if (!enabled) { observed.Clear(); return changed; }
        foreach (var drive in usb)
        {
            var existing = items.FirstOrDefault(i => i.IsAutoUsbPin && string.Equals(i.Target, drive.Root, StringComparison.OrdinalIgnoreCase));
            if (existing != null && existing.AttachedDriveKind != drive.Kind)
            {
                existing.AttachedDriveKind = drive.Kind;
                changed = true;
            }
            if (!observed.Add(drive.Root) || items.Any(i => string.Equals(i.Target, drive.Root, StringComparison.OrdinalIgnoreCase))) continue;
            items.Add(new DockItem { Kind = ItemKind.Folder, Target = drive.Root, Name = drive.Name, IsAutoUsbPin = true, AttachedDriveKind = drive.Kind });
            changed = true;
        }
        return changed;
    }
}
