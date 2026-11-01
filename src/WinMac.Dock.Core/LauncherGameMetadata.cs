using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace WinMac.Dock.Core;

public sealed record SteamGameMetadata(string Id, string Name, string InstallDirectory);
public sealed record BattleNetGameMetadata(string Code, string InstallDirectory);
public sealed record EaGameMetadata(string Id, string Name);

public static class LauncherGameMetadata
{
    // Valve's quoted KeyValues format: distinguish nested objects from scalar
    // properties rather than matching arbitrary app IDs or paths in the file.
    private sealed class Node { public string Value = ""; public Dictionary<string, Node> Children = new(StringComparer.OrdinalIgnoreCase); }
    private static Node Vdf(string text)
    {
        if (text.Length > 4 * 1024 * 1024) throw new InvalidDataException("Launcher manifest is too large.");
        var tokens = Regex.Matches(text, "\\\"((?:\\\\.|[^\\\"\\\\])*)\\\"|[{}]|//[^\\r\\n]*", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1))
            .Cast<Match>().Where(m => !m.Value.StartsWith("//", StringComparison.Ordinal)).ToArray();
        int i = 0;
        string Text(Match match) => match.Groups[1].Value.Replace("\\\\", "\\").Replace("\\\"", "\"");
        Node Read(int depth)
        {
            if (depth > 32) throw new InvalidDataException("Launcher manifest is too deeply nested.");
            var node = new Node();
            while (i < tokens.Length && tokens[i].Value != "}")
            {
                var key = tokens[i++];
                if (key.Value == "{" || i >= tokens.Length) throw new InvalidDataException("Invalid launcher manifest.");
                var value = tokens[i++];
                if (value.Value == "{")
                {
                    node.Children[Text(key)] = Read(depth + 1);
                    if (i >= tokens.Length || tokens[i++].Value != "}") throw new InvalidDataException("Invalid launcher manifest.");
                }
                else if (value.Value == "}") throw new InvalidDataException("Invalid launcher manifest.");
                else node.Children[Text(key)] = new Node { Value = Text(value) };
            }
            return node;
        }
        return Read(0);
    }
    public static SteamGameMetadata? SteamGame(string text)
    {
        var root = Vdf(text).Children.GetValueOrDefault("AppState");
        string Get(string key) => root?.Children.GetValueOrDefault(key)?.Value ?? "";
        string id = Get("appid"), name = Get("name"), directory = Get("installdir");
        if (!ulong.TryParse(id, out _) || !uint.TryParse(Get("StateFlags"), out uint state) || (state & 4) == 0
            || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(directory) || directory.Contains('/') || directory.Contains((char)92)
            || directory is "." or ".." || id == "228980" || name.Contains("Redistributable", StringComparison.OrdinalIgnoreCase)) return null;
        return new(id, name, directory);
    }
    public static IReadOnlyList<string> SteamLibraries(string text)
    {
        var root = Vdf(text).Children.GetValueOrDefault("libraryfolders");
        return root?.Children.Where(p => uint.TryParse(p.Key, out _)).Select(p => p.Value.Children.GetValueOrDefault("path")?.Value ?? p.Value.Value)
            .Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray() ?? [];
    }

    // Bounded protobuf wire reader. Only product code and installation path
    // are decoded; account, progress and other unrelated fields are skipped.
    private static IReadOnlyList<(int Field, byte[] Data)> Fields(byte[] bytes)
    {
        if (bytes.Length > 16 * 1024 * 1024) throw new InvalidDataException("Launcher database is too large.");
        int position = 0;
        ulong Varint()
        {
            ulong value = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                if (position >= bytes.Length) throw new InvalidDataException("Truncated launcher database.");
                byte next = bytes[position++]; value |= (ulong)(next & 127) << shift;
                if (next < 128) return value;
            }
            throw new InvalidDataException("Invalid launcher database.");
        }
        var result = new List<(int, byte[])>();
        while (position < bytes.Length)
        {
            ulong key = Varint(); int wire = (int)(key & 7), field = (int)(key >> 3);
            if (field == 0) throw new InvalidDataException("Invalid launcher database field.");
            if (wire == 0) { Varint(); continue; }
            int length = wire switch { 1 => 8, 5 => 4, 2 => checked((int)Varint()), _ => throw new InvalidDataException("Unsupported launcher database field.") };
            if (length < 0 || length > bytes.Length - position) throw new InvalidDataException("Truncated launcher database.");
            if (wire == 2) result.Add((field, bytes.AsSpan(position, length).ToArray()));
            position += length;
        }
        return result;
    }
    public static IReadOnlyList<BattleNetGameMetadata> BattleNetGames(byte[] bytes)
    {
        var games = new List<BattleNetGameMetadata>();
        foreach (var field in Fields(bytes).Where(f => f.Field == 1))
        {
            var product = Fields(field.Data);
            string code = Encoding.UTF8.GetString(product.FirstOrDefault(f => f.Field == 2).Data ?? []);
            var settings = product.FirstOrDefault(f => f.Field == 3).Data;
            if (settings == null || code is "agent" or "bna" || !Regex.IsMatch(code, "^[a-zA-Z0-9_]{1,32}$")) continue;
            string path = Encoding.UTF8.GetString(Fields(settings).FirstOrDefault(f => f.Field == 1).Data ?? []);
            if (!string.IsNullOrWhiteSpace(path)) games.Add(new(code, path));
        }
        return games;
    }
    public static EaGameMetadata? EaGame(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 2 * 1024 * 1024 });
        var root = XDocument.Load(reader).Root;
        if (root?.Name.LocalName != "DiPManifest") return null;
        var id = root.Elements().FirstOrDefault(e => e.Name.LocalName == "contentIDs")?.Elements().FirstOrDefault(e => e.Name.LocalName == "contentID" && !string.IsNullOrWhiteSpace(e.Value))?.Value.Trim();
        var titles = root.Elements().FirstOrDefault(e => e.Name.LocalName == "gameTitles")?.Elements().Where(e => e.Name.LocalName == "gameTitle").ToArray() ?? [];
        var title = titles.FirstOrDefault(e => string.Equals(e.Attribute("locale")?.Value, "en_US", StringComparison.OrdinalIgnoreCase))?.Value ?? titles.FirstOrDefault()?.Value ?? "";
        return string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) ? null : new(id, title.Trim());
    }
}
