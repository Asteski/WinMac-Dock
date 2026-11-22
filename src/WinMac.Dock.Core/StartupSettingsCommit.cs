namespace WinMac.Dock.Core;

public static class StartupSettingsCommit
{
    public static bool TryCommit(bool previous, bool requested, Action<bool> configure, Func<bool> save)
    {
        bool changed = previous != requested;
        try
        {
            if (changed) configure(requested);
            if (save()) return true;
        }
        catch (Exception failure)
        {
            if (changed)
            {
                try { configure(previous); }
                catch (Exception rollback) { throw new AggregateException("Saving startup settings failed and startup registration could not be restored.", failure, rollback); }
            }
            throw;
        }
        if (changed) configure(previous);
        return false;
    }
}
