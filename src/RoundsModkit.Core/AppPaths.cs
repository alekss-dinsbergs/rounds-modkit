namespace RoundsModkit.Core;

public static class AppPaths
{
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
        "RoundsModkit");

    public static string CacheDir => Path.Combine(DataDir, "cache");
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
}
