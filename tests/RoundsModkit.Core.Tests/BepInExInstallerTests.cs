using RoundsModkit.Core;

namespace RoundsModkit.Core.Tests;

public sealed class BepInExInstallerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("rmk-bep-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    // Hits GitHub, so it only runs with RMK_NETWORK_TESTS=1.
    [Fact]
    public async Task InstallsVerifiedArchiveAndUninstallKeepsUserData()
    {
        if (Environment.GetEnvironmentVariable("RMK_NETWORK_TESTS") != "1") return;

        File.WriteAllText(Path.Combine(_dir, "Rounds.exe"), "");
        var game = new GameInstall(_dir, _dir, _dir);
        using var http = new HttpClient();
        var installer = new BepInExInstaller(game, http);

        await installer.InstallAsync();
        Assert.True(installer.IsInstalled);
        Assert.StartsWith("5.4.23", installer.InstalledVersion);

        File.WriteAllText(Path.Combine(game.ConfigDir, "mine.cfg"), "x");
        installer.Uninstall();
        Assert.False(installer.IsInstalled);
        Assert.False(File.Exists(Path.Combine(_dir, "winhttp.dll")));
        Assert.True(File.Exists(Path.Combine(game.ConfigDir, "mine.cfg")));
        Assert.True(Directory.Exists(game.PluginsDir));
    }
}
