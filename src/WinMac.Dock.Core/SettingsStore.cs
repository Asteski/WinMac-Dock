using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace WinMac.Dock.Core;

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Options = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(type =>
        {
            if (type.Type != typeof(DockItem)) return;
            foreach (var property in type.Properties)
            {
                string name = property.Name;
                property.ShouldSerialize = (item, value) => ItemSettingsSerialization.Includes((DockItem)item, name, value);
            }
        });
        return new() { WriteIndented = true, TypeInfoResolver = resolver };
    }
    public string Path { get; } = path;
    public DockSettings RestoreFrom(string recoveryPath)
    {
        // Validate the complete recovery before touching the current configuration.
        var settings = JsonSerializer.Deserialize<DockSettings>(File.ReadAllText(recoveryPath), Options)
            ?? throw new InvalidDataException("Recovery settings are empty.");
        settings.Validate();
        if (File.Exists(Path)) File.Copy(Path, Path + ".before-recovery-" + Guid.NewGuid().ToString("N"));
        Save(settings);
        return settings;
    }
    public DockSettings Load(Func<DockSettings> defaults)
    {
        if (!File.Exists(Path))
        {
            var initial = defaults();
            Save(initial);
            return initial;
        }
        string json = File.ReadAllText(Path);
        var settings = JsonSerializer.Deserialize<DockSettings>(json, Options) ?? throw new InvalidDataException("Settings are empty.");
        settings.Validate();
        // Migrate verbose legacy items once, using the same atomic save and backup.
        if (json != JsonSerializer.Serialize(settings, Options)) Save(settings);
        return settings;
    }

    public void Save(DockSettings settings)
    {
        settings.Validate();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, Options));
        try
        {
            if (File.Exists(Path))
            {
                try { File.Replace(temp, Path, Path + ".bak"); }
                catch (FileNotFoundException) when (File.Exists(temp) && !File.Exists(Path))
                {
                    File.Move(temp, Path, overwrite: true);
                }
            }
            else File.Move(temp, Path, overwrite: true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
