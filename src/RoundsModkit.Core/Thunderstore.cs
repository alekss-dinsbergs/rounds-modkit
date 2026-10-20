using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoundsModkit.Core;

public sealed record ThunderstoreVersion(
    [property: JsonPropertyName("version_number")] string VersionNumber,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("dependencies")] string[] Dependencies,
    [property: JsonPropertyName("download_url")] string DownloadUrl,
    [property: JsonPropertyName("downloads")] long Downloads,
    [property: JsonPropertyName("file_size")] long FileSize);

public sealed record ThunderstorePackage(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("full_name")] string FullName,
    [property: JsonPropertyName("owner")] string Owner,
    [property: JsonPropertyName("package_url")] string PackageUrl,
    [property: JsonPropertyName("date_updated")] DateTimeOffset DateUpdated,
    [property: JsonPropertyName("rating_score")] int Rating,
    [property: JsonPropertyName("is_deprecated")] bool IsDeprecated,
    [property: JsonPropertyName("has_nsfw_content")] bool HasNsfwContent,
    [property: JsonPropertyName("categories")] string[] Categories,
    [property: JsonPropertyName("versions")] ThunderstoreVersion[] Versions)
{
    /// <summary>The API lists versions newest first.</summary>
    public ThunderstoreVersion Latest => Versions[0];
    public long TotalDownloads => Versions.Sum(v => v.Downloads);
}

/// <summary>A "Owner-Name-1.2.3" dependency string. Thunderstore names can't contain '-'.</summary>
public readonly record struct PackageRef(string Owner, string Name, string? Version)
{
    public string Id => $"{Owner}-{Name}";

    public static PackageRef? Parse(string s)
    {
        var p = s.Split('-');
        return p.Length switch
        {
            >= 3 => new PackageRef(p[0], p[1], p[2]),
            2 => new PackageRef(p[0], p[1], null),
            _ => null,
        };
    }

    // The launcher installs BepInEx itself; BepInExPack_ROUNDS is the same loader with stock settings.
    public bool IsLoaderPack => Name.StartsWith("BepInExPack", StringComparison.OrdinalIgnoreCase);
}

public sealed record PlannedInstall(ThunderstorePackage Package, ThunderstoreVersion Version);

public sealed record InstallPlan(
    IReadOnlyList<PlannedInstall> ToInstall,
    IReadOnlyList<ModInfo> ToEnable,
    IReadOnlyList<string> Unresolved);

public sealed class ThunderstoreIndex
{
    public const string ApiUrl = "https://thunderstore.io/c/rounds/api/v1/package/";
    private static readonly TimeSpan MaxAge = TimeSpan.FromHours(1);

    private static readonly HashSet<string> Hidden = new(StringComparer.OrdinalIgnoreCase) { "ebkr-r2modman" };

    private readonly Dictionary<string, ThunderstorePackage> _byId;
    public IReadOnlyList<ThunderstorePackage> Packages { get; }
    public DateTime FetchedAt { get; }

