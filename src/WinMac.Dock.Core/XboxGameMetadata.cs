using System.Xml;
using System.Xml.Linq;

namespace WinMac.Dock.Core;

public sealed record XboxGameMetadata(string Identity, string DisplayName, string ApplicationId)
{
    public static XboxGameMetadata? Read(Stream stream)
    {
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024 });
        var document = XDocument.Load(reader);
        var root = document.Root;
        if (root?.Name.LocalName != "Game") return null;
        var identity = root.Elements().FirstOrDefault(e => e.Name.LocalName == "Identity")?.Attribute("Name")?.Value;
        var executable = root.Elements().FirstOrDefault(e => e.Name.LocalName == "ExecutableList")?.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "Executable" && !string.Equals(e.Attribute("IsDevOnly")?.Value, "true", StringComparison.OrdinalIgnoreCase)
                && (e.Attribute("TargetDeviceFamily") == null || string.Equals(e.Attribute("TargetDeviceFamily")?.Value, "PC", StringComparison.OrdinalIgnoreCase)));
        var appId = executable?.Attribute("Id")?.Value;
        if (string.IsNullOrWhiteSpace(identity) || string.IsNullOrWhiteSpace(appId)) return null;
        var title = root.Elements().FirstOrDefault(e => e.Name.LocalName == "ShellVisuals")?.Attribute("DefaultDisplayName")?.Value ?? "";
        if (title.StartsWith("ms-resource:", StringComparison.OrdinalIgnoreCase)) title = "";
        return new(identity, title, appId);
    }
}
