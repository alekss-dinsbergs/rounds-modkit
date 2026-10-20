using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoundsModkit.Core;

namespace RoundsModkit.Launcher.ViewModels;

public partial class ConfigFileViewModel : ObservableObject
{
    private readonly string _path;
    private readonly Action<string> _report;
    private BepInExConfig? _config;

    public ConfigFileViewModel(string path, Action<string> report)
    {
        _path = path;
        _report = report;
    }

    public string FileName => Path.GetFileName(_path);
    public string Title => _config?.PluginName ?? FileName;
    public ObservableCollection<ConfigEntryViewModel> Entries { get; } = [];

    [ObservableProperty] private bool _dirty;

    /// <summary>Loaded lazily so a broken file only fails when the user opens it.</summary>
    public void EnsureLoaded()
    {
        if (_config is not null) return;
        Reload();
    }

    [RelayCommand]
    private void Reload()
    {
        try
        {
            _config = BepInExConfig.Load(_path);
            Entries.Clear();
            foreach (var e in _config.Entries) Entries.Add(new ConfigEntryViewModel(e, () => Dirty = true));
            Dirty = false;
            OnPropertyChanged(nameof(Title));
        }
        catch (Exception e) { _report($"Couldn't read {FileName}: {e.Message}"); }
    }

    [RelayCommand]
    private void Save()
    {
        if (_config is null) return;
        try
        {
            _config.Save();
            Dirty = false;
            _report($"Saved {FileName}");
        }
        catch (Exception e) { _report($"Couldn't save {FileName}: {e.Message}"); }
    }
}

public partial class ConfigEntryViewModel : ObservableObject
{
    private readonly ConfigEntry _entry;
    private readonly Action _changed;

    public ConfigEntryViewModel(ConfigEntry entry, Action changed)
    {
        _entry = entry;
        _changed = changed;
    }

    public string Section => _entry.Section;
    public string Key => _entry.Key;
    public string Description => _entry.Description;
    public string Hint => string.Join("  ·  ", new[]
    {
        _entry.SettingType,
        _entry.DefaultValue is null ? null : $"default: {_entry.DefaultValue}",
        _entry.AcceptableRange,
        _entry.IsFlags ? "comma-separated: " + string.Join(", ", _entry.AcceptableValues) : null,
    }.Where(s => !string.IsNullOrEmpty(s)));

    public bool IsBoolean => _entry.IsBoolean;
    public bool IsChoice => _entry.IsChoice;
    public bool IsText => !IsBoolean && !IsChoice;
    public IReadOnlyList<string> Choices => _entry.AcceptableValues;

    public string Value
    {
        get => _entry.Value;
        set
        {
            if (value == _entry.Value) return;
            _entry.Value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BoolValue));
            _changed();
        }
    }

    public bool BoolValue
    {
        get => string.Equals(_entry.Value, "true", StringComparison.OrdinalIgnoreCase);
        set => Value = value ? "true" : "false";
    }
}
