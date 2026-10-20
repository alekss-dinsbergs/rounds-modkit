using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoundsModkit.Core;

/// <summary>
/// A shareable set of Thunderstore mods, stored as JSON. Mods use Thunderstore's dependency
/// format, "Owner-Name-1.2.3"; leaving the version off means "latest".
/// </summary>
public sealed class ModPreset
{
    public const int CurrentFormat = 1;

    [JsonPropertyName("format")] public int Format { get; set; } = CurrentFormat;
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";

    /// <summary>When true, applying disables Thunderstore mods the preset doesn't need.</summary>
    [JsonPropertyName("exclusive")] public bool Exclusive { get; set; } = true;

    [JsonPropertyName("mods")] public List<string> Mods { get; set; } = [];

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static ModPreset Parse(string json)
    {
        var preset = JsonSerializer.Deserialize<ModPreset>(json) ?? throw new InvalidDataException("Empty preset");
        if (preset.Format > CurrentFormat)
            throw new InvalidDataException($"Preset format {preset.Format} is newer than this launcher supports ({CurrentFormat}).");
        if (string.IsNullOrWhiteSpace(preset.Name)) throw new InvalidDataException("Preset has no name.");
        return preset;
    }

    public static ModPreset Load(string path) => Parse(File.ReadAllText(path));

    public string Serialize() => JsonSerializer.Serialize(this, Json) + "\n";

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, Serialize());
    }

    [JsonIgnore]
    public IEnumerable<PackageRef> Refs => Mods.Select(PackageRef.Parse).OfType<PackageRef>();

    public static string FileNameFor(string name) =>
        string.Concat(name.Trim().ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-')).Trim('-') + ".json";

    /// <summary>
    /// Captures the enabled Thunderstore mods with their installed versions. Mods without a
    /// Thunderstore manifest (local plugins, loose dlls) can't be shared this way and are returned
    /// in <paramref name="skipped"/>.
    /// </summary>
    public static ModPreset FromInstalled(string name, string description, IEnumerable<ModInfo> mods,
        ThunderstoreIndex? index, out List<string> skipped)
    {
        skipped = [];
        var refs = new List<string>();
        foreach (var m in mods.Where(m => m.Enabled))
        {
            if (m.Manifest?.Name is null)
            {
                skipped.Add(m.DisplayName);
                continue;
            }
            // Browser installs are named Owner-Name. Manual imports only know their name, so look
            // the owner up on Thunderstore and require a single match.
            var id = m.Id.Contains('-') ? m.Id : UniqueIdByName(index, m.Manifest.Name);
            if (id is null)
            {
                skipped.Add(m.DisplayName);
                continue;
            }
            refs.Add(m.Version is null ? id : $"{id}-{m.Version}");
        }
        refs.Sort(StringComparer.OrdinalIgnoreCase);
        return new ModPreset { Name = name, Description = description, Mods = refs };
    }

    private static string? UniqueIdByName(ThunderstoreIndex? index, string name)
    {
        var matches = index?.Packages.Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        return matches is { Count: 1 } ? matches[0].FullName : null;
    }
}

public sealed record PresetPlan(InstallPlan Install, IReadOnlyList<ModInfo> ToDisable)
{
    public bool IsNoOp => Install.ToInstall.Count == 0 && Install.ToEnable.Count == 0 && ToDisable.Count == 0;

    public string Summary()
    {
        var parts = new List<string>();
        if (Install.ToInstall.Count > 0) parts.Add($"install {Install.ToInstall.Count}");
        if (Install.ToEnable.Count > 0) parts.Add($"enable {Install.ToEnable.Count}");
        if (ToDisable.Count > 0) parts.Add($"disable {ToDisable.Count}");
        var text = parts.Count == 0 ? "Already applied" : "Will " + string.Join(", ", parts);
        return Install.Unresolved.Count > 0 ? $"{text} · {Install.Unresolved.Count} unavailable" : text;
    }
}

