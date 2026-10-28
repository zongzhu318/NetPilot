namespace NetPilot.Core.Protocols;

public sealed class MessageReceivedEventArgs : EventArgs
{
    public required byte[] Data { get; init; }
    public string? RemoteId { get; init; }
    public string? RemoteEndpoint { get; init; }
    public bool IsBinary { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

public sealed class TransportStateChangedEventArgs(Models.TransportState state, string message) : EventArgs
{
    public Models.TransportState State { get; } = state;
    public string Message { get; } = message;
}

public sealed class TransportErrorEventArgs(string message, Exception? exception = null) : EventArgs
{
    public string Message { get; } = message;
    public Exception? Exception { get; } = exception;
}
