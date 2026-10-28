using CommunityToolkit.Mvvm.ComponentModel;
using NetPilot.Core.Codec;
using NetPilot.Core.Models;

namespace NetPilot.App.ViewModels;

public sealed class LogEntryViewModel : ObservableObject
{
    private MessageFormat _format;
    private TextEncodingKind _encoding;

    public LogEntryViewModel(LogEntry entry, MessageFormat format, TextEncodingKind encoding)
    {
        Entry = entry;
        _format = format;
        _encoding = encoding;
        Build();
    }

    public LogEntry Entry { get; }
    public DateTime Timestamp => Entry.Timestamp;
    public string SessionName => Entry.SessionName;
    public string? RemoteId => Entry.RemoteId;
    public byte[] Data => Entry.Data;
    public bool HasData => Entry.Data.Length > 0;
    public bool IsTruncated => Entry.IsTruncated;
    public string TimeLabel => Entry.Timestamp.ToString("HH:mm:ss.fff");
    public string DirectionLabel => Entry.Direction switch
    {
        LogDirection.Send => "发送",
        LogDirection.Receive => "接收",
        LogDirection.Error => "错误",
        _ => "信息"
    };

    public string DirectionGlyph => Entry.Direction switch
    {
        LogDirection.Send => "↑",
        LogDirection.Receive => "↓",
        LogDirection.Error => "!",
        _ => "i"
    };

    public bool IsSend => Entry.Direction == LogDirection.Send;
    public bool IsReceive => Entry.Direction == LogDirection.Receive;
    public bool IsError => Entry.Direction == LogDirection.Error;
    public bool IsInfo => Entry.Direction == LogDirection.Info;
    public string RemoteLabel => Entry.Remote ?? string.Empty;
    public string SizeLabel => Entry.Data.Length switch
    {
        >= 1024 * 1024 => $"{Entry.Data.Length / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{Entry.Data.Length / 1024d:0.0} KB",
        _ => $"{Entry.Data.Length} B"
    };

    public string Preview { get; private set; } = string.Empty;
    public string PreviewLine => Preview.ReplaceLineEndings(" ⏎ ");
    public string DetailText { get; private set; } = string.Empty;
    public string DetailSyntax { get; private set; } = "None";
    public string FullText { get; private set; } = string.Empty;
    public bool IsJsonDetail { get; private set; }

    public void Refresh(MessageFormat format, TextEncodingKind encoding)
    {
        if (_format == format && _encoding == encoding)
        {
            return;
        }

        _format = format;
        _encoding = encoding;
        Build();
        OnPropertyChanged(nameof(Preview));
        OnPropertyChanged(nameof(PreviewLine));
        OnPropertyChanged(nameof(DetailText));
        OnPropertyChanged(nameof(DetailSyntax));
        OnPropertyChanged(nameof(FullText));
        OnPropertyChanged(nameof(IsJsonDetail));
    }

    private void Build()
    {
        if (Entry.Data.Length == 0 && !string.IsNullOrEmpty(Entry.Text))
        {
            var text = Entry.Text;
            FullText = text;
            DetailText = text;
            Preview = text.Length > 900 ? text[..900] + "…" : text;
            DetailSyntax = "None";
            return;
        }

        FullText = CodecEngine.FormatForDisplay(Entry.Data, _format, _encoding);
        DetailText = FullText.Length > 262_144 ? FullText[..262_144] + "\n…（内容过长，已截断显示）" : FullText;
        IsJsonDetail = _format == MessageFormat.Json && JsonFormatter.TryFormat(FullText, out _, out _);
        DetailSyntax = IsJsonDetail ? "Json" : "None";

        var preview = FullText.Length > 900 ? FullText[..900] + "…" : FullText;
        if (Entry.IsTruncated)
        {
            preview += "\n…（报文过大，仅保留前 256 KB）";
        }

        Preview = preview;
    }

    public bool MatchesFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return true;
        }

        return Preview.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || FullText.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || RemoteLabel.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || DirectionLabel.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || TimeLabel.Contains(filter, StringComparison.OrdinalIgnoreCase)
               || (Entry.Text?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }
}
