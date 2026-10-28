using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using NetPilot.App.Services;
using NetPilot.App.ViewModels;
using NetPilot.Core.Storage;

namespace NetPilot.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new Views.MainWindow();
            var settings = new SettingsService();
            var services = new AppServices(
                settings,
                new GlobalLogService(settings.Settings),
                new FavoriteStore(),
                new DesktopDialogService(window),
                new ToastService());

            ThemeManager.Apply(settings.Settings.Theme);
            window.DataContext = new MainWindowViewModel(services, window);
            desktop.MainWindow = window;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
