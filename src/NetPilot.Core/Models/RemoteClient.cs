namespace NetPilot.Core.Models;

public sealed class RemoteClient
{
    public required string Id { get; init; }
    public required string Endpoint { get; init; }
    public DateTime ConnectedAt { get; init; } = DateTime.Now;
    public DateTime LastSeenAt { get; set; } = DateTime.Now;
    public long BytesReceived { get; set; }
    public long BytesSent { get; set; }
    public string? Extra { get; init; }
}
