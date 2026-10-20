using RoundsModkit.Core;

namespace RoundsModkit.Core.Tests;

public sealed class PresetTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rmk-preset-").FullName;
    private readonly GameInstall _game;

    public PresetTests()
    {
        File.WriteAllText(Path.Combine(_dir, "Rounds.exe"), "");
        _game = new GameInstall(_dir, _dir, _dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ThunderstorePackage Pkg(string fullName, string[] versions, string[]? deps = null)
    {
        var p = fullName.Split('-');
        return new ThunderstorePackage(p[1], fullName, p[0], "", DateTimeOffset.UnixEpoch, 0, false, false, ["Mods"],
            versions.Select(v => new ThunderstoreVersion(v, "", "", deps ?? [], "", 1, 1)).ToArray());
    }

    private void Installed(string id, string name, string? version, bool enabled = true, string[]? deps = null)
    {
        var dir = Path.Combine(enabled ? _game.PluginsDir : _game.DisabledPluginsDir, id);
        Directory.CreateDirectory(dir);
        if (version is null) return; // a local plugin without a Thunderstore manifest
        var depJson = string.Join(",", (deps ?? []).Select(d => $"\"{d}\""));
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            $$"""{"name":"{{name}}","version_number":"{{version}}","dependencies":[{{depJson}}]}""");
    }

    private IReadOnlyList<ModInfo> Mods() => new ModManager(_game).List();

    private static readonly ThunderstoreIndex Index = new([
        Pkg("olavim-RoundsWithFriends", ["2.2.3", "2.2.2"], ["BepInEx-BepInExPack_ROUNDS-5.4.1100", "willis81808-UnboundLib-2.11.0"]),
        Pkg("willis81808-UnboundLib", ["3.2.14", "3.2.13"], ["willis81808-MMHook-1.0.0"]),
        Pkg("willis81808-MMHook", ["1.0.0"]),
        Pkg("someone-CardPack", ["1.0.0"], ["willis81808-UnboundLib-3.0.0"]),
    ], DateTime.Now);

    [Fact]
    public void RoundTripsAndRejectsNewerFormat()
    {
        var p = new ModPreset { Name = "RWF", Description = "4 players", Mods = ["olavim-RoundsWithFriends-2.2.2"] };
        var json = p.Serialize();
        Assert.DoesNotContain("Refs", json);
        var back = ModPreset.Parse(json);
        Assert.Equal(("RWF", "4 players", true), (back.Name, back.Description, back.Exclusive));
        Assert.Equal(["olavim-RoundsWithFriends-2.2.2"], back.Mods);
        Assert.Throws<InvalidDataException>(() => ModPreset.Parse("""{"format":99,"name":"x","mods":[]}"""));
        Assert.Throws<InvalidDataException>(() => ModPreset.Parse("""{"mods":[]}"""));
        Assert.Equal("rounds-with-friends.json", ModPreset.FileNameFor(" Rounds With Friends! "));
    }

    [Fact]
    public void ExportsEnabledThunderstoreModsWithVersions()
    {
        Installed("olavim-RoundsWithFriends", "RoundsWithFriends", "2.2.2");
        Installed("willis81808-UnboundLib", "UnboundLib", "3.2.14");
        Installed("MMHook", "MMHook", "1.0.0");                 // manual import, resolved by name
        Installed("someone-CardPack", "CardPack", "1.0.0", enabled: false);
        Installed("RoundsModkit.ControllerFix", "", null);       // local plugin

        var preset = ModPreset.FromInstalled("Mine", "", Mods(), Index, out var skipped);
        Assert.Equal(["olavim-RoundsWithFriends-2.2.2", "willis81808-MMHook-1.0.0", "willis81808-UnboundLib-3.2.14"], preset.Mods);
        Assert.Equal(["RoundsModkit.ControllerFix"], skipped);
    }

    [Fact]
    public void ExclusiveApplyDisablesOthersButKeepsDependenciesAndLocalPlugins()
    {
        Installed("olavim-RoundsWithFriends", "RoundsWithFriends", "2.2.2", deps: ["willis81808-UnboundLib-2.11.0"]);
        Installed("willis81808-UnboundLib", "UnboundLib", "3.2.14", deps: ["willis81808-MMHook-1.0.0"]);
        Installed("willis81808-MMHook", "MMHook", "1.0.0");
        Installed("someone-CardPack", "CardPack", "1.0.0");
        Installed("RoundsModkit.ControllerFix", "", null);

        var preset = new ModPreset { Name = "RWF only", Mods = ["olavim-RoundsWithFriends-2.2.2"] };
        var plan = PresetApplier.Plan(preset, Index, Mods());
        Assert.Empty(plan.Install.ToInstall);
        Assert.Equal("someone-CardPack", Assert.Single(plan.ToDisable).Id);
        Assert.Equal("Will disable 1", plan.Summary());

        preset.Exclusive = false;
        Assert.True(PresetApplier.Plan(preset, Index, Mods()).IsNoOp);
    }

    [Fact]
    public void PinsVersionsEvenWhenReachedAsDependency()
    {
        Installed("willis81808-UnboundLib", "UnboundLib", "3.2.14", enabled: false);
        // CardPack pulls UnboundLib in as a dependency before the preset's own pinned entry for it.
        var preset = new ModPreset { Name = "Pinned", Mods = ["someone-CardPack", "willis81808-UnboundLib-3.2.13"] };

        var plan = PresetApplier.Plan(preset, Index, Mods());
        Assert.Equal(
            ["willis81808-MMHook@1.0.0", "willis81808-UnboundLib@3.2.13", "someone-CardPack@1.0.0"],
            plan.Install.ToInstall.Select(i => $"{i.Package.FullName}@{i.Version.VersionNumber}"));
        Assert.Empty(plan.Install.Unresolved);

        var missing = PresetApplier.Plan(new ModPreset { Name = "Old", Mods = ["willis81808-UnboundLib-1.0.0", "gone-Mod"] }, Index, Mods());
        Assert.Equal(["willis81808-UnboundLib-1.0.0 (version no longer on Thunderstore)", "gone-Mod"], missing.Install.Unresolved);
    }

    // Hits api.github.com, so it only runs with RMK_NETWORK_TESTS=1.
    [Fact]
    public async Task LoadsPresetsFromPublicRepo()
    {
        if (Environment.GetEnvironmentVariable("RMK_NETWORK_TESTS") != "1") return;
        using var http = new HttpClient { DefaultRequestHeaders = { { "User-Agent", "RoundsModkit-tests" } } };
        var entries = await PresetSources.LoadRepoAsync(http);
        var rwf = Assert.Single(entries, e => e.FileName == "rounds-with-friends.json");
        Assert.Contains("olavim-RoundsWithFriends-2.2.2", rwf.Preset.Mods);
    }
}
