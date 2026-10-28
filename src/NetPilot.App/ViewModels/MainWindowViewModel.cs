using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetPilot.App.Services;
using NetPilot.App.Views;
using NetPilot.Core.Models;
using NetPilot.Core.Storage;

namespace NetPilot.App.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private readonly AppServices _services;
    private readonly Window? _owner;

    private readonly DispatcherTimer _toastTimer;

    private SessionViewModel? _activeSession;
    private string _themeButtonText = "深色模式";
    private string _toastText = string.Empty;
    private bool _toastVisible;

    public MainWindowViewModel(AppServices services, Window? owner = null)
    {
        _services = services;
        _owner = owner;
        Sessions = new ObservableCollection<SessionViewModel>();
        Favorites = new ObservableCollection<FavoriteEntry>();

        NewSessionCommand = new RelayCommand<string>(CreateSession);
        SelectSessionCommand = new RelayCommand<SessionViewModel>(SelectSession);
        NextSessionCommand = new RelayCommand(() => CycleSession(1));
        PreviousSessionCommand = new RelayCommand(() => CycleSession(-1));
        CloseSessionCommand = new AsyncRelayCommand<SessionViewModel>(CloseSessionAsync);
        CloseActiveSessionCommand = new AsyncRelayCommand(CloseActiveSessionAsync);
        SaveSessionCommand = new AsyncRelayCommand(SaveSessionAsync);
        SaveSessionAsCommand = new AsyncRelayCommand(SaveSessionAsAsync);
        OpenSessionCommand = new AsyncRelayCommand(OpenSessionAsync);
        SaveFavoriteCommand = new AsyncRelayCommand(SaveFavoriteAsync);
        ApplyFavoriteCommand = new RelayCommand<FavoriteEntry>(ApplyFavorite);
        DeleteFavoriteCommand = new AsyncRelayCommand<FavoriteEntry>(DeleteFavoriteAsync);
        OpenFavoritesCommand = new RelayCommand(OpenFavorites);
        OpenSettingsCommand = new AsyncRelayCommand(OpenSettingsAsync);
        OpenGlobalLogCommand = new RelayCommand(OpenGlobalLog);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        AboutCommand = new AsyncRelayCommand(ShowAboutAsync);
        ExitCommand = new RelayCommand(() => _owner?.Close());

        Sessions.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasSessions));
            OnPropertyChanged(nameof(SessionCountText));
            NotifyMainCommands();
        };

        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
        _toastTimer.Tick += (_, _) =>
        {
            _toastTimer.Stop();
            ToastVisible = false;
        };
        _services.Toasts.Shown += OnToastShown;

        _services.SettingsService.Changed += OnSettingsChanged;
        _themeButtonText = _services.Settings.Theme == AppTheme.Dark ? "浅色模式" : "深色模式";
        ReloadFavorites();
        ApplySettings(_services.Settings);

        var first = SessionFactory.Create(_services, ProtocolKind.Tcp, SessionMode.Client);
        Sessions.Add(first);
        ActiveSession = first;
    }

    public ObservableCollection<SessionViewModel> Sessions { get; }
    public ObservableCollection<FavoriteEntry> Favorites { get; }

    public RelayCommand<string> NewSessionCommand { get; }
    public RelayCommand<SessionViewModel> SelectSessionCommand { get; }
    public RelayCommand NextSessionCommand { get; }
    public RelayCommand PreviousSessionCommand { get; }
    public AsyncRelayCommand<SessionViewModel> CloseSessionCommand { get; }
    public AsyncRelayCommand CloseActiveSessionCommand { get; }
    public AsyncRelayCommand SaveSessionCommand { get; }
    public AsyncRelayCommand SaveSessionAsCommand { get; }
    public AsyncRelayCommand OpenSessionCommand { get; }
    public AsyncRelayCommand SaveFavoriteCommand { get; }
    public RelayCommand<FavoriteEntry> ApplyFavoriteCommand { get; }
    public AsyncRelayCommand<FavoriteEntry> DeleteFavoriteCommand { get; }
    public RelayCommand OpenFavoritesCommand { get; }
    public AsyncRelayCommand OpenSettingsCommand { get; }
    public RelayCommand OpenGlobalLogCommand { get; }
    public RelayCommand ToggleThemeCommand { get; }
    public AsyncRelayCommand AboutCommand { get; }
    public RelayCommand ExitCommand { get; }

    public SessionViewModel? ActiveSession
    {
        get => _activeSession;
        private set
        {
            var previous = _activeSession;
            if (!SetProperty(ref _activeSession, value))
            {
                return;
            }

            if (previous is not null)
            {
                previous.IsSelected = false;
                previous.PropertyChanged -= OnSessionPropertyChanged;
            }

            if (value is not null)
            {
                value.IsSelected = true;
                value.PropertyChanged += OnSessionPropertyChanged;
            }

            OnPropertyChanged(nameof(HasActiveSession));
            OnPropertyChanged(nameof(WindowTitle));
            OnPropertyChanged(nameof(ActiveStatusMessage));
            OnPropertyChanged(nameof(ActiveStatusBrush));
            OnPropertyChanged(nameof(ActiveTrafficText));
            NotifyMainCommands();
        }
    }

    public bool HasActiveSession => ActiveSession is not null;
    public bool HasSessions => Sessions.Count > 0;
    public bool HasFavorites => Favorites.Count > 0;
    public string SessionCountText => $"{Sessions.Count} 个标签";
    public string FavoriteCountText => $"{Favorites.Count} 个收藏";

    public string WindowTitle => ActiveSession is null
        ? "NetPilot"
        : $"{ActiveSession.SessionName} · {ActiveSession.ProtocolDisplay} - NetPilot";

    public string ActiveStatusMessage => ActiveSession?.StatusMessage ?? "尚未打开会话";
    public IBrush ActiveStatusBrush => ActiveSession?.StatusBrush ?? Brushes.Gray;
    public string ActiveTrafficText => ActiveSession is null
        ? string.Empty
        : $"发送 {ActiveSession.SentTrafficText}    接收 {ActiveSession.ReceivedTrafficText}";

    public string ThemeButtonText
    {
        get => _themeButtonText;
        private set => SetProperty(ref _themeButtonText, value);
    }

    public string ToastText
    {
        get => _toastText;
        private set => SetProperty(ref _toastText, value);
    }

    public bool ToastVisible
    {
        get => _toastVisible;
        private set => SetProperty(ref _toastVisible, value);
    }

    public string VersionText
    {
        get
        {
            var version = typeof(MainWindowViewModel).Assembly.GetName().Version;
            return version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    private void CreateSession(string? spec)
    {
        if (!SessionFactory.TryParseSpec(spec, out var protocol, out var mode))
        {
            return;
        }

        var session = SessionFactory.Create(_services, protocol, mode);
        session.SessionName = UniqueName(session.SessionName);
        session.ApplySettings(_services.Settings);
        Sessions.Add(session);
        ActiveSession = session;
    }

    private void SelectSession(SessionViewModel? session)
    {
        if (session is not null)
        {
            ActiveSession = session;
        }
    }

    private void CycleSession(int delta)
    {
        if (Sessions.Count < 2)
        {
            return;
        }

        var index = ActiveSession is null ? 0 : Sessions.IndexOf(ActiveSession);
        index = (index + delta + Sessions.Count) % Sessions.Count;
        ActiveSession = Sessions[index];
    }

    private void OnToastShown(object? sender, string message)
    {
        Dispatcher.UIThread.Post(() =>
        {
            ToastText = message;
            ToastVisible = true;
            _toastTimer.Stop();
            _toastTimer.Start();
        });
    }

    private async Task CloseSessionAsync(SessionViewModel? session)
    {
        if (session is null)
        {
            return;
        }

        if (!Sessions.Contains(session))
        {
            return;
        }

        if (session.IsActive)
        {
            var confirmed = await _services.Dialogs.ConfirmAsync("关闭会话",
                $"“{session.SessionName}”正在运行，关闭后将断开连接。是否继续？");
            if (!confirmed)
            {
                return;
            }
        }

        await session.ShutdownAsync();
        Sessions.Remove(session);
        if (ActiveSession == session)
        {
            ActiveSession = Sessions.Count > 0 ? Sessions[^1] : null;
        }
    }

    private async Task CloseActiveSessionAsync()
    {
        await CloseSessionAsync(ActiveSession);
    }

    private async Task SaveSessionAsync()
    {
        var session = ActiveSession;
        if (session is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(session.FilePath))
        {
            await SaveSessionAsAsync();
            return;
        }

        await SaveToPathAsync(session, session.FilePath);
    }

    private async Task SaveSessionAsAsync()
    {
        var session = ActiveSession;
        if (session is null)
        {
            return;
        }

        var suggested = SessionStore.DefaultFileName(session.SessionName);
        var path = await _services.Dialogs.PickSaveFileAsync("保存会话", suggested,
            new FilePickerFileType("NetPilot 会话") { Patterns = ["*.np.json"] },
            new FilePickerFileType("JSON 文件") { Patterns = ["*.json"] });
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            path += ".np.json";
        }

        await SaveToPathAsync(session, path);
    }

    private async Task SaveToPathAsync(SessionViewModel session, string path)
    {
        try
        {
            await SessionStore.SaveAsync(path, session.ToConfig());
            session.FilePath = path;
            session.LogInfo($"会话已保存：{path}");
        }
        catch (Exception ex)
        {
            await _services.Dialogs.ShowMessageAsync("保存失败", ex.Message, true);
        }
    }

    private async Task OpenSessionAsync()
    {
        var path = await _services.Dialogs.PickOpenFileAsync("打开会话",
            new FilePickerFileType("NetPilot 会话") { Patterns = ["*.np.json"] },
            new FilePickerFileType("JSON 文件") { Patterns = ["*.json"] });
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var config = await SessionStore.LoadAsync(path);
            var session = SessionFactory.Create(_services, config, path);
            session.SessionName = UniqueName(session.SessionName);
            session.ApplySettings(_services.Settings);
            Sessions.Add(session);
            ActiveSession = session;
            session.LogInfo($"已载入会话：{path}");
        }
        catch (Exception ex)
        {
            await _services.Dialogs.ShowMessageAsync("打开会话失败", ex.Message, true);
        }
    }

    private async Task SaveFavoriteAsync()
    {
        var session = ActiveSession;
        if (session is null)
        {
            return;
        }

        var name = await _services.Dialogs.PromptAsync("收藏会话", "收藏名称", session.SessionName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        _services.Favorites.AddOrUpdate(new FavoriteEntry
        {
            Name = name.Trim(),
            CreatedAt = DateTime.Now,
            Config = session.ToConfig()
        });
        ReloadFavorites();
        session.LogInfo($"已收藏会话：{name.Trim()}");
    }

    private void ApplyFavorite(FavoriteEntry? favorite)
    {
        if (favorite?.Config is null)
        {
            return;
        }

        var session = SessionFactory.Create(_services, favorite.Config.Clone());
        session.SessionName = UniqueName(string.IsNullOrWhiteSpace(favorite.Config.SessionName)
            ? favorite.Name
            : favorite.Config.SessionName);
        session.ApplySettings(_services.Settings);
        Sessions.Add(session);
        ActiveSession = session;
        session.LogInfo($"已应用收藏：{favorite.Name}");
    }

    private async Task DeleteFavoriteAsync(FavoriteEntry? favorite)
    {
        if (favorite is null)
        {
            return;
        }

        var confirmed = await _services.Dialogs.ConfirmAsync("删除收藏", $"确定删除收藏“{favorite.Name}”吗？");
        if (!confirmed)
        {
            return;
        }

        _services.Favorites.Remove(favorite.Name);
        ReloadFavorites();
    }

    private void ReloadFavorites()
    {
        Favorites.Clear();
        foreach (var favorite in _services.Favorites.Favorites)
        {
            Favorites.Add(favorite);
        }

        OnPropertyChanged(nameof(HasFavorites));
        OnPropertyChanged(nameof(FavoriteCountText));
    }

    private void OpenFavorites()
    {
        if (_owner is null)
        {
            return;
        }

        var window = new FavoritesWindow { DataContext = this };
        window.Show(_owner);
    }

    private async Task OpenSettingsAsync()
    {
        if (_owner is null)
        {
            return;
        }

        var viewModel = new SettingsViewModel(_services);
        var window = new SettingsWindow { DataContext = viewModel };
        var saved = await window.ShowDialog<bool>(_owner);
        if (saved)
        {
            ApplySettings(_services.Settings);
        }
    }

    private void OpenGlobalLog()
    {
        if (_owner is null)
        {
            return;
        }

        var window = new GlobalLogWindow { DataContext = new GlobalLogViewModel(_services.GlobalLog) };
        window.Show(_owner);
    }

    private void ToggleTheme()
    {
        var next = _services.Settings.Theme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _services.SettingsService.Update(settings => settings.Theme = next);
        ThemeManager.Apply(next);
        ThemeButtonText = next == AppTheme.Dark ? "浅色模式" : "深色模式";
    }

    private async Task ShowAboutAsync()
    {
        var message =
            "NetPilot 网络调试工具\n\n" +
            "集成 TCP / UDP / WebSocket / HTTP 的客户端与服务端调试能力，\n" +
            "支持文本、Hex、Base64、JSON 编解码与报文记录导出。\n\n" +
            "所有数据仅保存在本机，不会上传到任何服务器。\n" +
            $"版本：{VersionText}";
        await _services.Dialogs.ShowMessageAsync("关于 NetPilot", message);
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SessionViewModel.SessionName):
                OnPropertyChanged(nameof(WindowTitle));
                break;
            case nameof(SessionViewModel.StatusMessage):
                OnPropertyChanged(nameof(ActiveStatusMessage));
                break;
            case nameof(SessionViewModel.StatusBrush):
                OnPropertyChanged(nameof(ActiveStatusBrush));
                break;
            case nameof(SessionViewModel.SentTrafficText):
            case nameof(SessionViewModel.ReceivedTrafficText):
                OnPropertyChanged(nameof(ActiveTrafficText));
                break;
        }
    }

    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        ApplySettings(settings);
    }

    private void ApplySettings(AppSettings settings)
    {
        foreach (var session in Sessions)
        {
            session.ApplySettings(settings);
        }

        if (_owner is not null)
        {
            _owner.FontSize = settings.FontSize;
            _owner.FontFamily = string.IsNullOrWhiteSpace(settings.FontFamily)
                ? FontFamily.Default
                : new FontFamily(settings.FontFamily);
        }

        _themeButtonText = settings.Theme == AppTheme.Dark ? "浅色模式" : "深色模式";
        OnPropertyChanged(nameof(ThemeButtonText));
    }

    private string UniqueName(string name)
    {
        if (!Sessions.Any(x => string.Equals(x.SessionName, name, StringComparison.OrdinalIgnoreCase)))
        {
            return name;
        }

        for (var index = 2; ; index++)
        {
            var candidate = $"{name} {index}";
            if (!Sessions.Any(x => string.Equals(x.SessionName, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }
        }
    }

    private void NotifyMainCommands()
    {
        SaveSessionCommand.NotifyCanExecuteChanged();
        SaveSessionAsCommand.NotifyCanExecuteChanged();
        SaveFavoriteCommand.NotifyCanExecuteChanged();
        CloseActiveSessionCommand.NotifyCanExecuteChanged();
    }

    public async Task ShutdownAsync()
    {
        _toastTimer.Stop();
        _services.Toasts.Shown -= OnToastShown;
        _services.SettingsService.Changed -= OnSettingsChanged;
        foreach (var session in Sessions.ToList())
        {
            try
            {
                await session.ShutdownAsync();
            }
            catch
            {
                // ignored
            }
        }
    }
}
