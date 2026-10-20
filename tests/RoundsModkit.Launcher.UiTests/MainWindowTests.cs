using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using RoundsModkit.Core;
using RoundsModkit.Launcher.ViewModels;
using RoundsModkit.Launcher.Views;

[assembly: AvaloniaTestApplication(typeof(RoundsModkit.Launcher.UiTests.TestApp))]

namespace RoundsModkit.Launcher.UiTests;

public static class TestApp
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseSkia()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class MainWindowTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rmk-ui-").FullName;
    private readonly GameInstall _game;

    // Set RMK_SCREENSHOT_DIR to keep rendered PNGs for a visual check.
    private static readonly string? ShotDir = Environment.GetEnvironmentVariable("RMK_SCREENSHOT_DIR");

    public MainWindowTests()
    {
        File.WriteAllText(Path.Combine(_dir, "Rounds.exe"), "");
        _game = new GameInstall(_dir, _dir, _dir);
        Directory.CreateDirectory(Path.Combine(_game.PluginsDir, "CoolMod"));
        File.WriteAllText(Path.Combine(_game.PluginsDir, "CoolMod", "manifest.json"),
            """{"name":"CoolMod","version_number":"1.2.3","description":"Adds cool cards","dependencies":["willis81808-UnboundLib-3.2.0","BepInEx-BepInExPack_ROUNDS-5.4.1900"]}""");
        Directory.CreateDirectory(_game.DisabledPluginsDir);
        File.WriteAllText(Path.Combine(_game.DisabledPluginsDir, "OldMod.dll"), "");
        Directory.CreateDirectory(_game.ConfigDir);
        File.WriteAllText(Path.Combine(_game.ConfigDir, "com.roundsmodkit.example.cfg"), """
            ## Settings file was created by plugin Modkit Example v0.1.0
            ## Plugin GUID: com.roundsmodkit.example

            [General]

            ## Turns the example patch on or off.
            # Setting type: Boolean
            # Default value: true
            Enabled = true

            ## How much to log when a card pick starts.
            # Setting type: PickLogStyle
            # Default value: Short
            # Acceptable values: Off, Short, Verbose
            PickLogStyle = Short

            # Setting type: Single
            # Default value: 1
            # Acceptable value range: From 0.1 to 5
            Speed = 1
            """);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private (MainWindow, MainViewModel) Open()
    {
        var window = new MainWindow { Width = 980, Height = 680 };
        var vm = new MainViewModel(window, _game);
        window.DataContext = vm;
        window.Show();
        return (window, vm);
    }

    private static void Shot(Window window, string name)
    {
        Dispatcher();
        var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        if (ShotDir is not null)
        {
            Directory.CreateDirectory(ShotDir);
            frame!.Save(Path.Combine(ShotDir, name + ".png"));
        }
    }

    private static void Dispatcher() => Avalonia.Threading.Dispatcher.UIThread.RunJobs();

    private static void SelectTab(Window window, int index)
    {
        window.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = index;
        Dispatcher();
    }

    [AvaloniaFact]
    public void RendersAllTabsAgainstFakeInstall()
    {
        var (window, vm) = Open();

        Assert.Equal(_game.GameDir, vm.GameDir);
        Assert.False(vm.BranchOk);
        Assert.Equal("BepInEx not installed", vm.BepInExText);
        Shot(window, "1-game");

        SelectTab(window, 3);
        Assert.Equal(2, vm.Mods.Count);
        var cool = vm.Mods.Single(m => m.Name == "CoolMod");
        Assert.Equal("Missing: UnboundLib", cool.MissingDependencies);
        Assert.False(vm.Mods.Single(m => m.Name == "OldMod.dll").IsEnabled);
        Shot(window, "2-mods");

        SelectTab(window, 4);
        vm.SelectedConfig = vm.Configs.Single();
        Dispatcher();
        Assert.Equal("Modkit Example v0.1.0", vm.SelectedConfig.Title);
        Assert.Equal(3, vm.SelectedConfig.Entries.Count);
        Shot(window, "3-config");
        window.Close();
    }

    [AvaloniaFact]
    public void BrowseTabShowsStatusesAndDependencyCounts()
    {
        var (window, vm) = Open();
        static ThunderstorePackage Pkg(string full, string version, string desc, params string[] deps)
        {
            var p = full.Split('-');
            return new ThunderstorePackage(p[1], full, p[0], "https://thunderstore.io", DateTimeOffset.UnixEpoch, 0, false, false,
                ["Mods"], [new ThunderstoreVersion(version, desc, "", deps, "", 1000, 1)]);
        }
        // CoolMod is installed at 1.2.3 under its manifest name, so 1.3.0 shows as an update.
        vm.Browser.UseIndex(new ThunderstoreIndex([
            Pkg("someone-CoolMod", "1.3.0", "Adds cool cards"),
            Pkg("willis81808-UnboundLib", "3.2.14", "Core library for ROUNDS mods", "willis81808-MMHook-1.0.0", "BepInEx-BepInExPack_ROUNDS-5.4.1900"),
            Pkg("willis81808-MMHook", "1.0.0", "Hook generator output"),
            Pkg("pykess-ModdingUtils", "0.4.8", "Shared modding helpers", "willis81808-UnboundLib-3.2.0"),
        ], DateTime.Now));
        SelectTab(window, MainViewModel.BrowseTab);

        var utils = vm.Browser.Results.Single(r => r.Package.Name == "ModdingUtils");
        Assert.Equal("Install + 2 dependencies", utils.ActionText);
        var cool = vm.Browser.Results.Single(r => r.Package.Name == "CoolMod");
        Assert.Equal("Update: 1.2.3 → 1.3.0", cool.StatusText);
        vm.Browser.Selected = utils;
        Shot(window, "4-browse");

        vm.Browser.SearchText = "hook";
        Assert.Equal("willis81808-MMHook", Assert.Single(vm.Browser.Results).Package.FullName);
        window.Close();
    }

    // Hits thunderstore.io, so it only runs with RMK_NETWORK_TESTS=1.
    [AvaloniaFact]
    public async Task BrowseTabAgainstRealThunderstore()
    {
        if (Environment.GetEnvironmentVariable("RMK_NETWORK_TESTS") != "1") return;
        var (window, vm) = Open();
        SelectTab(window, MainViewModel.BrowseTab);
        for (int i = 0; i < 300 && vm.Browser.Results.Count == 0; i++) { await Task.Delay(100); Dispatcher(); }
        Assert.NotEmpty(vm.Browser.Results);

        vm.Browser.SearchText = "UnboundLib";
        Assert.Equal("willis81808-UnboundLib", vm.Browser.Results[0].Package.FullName);
        vm.Browser.SearchText = "";
        // Let every icon load finish so no work is left on the headless dispatcher at teardown.
        for (int i = 0; i < 600 && vm.Browser.Results.Any(r => !r.IconSettled); i++) { await Task.Delay(100); Dispatcher(); }
        Assert.All(vm.Browser.Results, r => Assert.True(r.IconSettled));
        Shot(window, "5-browse-live");
        window.Close();
    }

    [AvaloniaFact]
    public void PresetsTabShowsPlanForPreset()
    {
        var (window, vm) = Open();
        static ThunderstorePackage Pkg(string full, string version, params string[] deps)
        {
            var p = full.Split('-');
            return new ThunderstorePackage(p[1], full, p[0], "", DateTimeOffset.UnixEpoch, 0, false, false,
                ["Mods"], [new ThunderstoreVersion(version, "", "", deps, "", 1, 1)]);
        }
        var index = new ThunderstoreIndex([
            Pkg("olavim-RoundsWithFriends", "2.2.2", "willis81808-UnboundLib-2.11.0"),
            Pkg("willis81808-UnboundLib", "3.2.14", "willis81808-MMHook-1.0.0"),
            Pkg("willis81808-MMHook", "1.0.0"),
        ], DateTime.Now);
        vm.Presets.UseIndex(index);

        var preset = new ModPreset
        {
            Name = "Rounds With Friends",
            Description = "Up to 16 players and team modes.",
            Mods = ["olavim-RoundsWithFriends-2.2.2", "willis81808-MMHook-1.0.0", "willis81808-UnboundLib-3.2.14"],
        };
        var item = new PresetItemViewModel(new PresetSources.Entry("Repo", "rounds-with-friends.json", preset, null));
        vm.Presets.Presets.Add(item);
        item.UpdatePlan(index, new ModManager(_game).List());
        vm.Presets.Selected = item;

        // The fixture has CoolMod (with a manifest) enabled, so exclusive mode disables it.
        Assert.Equal("Will install 3, disable 1", item.PlanText);
        Assert.Contains("RoundsWithFriends 2.2.2  ·  olavim  ·  not installed", item.ModLines);

        SelectTab(window, MainViewModel.PresetsTab);
        Assert.Same(item, vm.Presets.Selected);
        Shot(window, "6-presets");
        window.Close();
    }

    [AvaloniaFact]
    public void TogglingModAndSavingConfigTouchDisk()
    {
        var (window, vm) = Open();

        vm.Mods.Single(m => m.Name == "CoolMod").IsEnabled = false;
        Assert.True(Directory.Exists(Path.Combine(_game.DisabledPluginsDir, "CoolMod")));
        Assert.False(Directory.Exists(Path.Combine(_game.PluginsDir, "CoolMod")));

        var cfg = vm.Configs.Single();
        vm.SelectedConfig = cfg;
        cfg.Entries.Single(e => e.Key == "Enabled").BoolValue = false;
        cfg.Entries.Single(e => e.Key == "PickLogStyle").Value = "Verbose";
        Assert.True(cfg.Dirty);
        cfg.SaveCommand.Execute(null);
        Assert.False(cfg.Dirty);

        var text = File.ReadAllText(Path.Combine(_game.ConfigDir, "com.roundsmodkit.example.cfg"));
        Assert.Contains("Enabled = false", text);
        Assert.Contains("PickLogStyle = Verbose", text);
        Assert.Contains("## How much to log when a card pick starts.", text);
        window.Close();
    }
}
