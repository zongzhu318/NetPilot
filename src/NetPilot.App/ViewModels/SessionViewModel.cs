using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetPilot.App.Services;
using NetPilot.Core.Codec;
using NetPilot.Core.Logging;
using NetPilot.Core.Models;
using NetPilot.Core.Protocols;
using NetPilot.Core.Storage;

namespace NetPilot.App.ViewModels;

public abstract class SessionViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly IBrush RunningBrush = new SolidColorBrush(Color.Parse("#22C55E"));
    private static readonly IBrush StartingBrush = new SolidColorBrush(Color.Parse("#F59E0B"));
    private static readonly IBrush StoppedBrush = new SolidColorBrush(Color.Parse("#EF4444"));
    private static readonly IBrush IdleBrush = new SolidColorBrush(Color.Parse("#94A3B8"));

    private readonly List<LogEntryViewModel> _allLogs = [];
    private readonly TemplateEngine _templateEngine = new();
    private readonly object _loopSync = new();

    protected readonly AppServices Services;

    private string _sessionName = "未命名会话";
    private string? _filePath;
    private TransportState _state = TransportState.Stopped;
    private string _statusMessage = "尚未连接";
    private bool _isBusy;
    private bool _isSelected;

    private SessionMode _mode;
    private EnumOption<SessionMode> _modeOption = null!;

    private string _host = "127.0.0.1";
    private int _port = 8080;
    private string _localHost = "0.0.0.0";
    private int _localPort;
    private bool _useTls;
    private bool _ignoreCertificate;
    private bool _autoReconnect;
    private int _reconnectIntervalMs = 3000;

    private MessageFormat _displayFormat = MessageFormat.Text;
    private EnumOption<MessageFormat> _displayFormatOption = null!;
    private TextEncodingKind _displayEncoding = TextEncodingKind.Utf8;
    private EnumOption<TextEncodingKind> _displayEncodingOption = null!;

    private string _sendText = string.Empty;
    private SendFormat _sendFormat = SendFormat.Text;
    private EnumOption<SendFormat> _sendFormatOption = null!;
    private TextEncodingKind _sendEncoding = TextEncodingKind.Utf8;
    private EnumOption<TextEncodingKind> _sendEncodingOption = null!;
    private NewlineMode _newlineMode = NewlineMode.None;
    private EnumOption<NewlineMode> _newlineOption = null!;
    private string _customNewline = string.Empty;
    private bool _useTemplateVariables;
    private bool _sendAsBinary;
    private bool _isLooping;
    private int _loopIntervalMs = 1000;
    private int _loopCount;
    private string _loopProgressText = string.Empty;
    private string? _selectedHistory;

    private string _logFilter = string.Empty;
    private bool _showSendLogs = true;
    private bool _showReceiveLogs = true;
    private bool _showOtherLogs = true;
    private bool _autoScroll = true;
    private bool _showTimestamps = true;
    private int _maxLogEntries = 2000;
    private LogEntryViewModel? _selectedLog;

    private ClientItemViewModel? _selectedClient;
    private bool _broadcast;

    private long _sentBytes;
    private long _receivedBytes;
    private long _sentPackets;
    private long _receivedPackets;

    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private ITransport? _transport;
    private bool _disposed;

    protected SessionViewModel(AppServices services, SessionMode mode, string clientModeLabel, string serverModeLabel)
    {
        Services = services;
        ClientModeLabel = clientModeLabel;
        ServerModeLabel = serverModeLabel;
        ModeOptions =
        [
            new EnumOption<SessionMode>(SessionMode.Client, clientModeLabel),
            new EnumOption<SessionMode>(SessionMode.Server, serverModeLabel)
        ];
        _mode = mode;
        _modeOption = ModeOptions.First(x => x.Value == mode);

        DisplayFormatOptions = OptionLists.DisplayFormats;
        DisplayEncodingOptions = OptionLists.Encodings;
        SendFormatOptions = OptionLists.SendFormats;
        SendEncodingOptions = OptionLists.Encodings;
        NewlineOptions = OptionLists.Newlines;

        _displayFormatOption = DisplayFormatOptions.First(x => x.Value == MessageFormat.Text);
        _displayEncodingOption = DisplayEncodingOptions.First(x => x.Value == services.Settings.DefaultEncoding);
        _displayEncoding = services.Settings.DefaultEncoding;
        _sendFormatOption = SendFormatOptions.First(x => x.Value == SendFormat.Text);
        _sendEncodingOption = SendEncodingOptions.First(x => x.Value == services.Settings.DefaultEncoding);
        _sendEncoding = services.Settings.DefaultEncoding;
        _newlineOption = NewlineOptions.First(x => x.Value == NewlineMode.None);
        _maxLogEntries = Math.Max(100, services.Settings.MaxLogEntries);
        _showTimestamps = services.Settings.ShowTimestamps;
        _autoScroll = services.Settings.AutoScroll;
        _loopIntervalMs = Math.Max(10, services.Settings.DefaultLoopIntervalMs);
        _statusMessage = DefaultStatusMessage;

        Logs = new ObservableCollection<LogEntryViewModel>();
        Clients = new ObservableCollection<ClientItemViewModel>();
        SendHistory = new ObservableCollection<string>();

        ConnectCommand = new AsyncRelayCommand(ConnectAsync, () => !IsBusy);
        SendCommand = new AsyncRelayCommand(SendAsync, () => CanSendNow);
        ToggleLoopCommand = new AsyncRelayCommand(ToggleLoopAsync, () => IsLooping || CanSendNow);
        LoadFileCommand = new AsyncRelayCommand(LoadFileAsync);
        ExportLogsCommand = new AsyncRelayCommand<string>(ExportLogsAsync, _ => _allLogs.Count > 0);
        ExportSelectedCommand = new AsyncRelayCommand<string>(ExportSelectedAsync, _ => SelectedLog is not null);
        CopySelectedCommand = new AsyncRelayCommand(CopySelectedAsync, () => SelectedLog is not null);
        ReplySelectedCommand = new AsyncRelayCommand(ReplySelectedAsync, () => CanReplySelected);
        LoadSelectedTextCommand = new AsyncRelayCommand(LoadSelectedTextAsync, () => SelectedLog?.HasData == true);
        ClearLogsCommand = new RelayCommand(ClearLogs);
        ClearSendCommand = new RelayCommand(() => SendText = string.Empty);
        CodecCommand = new RelayCommand<string>(ApplyCodec);
        UseHistoryCommand = new RelayCommand<string>(text =>
        {
            if (text is not null)
            {
                SendText = text;
            }
        });
        CloseDetailCommand = new RelayCommand(() => SelectedLog = null);
    }

    public ObservableCollection<LogEntryViewModel> Logs { get; }
    public ObservableCollection<ClientItemViewModel> Clients { get; }
    public ObservableCollection<string> SendHistory { get; }

    public abstract ProtocolKind Protocol { get; }
    public abstract string ProtocolDisplay { get; }
    public string ProtocolShort => Protocol switch
    {
        ProtocolKind.Tcp => "TCP",
        ProtocolKind.Udp => "UDP",
        ProtocolKind.WebSocket => "WS",
        _ => "HTTP"
    };

    public bool IsTcp => Protocol == ProtocolKind.Tcp;
    public bool IsUdp => Protocol == ProtocolKind.Udp;
    public bool IsWebSocket => Protocol == ProtocolKind.WebSocket;
    public bool IsHttp => Protocol == ProtocolKind.Http;

    public IReadOnlyList<EnumOption<SessionMode>> ModeOptions { get; }
    public IReadOnlyList<EnumOption<MessageFormat>> DisplayFormatOptions { get; }
    public IReadOnlyList<EnumOption<TextEncodingKind>> DisplayEncodingOptions { get; }
    public IReadOnlyList<EnumOption<SendFormat>> SendFormatOptions { get; }
    public IReadOnlyList<EnumOption<TextEncodingKind>> SendEncodingOptions { get; }
    public IReadOnlyList<EnumOption<NewlineMode>> NewlineOptions { get; }
    public string ClientModeLabel { get; }
    public string ServerModeLabel { get; }

    public AsyncRelayCommand ConnectCommand { get; }
    public AsyncRelayCommand SendCommand { get; }
    public AsyncRelayCommand ToggleLoopCommand { get; }
    public AsyncRelayCommand LoadFileCommand { get; }
    public AsyncRelayCommand<string> ExportLogsCommand { get; }
    public AsyncRelayCommand<string> ExportSelectedCommand { get; }
    public AsyncRelayCommand CopySelectedCommand { get; }
    public AsyncRelayCommand ReplySelectedCommand { get; }
    public AsyncRelayCommand LoadSelectedTextCommand { get; }
    public RelayCommand ClearLogsCommand { get; }
    public RelayCommand ClearSendCommand { get; }
    public RelayCommand<string> CodecCommand { get; }
    public RelayCommand<string> UseHistoryCommand { get; }
    public RelayCommand CloseDetailCommand { get; }

    public string SessionName
    {
        get => _sessionName;
        set => SetProperty(ref _sessionName, value);
    }

    public string? FilePath
    {
        get => _filePath;
        set
        {
            if (SetProperty(ref _filePath, value))
            {
                OnPropertyChanged(nameof(HasFilePath));
            }
        }
    }

    public bool HasFilePath => !string.IsNullOrWhiteSpace(FilePath);

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public SessionMode Mode => _mode;
    public bool IsServerMode => _mode == SessionMode.Server;
    public bool IsClientMode => _mode == SessionMode.Client;

    public EnumOption<SessionMode> ModeOption
    {
        get => _modeOption;
        set
        {
            if (value is null || !SetProperty(ref _modeOption, value))
            {
                return;
            }

            if (value.Value != _mode)
            {
                _ = ChangeModeAsync(value.Value);
            }
        }
    }

    private async Task ChangeModeAsync(SessionMode mode)
    {
        if (IsBusy)
        {
            _modeOption = ModeOptions.First(x => x.Value == _mode);
            OnPropertyChanged(nameof(ModeOption));
            return;
        }

        if (IsActive)
        {
            await StopSessionAsync();
        }

        _mode = mode;
        _modeOption = ModeOptions.First(x => x.Value == mode);
        OnPropertyChanged(nameof(ModeOption));
        OnPropertyChanged(nameof(Mode));
        RaiseModeDependentProperties();
        OnModeChanged();
        NotifyCommandStates();
    }

    protected void SetModeFromConfig(SessionMode mode)
    {
        _mode = mode;
        _modeOption = ModeOptions.First(x => x.Value == mode);
        OnPropertyChanged(nameof(ModeOption));
        OnPropertyChanged(nameof(Mode));
        RaiseModeDependentProperties();
        _statusMessage = DefaultStatusMessage;
        OnPropertyChanged(nameof(StatusMessage));
    }

    protected virtual void OnModeChanged()
    {
        ClearClientItems();
        Broadcast = false;
        SetStateCore(TransportState.Stopped, DefaultStatusMessage);
    }

    private void RaiseModeDependentProperties()
    {
        OnPropertyChanged(nameof(IsServerMode));
        OnPropertyChanged(nameof(IsClientMode));
        OnPropertyChanged(nameof(ShowTlsOption));
        OnPropertyChanged(nameof(ShowReconnectOption));
        OnPropertyChanged(nameof(ShowClientList));
        OnPropertyChanged(nameof(ShowBroadcastOption));
        OnPropertyChanged(nameof(ShowIgnoreCertificateOption));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(RemoteTargetLabel));
    }

    public TransportState State => _state;
    public string StatusMessage => _statusMessage;
    public bool IsRunning => _state == TransportState.Running;
    public bool IsStarting => _state == TransportState.Starting;
    public bool IsStopping => _state == TransportState.Stopping;
    public bool IsFaulted => _state == TransportState.Faulted;
    public bool IsStopped => _state is TransportState.Stopped or TransportState.Faulted;
    public bool IsActive => _state is TransportState.Starting or TransportState.Running;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(ConnectButtonText));
                NotifyCommandStates();
            }
        }
    }

    public string StatusLabel => _state switch
    {
        TransportState.Starting => "连接中",
        TransportState.Running => RunningStatusLabel,
        TransportState.Stopping => "停止中",
        TransportState.Faulted => "异常",
        _ => "已停止"
    };

    protected virtual string RunningStatusLabel => IsServerMode ? "监听中" : "已连接";
    protected virtual string DefaultStatusMessage => IsServerMode ? "尚未启动" : "尚未连接";

    public IBrush StatusBrush => _state switch
    {
        TransportState.Running => RunningBrush,
        TransportState.Starting or TransportState.Stopping => StartingBrush,
        TransportState.Faulted => StoppedBrush,
        _ => IdleBrush
    };

    public virtual string ConnectButtonText => IsActive
        ? (IsServerMode ? "停止" : "断开")
        : (IsServerMode ? "启动" : "连接");

    public virtual bool SupportsConnection => true;
    public virtual bool SupportsTls => false;
    public virtual bool SupportsReconnect => false;
    public virtual bool SupportsClients => false;
    public virtual bool SupportsBinaryMessages => false;
    public virtual bool IsSendAvailable => true;
    public virtual bool ShowIgnoreCertificateOption => false;
    public bool ShowTlsOption => SupportsTls && IsClientMode;
    public bool ShowReconnectOption => SupportsReconnect && IsClientMode;
    public bool ShowClientList => SupportsClients && IsServerMode;
    public bool ShowBroadcastOption => ShowClientList;
    public virtual string RemoteTargetLabel => $"{Host}:{Port}";

    public string Host
    {
        get => _host;
        set
        {
            if (SetProperty(ref _host, value))
            {
                OnPropertyChanged(nameof(RemoteTargetLabel));
            }
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            if (SetProperty(ref _port, value))
            {
                OnPropertyChanged(nameof(RemoteTargetLabel));
            }
        }
    }

    public string LocalHost
    {
        get => _localHost;
        set => SetProperty(ref _localHost, value);
    }

    public int LocalPort
    {
        get => _localPort;
        set => SetProperty(ref _localPort, value);
    }

    public bool UseTls
    {
        get => _useTls;
        set => SetProperty(ref _useTls, value);
    }

    public bool IgnoreCertificate
    {
        get => _ignoreCertificate;
        set => SetProperty(ref _ignoreCertificate, value);
    }

    public bool AutoReconnect
    {
        get => _autoReconnect;
        set => SetProperty(ref _autoReconnect, value);
    }

    public int ReconnectIntervalMs
    {
        get => _reconnectIntervalMs;
        set => SetProperty(ref _reconnectIntervalMs, Math.Max(500, value));
    }

    public MessageFormat DisplayFormat
    {
        get => _displayFormat;
        set
        {
            if (!SetProperty(ref _displayFormat, value))
            {
                return;
            }

            _displayFormatOption = DisplayFormatOptions.First(x => x.Value == value);
            OnPropertyChanged(nameof(DisplayFormatOption));
            RefreshLogDisplay();
        }
    }

    public EnumOption<MessageFormat> DisplayFormatOption
    {
        get => _displayFormatOption;
        set
        {
            if (value is not null)
            {
                DisplayFormat = value.Value;
            }
        }
    }

    public TextEncodingKind DisplayEncoding
    {
        get => _displayEncoding;
        set
        {
            if (!SetProperty(ref _displayEncoding, value))
            {
                return;
            }

            _displayEncodingOption = DisplayEncodingOptions.First(x => x.Value == value);
            OnPropertyChanged(nameof(DisplayEncodingOption));
            RefreshLogDisplay();
        }
    }

    public EnumOption<TextEncodingKind> DisplayEncodingOption
    {
        get => _displayEncodingOption;
        set
        {
            if (value is not null)
            {
                DisplayEncoding = value.Value;
            }
        }
    }

    public string SendText
    {
        get => _sendText;
        set
        {
            if (SetProperty(ref _sendText, value))
            {
                OnPropertyChanged(nameof(SendCharCountText));
                OnPropertyChanged(nameof(HasSendText));
                OnPropertyChanged(nameof(CanSendNow));
                SendCommand.NotifyCanExecuteChanged();
                ToggleLoopCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public SendFormat SendFormat
    {
        get => _sendFormat;
        set
        {
            if (!SetProperty(ref _sendFormat, value))
            {
                return;
            }

            _sendFormatOption = SendFormatOptions.First(x => x.Value == value);
            OnPropertyChanged(nameof(SendFormatOption));
            OnPropertyChanged(nameof(SendSyntax));
        }
    }

    public EnumOption<SendFormat> SendFormatOption
    {
        get => _sendFormatOption;
        set
        {
            if (value is not null)
            {
                SendFormat = value.Value;
            }
        }
    }

    public TextEncodingKind SendEncoding
    {
        get => _sendEncoding;
        set
        {
            if (!SetProperty(ref _sendEncoding, value))
            {
                return;
            }

            _sendEncodingOption = SendEncodingOptions.First(x => x.Value == value);
            OnPropertyChanged(nameof(SendEncodingOption));
        }
    }

    public EnumOption<TextEncodingKind> SendEncodingOption
    {
        get => _sendEncodingOption;
        set
        {
            if (value is not null)
            {
                SendEncoding = value.Value;
            }
        }
    }

    public NewlineMode NewlineMode
    {
        get => _newlineMode;
        set
        {
            if (!SetProperty(ref _newlineMode, value))
            {
                return;
            }

            _newlineOption = NewlineOptions.First(x => x.Value == value);
            OnPropertyChanged(nameof(NewlineOption));
            OnPropertyChanged(nameof(ShowCustomNewline));
        }
    }

    public bool ShowCustomNewline => NewlineMode == NewlineMode.Custom;

    public EnumOption<NewlineMode> NewlineOption
    {
        get => _newlineOption;
        set
        {
            if (value is not null)
            {
                NewlineMode = value.Value;
            }
        }
    }

    public string CustomNewline
    {
        get => _customNewline;
        set => SetProperty(ref _customNewline, value);
    }

    public bool UseTemplateVariables
    {
        get => _useTemplateVariables;
        set => SetProperty(ref _useTemplateVariables, value);
    }

    public bool SendAsBinary
    {
        get => _sendAsBinary;
        set => SetProperty(ref _sendAsBinary, value);
    }

    public string SendSyntax => SendFormat == SendFormat.Json ? "Json" : "None";
    public string SendCharCountText => $"{SendText.Length:N0} 字符";
    public bool HasSendText => !string.IsNullOrEmpty(SendText);
    public bool IsLooping
    {
        get => _isLooping;
        private set
        {
            if (SetProperty(ref _isLooping, value))
            {
                OnPropertyChanged(nameof(CanSendNow));
                OnPropertyChanged(nameof(ToggleLoopText));
                NotifyCommandStates();
            }
        }
    }

    public int LoopIntervalMs
    {
        get => _loopIntervalMs;
        set => SetProperty(ref _loopIntervalMs, Math.Max(10, value));
    }

    public int LoopCount
    {
        get => _loopCount;
        set => SetProperty(ref _loopCount, Math.Max(0, value));
    }

    public string LoopProgressText
    {
        get => _loopProgressText;
        private set => SetProperty(ref _loopProgressText, value);
    }

    public string ToggleLoopText => IsLooping ? "停止循环" : "循环发送";

    public string? SelectedHistory
    {
        get => _selectedHistory;
        set
        {
            if (SetProperty(ref _selectedHistory, value) && value is not null)
            {
                SendText = value;
            }
        }
    }

    public string LogFilter
    {
        get => _logFilter;
        set
        {
            if (SetProperty(ref _logFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool ShowSendLogs
    {
        get => _showSendLogs;
        set
        {
            if (SetProperty(ref _showSendLogs, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool ShowReceiveLogs
    {
        get => _showReceiveLogs;
        set
        {
            if (SetProperty(ref _showReceiveLogs, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool ShowOtherLogs
    {
        get => _showOtherLogs;
        set
        {
            if (SetProperty(ref _showOtherLogs, value))
            {
                ApplyFilter();
            }
        }
    }

    public bool AutoScroll
    {
        get => _autoScroll;
        set => SetProperty(ref _autoScroll, value);
    }

    public bool ShowTimestamps
    {
        get => _showTimestamps;
        set => SetProperty(ref _showTimestamps, value);
    }

    public int MaxLogEntries
    {
        get => _maxLogEntries;
        set
        {
            if (SetProperty(ref _maxLogEntries, Math.Clamp(value, 100, 200_000)))
            {
                TrimLogs();
            }
        }
    }

    public LogEntryViewModel? SelectedLog
    {
        get => _selectedLog;
        set
        {
            if (SetProperty(ref _selectedLog, value))
            {
                OnPropertyChanged(nameof(HasSelectedLog));
                OnPropertyChanged(nameof(CanReplySelected));
                ReplySelectedCommand.NotifyCanExecuteChanged();
                LoadSelectedTextCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedLog => SelectedLog is not null;
    public bool CanReplySelected => SelectedLog is { IsReceive: true, HasData: true } && !IsBusy;

    public string LogCountText =>
        Logs.Count == _allLogs.Count ? $"{_allLogs.Count:N0} 条" : $"{Logs.Count:N0} / {_allLogs.Count:N0} 条";

    public bool HasLogs => _allLogs.Count > 0;

    public ClientItemViewModel? SelectedClient
    {
        get => _selectedClient;
        set
        {
            if (SetProperty(ref _selectedClient, value))
            {
                NotifyCommandStates();
            }
        }
    }

    public bool Broadcast
    {
        get => _broadcast;
        set => SetProperty(ref _broadcast, value);
    }

    public string ClientCountText => $"{Clients.Count} 个客户端";
    public bool HasClients => Clients.Count > 0;

    public string SentTrafficText => $"{FormatBytes(_sentBytes)} / {_sentPackets:N0} 包";
    public string ReceivedTrafficText => $"{FormatBytes(_receivedBytes)} / {_receivedPackets:N0} 包";

    public bool CanSendNow =>
        !IsBusy && !IsLooping && IsSendAvailable && IsTransportReady && !string.IsNullOrWhiteSpace(SendText);

    protected virtual bool IsTransportReady => _transport?.IsRunning == true;

    protected ITransport? Transport => _transport;

    public virtual SessionConfig ToConfig()
    {
        var config = new SessionConfig
        {
            SessionName = SessionName,
            Protocol = Protocol,
            Mode = Mode,
            Host = Host,
            Port = Port,
            LocalHost = LocalHost,
            LocalPort = LocalPort,
            UseTls = UseTls,
            IgnoreCertificate = IgnoreCertificate,
            AutoReconnect = AutoReconnect,
            ReconnectIntervalMs = ReconnectIntervalMs,
            DefaultEncoding = DisplayEncoding,
            DisplayFormat = DisplayFormat,
            SendTemplate = SendText,
            SendFormat = SendFormat,
            SendEncoding = SendEncoding,
            Newline = NewlineMode,
            CustomNewline = CustomNewline,
            UseTemplateVariables = UseTemplateVariables,
            HistoryList = SendHistory.Take(30).ToList()
        };
        WriteProtocolConfig(config);
        return config;
    }

    public virtual void ApplyConfig(SessionConfig config)
    {
        SessionName = config.SessionName;
        SetModeFromConfig(config.Mode);
        Host = config.Host;
        Port = config.Port;
        LocalHost = config.LocalHost;
        LocalPort = config.LocalPort;
        UseTls = config.UseTls;
        IgnoreCertificate = config.IgnoreCertificate;
        AutoReconnect = config.AutoReconnect;
        ReconnectIntervalMs = config.ReconnectIntervalMs;
        DisplayFormat = config.DisplayFormat;
        DisplayEncoding = config.DefaultEncoding;
        SendText = config.SendTemplate;
        SendFormat = config.SendFormat;
        SendEncoding = config.SendEncoding;
        NewlineMode = config.Newline;
        CustomNewline = config.CustomNewline;
        UseTemplateVariables = config.UseTemplateVariables;
        SendHistory.Clear();
        foreach (var item in config.HistoryList.Take(30))
        {
            SendHistory.Add(item);
        }

        SelectedHistory = null;
        ApplyProtocolConfig(config);
        _statusMessage = DefaultStatusMessage;
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(RemoteTargetLabel));
        NotifyCommandStates();
    }

    protected virtual void WriteProtocolConfig(SessionConfig config)
    {
    }

    protected virtual void ApplyProtocolConfig(SessionConfig config)
    {
    }

    protected virtual ITransport? CreateTransport() => null;

    protected virtual Task SendPayloadAsync(byte[] payload, string? remoteId, bool broadcast, bool binary)
    {
        var transport = _transport;
        if (transport is null || !transport.IsRunning)
        {
            throw new InvalidOperationException("会话尚未启动");
        }

        return transport.SendAsync(payload, remoteId, broadcast, binary);
    }

    private async Task ConnectAsync()
    {
        if (IsBusy || !SupportsConnection)
        {
            return;
        }

        if (IsActive)
        {
            await StopSessionAsync();
            return;
        }

        await StartSessionAsync();
    }

    protected virtual async Task StartSessionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        NotifyCommandStates();
        try
        {
            await DisposeTransportAsync();
            var transport = CreateTransport();
            if (transport is null)
            {
                SetStateCore(TransportState.Running, "就绪");
                return;
            }

            _transport = transport;
            AttachTransport(transport);
            await transport.StartAsync();
            SyncClientsFromTransport();
        }
        catch (Exception ex)
        {
            await DisposeTransportAsync();
            SetStateCore(TransportState.Faulted, "启动失败");
            AddLogCore(LogEntry.Error(SessionName, $"启动失败：{ex.Message}"));
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    private async Task StopSessionAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StopLoop("循环发送已停止", log: false);
        NotifyCommandStates();
        try
        {
            await DisposeTransportAsync();
            ClearClientItems();
            SetStateCore(TransportState.Stopped, IsServerMode ? "已停止" : "已断开");
        }
        finally
        {
            IsBusy = false;
            NotifyCommandStates();
        }
    }

    private async Task DisposeTransportAsync()
    {
        var transport = _transport;
        _transport = null;
        if (transport is null)
        {
            return;
        }

        DetachTransport(transport);
        try
        {
            await transport.DisposeAsync();
        }
        catch
        {
            // ignored
        }
    }

    private void AttachTransport(ITransport transport)
    {
        transport.MessageReceived += OnTransportMessageReceived;
        transport.ClientConnected += OnTransportClientConnected;
        transport.ClientDisconnected += OnTransportClientDisconnected;
        transport.StateChanged += OnTransportStateChanged;
        transport.ErrorOccurred += OnTransportError;
        transport.InfoOccurred += OnTransportInfo;
    }

    private void DetachTransport(ITransport transport)
    {
        transport.MessageReceived -= OnTransportMessageReceived;
        transport.ClientConnected -= OnTransportClientConnected;
        transport.ClientDisconnected -= OnTransportClientDisconnected;
        transport.StateChanged -= OnTransportStateChanged;
        transport.ErrorOccurred -= OnTransportError;
        transport.InfoOccurred -= OnTransportInfo;
    }

    private void OnTransportMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        AddLogCore(LogEntry.Create(LogDirection.Receive, e.Data, SessionName, e.RemoteEndpoint,
            remoteId: e.RemoteId, binaryHint: e.IsBinary));
    }

    private void OnTransportClientConnected(object? sender, RemoteClient client)
    {
        RunOnUi(() => UpsertClient(client));
    }

    private void OnTransportClientDisconnected(object? sender, RemoteClient client)
    {
        RunOnUi(() => RemoveClientItem(client.Id));
    }

    private void OnTransportStateChanged(object? sender, TransportStateChangedEventArgs e)
    {
        RunOnUi(() =>
        {
            SetStateCore(e.State, e.Message);
            if (e.State == TransportState.Running)
            {
                SyncClientsFromTransport();
            }
            else if (e.State is TransportState.Stopped or TransportState.Faulted)
            {
                ClearClientItems();
                if (e.State == TransportState.Faulted)
                {
                    StopLoop("连接异常，循环发送已停止", log: false);
                }
            }
        });
    }

    private void OnTransportError(object? sender, TransportErrorEventArgs e)
    {
        AddLogCore(LogEntry.Error(SessionName, e.Message));
    }

    private void OnTransportInfo(object? sender, string message)
    {
        AddLogCore(LogEntry.Info(SessionName, message));
    }

    private void SetStateCore(TransportState state, string message)
    {
        var changed = _state != state;
        _state = state;
        _statusMessage = message;
        OnPropertyChanged(nameof(State));
        OnPropertyChanged(nameof(StatusMessage));
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsStarting));
        OnPropertyChanged(nameof(IsStopping));
        OnPropertyChanged(nameof(IsFaulted));
        OnPropertyChanged(nameof(IsStopped));
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(ConnectButtonText));
        OnPropertyChanged(nameof(CanReplySelected));
        if (changed)
        {
            NotifyCommandStates();
        }
    }

    protected void SetStateCoreFromSession(TransportState state, string message) =>
        RunOnUi(() => SetStateCore(state, message));

    protected void SetStateDirect(TransportState state, string message) => SetStateCore(state, message);

    private void SyncClientsFromTransport()
    {
        var transport = _transport;
        if (transport is null)
        {
            return;
        }

        foreach (var client in transport.Clients)
        {
            UpsertClient(client);
        }
    }

    private void UpsertClient(RemoteClient client)
    {
        var existing = Clients.FirstOrDefault(x => x.Id == client.Id);
        if (existing is null)
        {
            Clients.Add(new ClientItemViewModel(client));
        }

        OnPropertyChanged(nameof(ClientCountText));
        OnPropertyChanged(nameof(HasClients));
        NotifyCommandStates();
    }

    private void RemoveClientItem(string id)
    {
        var existing = Clients.FirstOrDefault(x => x.Id == id);
        if (existing is not null)
        {
            Clients.Remove(existing);
            if (SelectedClient == existing)
            {
                SelectedClient = null;
            }
        }

        OnPropertyChanged(nameof(ClientCountText));
        OnPropertyChanged(nameof(HasClients));
        NotifyCommandStates();
    }

    private void ClearClientItems()
    {
        Clients.Clear();
        SelectedClient = null;
        OnPropertyChanged(nameof(ClientCountText));
        OnPropertyChanged(nameof(HasClients));
    }

    protected void AddLog(LogEntry entry) => RunOnUi(() => AddLogCore(entry));

    protected void AddInfo(string message) => AddLog(LogEntry.Info(SessionName, message));

    protected void AddError(string message) => AddLog(LogEntry.Error(SessionName, message));

    public void LogInfo(string message) => AddInfo(message);

    public void LogError(string message) => AddError(message);

    protected void AddLogCore(LogEntry entry)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => AddLogCore(entry));
            return;
        }

        var vm = new LogEntryViewModel(entry, DisplayFormat, DisplayEncoding);
        _allLogs.Add(vm);
        if (IsVisible(vm))
        {
            Logs.Add(vm);
        }

        if (entry.Direction is LogDirection.Send or LogDirection.Receive)
        {
            if (entry.Direction == LogDirection.Send)
            {
                _sentBytes += entry.Data.Length;
                _sentPackets++;
            }
            else
            {
                _receivedBytes += entry.Data.Length;
                _receivedPackets++;
            }

            OnPropertyChanged(nameof(SentTrafficText));
            OnPropertyChanged(nameof(ReceivedTrafficText));
        }

        var globalPreview = vm.Preview.ReplaceLineEndings(" ⏎ ");
        if (globalPreview.Length > 400)
        {
            globalPreview = globalPreview[..400] + "…";
        }

        Services.GlobalLog.Add(new GlobalLogEntry
        {
            Timestamp = entry.Timestamp,
            SessionName = SessionName,
            Direction = entry.Direction,
            Remote = $"{entry.Remote}{(string.IsNullOrWhiteSpace(entry.RemoteId) || entry.RemoteId == entry.Remote ? "" : $" [{entry.RemoteId}]")}",
            Size = entry.Data.Length,
            Preview = globalPreview
        });

        TrimLogs();
        OnPropertyChanged(nameof(LogCountText));
        OnPropertyChanged(nameof(HasLogs));
        ExportLogsCommand.NotifyCanExecuteChanged();
    }

    private void TrimLogs()
    {
        var changed = false;
        while (_allLogs.Count > MaxLogEntries)
        {
            var oldest = _allLogs[0];
            _allLogs.RemoveAt(0);
            Logs.Remove(oldest);
            if (SelectedLog == oldest)
            {
                SelectedLog = null;
            }

            changed = true;
        }

        if (changed)
        {
            OnPropertyChanged(nameof(LogCountText));
            OnPropertyChanged(nameof(HasLogs));
        }
    }

    private bool IsVisible(LogEntryViewModel log) => log.Entry.Direction switch
    {
        LogDirection.Send => ShowSendLogs,
        LogDirection.Receive => ShowReceiveLogs,
        _ => ShowOtherLogs
    } && log.MatchesFilter(LogFilter);

    private void ApplyFilter()
    {
        Logs.Clear();
        foreach (var log in _allLogs)
        {
            if (IsVisible(log))
            {
                Logs.Add(log);
            }
        }

        if (SelectedLog is not null && !Logs.Contains(SelectedLog))
        {
            SelectedLog = null;
        }

        OnPropertyChanged(nameof(LogCountText));
    }

    private void RefreshLogDisplay()
    {
        foreach (var log in _allLogs)
        {
            log.Refresh(DisplayFormat, DisplayEncoding);
        }

        ApplyFilter();
    }

    private void ClearLogs()
    {
        _allLogs.Clear();
        Logs.Clear();
        SelectedLog = null;
        OnPropertyChanged(nameof(LogCountText));
        OnPropertyChanged(nameof(HasLogs));
        ExportLogsCommand.NotifyCanExecuteChanged();
    }

    private async Task SendAsync()
    {
        await SendCurrentAsync();
    }

    protected virtual async Task<bool> SendCurrentAsync(bool addHistory = true)
    {
        if (string.IsNullOrWhiteSpace(SendText))
        {
            AddError("发送内容为空");
            return false;
        }

        byte[] payload;
        try
        {
            payload = BuildPayload(SendText);
        }
        catch (Exception ex)
        {
            AddError($"报文编码失败：{ex.Message}");
            return false;
        }

        if (payload.Length == 0)
        {
            AddError("编码后的报文为空");
            return false;
        }

        if (!TryResolveSendTarget(out var remoteId, out var label, out var broadcast, out var error))
        {
            AddError(error ?? "未找到发送目标");
            return false;
        }

        try
        {
            await SendPayloadAsync(payload, remoteId, broadcast, SendAsBinary);
            AddLogCore(LogEntry.Create(LogDirection.Send, payload, SessionName, label, remoteId: remoteId,
                binaryHint: SendAsBinary));
            if (addHistory)
            {
                AddSendHistory(SendText);
            }

            return true;
        }
        catch (Exception ex)
        {
            AddError($"发送失败：{ex.Message}");
            return false;
        }
    }

    protected virtual bool TryResolveSendTarget(out string? remoteId, out string label, out bool broadcast,
        out string? error)
    {
        remoteId = null;
        broadcast = false;
        error = null;
        if (IsServerMode && SupportsClients)
        {
            if (Broadcast)
            {
                broadcast = true;
                label = "广播";
                return true;
            }

            if (SelectedClient is null)
            {
                label = string.Empty;
                error = "请先选择一个目标客户端，或勾选切换为广播发送";
                return false;
            }

            remoteId = SelectedClient.Id;
            label = SelectedClient.Endpoint;
            return true;
        }

        label = RemoteTargetLabel;
        return true;
    }

    protected byte[] BuildPayload(string text)
    {
        if (SendFormat is SendFormat.Text or SendFormat.Json)
        {
            var content = UseTemplateVariables ? _templateEngine.Expand(text) : text;
            content += NewlineText();
            return CodecEngine.Encode(content, SendEncoding);
        }

        return SendFormat switch
        {
            SendFormat.Hex => CodecEngine.ParseHex(text),
            SendFormat.Base64 => CodecEngine.ParseBase64(text),
            _ => CodecEngine.Encode(text, SendEncoding)
        };
    }

    private string NewlineText() => NewlineMode switch
    {
        NewlineMode.Crlf => "\r\n",
        NewlineMode.Lf => "\n",
        NewlineMode.Custom => Unescape(CustomNewline),
        _ => string.Empty
    };

    private static string Unescape(string text) => text
        .Replace("\\r", "\r")
        .Replace("\\n", "\n")
        .Replace("\\t", "\t")
        .Replace("\\0", "\0");

    private void AddSendHistory(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var existing = SendHistory.FirstOrDefault(x => x == text);
        if (existing is not null)
        {
            SendHistory.Remove(existing);
        }

        SendHistory.Insert(0, text);
        while (SendHistory.Count > 30)
        {
            SendHistory.RemoveAt(SendHistory.Count - 1);
        }
    }

    private async Task ToggleLoopAsync()
    {
        if (IsLooping)
        {
            StopLoop("循环发送已停止");
            return;
        }

        if (!CanSendNow)
        {
            AddError("请先连接并填写发送内容，再启动循环发送");
            return;
        }

        if (!TryResolveSendTarget(out _, out _, out _, out var error))
        {
            AddError(error ?? "未找到发送目标");
            return;
        }

        var previousLoop = _loopTask;
        if (previousLoop is { IsCompleted: false })
        {
            try
            {
                await previousLoop;
            }
            catch
            {
                // ignored
            }
        }

        var interval = Math.Max(10, LoopIntervalMs);
        var max = LoopCount;
        IsLooping = true;
        LoopProgressText = "循环发送中…";
        var cts = new CancellationTokenSource();
        lock (_loopSync)
        {
            _loopCts = cts;
        }

        _loopTask = LoopAsync(max, interval, cts.Token);
        await Task.CompletedTask;
    }

    private async Task LoopAsync(int maxCount, int interval, CancellationToken token)
    {
        var sent = 0;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var ok = await SendCurrentAsync(sent == 0);
                if (!ok)
                {
                    break;
                }

                sent++;
                LoopProgressText = $"循环发送中：已发送 {sent:N0} 次";
                if (maxCount > 0 && sent >= maxCount)
                {
                    break;
                }

                await Task.Delay(interval, token);
            }
        }
        catch (OperationCanceledException)
        {
            // normal stop
        }
        finally
        {
            IsLooping = false;
            if (!token.IsCancellationRequested)
            {
                LoopProgressText = $"循环发送结束，共 {sent:N0} 次";
            }

            lock (_loopSync)
            {
                _loopCts?.Dispose();
                _loopCts = null;
                _loopTask = null;
            }

            NotifyCommandStates();
        }
    }

    private void StopLoop(string? message, bool log = true)
    {
        CancellationTokenSource? cts;
        lock (_loopSync)
        {
            cts = _loopCts;
            _loopCts = null;
        }

        cts?.Cancel();
        cts?.Dispose();
        if (IsLooping)
        {
            IsLooping = false;
            if (!string.IsNullOrEmpty(message))
            {
                LoopProgressText = message;
                if (log)
                {
                    AddInfo(message);
                }
            }
        }

        NotifyCommandStates();
    }

    private async Task LoadFileAsync()
    {
        var path = await Services.Dialogs.PickOpenFileAsync("选择要发送的文件",
            new FilePickerFileType("所有文件") { Patterns = ["*"] });
        if (path is null)
        {
            return;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (CodecEngine.LooksBinary(bytes))
            {
                SendFormat = SendFormat.Hex;
                SendText = CodecEngine.BytesToHex(bytes);
            }
            else
            {
                SendFormat = SendFormat.Text;
                SendText = CodecEngine.Decode(bytes, SendEncoding);
            }

            AddInfo($"已加载文件：{Path.GetFileName(path)}（{bytes.Length:N0} B）");
        }
        catch (Exception ex)
        {
            AddError($"读取文件失败：{ex.Message}");
        }
    }

    private async Task ExportLogsAsync(string? formatText)
    {
        await ExportAsync(formatText, false);
    }

    private async Task ExportSelectedAsync(string? formatText)
    {
        await ExportAsync(formatText, true);
    }

    private async Task ExportAsync(string? formatText, bool selectedOnly)
    {
        if (!Enum.TryParse<ExportFormat>(formatText, true, out var format))
        {
            return;
        }

        var entries = selectedOnly
            ? (SelectedLog is null ? [] : new List<LogEntry> { SelectedLog.Entry })
            : _allLogs.Select(x => x.Entry).ToList();
        if (entries.Count == 0)
        {
            AddError(selectedOnly ? "请先选择一条报文" : "没有可导出的日志");
            return;
        }

        var extension = format switch
        {
            ExportFormat.Csv => "csv",
            ExportFormat.Hex => "hex",
            _ => "txt"
        };
        var filter = new FilePickerFileType($"{extension.ToUpperInvariant()} 文件")
        {
            Patterns = [$"*.{extension}"]
        };
        var suggested = $"{SessionName}{(selectedOnly ? "-单条" : "")}-{DateTime.Now:yyyyMMdd-HHmmss}.{extension}";
        var path = await Services.Dialogs.PickSaveFileAsync("导出日志", suggested, filter);
        if (path is null)
        {
            return;
        }

        try
        {
            await LogExporter.ExportAsync(path, format, entries, DisplayFormat, DisplayEncoding);
            AddInfo($"日志已导出：{path}");
        }
        catch (Exception ex)
        {
            await Services.Dialogs.ShowMessageAsync("导出失败", ex.Message, true);
        }
    }

    private async Task CopySelectedAsync()
    {
        if (SelectedLog is null)
        {
            return;
        }

        await Services.Dialogs.CopyTextAsync(SelectedLog.FullText);
        Services.Toasts.Show("已复制到剪贴板");
    }

    private async Task ReplySelectedAsync()
    {
        if (SelectedLog is not { IsReceive: true, HasData: true } log)
        {
            AddError("请选择一条接收到的报文再回发");
            return;
        }

        var remoteId = log.RemoteId;
        if (IsServerMode)
        {
            if (string.IsNullOrEmpty(remoteId))
            {
                var client = Clients.FirstOrDefault(x => x.Endpoint == log.RemoteLabel);
                remoteId = client?.Id;
            }

            if (string.IsNullOrEmpty(remoteId))
            {
                AddError("无法确定该报文的来源客户端，可能已断开");
                return;
            }
        }

        try
        {
            await SendPayloadAsync(log.Data, remoteId, false, log.Entry.IsBinaryHint);
            AddLogCore(LogEntry.Create(LogDirection.Send, log.Data, SessionName, log.RemoteLabel,
                remoteId: remoteId, binaryHint: log.Entry.IsBinaryHint));
            AddInfo($"已回发 {log.Data.Length:N0} B 到 {log.RemoteLabel}");
        }
        catch (Exception ex)
        {
            AddError($"回发失败：{ex.Message}");
        }
    }

    private async Task LoadSelectedTextAsync()
    {
        if (SelectedLog is not { HasData: true } log)
        {
            return;
        }

        try
        {
            if (CodecEngine.LooksBinary(log.Data))
            {
                SendFormat = SendFormat.Hex;
                SendText = CodecEngine.BytesToHex(log.Data);
            }
            else
            {
                SendFormat = SendFormat.Text;
                SendText = CodecEngine.Decode(log.Data, SendEncoding);
            }

            if (SupportsBinaryMessages)
            {
                SendAsBinary = log.Entry.IsBinaryHint;
            }

            AddInfo("已将选中报文载入发送框");
        }
        catch (Exception ex)
        {
            AddError($"载入报文失败：{ex.Message}");
        }

        await Task.CompletedTask;
    }

    private void ApplyCodec(string? action)
    {
        try
        {
            switch (action)
            {
                case "toHex":
                    SendText = CodecEngine.BytesToHex(CodecEngine.Encode(SendText, SendEncoding));
                    SendFormat = SendFormat.Hex;
                    break;
                case "hexToText":
                    SendText = CodecEngine.Decode(CodecEngine.ParseHex(SendText), SendEncoding);
                    SendFormat = SendFormat.Text;
                    break;
                case "toBase64":
                    SendText = CodecEngine.BytesToBase64(CodecEngine.Encode(SendText, SendEncoding));
                    SendFormat = SendFormat.Base64;
                    break;
                case "base64ToText":
                    SendText = CodecEngine.Decode(CodecEngine.ParseBase64(SendText), SendEncoding);
                    SendFormat = SendFormat.Text;
                    break;
                case "jsonFormat":
                    if (JsonFormatter.TryFormat(SendText, out var formatted, out var error))
                    {
                        SendText = formatted;
                        SendFormat = SendFormat.Json;
                        AddInfo("JSON 格式化成功");
                    }
                    else
                    {
                        AddError($"JSON 校验失败：{error}");
                    }
                    break;
                case "jsonValidate":
                    if (JsonFormatter.TryFormat(SendText, out _, out var jsonError))
                    {
                        AddInfo("JSON 校验通过");
                    }
                    else
                    {
                        AddError($"JSON 校验失败：{jsonError}");
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            AddError($"转换失败：{ex.Message}");
        }
    }

    public void ApplySettings(AppSettings settings)
    {
        MaxLogEntries = settings.MaxLogEntries;
        ShowTimestamps = settings.ShowTimestamps;
    }

    protected void NotifyCommandStates()
    {
        ConnectCommand.NotifyCanExecuteChanged();
        SendCommand.NotifyCanExecuteChanged();
        ToggleLoopCommand.NotifyCanExecuteChanged();
        ReplySelectedCommand.NotifyCanExecuteChanged();
        LoadSelectedTextCommand.NotifyCanExecuteChanged();
        ExportLogsCommand.NotifyCanExecuteChanged();
        ExportSelectedCommand.NotifyCanExecuteChanged();
        CopySelectedCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(CanSendNow));
        OnPropertyChanged(nameof(ConnectButtonText));
    }

    protected int SelectedClientCount => Clients.Count;

    public virtual async Task ShutdownAsync()
    {
        if (_disposed)
        {
            return;
        }

        StopLoop(null, log: false);
        await DisposeTransportAsync();
        ClearClientItems();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await ShutdownAsync();
        GC.SuppressFinalize(this);
    }

    protected static void RunOnUi(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} B"
    };
}
