namespace WinMac.Dock.Core;

public sealed record FolderEntry(string Path, string Name, bool IsFolder, DateTime Modified, DateTime Created, string Type)
{
    public bool ShowExtensions { get; init; } = true;
    public string DisplayLabel => DisplayName(ShowExtensions);
    public string DisplayName(bool showExtensions) => IsFolder || showExtensions ? Name : System.IO.Path.GetFileNameWithoutExtension(Name);
    public override string ToString() => Name;
}
public sealed record FolderResult(IReadOnlyList<FolderEntry> Entries, string? Error);

public static class FolderListing
{
    public static FolderOptions StackOptions(FolderOptions options, bool downloads) =>
        downloads ? options with { Sort = FolderSort.Modified, Descending = true } : options;

    public static IOrderedEnumerable<FolderEntry> Sort(IEnumerable<FolderEntry> entries, FolderOptions options)
    {
        Func<FolderEntry, object> key = options.Sort switch
        {
            FolderSort.Modified => x => x.Modified,
            FolderSort.Size => x => x.IsFolder ? 0L : (File.Exists(x.Path) ? new FileInfo(x.Path).Length : 0L),
            FolderSort.Created => x => x.Created,
            FolderSort.Type => x => x.Type.ToUpperInvariant(),
            _ => x => x.Name.ToUpperInvariant()
        };
        return (options.Descending ? entries.OrderByDescending(key) : entries.OrderBy(key)).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase);
    }

    public static async Task<FolderResult> ReadAsync(string path, FolderOptions options, CancellationToken token) => await Task.Run(() =>
    {
        try
        {
            var rows = new List<FolderEntry>();
            foreach (var entry in new DirectoryInfo(path).EnumerateFileSystemInfos())
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if ((entry.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (options.FilesOnly && (entry.Attributes & FileAttributes.Directory) != 0) continue;
                    rows.Add(new(entry.FullName, entry.Name, (entry.Attributes & FileAttributes.Directory) != 0, entry.LastWriteTimeUtc, entry.CreationTimeUtc, entry.Extension));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return new FolderResult(Sort(rows, options).ToList(), null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        { return new FolderResult([], ex.Message); }
    }, token);
}
