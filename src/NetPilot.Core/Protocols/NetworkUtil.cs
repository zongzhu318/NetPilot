using System.Net;
using System.Net.Sockets;

namespace NetPilot.Core.Protocols;

public static class NetworkUtil
{
    public static IPAddress ResolveBindAddress(string? host)
    {
        if (string.IsNullOrWhiteSpace(host) || host == "*" || host == "0.0.0.0")
        {
            return IPAddress.Any;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        if (IPAddress.TryParse(host, out var address))
        {
            return address;
        }

        var addresses = Dns.GetHostAddresses(host);
        return addresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)
               ?? addresses.FirstOrDefault()
               ?? throw new InvalidOperationException($"无法解析监听地址：{host}");
    }

    public static IPAddress ResolveTargetAddress(string host)
    {
        if (IPAddress.TryParse(host, out var address))
        {
            return address;
        }

        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return IPAddress.Loopback;
        }

        var addresses = Dns.GetHostAddresses(host);
        return addresses.FirstOrDefault(x => x.AddressFamily == AddressFamily.InterNetwork)
               ?? addresses.FirstOrDefault()
               ?? throw new InvalidOperationException($"无法解析主机地址：{host}");
    }

    public static string ToUrlHost(IPAddress address) =>
        address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString();

    public static string EndpointText(IPEndPoint endpoint) => $"{endpoint.Address}:{endpoint.Port}";

    public static bool IsTcpPortAvailable(IPAddress address, int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(address, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            try
            {
                listener?.Stop();
            }
            catch
            {
                // ignored
            }
        }
    }

    public static bool IsUdpPortAvailable(IPAddress address, int port)
    {
        Socket? socket = null;
        try
        {
            socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Bind(new IPEndPoint(address, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        finally
        {
            socket?.Dispose();
        }
    }

    public static bool TryParseEndpoint(string? value, int defaultPort, out IPEndPoint endpoint)
    {
        endpoint = new IPEndPoint(IPAddress.Loopback, defaultPort);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (text.StartsWith('['))
        {
            var closing = text.IndexOf(']');
            if (closing < 0)
            {
                return false;
            }

            var hostText = text[1..closing];
            var portText = text[(closing + 1)..].TrimStart(':');
            if (!IPAddress.TryParse(hostText, out var v6))
            {
                return false;
            }

            var port = defaultPort;
            if (portText.Length > 0 && !int.TryParse(portText, out port))
            {
                return false;
            }

            endpoint = new IPEndPoint(v6, port);
            return true;
        }

        var colon = text.LastIndexOf(':');
        var host = colon > 0 ? text[..colon] : text;
        var parsedPort = defaultPort;
        if (colon > 0 && int.TryParse(text[(colon + 1)..], out var p))
        {
            parsedPort = p;
        }

        if (!IPAddress.TryParse(host, out var address))
        {
            return false;
        }

        endpoint = new IPEndPoint(address, parsedPort);
        return true;
    }
}
