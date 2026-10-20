using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace RoundsModkit.Core;

public sealed class BepInExInstaller(GameInstall game, HttpClient http)
{
    // ROUNDS is a 64-bit Mono Unity build, so it needs the win_x64 Mono pack even on Linux (Proton).
    public const string Version = "5.4.23.5";
    public const string DownloadUrl =
        "https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip";
    public const string Sha256 = "82f9878551030f54657792c0740d9d51a09500eeae1fba21106b0c441e6732c4";

    private static readonly string[] LoaderFiles = ["winhttp.dll", "doorstop_config.ini", ".doorstop_version", "changelog.txt"];

    public string CoreDll => Path.Combine(game.BepInExDir, "core", "BepInEx.dll");
    public bool IsInstalled => File.Exists(CoreDll) && File.Exists(Path.Combine(game.GameDir, "winhttp.dll"));

    public string? InstalledVersion =>
        File.Exists(CoreDll) ? FileVersionInfo.GetVersionInfo(CoreDll).FileVersion : null;

    /// <summary>True once the game has started with BepInEx at least once (it writes its own config then).</summary>
    public bool HasRunOnce => File.Exists(Path.Combine(game.ConfigDir, "BepInEx.cfg"));

    public async Task InstallAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var cache = Path.Combine(AppPaths.CacheDir, $"BepInEx_win_x64_{Version}.zip");
        if (!File.Exists(cache) || !HashMatches(cache))
        {
            progress?.Report($"Downloading BepInEx {Version}…");
            Directory.CreateDirectory(AppPaths.CacheDir);
            var tmp = cache + ".part";
            await using (var src = await http.GetStreamAsync(DownloadUrl, ct))
            await using (var dst = File.Create(tmp))
                await src.CopyToAsync(dst, ct);
            if (!HashMatches(tmp))
            {
                File.Delete(tmp);
                throw new InvalidDataException("Downloaded BepInEx archive failed its SHA-256 check.");
            }
            File.Move(tmp, cache, overwrite: true);
        }

        progress?.Report("Extracting into game folder…");
        ZipFile.ExtractToDirectory(cache, game.GameDir, overwriteFiles: true);
        Directory.CreateDirectory(game.PluginsDir);
        Directory.CreateDirectory(game.ConfigDir);
        progress?.Report($"BepInEx {Version} installed.");
    }

    /// <summary>Removes the loader and core only; plugins, disabled plugins and configs are kept.</summary>
    public void Uninstall()
    {
        foreach (var f in LoaderFiles)
        {
            var p = Path.Combine(game.GameDir, f);
            if (File.Exists(p)) File.Delete(p);
        }
        var core = Path.Combine(game.BepInExDir, "core");
        if (Directory.Exists(core)) Directory.Delete(core, recursive: true);
    }

    private static bool HashMatches(string path)
    {
        using var s = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(s)).Equals(Sha256, StringComparison.OrdinalIgnoreCase);
    }
}
