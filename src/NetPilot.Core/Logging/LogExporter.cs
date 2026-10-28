using System.Text;
using NetPilot.Core.Codec;
using NetPilot.Core.Models;

namespace NetPilot.Core.Logging;

public static class LogExporter
{
    public static async Task ExportAsync(string path, ExportFormat format, IEnumerable<LogEntry> entries,
        MessageFormat displayFormat, TextEncodingKind encoding)
    {
        var list = entries.ToList();
        var content = format switch
        {
            ExportFormat.Csv => BuildCsv(list, displayFormat, encoding),
            ExportFormat.Hex => BuildHex(list, encoding),
            _ => BuildTxt(list, displayFormat, encoding)
        };

        await File.WriteAllTextAsync(path, content, new UTF8Encoding(true)).ConfigureAwait(false);
    }

    public static string BuildTxt(IEnumerable<LogEntry> entries, MessageFormat displayFormat, TextEncodingKind encoding)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# NetPilot 报文日志");
        builder.AppendLine($"# 导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        builder.AppendLine();
        foreach (var entry in entries)
        {
            builder.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{DirectionLabel(entry.Direction)}] " +
                               $"{entry.SessionName}{(string.IsNullOrEmpty(entry.Remote) ? "" : $" <- {entry.Remote}")} " +
                               $"({entry.ByteCount} B)");
            var text = entry.Data.Length == 0 && !string.IsNullOrEmpty(entry.Text)
                ? entry.Text
                : CodecEngine.FormatForDisplay(entry.Data, displayFormat, encoding);
            if (text.Length > 0)
            {
                builder.AppendLine(text);
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    public static string BuildCsv(IEnumerable<LogEntry> entries, MessageFormat displayFormat, TextEncodingKind encoding)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Time,Direction,Session,Remote,Size,Text,Hex");
        foreach (var entry in entries)
        {
            var text = entry.Data.Length == 0 && !string.IsNullOrEmpty(entry.Text)
                ? entry.Text
                : CodecEngine.FormatForDisplay(entry.Data, displayFormat, encoding);
            var hex = entry.Data.Length > 0 ? CodecEngine.BytesToHex(entry.Data) : string.Empty;
            builder.AppendLine(string.Join(",",
                Csv(entry.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff")),
                Csv(DirectionLabel(entry.Direction)),
                Csv(entry.SessionName),
                Csv(entry.Remote ?? string.Empty),
                entry.ByteCount.ToString(),
                Csv(text),
                Csv(hex)));
        }

        return builder.ToString();
    }

    public static string BuildHex(IEnumerable<LogEntry> entries, TextEncodingKind encoding)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# NetPilot Hex 报文日志");
        builder.AppendLine($"# 导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
        builder.AppendLine();
        foreach (var entry in entries)
        {
            builder.AppendLine($"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff}] [{DirectionLabel(entry.Direction)}] " +
                               $"{entry.SessionName}{(string.IsNullOrEmpty(entry.Remote) ? "" : $" <- {entry.Remote}")} " +
                               $"({entry.ByteCount} B)");
            if (entry.Data.Length > 0)
            {
                builder.AppendLine(HexDump.Format(entry.Data));
            }
            else if (!string.IsNullOrWhiteSpace(entry.Text))
            {
                builder.AppendLine(HexDump.Format(CodecEngine.Encode(entry.Text, encoding)));
            }

            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string DirectionLabel(LogDirection direction) => direction switch
    {
        LogDirection.Send => "发送",
        LogDirection.Receive => "接收",
        LogDirection.Error => "错误",
        _ => "信息"
    };

    private static string Csv(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
