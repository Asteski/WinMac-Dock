using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinMac.Dock.Core;

// Keep old configurations readable without retaining the removed view mode.
public sealed class FolderViewConverter : JsonConverter<FolderView>
{
    public override FolderView Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            if (string.Equals(value, "Menu", StringComparison.OrdinalIgnoreCase) || value == "3") return FolderView.Menu;
            if (string.Equals(value, "List", StringComparison.OrdinalIgnoreCase) || value == "2") return FolderView.List;
            if (string.Equals(value, "Grid", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Fan", StringComparison.OrdinalIgnoreCase) || value is "0" or "1") return FolderView.Grid;
        }
        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int number) && number is >= 0 and <= 3)
            return number == 3 ? FolderView.Menu : number == 2 ? FolderView.List : FolderView.Grid;
        throw new JsonException("Unknown folder view.");
    }

    public override void Write(Utf8JsonWriter writer, FolderView value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value == FolderView.Menu ? "Menu" : value == FolderView.List ? "List" : "Grid");
}