    public ThunderstoreIndex(IReadOnlyList<ThunderstorePackage> packages, DateTime fetchedAt)
    {
        Packages = packages
            .Where(p => p.Versions.Length > 0 && !p.HasNsfwContent && !Hidden.Contains(p.FullName)
                        && !(PackageRef.Parse(p.FullName)?.IsLoaderPack ?? false))
            .ToList();
        _byId = new Dictionary<string, ThunderstorePackage>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Packages) _byId.TryAdd(p.FullName, p);
        FetchedAt = fetchedAt;
    }

    public ThunderstorePackage? Find(string id) => _byId.GetValueOrDefault(id);

    public IReadOnlyList<string> Categories =>
        Packages.SelectMany(p => p.Categories).Distinct().Order(StringComparer.OrdinalIgnoreCase).ToList();

    public static ThunderstoreIndex Parse(string json, DateTime fetchedAt) =>
        new(JsonSerializer.Deserialize<List<ThunderstorePackage>>(json) ?? [], fetchedAt);

    /// <summary>Uses a cached copy younger than an hour unless <paramref name="force"/> is set.</summary>
    public static async Task<ThunderstoreIndex> LoadAsync(HttpClient http, bool force = false, CancellationToken ct = default)
    {
        var cache = Path.Combine(AppPaths.CacheDir, "thunderstore-rounds.json");
        if (!force && File.Exists(cache) && DateTime.UtcNow - File.GetLastWriteTimeUtc(cache) < MaxAge)
            return Parse(await File.ReadAllTextAsync(cache, ct), File.GetLastWriteTime(cache));

        try
        {
            var json = await http.GetStringAsync(ApiUrl, ct);
            var index = Parse(json, DateTime.Now);
            Directory.CreateDirectory(AppPaths.CacheDir);
            await File.WriteAllTextAsync(cache + ".tmp", json, ct);
            File.Move(cache + ".tmp", cache, overwrite: true);
            return index;
        }
        catch (HttpRequestException) when (File.Exists(cache))
        {
            return Parse(await File.ReadAllTextAsync(cache, ct), File.GetLastWriteTime(cache));
        }
    }

    public IEnumerable<ThunderstorePackage> Search(string? query, string? category, bool includeDeprecated)
    {
        var q = query?.Trim() ?? "";
        var hits = Packages.Where(p =>
            (includeDeprecated || !p.IsDeprecated) &&
            (string.IsNullOrEmpty(category) || p.Categories.Contains(category)));

        if (q.Length == 0) return hits.OrderByDescending(p => p.TotalDownloads);

        return hits
            .Select(p => (p, score: Score(p, q)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .ThenByDescending(x => x.p.TotalDownloads)
            .Select(x => x.p);
    }

    private static int Score(ThunderstorePackage p, string q)
    {
        var name = p.Name.Replace('_', ' ');
        if (name.Equals(q, StringComparison.OrdinalIgnoreCase)) return 4;
        if (name.StartsWith(q, StringComparison.OrdinalIgnoreCase)) return 3;
        if (name.Contains(q, StringComparison.OrdinalIgnoreCase) || p.Owner.Contains(q, StringComparison.OrdinalIgnoreCase)) return 2;
        if (p.Latest.Description.Contains(q, StringComparison.OrdinalIgnoreCase)) return 1;
        return 0;
    }

    /// <summary>
    /// The package (always, so it doubles as reinstall/update) plus every dependency not already
    /// installed, dependencies first. Dependency versions on Thunderstore are minimums, so the
    /// latest version of each is used.
    /// </summary>
    public InstallPlan Plan(ThunderstorePackage root, InstalledMods installed) =>
        Plan([PackageRef.Parse(root.FullName)!.Value], installed, alwaysInstallRoots: true);

    /// <summary>
    /// Plans several packages at once. A root with a version is pinned to it, even when it's reached
    /// as another package's dependency; unpinned roots and dependencies use the latest version.
    /// </summary>
    public InstallPlan Plan(IEnumerable<PackageRef> roots, InstalledMods installed, bool alwaysInstallRoots = false)
    {
        var rootList = roots.ToList();
        var pins = rootList.Where(r => r.Version is not null)
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Version!, StringComparer.OrdinalIgnoreCase);
        var rootIds = rootList.Select(r => r.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var order = new List<PlannedInstall>();
        var enable = new List<ModInfo>();
        var unresolved = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Enable(ModInfo m)
        {
            if (!m.Enabled && !enable.Contains(m)) enable.Add(m);
        }

        void Visit(PackageRef r, string original)
        {
            if (r.IsLoaderPack || !visited.Add(r.Id)) return;
            var pin = pins.GetValueOrDefault(r.Id);
            var have = installed.Get(r);

            var satisfied = have is not null && (pin is null || have.Version == pin)
                            && !(alwaysInstallRoots && rootIds.Contains(r.Id));
            if (satisfied)
            {
                Enable(have!);
                return;
            }

            if (Find(r.Id) is not { } package)
            {
                unresolved.Add(original);
                return;
            }
            var version = pin is null ? package.Latest : package.Versions.FirstOrDefault(v => v.VersionNumber == pin);
            if (version is null)
            {
                unresolved.Add($"{r.Id}-{pin} (version no longer on Thunderstore)");
                return;
            }

            foreach (var dep in version.Dependencies)
            {
                if (PackageRef.Parse(dep) is { } d) Visit(d, dep);
                else unresolved.Add(dep);
            }
            order.Add(new PlannedInstall(package, version));
        }

        foreach (var r in rootList) Visit(r, r.Version is null ? r.Id : $"{r.Id}-{r.Version}");
        return new InstallPlan(order, enable, unresolved);
    }
}

/// <summary>What's on disk, matched the way Thunderstore dependencies name things.</summary>
public sealed class InstalledMods
{
    private readonly Dictionary<string, ModInfo> _byKey = new(StringComparer.OrdinalIgnoreCase);

    public InstalledMods(IEnumerable<ModInfo> mods)
    {
        // Browser installs use "Owner-Name" folders. Manual imports only know the manifest name, and
        // only those fall back to name matching, so two authors' same-named packages stay distinct.
        foreach (var m in mods)
        {
            _byKey.TryAdd(m.Id, m);
            if (!m.Id.Contains('-') && m.Manifest?.Name is { } n) _byKey.TryAdd(n, m);
        }
    }

    public ModInfo? Get(PackageRef r) => _byKey.GetValueOrDefault(r.Id) ?? _byKey.GetValueOrDefault(r.Name);
    public bool Has(PackageRef r) => Get(r) is not null;

    public static bool IsNewer(string candidate, string? installed) =>
        installed is null || !Version.TryParse(installed, out var i) || !Version.TryParse(candidate, out var c) || c > i;
}

public sealed class ThunderstoreInstaller(GameInstall game, HttpClient http)
{
    public async Task InstallAsync(InstallPlan plan, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(AppPaths.CacheDir, "packages");
        Directory.CreateDirectory(dir);
        var mods = new ModManager(game);
        foreach (var m in plan.ToEnable)
        {
            mods.SetEnabled(m, true);
            progress?.Report($"Enabled {m.DisplayName}");
        }

        foreach (var (p, v) in plan.ToInstall)
        {
            var zip = Path.Combine(dir, $"{p.FullName}-{v.VersionNumber}.zip");
            if (!File.Exists(zip) || new FileInfo(zip).Length != v.FileSize)
            {
                progress?.Report($"Downloading {p.FullName} {v.VersionNumber} ({v.FileSize / 1024} KB)…");
                await using (var src = await http.GetStreamAsync(v.DownloadUrl, ct))
                await using (var dst = File.Create(zip + ".part"))
                    await src.CopyToAsync(dst, ct);
                File.Move(zip + ".part", zip, overwrite: true);
            }

            // A disabled copy would otherwise shadow the fresh install on the next toggle.
            var disabled = Path.Combine(game.DisabledPluginsDir, p.FullName);
            if (Directory.Exists(disabled)) Directory.Delete(disabled, recursive: true);

            mods.Import(zip, id: p.FullName);
            progress?.Report($"Installed {p.FullName} {v.VersionNumber}");
        }
    }
}
