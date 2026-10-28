using NetPilot.App.Services;
using NetPilot.Core.Models;
using NetPilot.Core.Protocols;

namespace NetPilot.App.ViewModels;

public sealed class UdpSessionViewModel : SessionViewModel
{
    public UdpSessionViewModel(AppServices services, SessionMode mode)
        : base(services, mode, "发送端", "监听")
    {
    }

    public override ProtocolKind Protocol => ProtocolKind.Udp;
    public override string ProtocolDisplay => "UDP 调试";
    public override bool SupportsClients => true;

    public override string ConnectButtonText => IsActive
        ? (IsServerMode ? "停止监听" : "停止")
        : (IsServerMode ? "开始监听" : "启动");

    protected override string DefaultStatusMessage => IsServerMode ? "尚未开始监听" : "UDP 发送端尚未启动";

    public override string RemoteTargetLabel => IsServerMode ? $"{LocalHost}:{Port}" : $"{Host}:{Port}";

    protected override ITransport? CreateTransport() => IsServerMode
        ? new UdpTransport(UdpRole.Listen, Host, Port, LocalHost, LocalPort)
        : new UdpTransport(UdpRole.Send, Host, Port, LocalHost, LocalPort);
}
