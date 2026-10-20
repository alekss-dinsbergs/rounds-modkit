using CommunityToolkit.Mvvm.ComponentModel;
using RoundsModkit.Core;

namespace RoundsModkit.Launcher.ViewModels;

public partial class ModItemViewModel : ObservableObject
{
    private readonly ModManager _manager;
    private readonly Action<string> _report;
    private ModInfo _mod;

    public ModItemViewModel(ModManager manager, ModInfo mod, InstalledMods installed, Action<string> report)
    {
        _manager = manager;
        _mod = mod;
        _report = report;
        _isEnabled = mod.Enabled;

        var missing = (mod.Manifest?.Dependencies ?? [])
            .Select(PackageRef.Parse)
            .OfType<PackageRef>()
            .Where(r => !r.IsLoaderPack && !installed.Has(r))
            .Select(r => r.Name)
            .ToList();
        MissingDependencies = missing.Count > 0 ? "Missing: " + string.Join(", ", missing) : "";
    }

    public string Name => _mod.DisplayName;
    public string Details => string.Join("  ·  ", new[] { _mod.Version, _mod.Manifest?.Description }.Where(s => !string.IsNullOrEmpty(s)));
    public string MissingDependencies { get; }

    [ObservableProperty] private bool _isEnabled;
    private bool _reverting;

    partial void OnIsEnabledChanged(bool value)
    {
        if (_reverting) return;
        try
        {
            _mod = _manager.SetEnabled(_mod, value);
            _report($"{(value ? "Enabled" : "Disabled")} {Name}");
        }
        catch (Exception e)
        {
            _report($"Couldn't change {Name}: {e.Message}");
            _reverting = true;
            IsEnabled = _mod.Enabled;
            _reverting = false;
        }
    }
}
