using System.Net;
using System.Net.Sockets;

namespace NetPilot.Core.Protocols;

public enum UdpRole
{
    Send,
    Listen
}

public sealed class UdpTransport : TransportBase
{
    private readonly UdpRole _role;
    private readonly string _host;
    private readonly int _port;
    private readonly string _localHost;
    private readonly int _localPort;

    private readonly object _remoteSync = new();
    private readonly Dictionary<string, Models.RemoteClient> _remotes = new();
    private UdpClient? _udp;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;

    public UdpTransport(UdpRole role, string host, int port, string localHost, int localPort)
    {
        _role = role;
        _host = host;
        _port = port;
        _localHost = localHost;
        _localPort = localPort;
    }

    public override string Endpoint =>
        _role == UdpRole.Listen ? $"{_localHost}:{_port}" : $"{_host}:{_port}";

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("端口必须在 1 - 65535 之间");
        }

        var bindAddress = _role == UdpRole.Listen
            ? NetworkUtil.ResolveBindAddress(_localHost)
            : IPAddress.Any;
        var bindPort = _role == UdpRole.Listen ? _port : _localPort;

        if (_role == UdpRole.Listen && !NetworkUtil.IsUdpPortAvailable(bindAddress, _port))
        {
            throw new InvalidOperationException($"UDP 端口 {_port} 已被占用，请更换端口");
        }

        try
        {
            _cts = new CancellationTokenSource();
            _udp = new UdpClient(new IPEndPoint(bindAddress, bindPort)) { EnableBroadcast = true };
            _receiveTask = Task.Run(() => ReceiveLoopAsync(_udp, _cts.Token), CancellationToken.None);
            var label = _role == UdpRole.Listen ? "UDP 监听已启动" : "UDP 发送端已就绪";
            SetState(Models.TransportState.Running, _role == UdpRole.Listen
                ? $"正在监听 {_localHost}:{_port}"
                : $"UDP 就绪，目标 {_host}:{_port}");
            RaiseInfo($"{label}：{Endpoint}");
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _cts?.Dispose();
            _cts = null;
            _udp?.Dispose();
            _udp = null;
            SetState(Models.TransportState.Faulted, "启动失败");
            throw new InvalidOperationException($"启动 UDP 失败：{ex.Message}", ex);
        }
    }

    private async Task ReceiveLoopAsync(UdpClient udp, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(token).ConfigureAwait(false);
                if (result.Buffer.Length == 0)
                {
                    continue;
                }

                var endpoint = result.RemoteEndPoint;
                var id = NetworkUtil.EndpointText(endpoint);
                var isNew = false;
                lock (_remoteSync)
                {
                    if (!_remotes.TryGetValue(id, out var remote))
                    {
                        remote = new Models.RemoteClient
                        {
                            Id = id,
                            Endpoint = id,
                            ConnectedAt = DateTime.Now,
                            LastSeenAt = DateTime.Now
                        };
                        _remotes[id] = remote;
                        isNew = true;
                    }

                    remote.LastSeenAt = DateTime.Now;
                    remote.BytesReceived += result.Buffer.Length;
                }

                if (isNew)
                {
                    AddClient(new Models.RemoteClient
                    {
                        Id = id,
                        Endpoint = id,
                        ConnectedAt = DateTime.Now,
                        LastSeenAt = DateTime.Now
                    });
                    RaiseInfo($"收到新的 UDP 来源：{id}");
                }

                RaiseMessage(new MessageReceivedEventArgs
                {
                    Data = result.Buffer,
                    RemoteId = id,
                    RemoteEndpoint = id
                });
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    RaiseError($"UDP 接收失败：{ex.Message}", ex);
                }
            }
        }
    }

    public override async Task SendAsync(ReadOnlyMemory<byte> data, string? remoteId = null, bool broadcast = false,
        bool binary = false)
    {
        var udp = _udp;
        if (udp is null || State != Models.TransportState.Running)
        {
            throw new InvalidOperationException("UDP 尚未启动");
        }

        IPEndPoint target;
        if (broadcast)
        {
            target = new IPEndPoint(IPAddress.Broadcast, _port);
        }
        else if (!string.IsNullOrWhiteSpace(remoteId))
        {
            if (!NetworkUtil.TryParseEndpoint(remoteId, _port, out target))
            {
                throw new InvalidOperationException($"无效的目标地址：{remoteId}");
            }
        }
        else if (_role == UdpRole.Listen)
        {
            throw new InvalidOperationException("请先选择一个来源地址，或启用广播发送");
        }
        else
        {
            target = new IPEndPoint(NetworkUtil.ResolveTargetAddress(_host), _port);
        }

        await udp.SendAsync(data, target).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(remoteId))
        {
            lock (_remoteSync)
            {
                if (_remotes.TryGetValue(remoteId, out var remote))
                {
                    remote.BytesSent += data.Length;
                    remote.LastSeenAt = DateTime.Now;
                }
            }
        }
    }

    public override async Task StopAsync()
    {
        var cts = _cts;
        if (cts is null)
        {
            SetState(Models.TransportState.Stopped, "已停止");
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch
        {
            // ignored
        }

        try
        {
            _udp?.Dispose();
        }
        catch
        {
            // ignored
        }

        if (_receiveTask is not null)
        {
            try
            {
                await _receiveTask.ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        cts.Dispose();
        _cts = null;
        _udp = null;
        _receiveTask = null;
        lock (_remoteSync)
        {
            _remotes.Clear();
        }

        ClearClients();
        SetState(Models.TransportState.Stopped, "已停止");
        RaiseInfo("UDP 已停止");
    }
}
