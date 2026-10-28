using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetPilot.Core.Http;

namespace NetPilot.Core.Protocols;

public sealed class HttpServerTransport : TransportBase
{
    private const int MaxRequestBodyBytes = 2 * 1024 * 1024;

    private readonly string _host;
    private readonly int _port;
    private readonly HttpMockOptions _options;
    private WebApplication? _app;
    private CancellationTokenSource? _cts;

    public HttpServerTransport(string host, int port, HttpMockOptions options)
    {
        _host = host;
        _port = port;
        _options = options;
    }

    public override string Endpoint => $"http://{_host}:{_port}";

    public event EventHandler<HttpServerRequestInfo>? RequestReceived;

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
                ApplicationName = "NetPilot.HttpMockServer",
                EnvironmentName = Environments.Production
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.AddServerHeader = false;
                options.Listen(address, _port);
                options.Limits.MaxRequestBodySize = MaxRequestBodyBytes * 2L;
            });

            app = builder.Build();
            app.Run(HandleRequestAsync);

            _cts = new CancellationTokenSource();
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            _app = app;
            SetState(Models.TransportState.Running, $"Mock 服务已启动 {_host}:{_port}");
            RaiseInfo($"HTTP Mock 服务已启动：http://{NetworkUtil.ToUrlHost(address)}:{_port}");
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
            throw new InvalidOperationException($"启动 HTTP 服务失败：{ex.Message}", ex);
        }
    }

    private async Task HandleRequestAsync(HttpContext context)
    {
        var request = context.Request;
        byte[] body;
        try
        {
            body = await ReadLimitedAsync(request.Body, MaxRequestBodyBytes, context.RequestAborted)
                .ConfigureAwait(false);
        }
        catch
        {
            body = [];
        }

        var headers = new List<KeyValuePair<string, string>>();
        foreach (var header in request.Headers)
        {
            foreach (var value in header.Value)
            {
                headers.Add(new KeyValuePair<string, string>(header.Key, value ?? string.Empty));
            }
        }

        var requestInfo = new HttpServerRequestInfo
        {
            Timestamp = DateTime.Now,
            Method = request.Method,
            Path = request.Path.ToString(),
            QueryString = request.QueryString.ToString(),
            Remote = $"{context.Connection.RemoteIpAddress}:{context.Connection.RemotePort}",
            Headers = headers,
            Body = body,
            ContentType = request.ContentType ?? string.Empty
        };

        RequestReceived?.Invoke(this, requestInfo);

        var response = context.Response;
        response.StatusCode = _options.StatusCode;
        if (!string.IsNullOrWhiteSpace(_options.ContentType))
        {
            response.ContentType = _options.ContentType;
        }

        foreach (var header in _options.Headers.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)))
        {
            if (header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                response.ContentType = header.Value;
                continue;
            }

            response.Headers[header.Key] = header.Value;
        }

        if (_options.EnableCors)
        {
            response.Headers["Access-Control-Allow-Origin"] = "*";
            response.Headers["Access-Control-Allow-Methods"] = "*";
            response.Headers["Access-Control-Allow-Headers"] = "*";
            if (HttpMethods.IsOptions(request.Method))
            {
                response.StatusCode = StatusCodes.Status204NoContent;
                return;
            }
        }

        try
        {
            await response.Body.WriteAsync(_options.Body, context.RequestAborted).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int limit, CancellationToken token)
    {
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            if (memory.Length + read > limit)
            {
                var allowed = (int)Math.Max(0, limit - memory.Length);
                memory.Write(buffer, 0, allowed);
                break;
            }

            memory.Write(buffer, 0, read);
        }

        return memory.ToArray();
    }

    public override Task SendAsync(ReadOnlyMemory<byte> data, string? remoteId = null, bool broadcast = false,
        bool binary = false) =>
        throw new NotSupportedException("HTTP Mock 服务不支持主动发送报文");

    public override async Task StopAsync()
    {
        if (_app is null)
        {
            SetState(Models.TransportState.Stopped, "已停止");
            return;
        }

        SetState(Models.TransportState.Stopping, "正在停止 Mock 服务...");
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignored
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
        SetState(Models.TransportState.Stopped, "已停止");
        RaiseInfo("HTTP Mock 服务已停止");
    }
}
