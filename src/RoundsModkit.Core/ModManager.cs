using System.IO.Compression;
using System.Text.Json;

namespace RoundsModkit.Core;

public sealed record ThunderstoreManifest(string? Name, string? VersionNumber, string? Description, string[]? Dependencies)
{
    public static ThunderstoreManifest? TryRead(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var r = doc.RootElement;
            string? S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            var deps = r.TryGetProperty("dependencies", out var d) && d.ValueKind == JsonValueKind.Array
                ? d.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0).ToArray()
                : [];
            return new ThunderstoreManifest(S("name"), S("version_number"), S("description"), deps);
        }
        catch (JsonException) { return null; }
    }
}

public sealed record ModInfo(string Id, string Path, bool Enabled, bool IsDirectory, ThunderstoreManifest? Manifest)
{
    public string DisplayName => Manifest?.Name ?? Id;
    public string? Version => Manifest?.VersionNumber;
}

/// <summary>
/// A "mod" is one top-level entry of BepInEx/plugins (a folder or a single dll).
/// </summary>
public sealed class ModManager(GameInstall game)
{
    public IReadOnlyList<ModInfo> List()
    {
        var mods = new List<ModInfo>();
        Collect(game.PluginsDir, true, mods);
        Collect(game.DisabledPluginsDir, false, mods);
        return mods.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void Collect(string dir, bool enabled, List<ModInfo> into)
    {
        if (!Directory.Exists(dir)) return;
        foreach (var d in Directory.EnumerateDirectories(dir))
            into.Add(new ModInfo(System.IO.Path.GetFileName(d), d, enabled, true,
                ThunderstoreManifest.TryRead(System.IO.Path.Combine(d, "manifest.json"))));
        foreach (var f in Directory.EnumerateFiles(dir, "*.dll"))
            into.Add(new ModInfo(System.IO.Path.GetFileName(f), f, enabled, false, null));
    }

    public ModInfo SetEnabled(ModInfo mod, bool enabled)
    {
        if (mod.Enabled == enabled) return mod;
        var targetDir = enabled ? game.PluginsDir : game.DisabledPluginsDir;
        Directory.CreateDirectory(targetDir);
        var target = System.IO.Path.Combine(targetDir, mod.Id);
        if (File.Exists(target) || Directory.Exists(target))
            throw new IOException($"'{mod.Id}' already exists in {targetDir}");
        if (mod.IsDirectory) Directory.Move(mod.Path, target);
        else File.Move(mod.Path, target);
        return mod with { Path = target, Enabled = enabled };
    }

    public void Remove(ModInfo mod)
    {
        if (mod.IsDirectory) Directory.Delete(mod.Path, recursive: true);
        else File.Delete(mod.Path);
    }

    /// <summary>
    /// Installs a Thunderstore-style zip or a loose dll. Thunderstore packages keep their payload
    /// under plugins/ (or BepInEx/plugins/); a zip without that layout is treated as the plugin folder itself.
    /// </summary>
    public ModInfo Import(string sourcePath, string? id = null)
    {
        Directory.CreateDirectory(game.PluginsDir);
        if (sourcePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            var dest = System.IO.Path.Combine(game.PluginsDir, System.IO.Path.GetFileName(sourcePath));
            File.Copy(sourcePath, dest, overwrite: true);
            return new ModInfo(System.IO.Path.GetFileName(dest), dest, true, false, null);
        }

        using var zip = ZipFile.OpenRead(sourcePath);
        var manifestEntry = zip.GetEntry("manifest.json");
        ThunderstoreManifest? manifest = null;
        if (manifestEntry is not null)
        {
            var tmp = System.IO.Path.GetTempFileName();
            manifestEntry.ExtractToFile(tmp, overwrite: true);
            manifest = ThunderstoreManifest.TryRead(tmp);
            File.Delete(tmp);
        }

        id = SafeName(id ?? manifest?.Name ?? System.IO.Path.GetFileNameWithoutExtension(sourcePath));
        var modDir = System.IO.Path.Combine(game.PluginsDir, id);
        if (Directory.Exists(modDir)) Directory.Delete(modDir, recursive: true);
        Directory.CreateDirectory(modDir);

        string[] payloadRoots = ["BepInEx/plugins/", "plugins/"];
        var hasPayloadRoot = zip.Entries.Any(e => payloadRoots.Any(r => e.FullName.StartsWith(r, StringComparison.OrdinalIgnoreCase)));

        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            string rel = entry.FullName.Replace('\\', '/');
            string destRoot = modDir;

            if (hasPayloadRoot)
            {
                var root = payloadRoots.FirstOrDefault(r => rel.StartsWith(r, StringComparison.OrdinalIgnoreCase));
                if (root is not null) rel = rel[root.Length..];
                else if (rel.StartsWith("BepInEx/config/", StringComparison.OrdinalIgnoreCase) || rel.StartsWith("config/", StringComparison.OrdinalIgnoreCase))
                {
                    destRoot = game.ConfigDir;
                    rel = rel[(rel.IndexOf("config/", StringComparison.OrdinalIgnoreCase) + "config/".Length)..];
                    if (File.Exists(System.IO.Path.Combine(destRoot, rel))) continue; // never clobber user settings
                }
                else if (!IsPackageMetadata(rel)) continue;
            }

            var dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(destRoot, rel));
            if (!dest.StartsWith(System.IO.Path.GetFullPath(destRoot) + System.IO.Path.DirectorySeparatorChar))
                throw new InvalidDataException($"Zip entry escapes target folder: {entry.FullName}");
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, overwrite: true);
        }

        return new ModInfo(id, modDir, true, true, manifest);
    }

    private static bool IsPackageMetadata(string rel) =>
        rel is "manifest.json" or "README.md" or "CHANGELOG.md" or "icon.png";

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
}
