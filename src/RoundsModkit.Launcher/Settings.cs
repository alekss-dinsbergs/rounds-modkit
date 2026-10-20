using System.Text.Json;
using RoundsModkit.Core;

namespace RoundsModkit.Launcher;

public sealed class Settings
{
    public string? GameDir { get; set; }

    public static Settings Load()
    {
        try
        {
            return File.Exists(AppPaths.SettingsFile)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new()
                : new();
        }
        catch (JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.DataDir);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
