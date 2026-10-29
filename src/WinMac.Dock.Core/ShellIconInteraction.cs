namespace WinMac.Dock.Core;

// Keeps physical presses and asynchronous shell-toggle observations in order.
public sealed class ShellIconInteraction(bool preserveClickSequence)
{
    private bool wasPressed, pressActive, pressObserved;
    private long releasedAt = long.MinValue / 2, clickPendingUntil;

    public (bool NewPress, bool Active) Update(bool pressed, bool active, long now)
    {
        bool newPress = pressed && !wasPressed;
        if (newPress) { pressActive = active; pressObserved = true; }
        if (wasPressed && !pressed) releasedAt = now;
        wasPressed = pressed;
        return (newPress, preserveClickSequence && pressed ? pressActive : active);
    }

    public string? Click(string state, long now)
    {
        if (!preserveClickSequence) return null;
        bool active = pressObserved ? pressActive : state.EndsWith("On", StringComparison.Ordinal);
        clickPendingUntil = now + 250;
        pressObserved = false;
        return "Pressed" + (active ? "On" : "Off");
    }

    public bool HoldRelease(string state, string wanted, bool queuedPress, long now) =>
        !queuedPress && state == "PressedOff" && wanted.EndsWith("Off", StringComparison.Ordinal)
        && (now < clickPendingUntil || !wasPressed && now - releasedAt < 120);
}
