using NetPilot.Core.Models;

namespace NetPilot.Core.Protocols;

public interface ITransport : IAsyncDisposable
{
    TransportState State { get; }
    string Endpoint { get; }
    bool IsRunning { get; }
    IReadOnlyList<RemoteClient> Clients { get; }

    event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    event EventHandler<RemoteClient>? ClientConnected;
    event EventHandler<RemoteClient>? ClientDisconnected;
    event EventHandler<TransportStateChangedEventArgs>? StateChanged;
    event EventHandler<TransportErrorEventArgs>? ErrorOccurred;
    event EventHandler<string>? InfoOccurred;

    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync();
    Task SendAsync(ReadOnlyMemory<byte> data, string? remoteId = null, bool broadcast = false, bool binary = false);
}

public abstract class TransportBase : ITransport
{
    private readonly List<RemoteClient> _clients = [];
    private TransportState _state = TransportState.Stopped;

    public TransportState State => _state;
    public bool IsRunning => _state == TransportState.Running;
    public abstract string Endpoint { get; }
    public IReadOnlyList<RemoteClient> Clients
    {
        get
        {
            lock (_clients)
            {
                return _clients.ToList();
            }
        }
    }

    public event EventHandler<MessageReceivedEventArgs>? MessageReceived;
    public event EventHandler<RemoteClient>? ClientConnected;
    public event EventHandler<RemoteClient>? ClientDisconnected;
    public event EventHandler<TransportStateChangedEventArgs>? StateChanged;
    public event EventHandler<TransportErrorEventArgs>? ErrorOccurred;
    public event EventHandler<string>? InfoOccurred;

    public abstract Task StartAsync(CancellationToken cancellationToken = default);
    public abstract Task StopAsync();
    public abstract Task SendAsync(ReadOnlyMemory<byte> data, string? remoteId = null, bool broadcast = false, bool binary = false);

    protected void SetState(TransportState state, string message)
    {
        _state = state;
        StateChanged?.Invoke(this, new TransportStateChangedEventArgs(state, message));
    }

    protected void RaiseMessage(MessageReceivedEventArgs args) => MessageReceived?.Invoke(this, args);

    protected void AddClient(RemoteClient client)
    {
        lock (_clients)
        {
            var existing = _clients.FirstOrDefault(x => x.Id == client.Id);
            if (existing is not null)
            {
                _clients.Remove(existing);
            }

            _clients.Add(client);
        }

        ClientConnected?.Invoke(this, client);
    }

    protected void TouchClient(string id, long receivedBytes = 0, long sentBytes = 0)
    {
        lock (_clients)
        {
            var client = _clients.FirstOrDefault(x => x.Id == id);
            if (client is null)
            {
                return;
            }

            client.LastSeenAt = DateTime.Now;
            client.BytesReceived += receivedBytes;
            client.BytesSent += sentBytes;
        }
    }

    protected void RemoveClient(string id)
    {
        RemoteClient? client;
        lock (_clients)
        {
            client = _clients.FirstOrDefault(x => x.Id == id);
            if (client is not null)
            {
                _clients.Remove(client);
            }
        }

        if (client is not null)
        {
            ClientDisconnected?.Invoke(this, client);
        }
    }

    protected void ClearClients()
    {
        lock (_clients)
        {
            _clients.Clear();
        }
    }

    protected void RaiseError(string message, Exception? exception = null) =>
        ErrorOccurred?.Invoke(this, new TransportErrorEventArgs(message, exception));

    protected void RaiseInfo(string message) => InfoOccurred?.Invoke(this, message);

    public virtual async ValueTask DisposeAsync()
    {
        await StopAsync();
        GC.SuppressFinalize(this);
    }
}
