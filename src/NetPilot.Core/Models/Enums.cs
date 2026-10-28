namespace NetPilot.Core.Models;

public enum ProtocolKind
{
    Tcp,
    Udp,
    WebSocket,
    Http
}

public enum SessionMode
{
    Client,
    Server
}

public enum TransportState
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Faulted
}

public enum TextEncodingKind
{
    Utf8,
    Gbk,
    Gb2312,
    Ascii,
    Latin1
}

public enum MessageFormat
{
    Text,
    Json,
    Hex,
    Base64
}

public enum SendFormat
{
    Text,
    Json,
    Hex,
    Base64
}

public enum NewlineMode
{
    None,
    Crlf,
    Lf,
    Custom
}

public enum LogDirection
{
    Send,
    Receive,
    Info,
    Error
}

public enum HttpBodyKind
{
    None,
    Raw,
    FormUrlEncoded,
    FormData
}

public enum AppTheme
{
    Light,
    Dark
}

public enum ExportFormat
{
    Txt,
    Csv,
    Hex
}
