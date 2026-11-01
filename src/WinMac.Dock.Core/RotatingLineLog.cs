using System.Security.Cryptography;
using System.Text;

namespace WinMac.Dock.Core;

public sealed class RotatingLineLog
{
    private readonly string path, oldPath, mutexName;
    private readonly string? legacyPath;
    private bool migrated;
    private readonly int limit;

    public RotatingLineLog(string path, int lineLimit = 1000, string? legacyPath = null)
    {
        if (lineLimit < 1) throw new ArgumentOutOfRangeException(nameof(lineLimit));
        this.path = Path.GetFullPath(path);
        oldPath = Path.Combine(Path.GetDirectoryName(this.path)!, Path.GetFileNameWithoutExtension(this.path) + ".old" + Path.GetExtension(this.path));
        limit = lineLimit;
        this.legacyPath = legacyPath == null ? null : Path.GetFullPath(legacyPath);
        mutexName = "Local\\WinMac.Dock.Log." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(this.path.ToUpperInvariant())));
    }

    public void Write(string message)
    {
        using var mutex = new Mutex(false, mutexName);
        bool acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!migrated && legacyPath != null)
            {
                if (!File.Exists(path) && File.Exists(legacyPath)) File.Move(legacyPath, path);
                var legacyOld = Path.Combine(Path.GetDirectoryName(legacyPath)!, Path.GetFileNameWithoutExtension(legacyPath) + ".old" + Path.GetExtension(legacyPath));
                if (!File.Exists(oldPath) && File.Exists(legacyOld)) File.Move(legacyOld, oldPath);
                migrated = true;
            }
            int lines = File.Exists(path) ? File.ReadLines(path).Count() : 0;
            var incoming = message.Replace("\r\n", "\n").Replace('\r', '\n').TrimEnd('\n').Split('\n');
            StreamWriter? writer = null;
            try
            {
                foreach (var line in incoming)
                {
                    if (lines >= limit)
                    {
                        writer?.Dispose(); writer = null;
                        File.Delete(oldPath);
                        File.Move(path, oldPath);
                        lines = 0;
                    }
                    writer ??= new StreamWriter(path, append: true);
                    writer.WriteLine(line);
                    lines++;
                }
            }
            finally { writer?.Dispose(); }
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }
}
