using System.Diagnostics;

namespace RoundsModkit.Core;

public sealed class GameLauncher(GameInstall game)
{
    // Wine ships its own builtin winhttp.dll; without a native-first override Proton never
    // loads BepInEx's doorstop proxy and the game starts unmodded with no error.
    public const string ProtonLaunchOptions = "WINEDLLOVERRIDES=\"winhttp=n,b\" %command%";

    public static bool NeedsProtonOverride => !OperatingSystem.IsWindows();

    /// <summary>
    /// Launch options Steam has stored for ROUNDS, per Steam user. Steam rewrites localconfig.vdf
    /// on exit, so this only reflects changes made while Steam is running after it saves.
    /// </summary>
    public IReadOnlyDictionary<string, string> LaunchOptionsByUser()
    {
        var result = new Dictionary<string, string>();
        var userdata = Path.Combine(game.SteamRoot, "userdata");
        if (!Directory.Exists(userdata)) return result;
        foreach (var userDir in Directory.EnumerateDirectories(userdata))
        {
            var cfg = Path.Combine(userDir, "config", "localconfig.vdf");
            if (!File.Exists(cfg)) continue;
            // Child lookups are case-insensitive, which covers Steam's mixed "apps"/"Apps" spelling.
            var apps = VdfNode.Load(cfg).Path("UserLocalConfigStore", "Software", "Valve", "Steam", "apps");
            var opts = apps?[GameInstall.AppId]?["LaunchOptions"]?.Value;
            if (opts is not null) result[Path.GetFileName(userDir)] = opts;
        }
        return result;
    }

    public bool ProtonOverrideConfigured() =>
        LaunchOptionsByUser().Values.Any(o => o.Contains("winhttp=n,b") || o.Contains("winhttp=native,builtin"));

    /// <summary>Starts the game through Steam so Steamworks and Photon auth work the same as a normal launch.</summary>
    public void Launch()
    {
        var url = $"steam://rungameid/{GameInstall.AppId}";
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        else
            Process.Start(new ProcessStartInfo("xdg-open", url) { UseShellExecute = false });
    }

    public static void OpenFolder(string path)
    {
        Directory.CreateDirectory(path);
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\""));
        else
            Process.Start(new ProcessStartInfo("xdg-open", path) { UseShellExecute = false });
    }
}
