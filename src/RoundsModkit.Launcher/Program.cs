using Avalonia;
using RoundsModkit.Core;

namespace RoundsModkit.Launcher;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--status")) return PrintStatus();
        if (args.Length == 3 && args[0] == "--export-preset") return ExportPreset(args[1], args[2]);
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().WithInterFont().LogToTrace();

    private static GameInstall? FindGame() =>
        (Settings.Load().GameDir is { } dir ? GameLocator.FromGameDir(dir) : null) ?? GameLocator.Find();

    /// <summary>Writes the enabled Thunderstore mods as a preset file, e.g. into the repo's presets/.</summary>
    private static int ExportPreset(string name, string path)
    {
        if (FindGame() is not { } game)
        {
            Console.Error.WriteLine("ROUNDS not found");
            return 1;
        }
        using var http = new HttpClient { DefaultRequestHeaders = { { "User-Agent", "RoundsModkit" } } };
        var index = ThunderstoreIndex.LoadAsync(http).GetAwaiter().GetResult();
        var preset = ModPreset.FromInstalled(name, "", new ModManager(game).List(), index, out var skipped);
        preset.Save(path);
        Console.WriteLine($"Wrote {preset.Mods.Count} mods to {path}");
        if (skipped.Count > 0) Console.WriteLine($"Not included (not on Thunderstore): {string.Join(", ", skipped)}");
        return 0;
    }

    // Headless check for scripts and remote sessions without a display.
    private static int PrintStatus()
    {
        var game = FindGame();
        if (game is null)
        {
            Console.WriteLine("ROUNDS: not found");
            return 1;
        }
        var bep = new BepInExInstaller(game, new HttpClient());
        Console.WriteLine($"ROUNDS:   {game.GameDir}");
        Console.WriteLine($"Branch:   {game.Branch ?? "public"} (build {game.BuildId}){(game.IsModdingBranch ? "" : "  <- switch to old-rounds-for-mods")}");
        Console.WriteLine($"BepInEx:  {(bep.IsInstalled ? bep.InstalledVersion : "not installed")}{(bep.HasRunOnce ? "" : " (not yet run)")}");
        if (GameLauncher.NeedsProtonOverride)
            Console.WriteLine($"Proton:   launch override {(new GameLauncher(game).ProtonOverrideConfigured() ? "set" : "MISSING")}");
        foreach (var m in new ModManager(game).List())
            Console.WriteLine($"  [{(m.Enabled ? "x" : " ")}] {m.DisplayName} {m.Version}");
        return 0;
    }
}
