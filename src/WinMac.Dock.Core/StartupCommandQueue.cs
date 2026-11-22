namespace WinMac.Dock.Core;

public sealed class StartupCommandQueue
{
    private readonly Queue<string> pending = new();
    private readonly object gate = new();
    private Action<string>? dispatch;
    public void Receive(string command)
    {
        lock (gate)
        {
            if (dispatch == null) pending.Enqueue(command);
            else dispatch(command);
        }
    }
    public void Ready(Action<string> handler)
    {
        lock (gate)
        {
            dispatch = handler;
            while (pending.TryDequeue(out var command)) handler(command);
        }
    }
}
