using NetPilot.App.Services;
using NetPilot.Core.Models;
using NetPilot.Core.Protocols;

namespace NetPilot.App.ViewModels;

public sealed class TcpSessionViewModel : SessionViewModel
{
    public TcpSessionViewModel(AppServices services, SessionMode mode)
        : base(services, mode, "客户端", "服务端")
    {
    }

    public override ProtocolKind Protocol => ProtocolKind.Tcp;
    public override string ProtocolDisplay => "TCP 调试";
    public override bool SupportsTls => true;
    public override bool SupportsReconnect => true;
    public override bool SupportsClients => true;
    public override bool ShowIgnoreCertificateOption => IsClientMode;

    public override string ConnectButtonText => IsActive
        ? (IsServerMode ? "停止监听" : "断开连接")
        : (IsServerMode ? "启动监听" : "连接");

    protected override string DefaultStatusMessage => IsServerMode ? "尚未启动监听" : "尚未连接到服务端";

    protected override ITransport? CreateTransport() => IsServerMode
        ? new TcpServerTransport(LocalHost, Port)
        : new TcpClientTransport(Host, Port, UseTls, IgnoreCertificate, AutoReconnect, ReconnectIntervalMs);
}
