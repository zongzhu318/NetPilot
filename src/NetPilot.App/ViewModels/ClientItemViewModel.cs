using CommunityToolkit.Mvvm.ComponentModel;
using NetPilot.Core.Models;

namespace NetPilot.App.ViewModels;

public sealed partial class ClientItemViewModel : ObservableObject
{
    public ClientItemViewModel(RemoteClient client)
    {
        Id = client.Id;
        Endpoint = client.Endpoint;
        ConnectedAt = client.ConnectedAt;
        LastSeenAt = client.LastSeenAt;
        BytesReceived = client.BytesReceived;
        BytesSent = client.BytesSent;
    }

    public string Id { get; }
    public string Endpoint { get; }

    [ObservableProperty]
    private DateTime _connectedAt;

    [ObservableProperty]
    private DateTime _lastSeenAt;

    [ObservableProperty]
    private long _bytesReceived;

    [ObservableProperty]
    private long _bytesSent;

    public string ConnectedLabel => $"连接于 {ConnectedAt:HH:mm:ss}";
    public string TrafficLabel => $"收 {BytesReceived:N0} B / 发 {BytesSent:N0} B";
}
