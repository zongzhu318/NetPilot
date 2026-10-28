using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace NetPilot.Core.Protocols;

public sealed class TcpServerTransport : TransportBase
{
    private sealed class Connection
    {
        public required string Id { get; init; }
        public required TcpClient Client { get; init; }
        public required NetworkStream Stream { get; init; }
        public required Models.RemoteClient Info { get; init; }
        public required SemaphoreSlim SendLock { get; init; }
        public required CancellationTokenSource Cts { get; init; }
    }

    private readonly string _host;
    private readonly int _port;
    private readonly ConcurrentDictionary<string, Connection> _connections = new();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;

    public TcpServerTransport(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public override string Endpoint => $"{_host}:{_port}";

    public override Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("端口必须在 1 - 65535 之间");
        }

        var address = NetworkUtil.ResolveBindAddress(_host);
        if (!NetworkUtil.IsTcpPortAvailable(address, _port))
        {
            throw new InvalidOperationException($"端口 {_port} 已被占用，请更换端口或停止占用程序");
        }

        try
        {
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(address, _port);
            _listener.Start(128);
            SetState(Models.TransportState.Running, $"正在监听 {_host}:{_port}");
            RaiseInfo($"TCP 服务已启动：{_host}:{_port}");
            _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), CancellationToken.None);
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            SetState(Models.TransportState.Faulted, "启动失败");
            _listener?.Stop();
            _listener = null;
            _cts?.Dispose();
            _cts = null;
            throw new InvalidOperationException($"启动 TCP 服务失败：{ex.Message}", ex);
        }
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(token).ConfigureAwait(false);
                client.NoDelay = true;
                var remote = client.Client.RemoteEndPoint as IPEndPoint;
                var endpoint = remote is null ? "unknown" : NetworkUtil.EndpointText(remote);
                var id = endpoint;
                var info = new Models.RemoteClient
                {
                    Id = id,
                    Endpoint = endpoint,
                    ConnectedAt = DateTime.Now,
                    LastSeenAt = DateTime.Now
                };

                var connection = new Connection
                {
                    Id = id,
                    Client = client,
                    Stream = client.GetStream(),
                    Info = info,
                    SendLock = new SemaphoreSlim(1, 1),
                    Cts = CancellationTokenSource.CreateLinkedTokenSource(token)
                };

                if (!_connections.TryAdd(id, connection))
                {
                    id = $"{endpoint}#{Guid.NewGuid():N}";
                    connection = new Connection
                    {
                        Id = id,
                        Client = client,
                        Stream = connection.Stream,
                        Info = info,
                        SendLock = connection.SendLock,
                        Cts = connection.Cts
                    };
                    _connections[id] = connection;
                }

                AddClient(info);
                RaiseInfo($"客户端已连接：{endpoint}");
                _ = Task.Run(() => ReadLoopAsync(connection), CancellationToken.None);
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
                if (token.IsCancellationRequested)
                {
                    break;
                }

                RaiseError($"接受客户端连接失败：{ex.Message}", ex);
                await Task.Delay(200, token).ConfigureAwait(false);
            }
        }
    }

    private async Task ReadLoopAsync(Connection connection)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!connection.Cts.IsCancellationRequested)
            {
                var count = await connection.Stream.ReadAsync(buffer, connection.Cts.Token).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                TouchClient(connection.Id, receivedBytes: count);
                RaiseMessage(new MessageReceivedEventArgs
                {
                    Data = buffer.AsSpan(0, count).ToArray(),
                    RemoteId = connection.Id,
                    RemoteEndpoint = connection.Info.Endpoint
                });
            }
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
        catch (Exception ex)
        {
            if (!connection.Cts.IsCancellationRequested)
            {
                RaiseError($"客户端 {connection.Info.Endpoint} 读取失败：{ex.Message}", ex);
            }
        }
        finally
        {
            _connections.TryRemove(connection.Id, out _);
            connection.Cts.Cancel();
            try
            {
                connection.Stream.Dispose();
                connection.Client.Dispose();
            }
            catch
            {
                // ignored
            }

            connection.SendLock.Dispose();
            connection.Cts.Dispose();
            RemoveClient(connection.Id);
            RaiseInfo($"客户端已断开：{connection.Info.Endpoint}");
        }
    }

    public override async Task SendAsync(ReadOnlyMemory<byte> data, string? remoteId = null, bool broadcast = false,
        bool binary = false)
    {
        List<Connection> targets;
        if (broadcast)
        {
            targets = _connections.Values.ToList();
        }
        else if (!string.IsNullOrEmpty(remoteId))
        {
            if (!_connections.TryGetValue(remoteId, out var target))
            {
                throw new InvalidOperationException("所选客户端已断开");
            }

            targets = [target];
        }
        else
        {
            targets = _connections.Values.Take(2).ToList();
            if (targets.Count != 1)
            {
                throw new InvalidOperationException(targets.Count == 0 ? "当前没有已连接的客户端" : "请先选择一个目标客户端");
            }
        }

        if (targets.Count == 0)
        {
            throw new InvalidOperationException("当前没有已连接的客户端");
        }

        var errors = new List<string>();
        foreach (var target in targets)
        {
            try
            {
                await target.SendLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    await target.Stream.WriteAsync(data).ConfigureAwait(false);
                    await target.Stream.FlushAsync().ConfigureAwait(false);
                    TouchClient(target.Id, sentBytes: data.Length);
                }
                finally
                {
                    target.SendLock.Release();
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{target.Info.Endpoint}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException("部分发送失败：" + string.Join("; ", errors));
        }
    }

    public override async Task StopAsync()
    {
        var cts = _cts;
        if (cts is null)
        {
            SetState(Models.TransportState.Stopped, "已停止监听");
            return;
        }

        SetState(Models.TransportState.Stopping, "正在停止监听...");
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
            _listener?.Stop();
        }
        catch
        {
            // ignored
        }

        foreach (var connection in _connections.Values)
        {
            try
            {
                connection.Cts.Cancel();
                connection.Client.Close();
            }
            catch
            {
                // ignored
            }
        }

        if (_acceptTask is not null)
        {
            try
            {
                await _acceptTask.ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        for (var i = 0; i < 20 && !_connections.IsEmpty; i++)
        {
            await Task.Delay(25).ConfigureAwait(false);
        }

        _connections.Clear();
        ClearClients();
        cts.Dispose();
        _cts = null;
        _listener = null;
        _acceptTask = null;
        SetState(Models.TransportState.Stopped, "已停止监听");
        RaiseInfo("TCP 服务已停止");
    }
}
