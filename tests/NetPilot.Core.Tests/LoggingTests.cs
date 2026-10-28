using System.Text;
using NetPilot.Core.Logging;
using NetPilot.Core.Models;
using Xunit;

namespace NetPilot.Core.Tests;

public sealed class LoggingTests
{
    [Fact]
    public void RingBuffer_KeepsLatestItems()
    {
        var buffer = new RingBuffer<int>(3);
        foreach (var value in new[] { 1, 2, 3, 4, 5 })
        {
            buffer.Add(value);
        }

        Assert.Equal(3, buffer.Count);
        Assert.Equal(new[] { 3, 4, 5 }, buffer.Snapshot());
    }

    [Fact]
    public void RingBuffer_Clear_Resets()
    {
        var buffer = new RingBuffer<string>(2);
        buffer.Add("a");
        buffer.Clear();
        Assert.Empty(buffer.Snapshot());
        buffer.Add("b");
        Assert.Equal(new[] { "b" }, buffer.Snapshot());
    }

    [Fact]
    public void LogEntry_TruncatesLargePayload()
    {
        var data = new byte[10];
        var entry = LogEntry.Create(LogDirection.Receive, data, "s", maxPayloadBytes: 4);
        Assert.True(entry.IsTruncated);
        Assert.Equal(4, entry.Data.Length);
    }

    [Fact]
    public void BuildTxt_ContainsDirectionAndPayload()
    {
        var entry = LogEntry.Create(LogDirection.Send, Encoding.UTF8.GetBytes("hello"), "会话A", "127.0.0.1:80");
        var text = LogExporter.BuildTxt([entry], MessageFormat.Text, TextEncodingKind.Utf8);
        Assert.Contains("发送", text);
        Assert.Contains("hello", text);
        Assert.Contains("会话A", text);
    }

    [Fact]
    public void BuildCsv_EscapesQuotesAndKeepsHex()
    {
        var infoCsv = LogExporter.BuildCsv([LogEntry.Info("s", "a\"b")], MessageFormat.Text, TextEncodingKind.Utf8);
        var header = infoCsv.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        Assert.StartsWith("Time,Direction,Session,Remote,Size,Text,Hex", header);
        Assert.Contains("\"a\"\"b\"", infoCsv);

        var binary = LogEntry.Create(LogDirection.Receive, new byte[] { 0x41, 0x22 }, "s");
        var binaryCsv = LogExporter.BuildCsv([binary], MessageFormat.Text, TextEncodingKind.Utf8);
        Assert.Contains("\"41 22\"", binaryCsv);
    }

    [Fact]
    public void BuildHex_UsesDumpFormat()
    {
        var entry = LogEntry.Create(LogDirection.Send, new byte[] { 0x48, 0x69 }, "s");
        var text = LogExporter.BuildHex([entry], TextEncodingKind.Utf8);
        Assert.Contains("48 69", text);
        Assert.Contains("|Hi|", text);
    }

    [Fact]
    public async Task ExportAsync_WritesUtf8File()
    {
        var path = Path.Combine(Path.GetTempPath(), $"netpilot-export-{Guid.NewGuid():N}.txt");
        try
        {
            var entry = LogEntry.Info("会话", "信息日志");
            await LogExporter.ExportAsync(path, ExportFormat.Txt, [entry], MessageFormat.Text, TextEncodingKind.Utf8);
            Assert.True(File.Exists(path));
            Assert.Contains("信息日志", await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
