namespace WinMac.Dock.Core;

// One activation per deliberate edge contact. A wider release band prevents
// tiny cursor movements at the boundary from repeatedly toggling the dock.
public sealed class EdgeActivation
{
    private bool armed;
    private DateTime? entered;
    public void Reset() { armed = false; entered = null; }
    public bool Update(bool touching, bool released, bool blocked, DateTime now, int delayMs)
    {
        if (blocked) { Reset(); return false; }
        if (released) { armed = true; entered = null; return false; }
        if (!touching) { entered = null; return false; }
        if (!armed) return false;
        entered ??= now;
        if ((now - entered.Value).TotalMilliseconds < delayMs) return false;
        armed = false; entered = null; return true;
    }
}

public static class DockStartup
{
    public static bool Hidden(DockSettings settings, string command, HideMode? hideMode = null) => command == "--hide"
        || (command == "--startup" && settings.StartHidden && (hideMode ?? settings.HideMode) != HideMode.AlwaysVisible);
}