public static class PresetApplier
{
    public static PresetPlan Plan(ModPreset preset, ThunderstoreIndex index, IReadOnlyList<ModInfo> installedMods)
    {
        var installed = new InstalledMods(installedMods);
        var install = index.Plan(preset.Refs, installed);
        if (!preset.Exclusive) return new PresetPlan(install, []);

        var keep = Closure(preset, index, installed);
        var keepNames = keep.Select(id => PackageRef.Parse(id)!.Value.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        bool Needed(ModInfo m) => m.Id.Contains('-')
            ? keep.Contains(m.Id)
            : keepNames.Contains(m.Manifest!.Name!); // manual imports only know their package name

        // Only Thunderstore-managed mods (those with a manifest) are switched off; local plugins
        // such as the controller fix are left alone.
        var disable = installedMods.Where(m => m.Enabled && m.Manifest?.Name is not null && !Needed(m)).ToList();
        return new PresetPlan(install, disable);
    }

    /// <summary>Every Owner-Name the preset needs, including transitive dependencies.</summary>
    private static HashSet<string> Closure(ModPreset preset, ThunderstoreIndex index, InstalledMods installed)
    {
        var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<PackageRef>(preset.Refs);
        while (queue.Count > 0)
        {
            var r = queue.Dequeue();
            if (r.IsLoaderPack || !keep.Add(r.Id)) continue;
            var deps = installed.Get(r)?.Manifest?.Dependencies
                       ?? index.Find(r.Id)?.Latest.Dependencies
                       ?? [];
            foreach (var d in deps)
                if (PackageRef.Parse(d) is { } dr) queue.Enqueue(dr);
        }
        return keep;
    }

    public static async Task ApplyAsync(GameInstall game, HttpClient http, PresetPlan plan,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var mods = new ModManager(game);
        foreach (var m in plan.ToDisable)
        {
            mods.SetEnabled(m, false);
            progress?.Report($"Disabled {m.DisplayName}");
        }
        await new ThunderstoreInstaller(game, http).InstallAsync(plan.Install, progress, ct);
    }
}

/// <summary>Where presets come from: the public repo's presets/ folder and a local folder.</summary>
public static class PresetSources
{
    public const string RepoOwner = "alekss-dinsbergs";
    public const string RepoName = "rounds-modkit";
    public const string RepoContentsUrl = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/contents/presets";

    public static string LocalDir => Path.Combine(AppPaths.DataDir, "presets");

    public sealed record Entry(string Source, string FileName, ModPreset Preset, string? LocalPath);

    public static IEnumerable<Entry> LoadLocal(Action<string>? report = null)
    {
        if (!Directory.Exists(LocalDir)) yield break;
        foreach (var f in Directory.EnumerateFiles(LocalDir, "*.json").Order(StringComparer.OrdinalIgnoreCase))
        {
            ModPreset? p = null;
            try { p = ModPreset.Load(f); }
            catch (Exception e) when (e is JsonException or InvalidDataException) { report?.Invoke($"Skipping {Path.GetFileName(f)}: {e.Message}"); }
            if (p is not null) yield return new Entry("Local", Path.GetFileName(f), p, f);
        }
    }

    /// <summary>Lists presets/*.json in the GitHub repo (unauthenticated API, 60 requests/hour).</summary>
    public static async Task<List<Entry>> LoadRepoAsync(HttpClient http, Action<string>? report = null, CancellationToken ct = default)
    {
        var result = new List<Entry>();
        using var listing = JsonDocument.Parse(await http.GetStringAsync(RepoContentsUrl, ct));
        foreach (var item in listing.RootElement.EnumerateArray())
        {
            var name = item.GetProperty("name").GetString() ?? "";
            if (!name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
            var url = item.GetProperty("download_url").GetString();
            if (url is null) continue;
            try { result.Add(new Entry("Repo", name, ModPreset.Parse(await http.GetStringAsync(url, ct)), null)); }
            catch (Exception e) when (e is JsonException or InvalidDataException) { report?.Invoke($"Skipping repo preset {name}: {e.Message}"); }
        }
        return result;
    }
}
