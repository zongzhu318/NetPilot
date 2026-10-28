using NetPilot.Core.Models;

namespace NetPilot.Core.Http;

public sealed class HttpRequestSpec
{
    public string Method { get; init; } = "GET";
    public string Url { get; init; } = string.Empty;
    public List<HeaderItem> Headers { get; init; } = [];
    public HttpBodyKind BodyKind { get; init; }
    public string RawBody { get; init; } = string.Empty;
    public SendFormat RawBodyFormat { get; init; } = SendFormat.Text;
    public string RawContentType { get; init; } = "application/json";
    public TextEncodingKind Encoding { get; init; } = TextEncodingKind.Utf8;
    public List<HeaderItem> FormFields { get; init; } = [];
    public string FormFilePath { get; init; } = string.Empty;
    public bool IgnoreCertificate { get; init; }
    public int TimeoutSeconds { get; init; } = 100;
}

public sealed class HttpResponseData
{
    public int StatusCode { get; init; }
    public string ReasonPhrase { get; init; } = string.Empty;
    public required byte[] Body { get; init; }
    public required List<KeyValuePair<string, string>> Headers { get; init; }
    public TimeSpan Elapsed { get; init; }
    public string ContentType { get; init; } = string.Empty;
    public string FinalUrl { get; init; } = string.Empty;
    public string HttpVersion { get; init; } = string.Empty;

    public long ByteCount => Body.Length;
}

public sealed class HttpServerRequestInfo
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public string Method { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public string QueryString { get; init; } = string.Empty;
    public string Remote { get; init; } = string.Empty;
    public required List<KeyValuePair<string, string>> Headers { get; init; }
    public required byte[] Body { get; init; }
    public string ContentType { get; init; } = string.Empty;
}

public sealed class HttpMockOptions
{
    public int StatusCode { get; init; } = 200;
    public string ContentType { get; init; } = "application/json";
    public required byte[] Body { get; init; }
    public List<HeaderItem> Headers { get; init; } = [];
    public bool EnableCors { get; init; } = true;
}
