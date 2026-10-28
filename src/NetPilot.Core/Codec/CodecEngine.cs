using System.Text;
using NetPilot.Core.Models;

namespace NetPilot.Core.Codec;

public static class CodecEngine
{
    static CodecEngine()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static Encoding GetEncoding(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8 => new UTF8Encoding(false),
        TextEncodingKind.Gbk => Encoding.GetEncoding(936),
        TextEncodingKind.Gb2312 => Encoding.GetEncoding(936),
        TextEncodingKind.Ascii => Encoding.ASCII,
        TextEncodingKind.Latin1 => Encoding.Latin1,
        _ => Encoding.UTF8
    };

    public static string EncodingDisplayName(TextEncodingKind kind) => kind switch
    {
        TextEncodingKind.Utf8 => "UTF-8",
        TextEncodingKind.Gbk => "GBK",
        TextEncodingKind.Gb2312 => "GB2312",
        TextEncodingKind.Ascii => "ASCII",
        TextEncodingKind.Latin1 => "ISO-8859-1",
        _ => kind.ToString()
    };

    public static byte[] Encode(string text, TextEncodingKind kind) =>
        GetEncoding(kind).GetBytes(text);

    public static string Decode(ReadOnlySpan<byte> data, TextEncodingKind kind) =>
        GetEncoding(kind).GetString(data);

    public static bool LooksBinary(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return false;
        }

        var control = 0;
        foreach (var b in data)
        {
            if (b == 0)
            {
                return true;
            }

            if (b < 0x20 && b is not (0x09 or 0x0A or 0x0D))
            {
                control++;
            }
        }

        return control > Math.Max(1, data.Length / 20);
    }

    public static string BytesToHex(ReadOnlySpan<byte> data, string separator = " ")
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(data.Length * 3);
        for (var i = 0; i < data.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(separator);
            }

            builder.Append(data[i].ToString("X2"));
        }

        return builder.ToString();
    }

    public static byte[] ParseHex(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var builder = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '0' && i + 1 < text.Length && (text[i + 1] == 'x' || text[i + 1] == 'X'))
            {
                i++;
                continue;
            }

            if (Uri.IsHexDigit(c))
            {
                builder.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c) || c is ',' or ':' or '-' or ';')
            {
                continue;
            }

            throw new FormatException($"Hex 中包含非法字符 '{c}'（位置 {i + 1}）");
        }

        if (builder.Length % 2 != 0)
        {
            throw new FormatException("Hex 字符数量必须为偶数");
        }

        try
        {
            return Convert.FromHexString(builder.ToString());
        }
        catch (FormatException)
        {
            throw new FormatException("Hex 格式不正确，请检查输入");
        }
    }

    public static string BytesToBase64(ReadOnlySpan<byte> data) => Convert.ToBase64String(data);

    public static byte[] ParseBase64(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var cleaned = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
        try
        {
            return Convert.FromBase64String(cleaned);
        }
        catch (FormatException)
        {
            throw new FormatException("Base64 格式不正确，请检查输入");
        }
    }

    public static string FormatForDisplay(byte[] data, MessageFormat format, TextEncodingKind encoding)
    {
        if (data.Length == 0)
        {
            return string.Empty;
        }

        return format switch
        {
            MessageFormat.Hex => HexDump.Format(data),
            MessageFormat.Base64 => BytesToBase64(data),
            MessageFormat.Json => FormatJsonOrText(data, encoding),
            _ => LooksBinary(data)
                ? HexDump.Format(data)
                : Decode(data, encoding)
        };
    }

    private static string FormatJsonOrText(byte[] data, TextEncodingKind encoding)
    {
        if (LooksBinary(data))
        {
            return HexDump.Format(data);
        }

        var text = Decode(data, encoding);
        var result = JsonFormatter.TryFormat(text, out var formatted, out _);
        return result ? formatted : text;
    }
}
