using System.Collections.ObjectModel;
using Avalonia.Threading;
using NetPilot.Core.Models;
using NetPilot.Core.Storage;

namespace NetPilot.App.Services;

public sealed class GlobalLogEntry
{
    public DateTime Timestamp { get; init; }
    public string SessionName { get; init; } = string.Empty;
    public LogDirection Direction { get; init; }
    public string DirectionLabel => Direction switch
    {
        LogDirection.Send => "发送",
        LogDirection.Receive => "接收",
        LogDirection.Error => "错误",
        _ => "信息"
    };

    public string TimeLabel => Timestamp.ToString("HH:mm:ss.fff");
    public string Remote { get; init; } = string.Empty;
    public int Size { get; init; }
    public string Preview { get; init; } = string.Empty;
    public bool IsSend => Direction == LogDirection.Send;
    public bool IsReceive => Direction == LogDirection.Receive;
    public bool IsError => Direction == LogDirection.Error;
    public bool IsInfo => Direction == LogDirection.Info;

    public bool Matches(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return SessionName.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || Remote.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || Preview.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || DirectionLabel.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class GlobalLogService
{
    private readonly ObservableCollection<GlobalLogEntry> _entries = [];
    private int _limit = 5000;

    public GlobalLogService(AppSettings settings)
    {
        _limit = Math.Max(100, settings.GlobalLogLimit);
        Entries = new ReadOnlyObservableCollection<GlobalLogEntry>(_entries);
    }

    public ReadOnlyObservableCollection<GlobalLogEntry> Entries { get; }

    public event EventHandler? Changed;

    public void Add(GlobalLogEntry entry)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Add(entry));
            return;
        }

        _entries.Add(entry);
        Trim();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Clear()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Clear);
            return;
        }

        _entries.Clear();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void SetLimit(int limit)
    {
        _limit = Math.Max(100, limit);
        Trim();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Trim()
    {
        while (_entries.Count > _limit)
        {
            _entries.RemoveAt(0);
        }
    }
}
