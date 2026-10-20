using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using RoundsModkit.Launcher.ViewModels;
using RoundsModkit.Launcher.Views;

namespace RoundsModkit.Launcher;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            window.DataContext = new MainViewModel(window);
            desktop.MainWindow = window;
        }
        base.OnFrameworkInitializationCompleted();
    }
}
