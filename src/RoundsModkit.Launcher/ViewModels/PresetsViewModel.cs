using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoundsModkit.Core;

namespace RoundsModkit.Launcher.ViewModels;

public partial class PresetsViewModel : ObservableObject
{
    private readonly Window _window;
    private readonly HttpClient _http;
    private readonly Func<GameInstall?> _game;
    private readonly Action<string> _report;
    private readonly Action _changed;
    private ThunderstoreIndex? _index;
    private bool _loaded;

    [ObservableProperty] private PresetItemViewModel? _selected;
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newDescription = "";

    public ObservableCollection<PresetItemViewModel> Presets { get; } = [];
    public string LocalDir => PresetSources.LocalDir;

    public PresetsViewModel(Window window, HttpClient http, Func<GameInstall?> game, Action<string> report, Action changed)
    {
        _window = window;
        _http = http;
        _game = game;
        _report = report;
        _changed = changed;
    }

    partial void OnSelectedChanged(PresetItemViewModel? value) => value?.UpdatePlan(_index, CurrentMods());

    public async Task EnsureLoadedAsync()
    {
        if (!_loaded) await RefreshAsync();
    }

    /// <summary>For tests: use a known index and skip loading from disk and GitHub.</summary>
    internal void UseIndex(ThunderstoreIndex index)
    {
        _index = index;
        _loaded = true;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        _loaded = true;
        Busy = true;
        var keep = Selected?.Key;
        try
        {
            Presets.Clear();
            foreach (var e in PresetSources.LoadLocal(_report)) Presets.Add(new PresetItemViewModel(e));
            Status = "Loading presets from GitHub…";
            try
            {
                foreach (var e in await PresetSources.LoadRepoAsync(_http, _report)) Presets.Add(new PresetItemViewModel(e));
                Status = $"{Presets.Count} presets";
            }
            catch (HttpRequestException e)
            {
                Status = $"Repo presets unavailable ({e.StatusCode?.ToString() ?? e.Message}); showing local ones.";
            }

            try { _index ??= await ThunderstoreIndex.LoadAsync(_http); }
            catch (Exception e) { _report($"Couldn't load Thunderstore list: {e.Message}"); }
            RefreshPlans();
            Selected = Presets.FirstOrDefault(p => p.Key == keep) ?? Presets.FirstOrDefault();
        }
        finally { Busy = false; }
    }

    public void RefreshPlans()
    {
        var mods = CurrentMods();
        foreach (var p in Presets) p.UpdatePlan(_index, mods);
    }

    private IReadOnlyList<ModInfo> CurrentMods() => _game() is { } g ? new ModManager(g).List() : [];

    [RelayCommand]
    private async Task ApplyAsync()
    {
        if (Selected is null || _game() is not { } game) return;
        if (_index is null)
        {
            _report("Thunderstore list isn't loaded yet; try Refresh.");
            return;
        }
        var plan = PresetApplier.Plan(Selected.Preset, _index, CurrentMods());
        foreach (var u in plan.Install.Unresolved) _report($"Warning: can't resolve {u}");
        if (plan.IsNoOp)
        {
            _report($"'{Selected.Name}' is already applied.");
            return;
        }

        Busy = true;
        try
        {
            _report($"Applying '{Selected.Name}': {plan.Summary()}");
            await PresetApplier.ApplyAsync(game, _http, plan, new Progress<string>(_report));
            _report($"Applied '{Selected.Name}'.");
        }
        catch (Exception e) { _report($"Apply failed: {e.Message}"); }
        finally
        {
            Busy = false;
            _changed();
            RefreshPlans();
        }
    }

    [RelayCommand]
    private async Task SaveCurrentAsync()
    {
        if (_game() is null) return;
        if (string.IsNullOrWhiteSpace(NewName))
        {
            _report("Give the preset a name first.");
            return;
        }
        var preset = ModPreset.FromInstalled(NewName.Trim(), NewDescription.Trim(), CurrentMods(), _index, out var skipped);
        if (skipped.Count > 0)
            _report($"Not included (not on Thunderstore): {string.Join(", ", skipped)}");

        var file = await _window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save preset (save into the repo's presets/ folder to share it)",
            SuggestedFileName = ModPreset.FileNameFor(preset.Name),
            DefaultExtension = "json",
            SuggestedStartLocation = await _window.StorageProvider.TryGetFolderFromPathAsync(
                Directory.CreateDirectory(PresetSources.LocalDir).FullName),
            FileTypeChoices = [new FilePickerFileType("Preset") { Patterns = ["*.json"] }],
        });
        if (file?.TryGetLocalPath() is not { } path) return;

        preset.Save(path);
        _report($"Saved preset '{preset.Name}' with {preset.Mods.Count} mods to {path}");
        NewName = NewDescription = "";
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        var files = await _window.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import preset",
            AllowMultiple = true,
            FileTypeFilter = [new FilePickerFileType("Preset") { Patterns = ["*.json"] }],
        });
        Directory.CreateDirectory(PresetSources.LocalDir);
        foreach (var f in files)
        {
            if (f.TryGetLocalPath() is not { } path) continue;
            try
            {
                var preset = ModPreset.Load(path);
                preset.Save(Path.Combine(PresetSources.LocalDir, ModPreset.FileNameFor(preset.Name)));
                _report($"Imported preset '{preset.Name}'");
            }
            catch (Exception e) { _report($"Couldn't import {Path.GetFileName(path)}: {e.Message}"); }
        }
        await RefreshAsync();
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (Selected?.Entry.LocalPath is not { } path) return;
        File.Delete(path);
        _report($"Deleted local preset '{Selected.Name}'");
        await RefreshAsync();
    }

    [RelayCommand]
    private void OpenFolder() => GameLauncher.OpenFolder(PresetSources.LocalDir);
}

public partial class PresetItemViewModel : ObservableObject
{
    public PresetItemViewModel(PresetSources.Entry entry) => Entry = entry;

    public PresetSources.Entry Entry { get; }
    public ModPreset Preset => Entry.Preset;
    public string Key => $"{Entry.Source}/{Entry.FileName}";
    public string Name => Preset.Name;
    public string Description => Preset.Description;
    public string SourceText => Entry.Source == "Repo" ? $"{PresetSources.RepoOwner}/{PresetSources.RepoName}" : "Local";
    public bool IsLocal => Entry.LocalPath is not null;
    public string ModeText => Preset.Exclusive
        ? "Exclusive: other Thunderstore mods are disabled while this preset is applied"
        : "Additive: other mods are left as they are";

    [ObservableProperty] private string _planText = "";
    [ObservableProperty] private IReadOnlyList<string> _modLines = [];

    public void UpdatePlan(ThunderstoreIndex? index, IReadOnlyList<ModInfo> installedMods)
    {
        var installed = new InstalledMods(installedMods);
        ModLines = Preset.Refs.Select(r =>
        {
            var have = installed.Get(r);
            var state = have is null ? "not installed"
                : r.Version is not null && have.Version != r.Version ? $"have {have.Version}"
                : have.Enabled ? "✓" : "disabled";
            return $"{r.Name} {r.Version ?? "(latest)"}  ·  {r.Owner}  ·  {state}";
        }).ToList();

        PlanText = index is null
            ? "Thunderstore list not loaded"
            : PresetApplier.Plan(Preset, index, installedMods).Summary();
    }
}
