namespace RoundsModkit.Core;

public sealed record GameInstall(string GameDir, string SteamRoot, string LibraryDir)
{
    public const string AppId = "1557740";
    public const string ModdingBranch = "old-rounds-for-mods";

    public string ManagedDir => System.IO.Path.Combine(GameDir, "ROUNDS_Data", "Managed");
    public string BepInExDir => System.IO.Path.Combine(GameDir, "BepInEx");
    public string PluginsDir => System.IO.Path.Combine(BepInExDir, "plugins");
    public string DisabledPluginsDir => System.IO.Path.Combine(BepInExDir, "plugins-disabled");
    public string ConfigDir => System.IO.Path.Combine(BepInExDir, "config");
    public string AppManifestPath => System.IO.Path.Combine(LibraryDir, "steamapps", $"appmanifest_{AppId}.acf");

    /// <summary>Steam beta branch from the app manifest; null means the default (public) branch.</summary>
    public string? Branch
    {
        get
        {
            if (!File.Exists(AppManifestPath)) return null;
            var m = VdfNode.Load(AppManifestPath);
            var key = m.Path("AppState", "UserConfig", "BetaKey")?.Value
                      ?? m.Path("AppState", "MountedConfig", "BetaKey")?.Value;
            return string.IsNullOrEmpty(key) ? null : key;
        }
    }

    public string? BuildId => File.Exists(AppManifestPath)
        ? VdfNode.Load(AppManifestPath).Path("AppState", "buildid")?.Value
        : null;

    public bool IsModdingBranch => Branch == ModdingBranch;
}

public static class GameLocator
{
    public static IEnumerable<string> CandidateSteamRoots()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            if (ReadWindowsSteamPath() is { } reg) yield return reg;
            yield return @"C:\Program Files (x86)\Steam";
            yield return @"C:\Program Files\Steam";
        }
        else
        {
            yield return System.IO.Path.Combine(home, ".local", "share", "Steam");
            yield return System.IO.Path.Combine(home, ".steam", "steam");
            yield return System.IO.Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        }
    }

    public static GameInstall? Find()
    {
        var seen = new HashSet<string>();
        foreach (var root in CandidateSteamRoots())
        {
            if (!Directory.Exists(root)) continue;
            var real = ResolveLinks(root);
            if (!seen.Add(real)) continue;
            foreach (var lib in LibraryDirs(root))
            {
                var dir = System.IO.Path.Combine(lib, "steamapps", "common", "ROUNDS");
                if (File.Exists(System.IO.Path.Combine(dir, "Rounds.exe")))
                    return new GameInstall(dir, root, lib);
            }
        }
        return null;
    }

    /// <summary>Accepts a user-picked game folder (the one containing Rounds.exe).</summary>
    public static GameInstall? FromGameDir(string gameDir)
    {
        if (!File.Exists(System.IO.Path.Combine(gameDir, "Rounds.exe"))) return null;
        var common = Directory.GetParent(gameDir);
        var lib = common?.Parent?.Parent?.FullName ?? gameDir;
        var steamRoot = CandidateSteamRoots().FirstOrDefault(Directory.Exists) ?? lib;
        return new GameInstall(gameDir, steamRoot, lib);
    }

    public static IEnumerable<string> LibraryDirs(string steamRoot)
    {
        yield return steamRoot;
        var vdf = System.IO.Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        var root = VdfNode.Load(vdf)["libraryfolders"];
        if (root is null) yield break;
        foreach (var entry in root.Children.Values)
        {
            var p = entry["path"]?.Value;
            if (!string.IsNullOrEmpty(p)) yield return p;
        }
    }

    private static string ResolveLinks(string path)
    {
        try { return new DirectoryInfo(path).ResolveLinkTarget(true)?.FullName ?? System.IO.Path.GetFullPath(path); }
        catch (IOException) { return System.IO.Path.GetFullPath(path); }
    }

    private static string? ReadWindowsSteamPath()
    {
        if (!OperatingSystem.IsWindows()) return null;
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        return (key?.GetValue("SteamPath") as string)?.Replace('/', '\\');
    }
}
