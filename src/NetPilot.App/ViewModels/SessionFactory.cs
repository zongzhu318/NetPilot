using NetPilot.App.Services;
using NetPilot.Core.Models;

namespace NetPilot.App.ViewModels;

public static class SessionFactory
{
    public static SessionViewModel Create(AppServices services, ProtocolKind protocol, SessionMode mode)
    {
        SessionViewModel session = protocol switch
        {
            ProtocolKind.Tcp => new TcpSessionViewModel(services, mode),
            ProtocolKind.Udp => new UdpSessionViewModel(services, mode),
            ProtocolKind.WebSocket => new WebSocketSessionViewModel(services, mode),
            _ => new HttpSessionViewModel(services, mode)
        };

        session.SessionName = DefaultName(protocol, mode);
        if (mode == SessionMode.Server)
        {
            session.LocalHost = "0.0.0.0";
        }

        if (protocol == ProtocolKind.Http && mode == SessionMode.Server)
        {
            session.Port = 9000;
        }

        return session;
    }

    public static SessionViewModel Create(AppServices services, SessionConfig config, string? filePath = null)
    {
        var session = Create(services, config.Protocol, config.Mode);
        session.ApplyConfig(config);
        session.FilePath = filePath;
        return session;
    }

    public static bool TryParseSpec(string? spec, out ProtocolKind protocol, out SessionMode mode)
    {
        protocol = ProtocolKind.Tcp;
        mode = SessionMode.Client;
        if (string.IsNullOrWhiteSpace(spec))
        {
            return false;
        }

        var parts = spec.Split(':', 2, StringSplitOptions.TrimEntries);
        var protocolText = parts[0].ToLowerInvariant();
        var modeText = (parts.Length > 1 ? parts[1] : "Client").ToLowerInvariant();

        protocol = protocolText switch
        {
            "tcp" => ProtocolKind.Tcp,
            "udp" => ProtocolKind.Udp,
            "ws" or "websocket" => ProtocolKind.WebSocket,
            "http" => ProtocolKind.Http,
            _ => (ProtocolKind)(-1)
        };

        if ((int)protocol < 0)
        {
            return false;
        }

        mode = modeText switch
        {
            "client" => SessionMode.Client,
            "server" or "listen" or "mock" => SessionMode.Server,
            _ => (SessionMode)(-1)
        };

        return (int)mode >= 0;
    }

    public static string DefaultName(ProtocolKind protocol, SessionMode mode) => protocol switch
    {
        ProtocolKind.Tcp => mode == SessionMode.Client ? "TCP 客户端" : "TCP 服务端",
        ProtocolKind.Udp => mode == SessionMode.Client ? "UDP 发送端" : "UDP 监听",
        ProtocolKind.WebSocket => mode == SessionMode.Client ? "WebSocket 客户端" : "WebSocket 服务端",
        _ => mode == SessionMode.Client ? "HTTP 请求" : "HTTP Mock"
    };
}
