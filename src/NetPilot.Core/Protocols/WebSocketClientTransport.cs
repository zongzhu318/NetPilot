using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;

namespace NetPilot.Core.Protocols;

public sealed class WebSocketClientTransport : TransportBase
{
    private readonly string _url;
    private readonly string _subProtocol;
    private readonly IReadOnlyList<(string Key, string Value)> _headers;
    private readonly bool _ignoreCertificate;
    private readonly bool _autoReconnect;
    private readonly int _reconnectIntervalMs;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _supervisor;
    private TaskCompletionSource<bool>? _firstAttempt;
    private ClientWebSocket? _socket;
    private bool _firstAttemptCompleted;

    public WebSocketClientTransport(string url, string subProtocol, IEnumerable<(string, string)> headers,
        bool ignoreCertificate, bool autoReconnect, int reconnectIntervalMs)
    {
        _url = url;
        _subProtocol = subProtocol;
        _headers = headers.Where(x => !string.IsNullOrWhiteSpace(x.Item1)).ToList();
        _ignoreCertificate = ignoreCertificate;
        _autoReconnect = autoReconnect;
        _reconnectIntervalMs = Math.Max(500, reconnectIntervalMs);
    }

    public override string Endpoint => _url;

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(_url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "ws" && uri.Scheme != "wss"))
        {
            throw new InvalidOperationException("请输入有效的 ws:// 或 wss:// 地址");
        }

        if (_supervisor is { IsCompleted: false })
        {
            return;
        }

        _firstAttemptCompleted = false;
        _firstAttempt = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        SetState(Models.TransportState.Starting, "正在连接...");
        _supervisor = Task.Run(() => RunAsync(_cts.Token), CancellationToken.None);
        await _firstAttempt.Task.ConfigureAwait(false);
    }

    private async Task RunAsync(CancellationToken token)
    {
        var firstRound = true;
        while (!token.IsCancellationRequested)
        {
            try
            {
                if (!firstRound && _autoReconnect)
                {
                    SetState(Models.TransportState.Starting, "正在重连...");
                }

                await ConnectOnceAsync(token).ConfigureAwait(false);
                SetState(Models.TransportState.Running, "WebSocket 已连接");
                RaiseInfo($"WebSocket 已连接：{_url}");
                CompleteFirstAttempt();

                await ReceiveLoopAsync(token).ConfigureAwait(false);
                RaiseInfo("WebSocket 连接已关闭");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetState(Models.TransportState.Faulted, "连接失败");
                var prefix = _firstAttemptCompleted ? "WebSocket 连接断开" : "WebSocket 连接失败";
                RaiseError($"{prefix}：{ex.Message}", ex);
                CompleteFirstAttempt();
            }
            finally
            {
                CleanupSocket();
            }

            if (token.IsCancellationRequested || !_autoReconnect)
            {
                break;
            }

            RaiseInfo($"将在 {_reconnectIntervalMs} ms 后自动重连...");
            try
            {
                await Task.Delay(_reconnectIntervalMs, token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            firstRound = false;
        }

        CleanupSocket();
        CompleteFirstAttempt();
        if (!token.IsCancellationRequested)
        {
            SetState(Models.TransportState.Stopped, "已断开");
        }
    }

    private async Task ConnectOnceAsync(CancellationToken token)
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        if (!string.IsNullOrWhiteSpace(_subProtocol))
        {
            socket.Options.AddSubProtocol(_subProtocol.Trim());
        }

        foreach (var (key, value) in _headers)
        {
            try
            {
                socket.Options.SetRequestHeader(key, value);
            }
            catch (Exception ex)
            {
                RaiseError($"无法设置 WebSocket 请求头 {key}：{ex.Message}");
            }
        }

        if (_ignoreCertificate)
        {
            socket.Options.RemoteCertificateValidationCallback = AcceptAllCertificates;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            await socket.ConnectAsync(new Uri(_url), timeout.Token).ConfigureAwait(false);
            _socket = socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static bool AcceptAllCertificates(object sender, X509Certificate? certificate, X509Chain? chain,
        SslPolicyErrors errors) => true;

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var socket = _socket ?? throw new InvalidOperationException("WebSocket 未连接");
        var buffer = new byte[16 * 1024];
        using var message = new MemoryStream();

        while (!token.IsCancellationRequested)
        {
            var result = await socket.ReceiveAsync(buffer, token).ConfigureAwait(false);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                try
                {
                    await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", token)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }

                return;
            }

            message.Write(buffer, 0, result.Count);
            if (message.Length > 32 * 1024 * 1024)
            {
                message.SetLength(0);
                RaiseError("收到的 WebSocket 消息超过 32MB，已丢弃");
                continue;
            }

            if (!result.EndOfMessage)
            {
                continue;
            }

            var payload = message.ToArray();
            message.SetLength(0);
            RaiseMessage(new MessageReceivedEventArgs
            {
                Data = payload,
                RemoteEndpoint = _url,
                IsBinary = result.MessageType == WebSocketMessageType.Binary
            });
        }
    }

    private void CompleteFirstAttempt()
    {
        if (_firstAttemptCompleted)
        {
            return;
        }

        _firstAttemptCompleted = true;
        _firstAttempt?.TrySetResult(true);
    }

    private void CleanupSocket()
    {
        try
        {
            _socket?.Dispose();
        }
        catch
        {
            // ignored
        }

        _socket = null;
    }

    public override async Task SendAsync(ReadOnlyMemory<byte> data, string? remoteId = null, bool broadcast = false,
        bool binary = false)
    {
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket 尚未连接");
        }

        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await socket.SendAsync(data,
                binary ? WebSocketMessageType.Binary : WebSocketMessageType.Text,
                true, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public override async Task StopAsync()
    {
        var cts = _cts;
        if (cts is null)
        {
            SetState(Models.TransportState.Stopped, "已断开");
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

        var socket = _socket;
        if (socket is { State: WebSocketState.Open })
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "client closing", timeout.Token)
                    .ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        try
        {
            socket?.Dispose();
        }
        catch
        {
            // ignored
        }

        if (_supervisor is not null)
        {
            try
            {
                await _supervisor.ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        CleanupSocket();
        cts.Dispose();
        _cts = null;
        _supervisor = null;

        try
        {
            await _sendLock.WaitAsync().ConfigureAwait(false);
            _sendLock.Release();
        }
        catch (ObjectDisposedException)
        {
            // ignored
        }

        SetState(Models.TransportState.Stopped, "已断开");
    }

    public override async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _sendLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
