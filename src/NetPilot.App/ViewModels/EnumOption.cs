namespace NetPilot.App.ViewModels;

public sealed class EnumOption<T>(T value, string display)
{
    public T Value { get; } = value;
    public string Display { get; } = display;

    public override string ToString() => Display;
}

public static class OptionLists
{
    public static IReadOnlyList<EnumOption<Core.Models.TextEncodingKind>> Encodings { get; } =
    [
        new(Core.Models.TextEncodingKind.Utf8, "UTF-8"),
        new(Core.Models.TextEncodingKind.Gbk, "GBK"),
        new(Core.Models.TextEncodingKind.Gb2312, "GB2312"),
        new(Core.Models.TextEncodingKind.Ascii, "ASCII"),
        new(Core.Models.TextEncodingKind.Latin1, "ISO-8859-1")
    ];

    public static IReadOnlyList<EnumOption<Core.Models.MessageFormat>> DisplayFormats { get; } =
    [
        new(Core.Models.MessageFormat.Text, "文本"),
        new(Core.Models.MessageFormat.Json, "JSON"),
        new(Core.Models.MessageFormat.Hex, "Hex"),
        new(Core.Models.MessageFormat.Base64, "Base64")
    ];

    public static IReadOnlyList<EnumOption<Core.Models.SendFormat>> SendFormats { get; } =
    [
        new(Core.Models.SendFormat.Text, "文本"),
        new(Core.Models.SendFormat.Json, "JSON"),
        new(Core.Models.SendFormat.Hex, "Hex"),
        new(Core.Models.SendFormat.Base64, "Base64")
    ];

    public static IReadOnlyList<EnumOption<Core.Models.NewlineMode>> Newlines { get; } =
    [
        new(Core.Models.NewlineMode.None, "无结束符"),
        new(Core.Models.NewlineMode.Crlf, "CRLF (\\r\\n)"),
        new(Core.Models.NewlineMode.Lf, "LF (\\n)"),
        new(Core.Models.NewlineMode.Custom, "自定义")
    ];

    public static IReadOnlyList<EnumOption<Core.Models.SessionMode>> Modes { get; } =
    [
        new(Core.Models.SessionMode.Client, "客户端"),
        new(Core.Models.SessionMode.Server, "服务端")
    ];
}
