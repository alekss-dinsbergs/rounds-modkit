using RoundsModkit.Core;

namespace RoundsModkit.Core.Tests;

public sealed class ThunderstoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rmk-ts-").FullName;
    private readonly GameInstall _game;

    public ThunderstoreTests()
    {
        File.WriteAllText(Path.Combine(_dir, "Rounds.exe"), "");
        _game = new GameInstall(_dir, _dir, _dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static ThunderstorePackage Pkg(string fullName, string version = "1.0.0", string[]? deps = null,
        bool deprecated = false, bool nsfw = false, long downloads = 10, string desc = "", string[]? cats = null)
    {
        var p = fullName.Split('-');
        return new ThunderstorePackage(p[1], fullName, p[0], $"https://thunderstore.io/c/rounds/p/{p[0]}/{p[1]}/",
            DateTimeOffset.UnixEpoch, 0, deprecated, nsfw, cats ?? ["Mods"],
            [new ThunderstoreVersion(version, desc, "", deps ?? [], "", downloads, 1)]);
    }

    private static ThunderstoreIndex Index(params ThunderstorePackage[] p) => new(p, DateTime.Now);

    private void InstallFolder(string id, string name, string version, bool enabled = true)
    {
        var dir = Path.Combine(enabled ? _game.PluginsDir : _game.DisabledPluginsDir, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "manifest.json"), $$"""{"name":"{{name}}","version_number":"{{version}}"}""");
    }

    private InstalledMods Installed() => new(new ModManager(_game).List());

    [Fact]
    public void ParsesApiShape()
    {
        var json = """
            [{"name":"UnboundLib","full_name":"willis81808-UnboundLib","owner":"willis81808",
              "package_url":"https://thunderstore.io/c/rounds/p/willis81808/UnboundLib/",
              "date_updated":"2024-01-02T03:04:05Z","rating_score":5,"is_deprecated":false,"has_nsfw_content":false,
              "categories":["Libraries"],"extra_field":1,
              "versions":[{"version_number":"3.2.14","description":"lib","icon":"i","dependencies":["BepInEx-BepInExPack_ROUNDS-5.4.1900"],
                           "download_url":"d","downloads":100,"file_size":5},
                          {"version_number":"3.2.13","description":"old","icon":"i","dependencies":[],"download_url":"d","downloads":50,"file_size":5}]}]
            """;
        var index = ThunderstoreIndex.Parse(json, DateTime.Now);
        var p = Assert.Single(index.Packages);
        Assert.Equal("3.2.14", p.Latest.VersionNumber);
        Assert.Equal(150, p.TotalDownloads);
        Assert.Equal(["Libraries"], index.Categories);
    }

    [Fact]
    public void HidesLoaderPackNsfwAndManagerButSearchRanksNameFirst()
    {
        var index = Index(
            Pkg("BepInEx-BepInExPack_ROUNDS"),
            Pkg("ebkr-r2modman"),
            Pkg("x-Spicy", nsfw: true),
            Pkg("a-CardPack", downloads: 5, desc: "more cards"),
            Pkg("b-Cards", downloads: 1),
            Pkg("c-Other", downloads: 100, desc: "adds cards"),
            Pkg("d-OldCards", deprecated: true));

        Assert.Equal(["c-Other", "a-CardPack", "b-Cards"], index.Search(null, null, false).Select(p => p.FullName));
        Assert.Equal(["b-Cards", "c-Other", "a-CardPack"], index.Search("cards", null, false).Select(p => p.FullName));
        Assert.Contains(index.Search("cards", null, true), p => p.FullName == "d-OldCards");
    }

    [Fact]
    public void PlanInstallsMissingDependenciesFirstAndSkipsLoaderPack()
    {
        var index = Index(
            Pkg("m-Mod", deps: ["BepInEx-BepInExPack_ROUNDS-5.4.1900", "w-Unbound-3.0.0", "w-ModdingUtils-1.0.0"]),
            Pkg("w-Unbound", deps: ["w-MMHook-1.0.0"]),
            Pkg("w-MMHook"),
            Pkg("w-ModdingUtils", deps: ["w-Unbound-3.0.0", "gone-Missing-1.0.0"]));

        var plan = index.Plan(index.Find("m-Mod")!, Installed());
        Assert.Equal(["w-MMHook", "w-Unbound", "w-ModdingUtils", "m-Mod"], plan.ToInstall.Select(p => p.Package.FullName));
        Assert.Equal(["gone-Missing-1.0.0"], plan.Unresolved);
    }

    [Fact]
    public void PlanSkipsInstalledReEnablesDisabledAndKeepsAuthorsApart()
    {
        InstallFolder("w-MMHook", "MMHook", "1.0.0", enabled: false);
        // Same package name from another author must not satisfy the dependency.
        InstallFolder("olavim-Unbound", "Unbound", "3.2.13");
        var index = Index(Pkg("m-Mod", deps: ["w-Unbound-3.0.0", "w-MMHook-1.0.0"]), Pkg("w-Unbound"), Pkg("w-MMHook"));

        var plan = index.Plan(index.Find("m-Mod")!, Installed());
        Assert.Equal(["w-Unbound", "m-Mod"], plan.ToInstall.Select(p => p.Package.FullName));
        Assert.Equal("w-MMHook", Assert.Single(plan.ToEnable).Id);
    }

    [Fact]
    public void ManualImportsMatchByManifestName()
    {
        InstallFolder("Unbound", "Unbound", "3.0.0");
        Assert.True(Installed().Has(PackageRef.Parse("w-Unbound-3.0.0")!.Value));
    }

    [Theory]
    [InlineData("1.2.0", "1.1.9", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.10.0", "1.9.0", true)]
    [InlineData("1.0.0", null, true)]
    public void VersionComparison(string candidate, string? installed, bool newer) =>
        Assert.Equal(newer, InstalledMods.IsNewer(candidate, installed));

    // Hits thunderstore.io, so it only runs with RMK_NETWORK_TESTS=1.
    [Fact]
    public async Task RealApiInstallsUnboundLibWithDependencies()
    {
        if (Environment.GetEnvironmentVariable("RMK_NETWORK_TESTS") != "1") return;
        using var http = new HttpClient { DefaultRequestHeaders = { { "User-Agent", "RoundsModkit-tests" } } };
        var index = await ThunderstoreIndex.LoadAsync(http);
        Assert.True(index.Packages.Count > 500);

        var plan = index.Plan(index.Find("willis81808-UnboundLib")!, Installed());
        Assert.Contains(plan.ToInstall, p => p.Package.FullName == "willis81808-MMHook");
        Assert.Equal("willis81808-UnboundLib", plan.ToInstall[^1].Package.FullName);

        await new ThunderstoreInstaller(_game, http).InstallAsync(plan);
        var installed = Installed();
        foreach (var (p, v) in plan.ToInstall)
        {
            var mod = installed.Get(PackageRef.Parse(p.FullName)!.Value);
            Assert.NotNull(mod);
            Assert.Equal(v.VersionNumber, mod!.Version);
        }
        Assert.NotEmpty(Directory.GetFiles(Path.Combine(_game.PluginsDir, "willis81808-UnboundLib"), "*.dll", SearchOption.AllDirectories));
        Assert.Empty(index.Plan(index.Find("willis81808-UnboundLib")!, installed).ToInstall.Skip(1));
    }
}
