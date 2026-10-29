namespace WinMac.Dock.Core;

public interface ITaskbarConfiguration
{
    uint State { get; set; }
    uint Edge { get; set; }
}

public sealed record TaskbarConfiguration(uint State, uint Edge)
{
    public static TaskbarConfiguration Capture(ITaskbarConfiguration taskbar) => new(taskbar.State, taskbar.Edge);

    public void PrepareToHide(ITaskbarConfiguration taskbar)
    {
        // Auto-hide releases Explorer's work-area reservation. Side/top taskbars
        // must first reach the bottom before Windows can apply it reliably.
        if (taskbar.Edge != 3) taskbar.Edge = 3;
        if ((taskbar.State & 1) == 0) taskbar.State |= 1;
        if (taskbar.Edge != 3 || (taskbar.State & 1) == 0)
            throw new InvalidOperationException("Windows could not move the taskbar to the bottom and enable auto-hide. The taskbar has been restored.");
    }

    public void Restore(ITaskbarConfiguration taskbar)
    {
        // Disable temporary auto-hide while still at the bottom; restore a side
        // placement before reinstating the original auto-hide preference.
        if (taskbar.Edge != Edge && (taskbar.State & 1) != 0) taskbar.State &= ~1u;
        if (taskbar.Edge != Edge) taskbar.Edge = Edge;
        if (taskbar.State != State) taskbar.State = State;
        if (taskbar.Edge != Edge || taskbar.State != State)
            throw new InvalidOperationException("Windows has not restored the original taskbar settings. Recovery information has been retained.");
    }
}
