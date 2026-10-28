using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NetPilot.Core.Protocols;

public sealed class WebSocketServerTransport : TransportBase
{
    private sealed class Connection
    {
        public required string Id { get; init; }
        public required WebSocket Socket { get; init; }
        public required Models.RemoteClient Info { get; init; }
        public required SemaphoreSlim SendLock { get; init; }
    }

    private readonly string _host;
    private readonly int _port;
    private readonly ConcurrentDictionary<string, Connection> _connections = new();
    private WebApplication? _app;
    private CancellationTokenSource? _cts;

    public WebSocketServerTransport(string host, int port)
    {
        _host = host;
        _port = port;
    }

    public override string Endpoint => $"{_host}:{_port}";

    public override async Task StartAsync(CancellationToken cancellationToken = default)
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

        WebApplication? app = null;
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ApplicationName = "NetPilot.WebSocketServer",
                EnvironmentName = Environments.Production
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Listen(address, _port);
            });

            app = builder.Build();
            app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(30) });
            app.Run(HandleHttpAsync);

            _cts = new CancellationTokenSource();
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            _app = app;
            SetState(Models.TransportState.Running, $"WS 服务已启动 {_host}:{_port}");
            RaiseInfo($"WebSocket 服务已启动：ws://{NetworkUtil.ToUrlHost(address)}:{_port}");
        }
        catch (Exception ex)
        {
            if (app is not null)
            {
                try
                {
                    await app.DisposeAsync().ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }
            }

            _cts?.Dispose();
            _cts = null;
            SetState(Models.TransportState.Faulted, "启动失败");
            throw new InvalidOperationException($"启动 WebSocket 服务失败：{ex.Message}", ex);
        }
    }

    private async Task HandleHttpAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync("NetPilot WebSocket Server：请使用 WebSocket 客户端连接").ConfigureAwait(false);
            return;
        }

        var remoteIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var remotePort = context.Connection.RemotePort;
        var endpoint = $"{remoteIp}:{remotePort}";
        var id = $"{endpoint}#{Guid.NewGuid():N}"[..Math.Min(48, endpoint.Length + 9)];

        WebSocket socket;
        try
        {
            socket = await context.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            RaiseError($"WebSocket 握手失败：{ex.Message}", ex);
            return;
        }

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
            Socket = socket,
            Info = info,
            SendLock = new SemaphoreSlim(1, 1)
        };

        _connections[id] = connection;
        AddClient(info);
        RaiseInfo($"WebSocket 客户端已连接：{endpoint}");

        try
        {
            await ReadLoopAsync(connection, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _connections.TryRemove(id, out _);
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "server closing", timeout.Token)
                        .ConfigureAwait(false);
                }
            }
            catch
            {
                // ignored
            }

            socket.Dispose();
            connection.SendLock.Dispose();
            RemoveClient(id);
            RaiseInfo($"WebSocket 客户端已断开：{endpoint}");
        }
    }

    private async Task ReadLoopAsync(Connection connection, CancellationToken token)
    {
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();
        while (!token.IsCancellationRequested && connection.Socket.State == WebSocketState.Open)
        {
            WebSocketReceiveResult result;
            try
            {
                result = await connection.Socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (WebSocketException)
            {
                break;
            }

            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            message.Write(buffer, 0, result.Count);
            TouchClient(connection.Id, receivedBytes: result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            var payload = message.ToArray();
            message.SetLength(0);
            RaiseMessage(new MessageReceivedEventArgs
            {
                Data = payload,
                RemoteId = connection.Id,
                RemoteEndpoint = connection.Info.Endpoint,
                IsBinary = result.MessageType == WebSocketMessageType.Binary
            });
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
                throw new InvalidOperationException("所选 WebSocket 客户端已断开");
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

        var messageType = binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text;
        var errors = new List<string>();
        foreach (var target in targets)
        {
            try
            {
                await target.SendLock.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (target.Socket.State != WebSocketState.Open)
                    {
                        errors.Add($"{target.Info.Endpoint}: 已关闭");
                        continue;
                    }

                    await target.Socket.SendAsync(data, messageType, true, CancellationToken.None)
                        .ConfigureAwait(false);
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
        if (_app is null)
        {
            SetState(Models.TransportState.Stopped, "已停止");
            return;
        }

        SetState(Models.TransportState.Stopping, "正在停止 WS 服务...");
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignored
        }

        foreach (var connection in _connections.Values)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await connection.Socket.CloseOutputAsync(WebSocketCloseStatus.EndpointUnavailable, "server stopping",
                    timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }

            try
            {
                connection.Socket.Dispose();
            }
            catch
            {
                // ignored
            }
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _app.StopAsync(timeout.Token).ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }

        try
        {
            await _app.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }

        _app = null;
        _cts?.Dispose();
        _cts = null;
        _connections.Clear();
        ClearClients();
        SetState(Models.TransportState.Stopped, "已停止");
        RaiseInfo("WebSocket 服务已停止");
    }
}
