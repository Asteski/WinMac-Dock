using System.Text.Json;

namespace WinMac.Dock.Core;

public sealed record EdgeProfileIdentity(string Name, string PictureFile)
{
    public static EdgeProfileIdentity Parse(JsonElement profile, string fallback)
    {
        string Text(string key) => profile.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() ?? "" : "";
        bool Flag(string key) => profile.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.True;
        string name = Text("name");
        if (Flag("is_using_default_name") || string.IsNullOrWhiteSpace(name))
        {
            string account = Text("gaia_name");
            if (account.Length == 0) account = string.Join(" ", new[] { Text("edge_account_first_name"), Text("edge_account_last_name") }.Where(s => s.Length > 0));
            if (account.Length > 0) name = account;
        }
        string picture = Flag("use_gaia_picture") ? Text("gaia_picture_file_name") : "";
        // Metadata must not redirect picture reads outside the profile folder.
        if (picture.Contains('/') || picture.Contains('\\') || picture.Contains(':') || picture is "." or "..") picture = "";
        return new(name.Length > 0 ? name : fallback, picture);
    }
}
