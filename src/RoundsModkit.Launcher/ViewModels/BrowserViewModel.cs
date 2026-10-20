using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoundsModkit.Core;

namespace RoundsModkit.Launcher.ViewModels;

public partial class BrowserViewModel : ObservableObject
{
    private const int PageSize = 100;
    public const string AllCategories = "All categories";

    private readonly HttpClient _http;
    private readonly Func<GameInstall?> _game;
    private readonly Action<string> _report;
    private readonly Action _installed;
    private ThunderstoreIndex? _index;
    private InstalledMods _installedMods = new([]);

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _category = AllCategories;
    [ObservableProperty] private bool _includeDeprecated;
    [ObservableProperty] private string _status = "Open this tab to load mods from Thunderstore.";
    [ObservableProperty] private bool _loading;
    [ObservableProperty] private bool _installing;
    [ObservableProperty] private BrowserItemViewModel? _selected;

    public ObservableCollection<BrowserItemViewModel> Results { get; } = [];
    public ObservableCollection<string> Categories { get; } = [AllCategories];

    public BrowserViewModel(HttpClient http, Func<GameInstall?> game, Action<string> report, Action installed)
    {
        _http = http;
        _game = game;
        _report = report;
        _installed = installed;
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnCategoryChanged(string value) => ApplyFilter();
    partial void OnIncludeDeprecatedChanged(bool value) => ApplyFilter();

    public async Task EnsureLoadedAsync()
    {
        if (_index is null && !Loading) await LoadAsync(false);
    }

    [RelayCommand]
    private Task Reload() => LoadAsync(true);

    private async Task LoadAsync(bool force)
    {
        Loading = true;
        Status = "Loading mod list from Thunderstore…";
        try
        {
            UseIndex(await ThunderstoreIndex.LoadAsync(_http, force));
        }
        catch (Exception e)
        {
            Status = $"Couldn't load Thunderstore: {e.Message}";
        }
        finally { Loading = false; }
    }

    internal void UseIndex(ThunderstoreIndex index)
    {
        _index = index;
        var selectedCategory = Category;
        Categories.Clear();
        Categories.Add(AllCategories);
        foreach (var c in index.Categories) Categories.Add(c);
        Category = Categories.Contains(selectedCategory) ? selectedCategory : AllCategories;
        ApplyFilter();
    }

    /// <summary>Called after installs or toggles elsewhere so statuses stay accurate.</summary>
    public void RefreshInstalled()
    {
        var game = _game();
        _installedMods = new InstalledMods(game is null ? [] : new ModManager(game).List());
        foreach (var r in Results) r.UpdateStatus();
    }

    private void ApplyFilter()
    {
        if (_index is null) return;
        var keep = Selected?.Package.FullName;
        var game = _game();
        _installedMods = new InstalledMods(game is null ? [] : new ModManager(game).List());

        var hits = _index.Search(SearchText, Category == AllCategories ? null : Category, IncludeDeprecated).ToList();
        Results.Clear();
        foreach (var p in hits.Take(PageSize))
        {
            var item = new BrowserItemViewModel(this, p);
            Results.Add(item);
            item.LoadIconAsync();
        }
        Status = hits.Count > PageSize
            ? $"Showing {PageSize} of {hits.Count} — refine the search to see more."
            : $"{hits.Count} mods · list from {_index.FetchedAt:g}";
        Selected = Results.FirstOrDefault(r => r.Package.FullName == keep) ?? Results.FirstOrDefault();
    }

    internal InstalledMods Installed => _installedMods;
    internal ThunderstoreIndex? Index => _index;
    internal HttpClient Http => _http;

    internal async Task InstallAsync(BrowserItemViewModel item)
    {
        var game = _game();
        if (game is null || _index is null) return;
        var plan = _index.Plan(item.Package, _installedMods);
        foreach (var u in plan.Unresolved)
            _report($"Warning: {item.Package.FullName} depends on {u}, which isn't on Thunderstore for ROUNDS.");

        Installing = true;
        try
        {
            await new ThunderstoreInstaller(game, _http).InstallAsync(plan, new Progress<string>(_report));
            if (!new BepInExInstaller(game, _http).IsInstalled)
                _report("Note: BepInEx isn't installed yet. Install it on the Game tab or these mods won't load.");
        }
        catch (Exception e) { _report($"Install failed: {e.Message}"); }
        finally
        {
            Installing = false;
            _installed();
        }
    }
}

public partial class BrowserItemViewModel : ObservableObject
{
    private static readonly SemaphoreSlim IconDownloads = new(6);
    private readonly BrowserViewModel _owner;
    private bool _iconRequested;

