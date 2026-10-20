using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoundsModkit.Core;

namespace RoundsModkit.Launcher.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private static readonly HttpClient Http = new() { DefaultRequestHeaders = { { "User-Agent", "RoundsModkit" } } };

    private readonly Window _window;
    private readonly Settings _settings = Settings.Load();
    private GameInstall? _game;

    [ObservableProperty] private string _gameDir = "not found";
    [ObservableProperty] private string _branchText = "";
    [ObservableProperty] private bool _branchOk;
    [ObservableProperty] private string _bepInExText = "";
    [ObservableProperty] private bool _bepInExInstalled;
    [ObservableProperty] private bool _protonOverrideMissing;
    [ObservableProperty] private string _log = "";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private ConfigFileViewModel? _selectedConfig;
    [ObservableProperty] private int _selectedTab;

    public const int BrowseTab = 1;
    public const int PresetsTab = 2;
    public BrowserViewModel Browser { get; }
    public PresetsViewModel Presets { get; }

    public bool ShowProtonHint => GameLauncher.NeedsProtonOverride;
    public string ProtonLaunchOptions => GameLauncher.ProtonLaunchOptions;
    public bool HasGame => _game is not null;

    public ObservableCollection<ModItemViewModel> Mods { get; } = [];
    public ObservableCollection<ConfigFileViewModel> Configs { get; } = [];

    public MainViewModel(Window window)
        : this(window, (Settings.Load().GameDir is { } d ? GameLocator.FromGameDir(d) : null) ?? GameLocator.Find())
    {
    }

    public MainViewModel(Window window, GameInstall? game)
    {
        _window = window;
        Browser = new BrowserViewModel(Http, () => _game, Report, Refresh);
        Presets = new PresetsViewModel(window, Http, () => _game, Report, Refresh);
        SetGame(game);
    }

    partial void OnSelectedConfigChanged(ConfigFileViewModel? value) => value?.EnsureLoaded();

    async partial void OnSelectedTabChanged(int value)
    {
        if (value == BrowseTab)
        {
            Browser.RefreshInstalled();
            await Browser.EnsureLoadedAsync();
        }
        else if (value == PresetsTab)
        {
            Presets.RefreshPlans();
            await Presets.EnsureLoadedAsync();
        }
    }

    private void SetGame(GameInstall? game)
    {
        _game = game;
        OnPropertyChanged(nameof(HasGame));
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        Mods.Clear();
        Configs.Clear();
        SelectedConfig = null;
        if (_game is null)
        {
            GameDir = "ROUNDS not found — use Browse to pick the folder containing Rounds.exe";
            BranchText = BepInExText = "";
            return;
        }

        GameDir = _game.GameDir;
        BranchOk = _game.IsModdingBranch;
        BranchText = BranchOk
            ? $"Branch: {GameInstall.ModdingBranch} (build {_game.BuildId})"
            : $"Branch: {_game.Branch ?? "public"} — most mods need '{GameInstall.ModdingBranch}'. Steam → ROUNDS → Properties → Betas.";

        var bep = new BepInExInstaller(_game, Http);
        BepInExInstalled = bep.IsInstalled;
        BepInExText = bep.IsInstalled
            ? $"BepInEx {bep.InstalledVersion}{(bep.HasRunOnce ? "" : " — launch once so it generates its config files")}"
            : "BepInEx not installed";
        ProtonOverrideMissing = GameLauncher.NeedsProtonOverride && !new GameLauncher(_game).ProtonOverrideConfigured();

        var manager = new ModManager(_game);
        var mods = manager.List();
        var installed = new InstalledMods(mods);
        foreach (var m in mods) Mods.Add(new ModItemViewModel(manager, m, installed, Report));
        Browser.RefreshInstalled();

        if (Directory.Exists(_game.ConfigDir))
            foreach (var f in Directory.EnumerateFiles(_game.ConfigDir, "*.cfg").OrderBy(Path.GetFileName))
                Configs.Add(new ConfigFileViewModel(f, Report));
    }

    [RelayCommand]
    private async Task BrowseAsync()
    {
        var picked = await _window.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select the ROUNDS folder (contains Rounds.exe)",
        });
        var path = picked.FirstOrDefault()?.TryGetLocalPath();
        if (path is null) return;
        var game = GameLocator.FromGameDir(path);
        if (game is null)
        {
            Report($"No Rounds.exe in {path}");
            return;
        }
        _settings.GameDir = path;
        _settings.Save();
        SetGame(game);
    }

    [RelayCommand]
    private async Task InstallBepInExAsync()
    {
        if (_game is null) return;
        Busy = true;
        try
        {
            await new BepInExInstaller(_game, Http).InstallAsync(new Progress<string>(Report));
        }
        catch (Exception e) { Report($"Install failed: {e.Message}"); }
        finally { Busy = false; Refresh(); }
    }

    [RelayCommand]
    private void UninstallBepInEx()
    {
        if (_game is null) return;
        Run(() => new BepInExInstaller(_game, Http).Uninstall(), "BepInEx loader removed (mods and configs kept).");
        Refresh();
    }

    [RelayCommand]
    private void Launch()
    {
        if (_game is null) return;
        if (ProtonOverrideMissing)
            Report("Warning: Steam launch options for ROUNDS don't include the winhttp override, so mods won't load under Proton.");
        Run(() => new GameLauncher(_game).Launch(), "Launching ROUNDS through Steam…");
    }

    [RelayCommand]
    private async Task ImportModAsync()
    {
        if (_game is null) return;
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import mod (Thunderstore .zip or plugin .dll)",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Mods") { Patterns = ["*.zip", "*.dll"] }],
        });
        var manager = new ModManager(_game);
        foreach (var f in files)
            if (f.TryGetLocalPath() is { } p)
                Run(() => Report($"Installed {manager.Import(p).DisplayName}"), null);
        Refresh();
    }

    [RelayCommand]
    private async Task CopyLaunchOptionsAsync()
    {
        if (_window.Clipboard is { } cb)
        {
            await cb.SetTextAsync(GameLauncher.ProtonLaunchOptions);
            Report("Copied. Paste into Steam → ROUNDS → Properties → General → Launch Options, then press Refresh.");
        }
    }

    [RelayCommand] private void OpenGameFolder() { if (_game is not null) GameLauncher.OpenFolder(_game.GameDir); }
    [RelayCommand] private void OpenPluginsFolder() { if (_game is not null) GameLauncher.OpenFolder(_game.PluginsDir); }
    [RelayCommand] private void OpenLog() { if (_game is not null) GameLauncher.OpenFolder(_game.BepInExDir); }

    private void Run(Action action, string? success)
    {
        try
        {
            action();
            if (success is not null) Report(success);
        }
        catch (Exception e) { Report($"Error: {e.Message}"); }
    }

    private void Report(string message) => Log = $"[{DateTime.Now:HH:mm:ss}] {message}\n{Log}";
}
