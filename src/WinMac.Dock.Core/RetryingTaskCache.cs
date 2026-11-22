namespace WinMac.Dock.Core;

public sealed class RetryingTaskCache<TKey, TValue>(TimeSpan retryDelay, Func<long>? clock = null) where TKey : notnull where TValue : class
{
    private sealed class Entry
    {
        public Task<TValue?> Task = System.Threading.Tasks.Task.FromResult<TValue?>(null);
        public long RetryAfter;
    }
    private readonly Dictionary<TKey, Entry> entries = new();
    private readonly Func<long> now = clock ?? (() => Environment.TickCount64);
    public Task<TValue?> GetOrCreate(TKey key, Func<Task<TValue?>> create)
    {
        lock (entries)
        {
            if (entries.TryGetValue(key, out var existing) && (!existing.Task.IsCompleted
                || (existing.Task.IsCompletedSuccessfully && existing.Task.Result != null) || now() < existing.RetryAfter)) return existing.Task;
            var entry = new Entry();
            entries[key] = entry;
            entry.Task = Run();
            return entry.Task;
            async Task<TValue?> Run()
            {
                try { return await create(); }
                finally { lock (entries) entry.RetryAfter = now() + (long)Math.Max(0, retryDelay.TotalMilliseconds); }
            }
        }
    }
}
