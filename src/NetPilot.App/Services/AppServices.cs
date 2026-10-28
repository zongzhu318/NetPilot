using NetPilot.Core.Storage;

namespace NetPilot.App.Services;

public sealed class AppServices
{
    public AppServices(SettingsService settings, GlobalLogService globalLog, FavoriteStore favorites,
        IDialogService dialogs, ToastService toasts)
    {
        SettingsService = settings;
        GlobalLog = globalLog;
        Favorites = favorites;
        Dialogs = dialogs;
        Toasts = toasts;
    }

    public SettingsService SettingsService { get; }
    public AppSettings Settings => SettingsService.Settings;
    public GlobalLogService GlobalLog { get; }
    public FavoriteStore Favorites { get; }
    public IDialogService Dialogs { get; }
    public ToastService Toasts { get; }
}
