using System.Text.Json;

namespace NetPilot.Core.Codec;

public static class JsonFormatter
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static bool TryFormat(string? text, out string formatted, out string error)
    {
        formatted = string.Empty;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "内容为空";
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(text, DocumentOptions);
            formatted = JsonSerializer.Serialize(document.RootElement, SerializerOptions);
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static bool TryFormat(byte[] data, System.Text.Encoding encoding, out string formatted, out string error) =>
        TryFormat(encoding.GetString(data), out formatted, out error);
}
