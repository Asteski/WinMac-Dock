using Windows.Management.Core;
using Windows.Storage;

namespace WinMac.Dock;

internal static class SnippingToolSettings
{
    internal const string PackageFamily = "Microsoft.ScreenSketch_8wekyb3d8bbwe";
    internal const string ScreenshotsKey = "AutoSaveScreenshotsLocation", RecordingsKey = "AutoSaveRecordingsLocation";
    internal sealed record Locations(string Screenshots, string Recordings);
    private static ApplicationData Data() => ApplicationDataManager.CreateForPackageFamily(PackageFamily);

    public static Locations Read()
    {
        var values = Data().LocalSettings.Values;
        string ReadPath(string key, string fallback) => values.TryGetValue(key, out var value) && value is string path && !string.IsNullOrWhiteSpace(path)
            ? Environment.ExpandEnvironmentVariables(path) : fallback;
        return new(ReadPath(ScreenshotsKey, KnownFolder(new("B7BEDE81-DF94-4682-A7D8-57A52620B86F"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots"))),
            ReadPath(RecordingsKey, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Screen Recordings")));
    }

    private static string KnownFolder(Guid id, string fallback)
    {
        if (Native.SHGetKnownFolderPath(ref id, 0x4000, 0, out var path) < 0) return fallback;
        try { return System.Runtime.InteropServices.Marshal.PtrToStringUni(path) ?? fallback; }
        finally { System.Runtime.InteropServices.Marshal.FreeCoTaskMem(path); }
    }

    public static void Apply(string screenshots, string recordings)
    {
        static string Validate(string path)
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim());
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException("Choose an absolute folder path.");
            return Path.GetFullPath(path);
        }
        screenshots = Validate(screenshots); recordings = Validate(recordings);
        var data = Data(); var values = data.LocalSettings.Values;
        Directory.CreateDirectory(screenshots); Directory.CreateDirectory(recordings);
        bool hadScreenshots = values.TryGetValue(ScreenshotsKey, out var oldScreenshots), hadRecordings = values.TryGetValue(RecordingsKey, out var oldRecordings);
        try
        {
            values[ScreenshotsKey] = screenshots; values[RecordingsKey] = recordings;
            data.SignalDataChanged();
        }
        catch
        {
            if (hadScreenshots) values[ScreenshotsKey] = oldScreenshots; else values.Remove(ScreenshotsKey);
            if (hadRecordings) values[RecordingsKey] = oldRecordings; else values.Remove(RecordingsKey);
            throw;
        }
    }
}
