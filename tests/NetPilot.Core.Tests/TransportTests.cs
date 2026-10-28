using System.Net;
using System.Net.Sockets;
using System.Text;
using NetPilot.Core.Http;
using NetPilot.Core.Models;
using NetPilot.Core.Protocols;
using Xunit;

namespace NetPilot.Core.Tests;

public sealed class TransportTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task TcpServerAndClient_ExchangeData()
    {
        var port = FreeTcpPort();
        await using var server = new TcpServerTransport("127.0.0.1", port);

        var serverReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientConnected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MessageReceived += (_, e) => serverReceived.TrySetResult(e.Data);
        server.ClientConnected += (_, client) => clientConnected.TrySetResult(client.Id);

        await server.StartAsync();
        Assert.True(server.IsRunning);

        await using var client = new TcpClientTransport("127.0.0.1", port, false, false, false, 500);
        var clientReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.MessageReceived += (_, e) => clientReceived.TrySetResult(e.Data);

        await client.StartAsync();
        Assert.True(client.IsRunning);
        var remoteId = await clientConnected.Task.WaitAsync(Timeout);

        await client.SendAsync(Encoding.UTF8.GetBytes("ping"));
        var received = await serverReceived.Task.WaitAsync(Timeout);
        Assert.Equal("ping", Encoding.UTF8.GetString(received));

        await server.SendAsync(Encoding.UTF8.GetBytes("pong"), remoteId);
        Assert.Equal("pong", Encoding.UTF8.GetString(await clientReceived.Task.WaitAsync(Timeout)));

        await client.StopAsync();
        await server.StopAsync();
        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task TcpServer_ReportsPortConflict()
    {
        var port = FreeTcpPort();
        await using var first = new TcpServerTransport("127.0.0.1", port);
        await using var second = new TcpServerTransport("127.0.0.1", port);
        await first.StartAsync();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => second.StartAsync());
        Assert.Contains("已被占用", error.Message);
    }

    [Fact]
    public async Task UdpSendAndListen_ReplyToSource()
    {
        var port = FreeUdpPort();
        await using var listener = new UdpTransport(UdpRole.Listen, "127.0.0.1", port, "0.0.0.0", 0);
        await using var sender = new UdpTransport(UdpRole.Send, "127.0.0.1", port, "0.0.0.0", 0);

        var listenerReceived = new TaskCompletionSource<MessageReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var senderReceived = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        listener.MessageReceived += (_, e) => listenerReceived.TrySetResult(e);
        sender.MessageReceived += (_, e) => senderReceived.TrySetResult(e.Data);

        await listener.StartAsync();
        await sender.StartAsync();

        await sender.SendAsync(Encoding.UTF8.GetBytes("hello udp"));
        var incoming = await listenerReceived.Task.WaitAsync(Timeout);
        Assert.Equal("hello udp", Encoding.UTF8.GetString(incoming.Data));
        Assert.NotNull(incoming.RemoteId);
        Assert.NotEmpty(listener.Clients);

        await listener.SendAsync(Encoding.UTF8.GetBytes("hi back"), incoming.RemoteId);
        Assert.Equal("hi back", Encoding.UTF8.GetString(await senderReceived.Task.WaitAsync(Timeout)));
    }

    [Fact]
    public async Task WebSocketServerAndClient_ExchangeTextAndBinary()
    {
        var port = FreeTcpPort();
        await using var server = new WebSocketServerTransport("127.0.0.1", port);

        var serverReceived = new TaskCompletionSource<MessageReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var clientConnected = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        server.MessageReceived += (_, e) => serverReceived.TrySetResult(e);
        server.ClientConnected += (_, client) => clientConnected.TrySetResult(client.Id);

        await server.StartAsync();

        await using var client = new WebSocketClientTransport(
            $"ws://127.0.0.1:{port}", string.Empty, Array.Empty<(string, string)>(), false, false, 500);
        var clientReceived = new TaskCompletionSource<MessageReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.MessageReceived += (_, e) => clientReceived.TrySetResult(e);

        await client.StartAsync();
        var remoteId = await clientConnected.Task.WaitAsync(Timeout);

        await client.SendAsync(Encoding.UTF8.GetBytes("ws text"));
        var fromClient = await serverReceived.Task.WaitAsync(Timeout);
        Assert.Equal("ws text", Encoding.UTF8.GetString(fromClient.Data));
        Assert.False(fromClient.IsBinary);

        serverReceived = new TaskCompletionSource<MessageReceivedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        await client.SendAsync(new byte[] { 1, 2, 3 }, binary: true);
        var binary = await serverReceived.Task.WaitAsync(Timeout);
        Assert.True(binary.IsBinary);

        await server.SendAsync(Encoding.UTF8.GetBytes("ws reply"), remoteId);
        var reply = await clientReceived.Task.WaitAsync(Timeout);
        Assert.Equal("ws reply", Encoding.UTF8.GetString(reply.Data));
    }

    [Fact]
    public async Task HttpMockServer_ReturnsConfiguredResponseAndCapturesRequest()
    {
        var port = FreeTcpPort();
        var options = new HttpMockOptions
        {
            StatusCode = 201,
            ContentType = "application/json",
            Body = Encoding.UTF8.GetBytes("{\"ok\":true}"),
            Headers = [new HeaderItem("X-Mock", "yes")],
            EnableCors = true
        };
        await using var server = new HttpServerTransport("127.0.0.1", port, options);

        HttpServerRequestInfo? captured = null;
        server.RequestReceived += (_, info) => captured = info;
        await server.StartAsync();

        var spec = new HttpRequestSpec
        {
            Method = "POST",
            Url = $"http://127.0.0.1:{port}/api/test?x=1",
            BodyKind = HttpBodyKind.Raw,
            RawBody = "{\"name\":\"netpilot\"}",
            RawContentType = "application/json",
            Headers = [new HeaderItem("X-Client", "unit-test")]
        };

        var response = await HttpRequestExecutor.ExecuteAsync(spec);

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("{\"ok\":true}", Encoding.UTF8.GetString(response.Body));
        Assert.Contains(response.Headers, h => h.Key == "X-Mock" && h.Value == "yes");
        Assert.Contains(response.Headers, h => h.Key == "Access-Control-Allow-Origin" && h.Value == "*");

        Assert.NotNull(captured);
        Assert.Equal("POST", captured!.Method);
        Assert.Equal("/api/test", captured.Path);
        Assert.Equal("?x=1", captured.QueryString);
        Assert.Contains("\"name\":\"netpilot\"", Encoding.UTF8.GetString(captured.Body));
        Assert.Contains(captured.Headers, h => h.Key == "X-Client" && h.Value == "unit-test");
    }

    [Fact]
    public async Task HttpExecutor_RejectsInvalidUrl() =>
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HttpRequestExecutor.ExecuteAsync(new HttpRequestSpec { Url = "not-a-url" }));

    [Fact]
    public async Task HttpExecutor_EncodesHexBody()
    {
        var port = FreeTcpPort();
        var options = new HttpMockOptions
        {
            StatusCode = 200,
            ContentType = "application/octet-stream",
            Body = [],
            EnableCors = false
        };
        await using var server = new HttpServerTransport("127.0.0.1", port, options);
        byte[]? body = null;
        server.RequestReceived += (_, info) => body = info.Body;
        await server.StartAsync();

        var spec = new HttpRequestSpec
        {
            Method = "PUT",
            Url = $"http://127.0.0.1:{port}/hex",
            BodyKind = HttpBodyKind.Raw,
            RawBody = "0x01 0x02 0xFF",
            RawBodyFormat = SendFormat.Hex
        };

        var response = await HttpRequestExecutor.ExecuteAsync(spec);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal(new byte[] { 1, 2, 0xFF }, body!);
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static int FreeUdpPort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }
}
