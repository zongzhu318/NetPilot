using System.Text;
using NetPilot.Core.Codec;
using NetPilot.Core.Models;
using Xunit;

namespace NetPilot.Core.Tests;

public sealed class CodecTests
{
    [Theory]
    [InlineData(TextEncodingKind.Utf8, "Hello NetPilot")]
    [InlineData(TextEncodingKind.Ascii, "Hello NetPilot")]
    [InlineData(TextEncodingKind.Latin1, "Hello NetPilot")]
    [InlineData(TextEncodingKind.Gbk, "你好，网络调试")]
    [InlineData(TextEncodingKind.Gb2312, "中文测试")]
    public void EncodeDecode_RoundTrips(TextEncodingKind kind, string text)
    {
        var bytes = CodecEngine.Encode(text, kind);
        Assert.Equal(text, CodecEngine.Decode(bytes, kind));
    }

    [Fact]
    public void GbkBytes_DecodeAsChinese()
    {
        var bytes = CodecEngine.Encode("测试", TextEncodingKind.Gbk);
        Assert.Equal(4, bytes.Length);
        Assert.NotEqual("测试", CodecEngine.Decode(bytes, TextEncodingKind.Utf8));
    }

    [Theory]
    [InlineData(new byte[] { 0x01, 0x02, 0x00, 0x03 }, true)]
    [InlineData(new byte[] { (byte)'a', (byte)'b', (byte)'\r', (byte)'\n' }, false)]
    [InlineData(new byte[0], false)]
    public void LooksBinary_DetectsControlBytes(byte[] data, bool expected) =>
        Assert.Equal(expected, CodecEngine.LooksBinary(data));

    [Fact]
    public void Hex_FormatAndParse_RoundTrips()
    {
        var data = new byte[] { 0x00, 0x0A, 0xFF, 0x10 };
        var text = CodecEngine.BytesToHex(data);
        Assert.Equal("00 0A FF 10", text);
        Assert.Equal(data, CodecEngine.ParseHex(text));
    }

    [Theory]
    [InlineData("0x00,0x0A-0xFF:10", new byte[] { 0x00, 0x0A, 0xFF, 0x10 })]
    [InlineData(" 01 02 \r\n 03 ", new byte[] { 1, 2, 3 })]
    public void ParseHex_AcceptsCommonSeparators(string text, byte[] expected) =>
        Assert.Equal(expected, CodecEngine.ParseHex(text));

    [Fact]
    public void ParseHex_Empty_ReturnsEmpty() => Assert.Empty(CodecEngine.ParseHex("   "));

    [Theory]
    [InlineData("0 12")]
    [InlineData("zz")]
    public void ParseHex_Invalid_Throws(string text) =>
        Assert.Throws<FormatException>(() => CodecEngine.ParseHex(text));

    [Fact]
    public void Base64_RoundTrips()
    {
        var data = Encoding.UTF8.GetBytes("NetPilot 数据");
        var base64 = CodecEngine.BytesToBase64(data);
        Assert.Equal(data, CodecEngine.ParseBase64(base64));
    }

    [Fact]
    public void ParseBase64_Invalid_Throws() =>
        Assert.Throws<FormatException>(() => CodecEngine.ParseBase64("%%%not-base64%%%"));

    [Fact]
    public void JsonFormatter_PrettyPrints()
    {
        Assert.True(JsonFormatter.TryFormat("{\"b\":2,\"a\":[1,true]}", out var formatted, out var error));
        Assert.Empty(error);
        Assert.Contains("\n", formatted);
        Assert.Contains("\"a\": [", formatted);
    }

    [Fact]
    public void JsonFormatter_Invalid_ReturnsError()
    {
        Assert.False(JsonFormatter.TryFormat("{oops", out _, out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void FormatForDisplay_Json_FormatsValidJson()
    {
        var data = Encoding.UTF8.GetBytes("{\"a\":1}");
        var text = CodecEngine.FormatForDisplay(data, MessageFormat.Json, TextEncodingKind.Utf8);
        Assert.Contains("\"a\": 1", text);
    }

    [Fact]
    public void FormatForDisplay_Text_FallsBackToHexForBinary()
    {
        var data = new byte[] { 0x00, 0x01, 0x02 };
        var text = CodecEngine.FormatForDisplay(data, MessageFormat.Text, TextEncodingKind.Utf8);
        Assert.Contains("00000000", text);
    }

    [Fact]
    public void HexDump_LaysOutSixteenBytesPerLine()
    {
        var data = Enumerable.Range(0x41, 16).Select(x => (byte)x).ToArray();
        var dump = HexDump.Format(data);
        Assert.StartsWith("00000000  41 42 43", dump);
        Assert.Contains("|ABCDEFGHIJKLMNOP|", dump);
    }
}
