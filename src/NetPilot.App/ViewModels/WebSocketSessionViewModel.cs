using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using NetPilot.App.Services;
using NetPilot.Core.Models;
using NetPilot.Core.Protocols;

namespace NetPilot.App.ViewModels;

public sealed class WebSocketSessionViewModel : SessionViewModel
{
    private string _wsUrl = "ws://127.0.0.1:8080";
    private string _subProtocol = string.Empty;

    public WebSocketSessionViewModel(AppServices services, SessionMode mode)
        : base(services, mode, "客户端", "服务端")
    {
        WsHeaders = new ObservableCollection<HeaderItem>();
        AddHeaderCommand = new RelayCommand(() => WsHeaders.Add(new HeaderItem("Header", string.Empty)));
        RemoveHeaderCommand = new RelayCommand<HeaderItem>(header =>
        {
            if (header is not null)
            {
                WsHeaders.Remove(header);
            }
        });
    }

    public override ProtocolKind Protocol => ProtocolKind.WebSocket;
    public override string ProtocolDisplay => "WebSocket 调试";
    public override bool SupportsReconnect => true;
    public override bool SupportsClients => true;
    public override bool SupportsBinaryMessages => true;
    public override bool ShowIgnoreCertificateOption => IsClientMode;

    public ObservableCollection<HeaderItem> WsHeaders { get; }
    public RelayCommand AddHeaderCommand { get; }
    public RelayCommand<HeaderItem> RemoveHeaderCommand { get; }

    public string WsUrl
    {
        get => _wsUrl;
        set
        {
            if (SetProperty(ref _wsUrl, value))
            {
                OnPropertyChanged(nameof(RemoteTargetLabel));
            }
        }
    }

    public string SubProtocol
    {
        get => _subProtocol;
        set => SetProperty(ref _subProtocol, value);
    }

    public override string RemoteTargetLabel => IsClientMode ? WsUrl : $"{LocalHost}:{Port}";

    public override string ConnectButtonText => IsActive
        ? (IsServerMode ? "停止服务" : "断开连接")
        : (IsServerMode ? "启动服务" : "连接");

    protected override string DefaultStatusMessage => IsServerMode ? "尚未启动服务" : "尚未连接 WebSocket";

    protected override ITransport? CreateTransport()
    {
        if (IsServerMode)
        {
            return new WebSocketServerTransport(LocalHost, Port);
        }

        var headers = WsHeaders
            .Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key))
            .Select(x => (x.Key.Trim(), x.Value))
            .ToList();
        return new WebSocketClientTransport(WsUrl, SubProtocol, headers, IgnoreCertificate, AutoReconnect,
            ReconnectIntervalMs);
    }

    protected override void WriteProtocolConfig(SessionConfig config)
    {
        base.WriteProtocolConfig(config);
        config.WsUrl = WsUrl;
        config.WsSubProtocol = SubProtocol;
        config.WsBinary = SendAsBinary;
        config.WsHeaders = WsHeaders.Select(x => x.Clone()).ToList();
    }

    protected override void ApplyProtocolConfig(SessionConfig config)
    {
        base.ApplyProtocolConfig(config);
        WsUrl = config.WsUrl;
        SubProtocol = config.WsSubProtocol;
        SendAsBinary = config.WsBinary;
        WsHeaders.Clear();
        foreach (var header in config.WsHeaders)
        {
            WsHeaders.Add(header.Clone());
        }

        OnPropertyChanged(nameof(RemoteTargetLabel));
    }
}
