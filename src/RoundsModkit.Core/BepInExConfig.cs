namespace RoundsModkit.Core;

public sealed class ConfigEntry
{
    public required string Section { get; init; }
    public required string Key { get; init; }
    public string Value { get; set; } = "";
    public string Description { get; init; } = "";
    public string? SettingType { get; init; }
    public string? DefaultValue { get; init; }
    public IReadOnlyList<string> AcceptableValues { get; init; } = [];
    public string? AcceptableRange { get; init; }
    internal int LineIndex { get; init; }

    public bool IsBoolean => SettingType == "Boolean";

    // BepInEx writes flags enums with a "# Multiple values can be set" note; those need free text.
    public bool IsChoice => AcceptableValues.Count > 0 && !IsFlags;
    public bool IsFlags { get; init; }
}

/// <summary>
/// Reads and writes BepInEx 5 .cfg files. Only value lines are rewritten on save, so comments,
/// ordering and entries this parser doesn't understand survive untouched.
/// </summary>
public sealed class BepInExConfig
{
    private readonly List<string> _lines;
    public string FilePath { get; }
    public string? PluginName { get; }
    public string? PluginGuid { get; }
    public IReadOnlyList<ConfigEntry> Entries { get; }

    private BepInExConfig(string path, List<string> lines, List<ConfigEntry> entries, string? name, string? guid)
    {
        FilePath = path;
        _lines = lines;
        Entries = entries;
        PluginName = name;
        PluginGuid = guid;
    }

    public static BepInExConfig Load(string path) => Parse(path, File.ReadAllText(path));

    public static BepInExConfig Parse(string path, string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').ToList();
        var entries = new List<ConfigEntry>();
        string section = "";
        string? name = null, guid = null;
        var desc = new List<string>();
        string? type = null, def = null, range = null;
        bool flags = false;
        List<string> acceptable = [];

        void Reset()
        {
            desc.Clear(); type = null; def = null; range = null; flags = false; acceptable = [];
        }

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("## Settings file was created by plugin "))
            {
                name = line["## Settings file was created by plugin ".Length..];
                continue;
            }
            if (line.StartsWith("## Plugin GUID: "))
            {
                guid = line["## Plugin GUID: ".Length..];
                continue;
            }
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                Reset();
                continue;
            }
            if (line.StartsWith("## "))
            {
                desc.Add(line[3..]);
                continue;
            }
            if (line.StartsWith("# "))
            {
                var meta = line[2..];
                if (meta.StartsWith("Setting type: ")) type = meta["Setting type: ".Length..];
                else if (meta.StartsWith("Default value: ")) def = meta["Default value: ".Length..];
                else if (meta.StartsWith("Default value:")) def = "";
                else if (meta.StartsWith("Acceptable values: "))
                    acceptable = meta["Acceptable values: ".Length..].Split(", ").ToList();
                else if (meta.StartsWith("Acceptable value range: ")) range = meta["Acceptable value range: ".Length..];
                else if (meta.StartsWith("Multiple values can be set")) flags = true;
                continue;
            }
            if (line.StartsWith('#') || line.StartsWith(';')) continue;

            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            entries.Add(new ConfigEntry
            {
                Section = section,
                Key = line[..eq].Trim(),
                Value = line[(eq + 1)..].Trim(),
                Description = string.Join("\n", desc),
                SettingType = type,
                DefaultValue = def,
                AcceptableValues = acceptable,
                AcceptableRange = range,
                IsFlags = flags,
                LineIndex = i,
            });
            Reset();
        }

        return new BepInExConfig(path, lines, entries, name, guid);
    }

    public string Serialize()
    {
        foreach (var e in Entries)
            _lines[e.LineIndex] = $"{e.Key} = {e.Value}";
        return string.Join(Environment.NewLine, _lines);
    }

    public void Save()
    {
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, Serialize());
        File.Move(tmp, FilePath, overwrite: true);
    }
}
