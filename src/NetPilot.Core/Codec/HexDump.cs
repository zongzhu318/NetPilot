using System.Text;

namespace NetPilot.Core.Codec;

public static class HexDump
{
    public static string Format(ReadOnlySpan<byte> data, int bytesPerLine = 16)
    {
        if (data.IsEmpty)
        {
            return string.Empty;
        }

        bytesPerLine = Math.Clamp(bytesPerLine, 1, 64);
        var builder = new StringBuilder(data.Length * 4);
        for (var offset = 0; offset < data.Length; offset += bytesPerLine)
        {
            var count = Math.Min(bytesPerLine, data.Length - offset);
            builder.Append(offset.ToString("X8")).Append("  ");

            for (var i = 0; i < bytesPerLine; i++)
            {
                if (i < count)
                {
                    builder.Append(data[offset + i].ToString("X2")).Append(' ');
                }
                else
                {
                    builder.Append("   ");
                }

                if (i == bytesPerLine / 2 - 1)
                {
                    builder.Append(' ');
                }
            }

            builder.Append(" |");
            for (var i = 0; i < count; i++)
            {
                var b = data[offset + i];
                builder.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
            }

            builder.Append('|');
            if (offset + bytesPerLine < data.Length)
            {
                builder.AppendLine();
            }
        }

        return builder.ToString();
    }
}