    public BrowserItemViewModel(BrowserViewModel owner, ThunderstorePackage package)
    {
        _owner = owner;
        Package = package;
        UpdateStatus();
    }

    public ThunderstorePackage Package { get; }
    public string Name => Package.Name.Replace('_', ' ');
    public string Owner => "by " + Package.Owner;
    public string Description => Package.Latest.Description;
    public string Meta => $"v{Package.Latest.VersionNumber}  ·  {Package.TotalDownloads:N0} downloads  ·  updated {Package.DateUpdated:yyyy-MM-dd}"
                          + (Package.IsDeprecated ? "  ·  DEPRECATED" : "");
    public string CategoriesText => string.Join(", ", Package.Categories);

    public IReadOnlyList<string> Dependencies => Package.Latest.Dependencies
        .Select(PackageRef.Parse).OfType<PackageRef>()
        .Where(r => !r.IsLoaderPack)
        .Select(r => $"{(_owner.Installed.Has(r) ? "✓" : "•")} {r.Name} ({r.Owner})")
        .ToList();
    public bool HasDependencies => Dependencies.Count > 0;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _actionText = "Install";
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private Bitmap? _icon;
    internal bool IconSettled { get; private set; }

    public void UpdateStatus()
    {
        var installed = _owner.Installed.Get(PackageRef.Parse(Package.FullName)!.Value);
        IsInstalled = installed is not null;
        var latest = Package.Latest.VersionNumber;
        var update = installed is not null && InstalledMods.IsNewer(latest, installed.Version);

        StatusText = installed switch
        {
            null => "",
            _ when update => $"Update: {installed.Version ?? "?"} → {latest}",
            { Enabled: false } => "Installed (disabled)",
            _ => "Installed",
        };

        var extra = _owner.Index?.Plan(Package, _owner.Installed).ToInstall.Count - 1 ?? 0;
        var verb = installed is null ? "Install" : update ? "Update" : "Reinstall";
        ActionText = extra > 0 ? $"{verb} + {extra} dependenc{(extra == 1 ? "y" : "ies")}" : verb;
        OnPropertyChanged(nameof(Dependencies));
    }

    [RelayCommand]
    private Task Install() => _owner.InstallAsync(this);

    [RelayCommand]
    private void OpenPage() => Process.Start(new ProcessStartInfo(Package.PackageUrl) { UseShellExecute = true });

    public async void LoadIconAsync()
    {
        if (_iconRequested) return;
        _iconRequested = true;
        try
        {
            var dir = Path.Combine(AppPaths.CacheDir, "icons");
            var file = Path.Combine(dir, $"{Package.FullName}-{Package.Latest.VersionNumber}.png");
            if (!File.Exists(file))
            {
                await IconDownloads.WaitAsync();
                try
                {
                    Directory.CreateDirectory(dir);
                    var bytes = await _owner.Http.GetByteArrayAsync(Package.Latest.Icon);
                    await File.WriteAllBytesAsync(file, bytes);
                }
                finally { IconDownloads.Release(); }
            }
            await using var s = File.OpenRead(file);
            Icon = Bitmap.DecodeToWidth(s, 96);
        }
        catch (Exception)
        {
            // A missing icon is cosmetic; the list stays usable without it.
        }
        finally { IconSettled = true; }
    }
}
