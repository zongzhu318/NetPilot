namespace NetPilot.Core.Models;

public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogDirection Direction { get; init; }
    public string? Remote { get; init; }
    public string? RemoteId { get; init; }
    public string SessionName { get; init; } = string.Empty;
    public byte[] Data { get; init; } = [];
    public string? Text { get; init; }
    public bool IsTruncated { get; init; }
    public bool IsBinaryHint { get; init; }

    public int ByteCount => Data.Length;

    public static LogEntry Create(LogDirection direction, byte[]? data, string sessionName,
        string? remote = null, string? text = null, int maxPayloadBytes = 256 * 1024, string? remoteId = null,
        bool binaryHint = false)
    {
        var source = data ?? [];
        var truncated = source.Length > maxPayloadBytes;
        var payload = truncated ? source.AsSpan(0, maxPayloadBytes).ToArray() : source;
        return new LogEntry
        {
            Timestamp = DateTime.Now,
            Direction = direction,
            Remote = remote,
            RemoteId = remoteId,
            SessionName = sessionName,
            Data = payload,
            Text = text,
            IsTruncated = truncated,
            IsBinaryHint = binaryHint
        };
    }

    public static LogEntry Info(string sessionName, string text, string? remote = null) => new()
    {
        Timestamp = DateTime.Now,
        Direction = LogDirection.Info,
        SessionName = sessionName,
        Text = text,
        Remote = remote
    };

    public static LogEntry Error(string sessionName, string text, string? remote = null) => new()
    {
        Timestamp = DateTime.Now,
        Direction = LogDirection.Error,
        SessionName = sessionName,
        Text = text,
        Remote = remote
    };
}
