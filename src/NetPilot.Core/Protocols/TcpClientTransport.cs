using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace NetPilot.Core.Protocols;

public sealed class TcpClientTransport : TransportBase
{
    private readonly string _host;
    private readonly int _port;
    private readonly bool _useTls;
    private readonly bool _ignoreCertificate;
    private readonly bool _autoReconnect;
    private readonly int _reconnectIntervalMs;

    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _supervisor;
    private TaskCompletionSource<bool>? _firstAttempt;
    private TcpClient? _client;
    private Stream? _stream;
    private bool _firstAttemptCompleted;

    public TcpClientTransport(string host, int port, bool useTls, bool ignoreCertificate,
        bool autoReconnect, int reconnectIntervalMs)
    {
        _host = host;
        _port = port;
        _useTls = useTls;
        _ignoreCertificate = ignoreCertificate;
        _autoReconnect = autoReconnect;
        _reconnectIntervalMs = Math.Max(500, reconnectIntervalMs);
    }

    public override string Endpoint => $"{_host}:{_port}";

    public override async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_supervisor is { IsCompleted: false })
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_host))
        {
            throw new InvalidOperationException("请输入远端地址");
        }

        if (_port is <= 0 or > 65535)
        {
            throw new InvalidOperationException("端口必须在 1 - 65535 之间");
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
                SetState(Models.TransportState.Running, $"已连接 {Endpoint}");
                RaiseInfo($"已连接到 {Endpoint}");
                CompleteFirstAttempt();

                await ReceiveLoopAsync(token).ConfigureAwait(false);
                RaiseInfo("连接已由对端关闭");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                SetState(Models.TransportState.Faulted, "连接失败");
                var prefix = _firstAttemptCompleted ? "连接断开" : "连接失败";
                RaiseError($"{prefix}：{ex.Message}", ex);
                CompleteFirstAttempt();
            }
            finally
            {
                CleanupConnection();
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

        CleanupConnection();
        CompleteFirstAttempt();
        if (!token.IsCancellationRequested)
        {
            SetState(Models.TransportState.Stopped, "已断开");
        }
    }

    private async Task ConnectOnceAsync(CancellationToken token)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            await client.ConnectAsync(_host, _port, timeout.Token).ConfigureAwait(false);

            Stream stream = client.GetStream();
            if (_useTls)
            {
                var ssl = new SslStream(stream, false, _ignoreCertificate ? AcceptAllCertificates : null);
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = _host,
                    EnabledSslProtocols = SslProtocols.None,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, timeout.Token).ConfigureAwait(false);
                stream = ssl;
            }

            _client = client;
            _stream = stream;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static bool AcceptAllCertificates(object sender, X509Certificate? certificate, X509Chain? chain,
        SslPolicyErrors errors) => true;

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var stream = _stream ?? throw new IOException("连接流不可用");
        var buffer = new byte[16 * 1024];
        while (!token.IsCancellationRequested)
        {
            var count = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (count == 0)
            {
                return;
            }

            RaiseMessage(new MessageReceivedEventArgs
            {
                Data = buffer.AsSpan(0, count).ToArray(),
                RemoteEndpoint = Endpoint
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

    private void CleanupConnection()
    {
        try
        {
            _stream?.Dispose();
        }
        catch
        {
            // ignored
        }

        try
        {
            _client?.Dispose();
        }
        catch
        {
            // ignored
        }

        _stream = null;
        _client = null;
    }

    public override async Task SendAsync(ReadOnlyMemory<byte> data, string? remoteId = null, bool broadcast = false,
        bool binary = false)
    {
        var stream = _stream;
        if (stream is null || State != Models.TransportState.Running)
        {
            throw new InvalidOperationException("尚未连接到服务端");
        }

        await _sendLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(data).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
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

        try
        {
            _client?.Close();
        }
        catch
        {
            // ignored
        }

        var supervisor = _supervisor;
        if (supervisor is not null)
        {
            try
            {
                await supervisor.ConfigureAwait(false);
            }
            catch
            {
                // ignored
            }
        }

        CleanupConnection();
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
