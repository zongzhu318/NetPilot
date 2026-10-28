namespace NetPilot.Core.Models;

public sealed class SessionConfig
{
    public string SessionName { get; set; } = "未命名会话";
    public ProtocolKind Protocol { get; set; } = ProtocolKind.Tcp;
    public SessionMode Mode { get; set; } = SessionMode.Client;

    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 8080;
    public string LocalHost { get; set; } = "0.0.0.0";
    public int LocalPort { get; set; }

    public bool UseTls { get; set; }
    public bool IgnoreCertificate { get; set; }
    public bool AutoReconnect { get; set; }
    public int ReconnectIntervalMs { get; set; } = 3000;

    public TextEncodingKind DefaultEncoding { get; set; } = TextEncodingKind.Utf8;
    public MessageFormat DisplayFormat { get; set; } = MessageFormat.Text;

    public string SendTemplate { get; set; } = string.Empty;
    public SendFormat SendFormat { get; set; } = SendFormat.Text;
    public TextEncodingKind SendEncoding { get; set; } = TextEncodingKind.Utf8;
    public NewlineMode Newline { get; set; } = NewlineMode.None;
    public string CustomNewline { get; set; } = string.Empty;
    public bool UseTemplateVariables { get; set; }
    public List<string> HistoryList { get; set; } = [];

    public string WsSubProtocol { get; set; } = string.Empty;
    public string WsUrl { get; set; } = "ws://127.0.0.1:8080";
    public bool WsBinary { get; set; }
    public List<HeaderItem> WsHeaders { get; set; } = [];

    public string HttpMethod { get; set; } = "GET";
    public string Url { get; set; } = "https://httpbin.org/get";
    public HttpBodyKind HttpBodyKind { get; set; } = HttpBodyKind.None;
    public string HttpRawBody { get; set; } = string.Empty;
    public string HttpRawContentType { get; set; } = "application/json";
    public List<HeaderItem> HttpHeaders { get; set; } = [];
    public List<HeaderItem> HttpFormFields { get; set; } = [];
    public string HttpFormFile { get; set; } = string.Empty;

    public int MockStatus { get; set; } = 200;
    public string MockContentType { get; set; } = "application/json";
    public string MockBody { get; set; } = "{\n  \"ok\": true,\n  \"message\": \"Hello from NetPilot\"\n}";
    public List<HeaderItem> MockHeaders { get; set; } = [];
    public bool MockEnableCors { get; set; } = true;

    public SessionConfig Clone() => new()
    {
        SessionName = SessionName,
        Protocol = Protocol,
        Mode = Mode,
        Host = Host,
        Port = Port,
        LocalHost = LocalHost,
        LocalPort = LocalPort,
        UseTls = UseTls,
        IgnoreCertificate = IgnoreCertificate,
        AutoReconnect = AutoReconnect,
        ReconnectIntervalMs = ReconnectIntervalMs,
        DefaultEncoding = DefaultEncoding,
        DisplayFormat = DisplayFormat,
        SendTemplate = SendTemplate,
        SendFormat = SendFormat,
        SendEncoding = SendEncoding,
        Newline = Newline,
        CustomNewline = CustomNewline,
        UseTemplateVariables = UseTemplateVariables,
        HistoryList = [.. HistoryList],
        WsSubProtocol = WsSubProtocol,
        WsUrl = WsUrl,
        WsBinary = WsBinary,
        WsHeaders = WsHeaders.Select(x => x.Clone()).ToList(),
        HttpMethod = HttpMethod,
        Url = Url,
        HttpBodyKind = HttpBodyKind,
        HttpRawBody = HttpRawBody,
        HttpRawContentType = HttpRawContentType,
        HttpHeaders = HttpHeaders.Select(x => x.Clone()).ToList(),
        HttpFormFields = HttpFormFields.Select(x => x.Clone()).ToList(),
        HttpFormFile = HttpFormFile,
        MockStatus = MockStatus,
        MockContentType = MockContentType,
        MockBody = MockBody,
        MockHeaders = MockHeaders.Select(x => x.Clone()).ToList(),
        MockEnableCors = MockEnableCors
    };
}
