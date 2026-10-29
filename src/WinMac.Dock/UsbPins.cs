using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinMac.Dock;

internal sealed partial class DockWindow
{
    private readonly UsbPinLayout usbPins = new();
    private readonly HashSet<string> knownUsbPins = [], pendingUsbBounces = [];
    private readonly HashSet<string> pendingModuleBounces = [];
    private readonly HashSet<Microsoft.UI.Xaml.Controls.Grid> queuedUsbBounces = [];
    private bool usbPinsObserved;

    private void QueueUsbAppearanceBounce(DockItem item, Microsoft.UI.Xaml.Controls.Image image, Microsoft.UI.Xaml.Controls.Grid icon)
    {
        if (closing || image.Source == null || !(pendingUsbBounces.Contains(item.Id) || pendingModuleBounces.Contains(item.Id)) || !queuedUsbBounces.Add(icon)) return;
        _ = BounceUsbAppearance(item, image, icon);
    }

    private async Task BounceUsbAppearance(DockItem item, Microsoft.UI.Xaml.Controls.Image image, Microsoft.UI.Xaml.Controls.Grid icon)
    {
        try
        {
            // Match downloaded-file feedback: wait for the loaded icon and
            // row resize to finish, then consume this appearance exactly once.
            await Task.Yield();
            bool Current() => !closing && image.Source != null && Settings.Items.Any(i => i.Id == item.Id)
                && buttons.Any(b => b.Entry.Item.Id == item.Id && b.Icon == icon);
            while (Current() && (!icon.IsLoaded || rowAnimating || rowStaging || rowFinishing || reconciling || positioning || hidden || visibilitySliding || popupCount > 0))
                await Task.Delay(32);
            if (!Current()) return;
            root.UpdateLayout();
            bool moduleAppearance = pendingModuleBounces.Remove(item.Id);
            if (pendingUsbBounces.Remove(item.Id) || moduleAppearance) Bounce(icon, appearance: true);
        }
        catch (Exception ex) { Log.Write(ex); }
        finally { queuedUsbBounces.Remove(icon); }
    }
    private readonly Microsoft.UI.Xaml.DispatcherTimer usbPinTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool usbTimerHooked, scanningUsb;
    private bool UsbPinningEnabled => Settings.Items.Any(i => IsFileExplorer(i) && i.PinUsbDrives);

    private void SyncUsbPinning()
    {
        if (Program.SeparatorAudit) return;
        var current = Settings.Items.Where(i => i.IsAutoUsbPin).Select(i => i.Id).ToHashSet();
        if (usbPinsObserved)
            foreach (var id in current.Where(id => !knownUsbPins.Contains(id))) pendingUsbBounces.Add(id);
        knownUsbPins.Clear(); knownUsbPins.UnionWith(current); usbPinsObserved = true;
        pendingUsbBounces.RemoveWhere(id => !current.Contains(id));
        if (MonitorOverride != null) return;
        if (!usbTimerHooked)
        {
            usbTimerHooked = true;
            usbPinTimer.Tick += async (_, _) => await ScanUsbPins();
        }
        if (UsbPinningEnabled || Settings.Items.Any(i => i.IsAutoUsbPin))
        {
            if (!usbPinTimer.IsEnabled) { usbPinTimer.Start(); _ = ScanUsbPins(); }
        }
        else { usbPinTimer.Stop(); usbPins.Update(Settings.Items, [], new HashSet<string>(StringComparer.OrdinalIgnoreCase), false); }
    }

    private async Task ScanUsbPins()
    {
        if (closing || MonitorOverride != null || scanningUsb) return;
        if (!UsbPinningEnabled && !Settings.Items.Any(i => i.IsAutoUsbPin)) return;
        scanningUsb = true;
        try
        {
            var snapshot = await Task.Run(() =>
            {
                var drives = DriveInfo.GetDrives();
                var present = drives.Select(d => d.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var usb = new List<UsbVolume>();
                foreach (var drive in drives)
                {
                    try
                    {
                        if (drive.DriveType is not (DriveType.Fixed or DriveType.Removable or DriveType.CDRom) || !TryReadAttachedVolume(drive.Name, out var kind) || !drive.IsReady) continue;
                        string label = drive.VolumeLabel;
                        string defaultName = kind switch { AttachedDriveKind.MemoryCard => "Memory card", AttachedDriveKind.Optical => "Optical drive", AttachedDriveKind.ExternalDisk => "External disk", _ => "USB drive" };
                        usb.Add(new(drive.Name, string.IsNullOrWhiteSpace(label) ? $"{defaultName} ({drive.Name[..2]})" : $"{label} ({drive.Name[..2]})", kind));
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                return (present, usb);
            });
            if (closing) return;
            if (usbPins.Update(Settings.Items, snapshot.usb, snapshot.present, UsbPinningEnabled)) { Save(); Reconcile(); }
            if (!UsbPinningEnabled) usbPinTimer.Stop();
        }
        catch (IOException ex) { Log.Write(ex); }
        catch (UnauthorizedAccessException ex) { Log.Write(ex); }
        finally { scanningUsb = false; }
    }

    private static bool TryReadAttachedVolume(string root, out AttachedDriveKind kind)
    {
        // Query the storage bus and peripheral type; USB hard disks often report DriveType.Fixed.
        // Zero access plus full sharing avoids holding up removal or requesting elevation.
        kind = AttachedDriveKind.ExternalDisk;
        using var device = OpenUsbVolume(@"\\.\" + root[..2], 0, 7, 0, 3, 0, 0);
        if (device.IsInvalid) return false;
        byte[] query = new byte[12], descriptor = new byte[1024];
        if (!QueryUsbVolume(device, 0x002D1400, query, query.Length, descriptor, descriptor.Length, out int read, 0) || read < 32) return false;
        string TextAt(int field)
        {
            int start = BitConverter.ToInt32(descriptor, field);
            if (start < 36 || start >= read) return "";
            int end = start;
            while (end < read && descriptor[end] != 0) end++;
            return System.Text.Encoding.ASCII.GetString(descriptor, start, end - start);
        }
        var detected = AttachedDriveDetection.Classify(BitConverter.ToInt32(descriptor, 28), descriptor[8], descriptor[10] != 0,
            TextAt(12) + " " + TextAt(16));
        if (detected == null) return false;
        kind = detected.Value;
        return true;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle OpenUsbVolume(string path, uint access, uint share, nint security, uint disposition, uint flags, nint template);
    [DllImport("kernel32.dll", EntryPoint = "DeviceIoControl", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryUsbVolume(SafeFileHandle device, uint code, byte[] input, int inputSize, [Out] byte[] output, int outputSize, out int returned, nint overlapped);
}
