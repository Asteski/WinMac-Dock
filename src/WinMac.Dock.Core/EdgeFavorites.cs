using System.Text;
using System.Text.Json;

namespace WinMac.Dock.Core;
public sealed record EdgeFavorite(string Name, string? Url, IReadOnlyList<EdgeFavorite> Children, string? WorkspaceId = null)
{
    public bool IsFavoritesBar { get; init; }
    public bool IsWorkspaceGroup { get; init; }
}
public static class EdgeFavorites
{
    public static IReadOnlyList<EdgeFavorite> Section(IEnumerable<EdgeFavorite> entries, bool workspaces) => workspaces
        ? entries.Where(entry => entry.IsWorkspaceGroup).SelectMany(entry => entry.Children).ToArray()
        : entries.Where(entry => !entry.IsWorkspaceGroup).ToArray();

    public static IReadOnlyList<EdgeFavorite> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("roots", out var roots) || roots.ValueKind != JsonValueKind.Object) return [];
        EdgeFavorite? Read(JsonElement node, int depth)
        {
            if (depth > 32 || node.ValueKind != JsonValueKind.Object) return null;
            string name = node.TryGetProperty("name", out var label) && label.ValueKind == JsonValueKind.String ? label.GetString() ?? "" : "";
            if (node.TryGetProperty("url", out var link) && link.ValueKind == JsonValueKind.String)
            {
                if (!Uri.TryCreate(link.GetString(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "file" or "edge")) return null;
                return new(string.IsNullOrWhiteSpace(name) ? uri.Host : name, uri.AbsoluteUri, []);
            }
            if (!node.TryGetProperty("children", out var children) || children.ValueKind != JsonValueKind.Array) return null;
            var entries = children.EnumerateArray().Select(child => Read(child, depth + 1)).OfType<EdgeFavorite>().ToArray();
            return entries.Length == 0 ? null : new(string.IsNullOrWhiteSpace(name) ? "Favorites" : name, null, entries);
        }
        return roots.EnumerateObject().Where(root => root.Name != "workspaces_v2").Select(root => Read(root.Value, 0) is { } entry ? entry with { IsFavoritesBar = root.Name == "bookmark_bar" } : null).OfType<EdgeFavorite>().ToArray();
    }
    public static IReadOnlyList<EdgeFavorite> ParseWorkspaces(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("workspaces", out var workspaces) || workspaces.ValueKind != JsonValueKind.Array) return [];
        var result = new List<EdgeFavorite>();
        var seen = new HashSet<Guid>();
        foreach (var workspace in workspaces.EnumerateArray())
        {
            if (workspace.ValueKind != JsonValueKind.Object
                || !workspace.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString())
                || !workspace.TryGetProperty("id", out var identity) || identity.ValueKind != JsonValueKind.String
                || !Guid.TryParse(identity.GetString(), out var id) || !seen.Add(id)) continue;
            result.Add(new(name.GetString()!, null, [], id.ToString()));
        }
        return result;
    }
    public static string QuoteArgument(string value)
    {
        var result = new StringBuilder("\"");
        int slashes = 0;
        foreach (char character in value)
        {
            if (character == '\\') { slashes++; continue; }
            result.Append('\\', character == '\"' ? slashes * 2 + 1 : slashes);
            result.Append(character); slashes = 0;
        }
        result.Append('\\', slashes * 2); result.Append('\"');
        return result.ToString();
    }
}
