using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinMac.Dock;
internal sealed partial class DockWindow
{
    private int requestedTaskViewBand;
    private void RequestTaskViewBand(int band)
    {
        if (requestedTaskViewBand == band) return;
        
        Native.SetPropW(Handle, "WinMac.Dock.TaskViewBand", band);
        if (materialHandle != 0) Native.SetPropW(materialHandle, "WinMac.Dock.TaskViewBand", band);
        requestedTaskViewBand = band;
        // Wake the existing Explorer-owned broker immediately. Waiting for
        // its 200ms fallback timer leaves the dock covered at Task View entry.
        var broker = Native.FindWindowExW(-3, 0, "WinMac.Dock.TaskViewBroker.V1", null);
        if (broker != 0) Native.PostMessage(broker, 0x0113, 1, 0);
    }
}
