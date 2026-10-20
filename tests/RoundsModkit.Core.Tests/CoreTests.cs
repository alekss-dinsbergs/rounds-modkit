using System.IO.Compression;
using RoundsModkit.Core;

namespace RoundsModkit.Core.Tests;

public class VdfTests
{
    [Fact]
    public void ParsesNestedAppManifest()
    {
        var vdf = VdfNode.Parse("""
            "AppState"
            {
                "appid"     "1557740"
                "buildid"   "6707853"
                "UserConfig" { "BetaKey" "old-rounds-for-mods" }
                // comment
                "weird"     "has { brace }"
            }
            """);
        Assert.Equal("6707853", vdf.Path("AppState", "buildid")?.Value);
        Assert.Equal("old-rounds-for-mods", vdf.Path("appstate", "userconfig", "betakey")?.Value);
        Assert.Equal("has { brace }", vdf.Path("AppState", "weird")?.Value);
    }

    [Fact]
    public void QuotedBraceValueIsNotStructural()
    {
        var vdf = VdfNode.Parse("\"a\" { \"x\" \"}\" \"y\" \"2\" }");
        Assert.Equal("}", vdf.Path("a", "x")?.Value);
        Assert.Equal("2", vdf.Path("a", "y")?.Value);
    }
}

public class ConfigTests
{
    private const string Sample = """
        ## Settings file was created by plugin Example Mod v1.0.0
        ## Plugin GUID: com.example.mod

        [General]

        ## Turns the mod on or off
        # Setting type: Boolean
        # Default value: true
        Enabled = true

        ## How fast
        # Setting type: Single
        # Default value: 1
        # Acceptable value range: From 0.1 to 5
        Speed = 1.5

        [Visual]

        # Setting type: Mode
        # Default value: Fancy
        # Acceptable values: Plain, Fancy, Extra
        Mode = Fancy

        # Setting type: Flags
        # Default value: A
        # Acceptable values: A, B
        # Multiple values can be set at the same time by separating them with , (e.g. Debug, Warning)
        Layers = A, B
        """;

    [Fact]
    public void ParsesEntriesAndMetadata()
    {
        var cfg = BepInExConfig.Parse("x.cfg", Sample);
        Assert.Equal("Example Mod v1.0.0", cfg.PluginName);
        Assert.Equal("com.example.mod", cfg.PluginGuid);
        Assert.Equal(4, cfg.Entries.Count);

        var enabled = cfg.Entries[0];
        Assert.Equal(("General", "Enabled", "true"), (enabled.Section, enabled.Key, enabled.Value));
        Assert.True(enabled.IsBoolean);
        Assert.Equal("Turns the mod on or off", enabled.Description);

        Assert.Equal("From 0.1 to 5", cfg.Entries[1].AcceptableRange);
        Assert.True(cfg.Entries[2].IsChoice);
        Assert.Equal(["Plain", "Fancy", "Extra"], cfg.Entries[2].AcceptableValues);
        Assert.True(cfg.Entries[3].IsFlags);
        Assert.False(cfg.Entries[3].IsChoice);
    }

    [Fact]
    public void SaveRewritesOnlyValueLines()
    {
        var cfg = BepInExConfig.Parse("x.cfg", Sample);
        cfg.Entries[0].Value = "false";
        cfg.Entries[2].Value = "Extra";
        var outText = cfg.Serialize().Replace("\r\n", "\n");

        var expected = Sample.Replace("Enabled = true", "Enabled = false").Replace("Mode = Fancy", "Mode = Extra");
        Assert.Equal(expected, outText);
    }
}

public sealed class ModManagerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rmk-").FullName;
    private readonly GameInstall _game;

    public ModManagerTests()
    {
        File.WriteAllText(Path.Combine(_dir, "Rounds.exe"), "");
        _game = new GameInstall(_dir, _dir, _dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string MakeZip(params (string name, string content)[] entries)
    {
        var path = Path.Combine(_dir, $"pkg{Guid.NewGuid():N}.zip");
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            using var w = new StreamWriter(zip.CreateEntry(name).Open());
            w.Write(content);
        }
        return path;
    }

    [Fact]
    public void ImportsThunderstorePackageWithPluginsFolder()
    {
        Directory.CreateDirectory(_game.ConfigDir);
        File.WriteAllText(Path.Combine(_game.ConfigDir, "keep.cfg"), "user");
        var zip = MakeZip(
            ("manifest.json", """{"name":"CoolMod","version_number":"1.2.3","dependencies":["willis81808-UnboundLib-3.2.0"]}"""),
            ("icon.png", "x"),
            ("plugins/CoolMod.dll", "dll"),
            ("config/keep.cfg", "packaged"),
            ("config/new.cfg", "packaged"),
            ("unrelated/thing.txt", "x"));

        var mods = new ModManager(_game);
        var mod = mods.Import(zip);

        Assert.Equal("CoolMod", mod.Id);
        Assert.Equal("1.2.3", mod.Version);
        Assert.True(File.Exists(Path.Combine(_game.PluginsDir, "CoolMod", "CoolMod.dll")));
        Assert.True(File.Exists(Path.Combine(_game.PluginsDir, "CoolMod", "manifest.json")));
        Assert.False(File.Exists(Path.Combine(_game.PluginsDir, "CoolMod", "thing.txt")));
        Assert.Equal("user", File.ReadAllText(Path.Combine(_game.ConfigDir, "keep.cfg")));
        Assert.Equal("packaged", File.ReadAllText(Path.Combine(_game.ConfigDir, "new.cfg")));
    }

    [Fact]
    public void ImportsFlatPackageAndToggles()
    {
        var zip = MakeZip(("manifest.json", """{"name":"Flat","version_number":"0.1.0"}"""), ("Flat.dll", "dll"));
        var mods = new ModManager(_game);
        var mod = mods.Import(zip);
        Assert.True(File.Exists(Path.Combine(_game.PluginsDir, "Flat", "Flat.dll")));

        var disabled = mods.SetEnabled(mod, false);
        Assert.False(Directory.Exists(Path.Combine(_game.PluginsDir, "Flat")));
        Assert.True(Directory.Exists(Path.Combine(_game.DisabledPluginsDir, "Flat")));
        Assert.Single(mods.List(), m => m.Id == "Flat" && !m.Enabled);

        mods.SetEnabled(disabled, true);
        Assert.Single(mods.List(), m => m.Id == "Flat" && m.Enabled);
    }

    [Fact]
    public void RejectsPathTraversal()
    {
        var zip = MakeZip(("../../evil.dll", "x"));
        Assert.Throws<InvalidDataException>(() => new ModManager(_game).Import(zip));
    }
}
