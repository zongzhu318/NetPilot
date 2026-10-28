using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using NetPilot.App.Services;
using NetPilot.Core.Codec;
using NetPilot.Core.Http;
using NetPilot.Core.Models;
using NetPilot.Core.Protocols;

namespace NetPilot.App.ViewModels;

public enum ResponseDisplayMode
{
    Auto,
    Text,
    Json,
    Hex,
    Base64
}

public sealed class HttpSessionViewModel : SessionViewModel
{
    private static readonly IBrush SuccessStatusBrush = new SolidColorBrush(Color.Parse("#15803D"));
    private static readonly IBrush WarningStatusBrush = new SolidColorBrush(Color.Parse("#B45309"));
    private static readonly IBrush ErrorStatusBrush = new SolidColorBrush(Color.Parse("#DC2626"));

    private string _method = "GET";
    private string _url = "https://httpbin.org/get";
    private HttpBodyKind _bodyKind = HttpBodyKind.None;
    private EnumOption<HttpBodyKind> _bodyKindOption = null!;
    private string _rawBody = string.Empty;
    private string _rawContentType = "application/json";
    private string _formFilePath = string.Empty;

    private int _mockStatus = 200;
    private string _mockContentType = "application/json";
    private string _mockBody = "{\n  \"ok\": true,\n  \"message\": \"Hello from NetPilot\"\n}";
    private bool _mockEnableCors = true;

    private bool _isRequesting;
    private bool _hasResponse;
    private int _responseStatusCode;
    private string _responseStatusText = string.Empty;
    private string _responseReason = string.Empty;
    private string _responseElapsedText = string.Empty;
    private string _responseSizeText = string.Empty;
    private string _responseContentType = string.Empty;
    private string _responseBodyText = string.Empty;
    private string _responseBodySyntax = "None";
    private string _responseError = string.Empty;
    private bool _responseIsSuccess;
    private byte[] _responseBody = [];
    private ResponseDisplayMode _responseDisplayMode = ResponseDisplayMode.Auto;
    private EnumOption<ResponseDisplayMode> _responseDisplayModeOption = null!;

    private CancellationTokenSource? _requestCts;

    public HttpSessionViewModel(AppServices services, SessionMode mode)
        : base(services, mode, "客户端", "Mock 服务端")
    {
        HttpMethods = ["GET", "POST", "PUT", "DELETE", "PATCH", "HEAD", "OPTIONS"];
        BodyKindOptions =
        [
            new EnumOption<HttpBodyKind>(HttpBodyKind.None, "无"),
            new EnumOption<HttpBodyKind>(HttpBodyKind.Raw, "Raw"),
            new EnumOption<HttpBodyKind>(HttpBodyKind.FormUrlEncoded, "x-www-form-urlencoded"),
            new EnumOption<HttpBodyKind>(HttpBodyKind.FormData, "form-data")
        ];
        ResponseFormatOptions =
        [
            new EnumOption<ResponseDisplayMode>(ResponseDisplayMode.Auto, "自动"),
            new EnumOption<ResponseDisplayMode>(ResponseDisplayMode.Text, "文本"),
            new EnumOption<ResponseDisplayMode>(ResponseDisplayMode.Json, "JSON"),
            new EnumOption<ResponseDisplayMode>(ResponseDisplayMode.Hex, "Hex"),
            new EnumOption<ResponseDisplayMode>(ResponseDisplayMode.Base64, "Base64")
        ];

        _bodyKindOption = BodyKindOptions[0];
        _responseDisplayModeOption = ResponseFormatOptions[0];

        RequestHeaders = new ObservableCollection<HeaderItem>
        {
            new("Accept", "application/json"),
            new("User-Agent", "NetPilot/1.0")
        };
        FormFields = new ObservableCollection<HeaderItem>();
        MockHeaders = new ObservableCollection<HeaderItem>();
        ResponseHeaders = new ObservableCollection<HeaderItem>();

        SendRequestCommand = new AsyncRelayCommand(SendRequestAsync, () => CanSendRequest);
        CancelRequestCommand = new RelayCommand(CancelRequest, () => IsRequesting);
        AddRequestHeaderCommand = new RelayCommand(() => RequestHeaders.Add(new HeaderItem("Header", string.Empty)));
        RemoveRequestHeaderCommand = new RelayCommand<HeaderItem>(x =>
        {
            if (x is not null)
            {
                RequestHeaders.Remove(x);
            }
        });
        AddCommonHeaderCommand = new RelayCommand<string>(AddCommonHeader);
        AddFormFieldCommand = new RelayCommand(() => FormFields.Add(new HeaderItem("field", string.Empty)));
        RemoveFormFieldCommand = new RelayCommand<HeaderItem>(x =>
        {
            if (x is not null)
            {
                FormFields.Remove(x);
            }
        });
        ChooseFormFileCommand = new AsyncRelayCommand(ChooseFormFileAsync);
        ClearFormFileCommand = new RelayCommand(() => FormFilePath = string.Empty);
        AddMockHeaderCommand = new RelayCommand(() => MockHeaders.Add(new HeaderItem("Header", string.Empty)));
        RemoveMockHeaderCommand = new RelayCommand<HeaderItem>(x =>
        {
            if (x is not null)
            {
                MockHeaders.Remove(x);
            }
        });
        FormatRawBodyCommand = new RelayCommand(FormatRawBody);
        FormatMockBodyCommand = new RelayCommand(FormatMockBody);
        CopyResponseCommand = new AsyncRelayCommand(CopyResponseAsync, () => HasResponse);
        SaveResponseCommand = new AsyncRelayCommand(SaveResponseAsync, () => HasResponse);

        if (IsClientMode)
        {
            SetStateDirect(TransportState.Running, "就绪");
        }
    }

    public override ProtocolKind Protocol => ProtocolKind.Http;
    public override string ProtocolDisplay => "HTTP 调试";
    public override bool SupportsConnection => IsServerMode;
    public override bool IsSendAvailable => false;
    public override bool ShowIgnoreCertificateOption => IsClientMode;
    protected override string RunningStatusLabel => IsServerMode ? "Mock 服务中" : "就绪";
    protected override string DefaultStatusMessage => IsServerMode ? "尚未启动 Mock 服务" : "就绪";

    public IReadOnlyList<string> HttpMethods { get; }
    public IReadOnlyList<EnumOption<HttpBodyKind>> BodyKindOptions { get; }
    public IReadOnlyList<EnumOption<ResponseDisplayMode>> ResponseFormatOptions { get; }

    public ObservableCollection<HeaderItem> RequestHeaders { get; }
    public ObservableCollection<HeaderItem> FormFields { get; }
    public ObservableCollection<HeaderItem> MockHeaders { get; }
    public ObservableCollection<HeaderItem> ResponseHeaders { get; }

    public AsyncRelayCommand SendRequestCommand { get; }
    public RelayCommand CancelRequestCommand { get; }
    public RelayCommand AddRequestHeaderCommand { get; }
    public RelayCommand<HeaderItem> RemoveRequestHeaderCommand { get; }
    public RelayCommand<string> AddCommonHeaderCommand { get; }
    public RelayCommand AddFormFieldCommand { get; }
    public RelayCommand<HeaderItem> RemoveFormFieldCommand { get; }
    public AsyncRelayCommand ChooseFormFileCommand { get; }
    public RelayCommand ClearFormFileCommand { get; }
    public RelayCommand AddMockHeaderCommand { get; }
    public RelayCommand<HeaderItem> RemoveMockHeaderCommand { get; }
    public RelayCommand FormatRawBodyCommand { get; }
    public RelayCommand FormatMockBodyCommand { get; }
    public AsyncRelayCommand CopyResponseCommand { get; }
    public AsyncRelayCommand SaveResponseCommand { get; }

    public bool ShowRequestPanel => IsClientMode;
    public bool ShowMockPanel => IsServerMode;
    public bool ShowConnectButton => IsServerMode;
    public bool ShowRawBody => BodyKind == HttpBodyKind.Raw;
    public bool ShowFormFields => BodyKind is HttpBodyKind.FormUrlEncoded or HttpBodyKind.FormData;
    public bool ShowFormFile => BodyKind == HttpBodyKind.FormData;
    public bool HasFormFile => !string.IsNullOrWhiteSpace(FormFilePath);
    public string FormFileName => string.IsNullOrWhiteSpace(FormFilePath) ? "未选择文件" : Path.GetFileName(FormFilePath);
    public string MockUrlText => $"http://{LocalHost}:{Port}";

    public string HttpMethod
    {
        get => _method;
        set
        {
            if (SetProperty(ref _method, value))
            {
                OnPropertyChanged(nameof(CanSendRequest));
                SendRequestCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public string Url
    {
        get => _url;
        set
        {
            if (SetProperty(ref _url, value))
            {
                OnPropertyChanged(nameof(CanSendRequest));
                SendRequestCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public HttpBodyKind BodyKind
    {
        get => _bodyKind;
        set
        {
            if (!SetProperty(ref _bodyKind, value))
            {
                return;
            }

            _bodyKindOption = BodyKindOptions.First(x => x.Value == value);
            OnPropertyChanged(nameof(BodyKindOption));
            OnPropertyChanged(nameof(ShowRawBody));
            OnPropertyChanged(nameof(ShowFormFields));
            OnPropertyChanged(nameof(ShowFormFile));
        }
    }

    public EnumOption<HttpBodyKind> BodyKindOption
    {
        get => _bodyKindOption;
        set
        {
            if (value is not null)
            {
                BodyKind = value.Value;
            }
        }
    }

    public string RawBody
    {
        get => _rawBody;
        set => SetProperty(ref _rawBody, value);
    }

    public string RawContentType
    {
        get => _rawContentType;
        set => SetProperty(ref _rawContentType, value);
    }

    public string FormFilePath
    {
        get => _formFilePath;
        set
        {
            if (SetProperty(ref _formFilePath, value))
            {
                OnPropertyChanged(nameof(HasFormFile));
                OnPropertyChanged(nameof(FormFileName));
            }
        }
    }

    public int MockStatus
    {
        get => _mockStatus;
        set => SetProperty(ref _mockStatus, Math.Clamp(value, 100, 599));
    }

    public string MockContentType
    {
        get => _mockContentType;
        set => SetProperty(ref _mockContentType, value);
    }

    public string MockBody
    {
        get => _mockBody;
        set => SetProperty(ref _mockBody, value);
    }

    public bool MockEnableCors
    {
        get => _mockEnableCors;
        set => SetProperty(ref _mockEnableCors, value);
    }

    public bool IsRequesting
    {
        get => _isRequesting;
        private set
        {
            if (SetProperty(ref _isRequesting, value))
            {
                OnPropertyChanged(nameof(CanSendRequest));
                SendRequestCommand.NotifyCanExecuteChanged();
                CancelRequestCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool CanSendRequest => IsClientMode && !IsRequesting && !string.IsNullOrWhiteSpace(Url);

    public bool HasResponse
    {
        get => _hasResponse;
        private set
        {
            if (SetProperty(ref _hasResponse, value))
            {
                CopyResponseCommand.NotifyCanExecuteChanged();
                SaveResponseCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public int ResponseStatusCode
    {
        get => _responseStatusCode;
        private set
        {
            if (SetProperty(ref _responseStatusCode, value))
            {
                OnPropertyChanged(nameof(ResponseStatusBrush));
            }
        }
    }

    public string ResponseStatusText
    {
        get => _responseStatusText;
        private set => SetProperty(ref _responseStatusText, value);
    }

    public string ResponseReason
    {
        get => _responseReason;
        private set => SetProperty(ref _responseReason, value);
    }

    public string ResponseElapsedText
    {
        get => _responseElapsedText;
        private set => SetProperty(ref _responseElapsedText, value);
    }

    public string ResponseSizeText
    {
        get => _responseSizeText;
        private set => SetProperty(ref _responseSizeText, value);
    }

    public string ResponseContentType
    {
        get => _responseContentType;
        private set => SetProperty(ref _responseContentType, value);
    }

    public string ResponseBodyText
    {
        get => _responseBodyText;
        private set => SetProperty(ref _responseBodyText, value);
    }

    public string ResponseBodySyntax
    {
        get => _responseBodySyntax;
        private set => SetProperty(ref _responseBodySyntax, value);
    }

    public string ResponseError
    {
        get => _responseError;
        private set
        {
            if (SetProperty(ref _responseError, value))
            {
                OnPropertyChanged(nameof(HasResponseError));
            }
        }
    }

    public bool HasResponseError => !string.IsNullOrWhiteSpace(ResponseError);

    public bool ResponseIsSuccess
    {
        get => _responseIsSuccess;
        private set => SetProperty(ref _responseIsSuccess, value);
    }

    public IBrush ResponseStatusBrush => ResponseStatusCode >= 500
        ? ErrorStatusBrush
        : ResponseStatusCode >= 400
            ? WarningStatusBrush
            : SuccessStatusBrush;

    public ResponseDisplayMode ResponseDisplayMode
    {
        get => _responseDisplayMode;
        set
        {
            if (!SetProperty(ref _responseDisplayMode, value))
            {
                return;
            }

            _responseDisplayModeOption = ResponseFormatOptions.First(x => x.Value == value);
            OnPropertyChanged(nameof(ResponseDisplayModeOption));
            RefreshResponseDisplay();
        }
    }

    public EnumOption<ResponseDisplayMode> ResponseDisplayModeOption
    {
        get => _responseDisplayModeOption;
        set
        {
            if (value is not null)
            {
                ResponseDisplayMode = value.Value;
            }
        }
    }

    public string ResponseHeaderCountText => $"{ResponseHeaders.Count} 个响应头";

    protected override void OnModeChanged()
    {
        base.OnModeChanged();
        if (IsClientMode)
        {
            SetStateDirect(TransportState.Running, "就绪");
        }

        OnPropertyChanged(nameof(ShowRequestPanel));
        OnPropertyChanged(nameof(ShowMockPanel));
        OnPropertyChanged(nameof(ShowConnectButton));
        OnPropertyChanged(nameof(ShowIgnoreCertificateOption));
        OnPropertyChanged(nameof(CanSendRequest));
        OnPropertyChanged(nameof(MockUrlText));
        SendRequestCommand.NotifyCanExecuteChanged();
    }

    protected override ITransport? CreateTransport()
    {
        if (IsClientMode)
        {
            return null;
        }

        var mock = new HttpMockOptions
        {
            StatusCode = MockStatus,
            ContentType = MockContentType,
            Body = CodecEngine.Encode(MockBody, TextEncodingKind.Utf8),
            Headers = MockHeaders.Where(x => x.Enabled).Select(x => x.Clone()).ToList(),
            EnableCors = MockEnableCors
        };
        var transport = new HttpServerTransport(LocalHost, Port, mock);
        transport.RequestReceived += OnHttpRequestReceived;
        return transport;
    }

    private void OnHttpRequestReceived(object? sender, HttpServerRequestInfo info)
    {
        var requestLine = $"{info.Method} {info.Path}{info.QueryString}";
        var headerText = string.Join("\n", info.Headers.Select(x => $"{x.Key}: {x.Value}"));
        var text = headerText.Length > 0 ? $"{requestLine}\n{headerText}" : requestLine;
        AddLogCore(LogEntry.Create(LogDirection.Receive, info.Body, SessionName, info.Remote, text: text));
        AddInfo($"收到 HTTP 请求：{requestLine}（{info.Body.Length:N0} B）");
    }

    private async Task SendRequestAsync()
    {
        if (!CanSendRequest)
        {
            return;
        }

        IsRequesting = true;
        ResponseError = string.Empty;
        _requestCts = new CancellationTokenSource();
        var token = _requestCts.Token;
        var spec = BuildRequestSpec();

        try
        {
            var requestLogText = BuildRequestLogText(spec);
            var requestBytes = BuildRequestLogBytes(spec);
            AddLogCore(LogEntry.Create(LogDirection.Send, requestBytes, SessionName, spec.Url, text: requestLogText));

            var response = await HttpRequestExecutor.ExecuteAsync(spec, token);
            ApplyResponse(response);

            AddLogCore(LogEntry.Create(LogDirection.Receive, response.Body, SessionName,
                $"{response.StatusCode} {response.ReasonPhrase}"));
            AddInfo($"HTTP {response.StatusCode} {response.ReasonPhrase} · {response.Elapsed.TotalMilliseconds:0} ms · {response.Body.Length:N0} B");
        }
        catch (OperationCanceledException)
        {
            ResponseError = "请求已取消";
            AddInfo("HTTP 请求已取消");
        }
        catch (Exception ex)
        {
            ResponseError = ex.Message;
            HasResponse = false;
            AddError($"HTTP 请求失败：{ex.Message}");
        }
        finally
        {
            IsRequesting = false;
            _requestCts?.Dispose();
            _requestCts = null;
        }
    }

    private HttpRequestSpec BuildRequestSpec()
    {
        var url = Url.Trim();
        if (!url.Contains("://", StringComparison.Ordinal))
        {
            url = "http://" + url;
        }

        return new HttpRequestSpec
        {
            Method = HttpMethod,
            Url = url,
            Headers = RequestHeaders.Where(x => x.Enabled).Select(x => x.Clone()).ToList(),
            BodyKind = BodyKind,
            RawBody = RawBody,
            RawBodyFormat = SendFormat,
            RawContentType = RawContentType,
            Encoding = SendEncoding,
            FormFields = FormFields.Where(x => x.Enabled).Select(x => x.Clone()).ToList(),
            FormFilePath = FormFilePath,
            IgnoreCertificate = IgnoreCertificate
        };
    }

    private static string BuildRequestLogText(HttpRequestSpec spec)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"{spec.Method} {spec.Url}");
        foreach (var header in spec.Headers.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)))
        {
            builder.AppendLine($"{header.Key}: {header.Value}");
        }

        if (spec.BodyKind == HttpBodyKind.FormUrlEncoded)
        {
            builder.AppendLine();
            builder.AppendLine(string.Join("&", spec.FormFields
                .Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key))
                .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}")));
        }
        else if (spec.BodyKind == HttpBodyKind.FormData)
        {
            builder.AppendLine();
            builder.AppendLine("form-data:");
            foreach (var field in spec.FormFields.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)))
            {
                builder.AppendLine($"{field.Key} = {field.Value}");
            }

            if (!string.IsNullOrWhiteSpace(spec.FormFilePath))
            {
                builder.AppendLine($"file = {spec.FormFilePath}");
            }
        }
        else if (spec.BodyKind == HttpBodyKind.Raw)
        {
            builder.AppendLine();
            builder.AppendLine(spec.RawBody);
        }

        return builder.ToString().TrimEnd();
    }

    private static byte[] BuildRequestLogBytes(HttpRequestSpec spec)
    {
        try
        {
            return spec.BodyKind switch
            {
                HttpBodyKind.Raw => spec.RawBodyFormat switch
                {
                    SendFormat.Hex => CodecEngine.ParseHex(spec.RawBody),
                    SendFormat.Base64 => CodecEngine.ParseBase64(spec.RawBody),
                    _ => CodecEngine.Encode(spec.RawBody, spec.Encoding)
                },
                HttpBodyKind.FormUrlEncoded => CodecEngine.Encode(
                    string.Join("&", spec.FormFields
                        .Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key))
                        .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value)}")),
                    TextEncodingKind.Utf8),
                _ => []
            };
        }
        catch
        {
            return [];
        }
    }

    private void ApplyResponse(HttpResponseData response)
    {
        ResponseStatusCode = response.StatusCode;
        ResponseReason = response.ReasonPhrase;
        ResponseStatusText = $"{response.StatusCode} {response.ReasonPhrase}";
        ResponseElapsedText = $"{response.Elapsed.TotalMilliseconds:0} ms";
        ResponseSizeText = FormatBytes(response.Body.Length);
        ResponseContentType = response.ContentType;
        ResponseIsSuccess = response.StatusCode is >= 200 and < 400;

        ResponseHeaders.Clear();
        foreach (var header in response.Headers)
        {
            ResponseHeaders.Add(new HeaderItem(header.Key, header.Value));
        }

        OnPropertyChanged(nameof(ResponseHeaderCountText));
        _responseBody = response.Body;
        HasResponse = true;
        RefreshResponseDisplay();
    }

    private void RefreshResponseDisplay()
    {
        if (!HasResponse)
        {
            return;
        }

        if (_responseBody.Length == 0)
        {
            ResponseBodyText = string.Empty;
            ResponseBodySyntax = "None";
            return;
        }

        var encoding = ResolveResponseEncoding(ResponseContentType);
        var text = string.Empty;
        var syntax = "None";
        switch (ResponseDisplayMode)
        {
            case ResponseDisplayMode.Hex:
                text = HexDump.Format(_responseBody);
                break;
            case ResponseDisplayMode.Base64:
                text = CodecEngine.BytesToBase64(_responseBody);
                break;
            case ResponseDisplayMode.Text:
                text = CodecEngine.LooksBinary(_responseBody)
                    ? HexDump.Format(_responseBody)
                    : CodecEngine.Decode(_responseBody, encoding);
                break;
            case ResponseDisplayMode.Json:
                if (!CodecEngine.LooksBinary(_responseBody))
                {
                    var raw = CodecEngine.Decode(_responseBody, encoding);
                    if (JsonFormatter.TryFormat(raw, out var formatted, out _))
                    {
                        text = formatted;
                        syntax = "Json";
                    }
                    else
                    {
                        text = raw;
                    }
                }
                else
                {
                    text = HexDump.Format(_responseBody);
                }
                break;
            default:
                if (CodecEngine.LooksBinary(_responseBody))
                {
                    text = HexDump.Format(_responseBody);
                }
                else
                {
                    var raw = CodecEngine.Decode(_responseBody, encoding);
                    if (JsonFormatter.TryFormat(raw, out var pretty, out _) &&
                        (ResponseContentType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
                         raw.TrimStart().StartsWith('{') || raw.TrimStart().StartsWith('[')))
                    {
                        text = pretty;
                        syntax = "Json";
                    }
                    else
                    {
                        text = raw;
                    }
                }

                break;
        }

        ResponseBodyText = text;
        ResponseBodySyntax = syntax;
    }

    private static TextEncodingKind ResolveResponseEncoding(string contentType)
    {
        var lower = contentType.ToLowerInvariant();
        if (lower.Contains("gbk") || lower.Contains("gb2312"))
        {
            return TextEncodingKind.Gbk;
        }

        if (lower.Contains("iso-8859-1"))
        {
            return TextEncodingKind.Latin1;
        }

        return TextEncodingKind.Utf8;
    }

    private void FormatRawBody()
    {
        if (JsonFormatter.TryFormat(RawBody, out var formatted, out var error))
        {
            RawBody = formatted;
            SendFormat = SendFormat.Json;
            AddInfo("请求体 JSON 格式化成功");
        }
        else
        {
            AddError($"请求体 JSON 校验失败：{error}");
        }
    }

    private void FormatMockBody()
    {
        if (JsonFormatter.TryFormat(MockBody, out var formatted, out var error))
        {
            MockBody = formatted;
            AddInfo("Mock 响应体 JSON 格式化成功");
        }
        else
        {
            AddError($"Mock 响应体 JSON 校验失败：{error}");
        }
    }

    private async Task ChooseFormFileAsync()
    {
        var path = await Services.Dialogs.PickOpenFileAsync("选择要上传的文件",
            new FilePickerFileType("所有文件") { Patterns = ["*"] });
        if (path is not null)
        {
            FormFilePath = path;
        }
    }

    private void CancelRequest() => _requestCts?.Cancel();

    private void AddCommonHeader(string? preset)
    {
        var (key, value) = preset switch
        {
            "Content-Type" => ("Content-Type", "application/json"),
            "Authorization" => ("Authorization", "Bearer "),
            "Accept" => ("Accept", "application/json"),
            "User-Agent" => ("User-Agent", "NetPilot/1.0"),
            "X-Request-Id" => ("X-Request-Id", string.Empty),
            _ => (preset ?? "Header", string.Empty)
        };
        RequestHeaders.Add(new HeaderItem(key, value));
    }

    private async Task CopyResponseAsync()
    {
        if (HasResponse && !string.IsNullOrEmpty(ResponseBodyText))
        {
            await Services.Dialogs.CopyTextAsync(ResponseBodyText);
            Services.Toasts.Show("响应内容已复制到剪贴板");
        }
    }

    private async Task SaveResponseAsync()
    {
        if (!HasResponse)
        {
            return;
        }

        var path = await Services.Dialogs.PickSaveFileAsync("保存响应报文",
            $"response-{DateTime.Now:yyyyMMdd-HHmmss}.bin",
            new FilePickerFileType("二进制文件") { Patterns = ["*.bin"] },
            new FilePickerFileType("所有文件") { Patterns = ["*"] });
        if (path is null)
        {
            return;
        }

        try
        {
            await File.WriteAllBytesAsync(path, _responseBody);
            AddInfo($"响应已保存：{path}");
        }
        catch (Exception ex)
        {
            AddError($"保存响应失败：{ex.Message}");
        }
    }

    protected override void WriteProtocolConfig(SessionConfig config)
    {
        base.WriteProtocolConfig(config);
        config.HttpMethod = HttpMethod;
        config.Url = Url;
        config.HttpBodyKind = BodyKind;
        config.HttpRawBody = RawBody;
        config.HttpRawContentType = RawContentType;
        config.HttpHeaders = RequestHeaders.Select(x => x.Clone()).ToList();
        config.HttpFormFields = FormFields.Select(x => x.Clone()).ToList();
        config.HttpFormFile = FormFilePath;
        config.MockStatus = MockStatus;
        config.MockContentType = MockContentType;
        config.MockBody = MockBody;
        config.MockHeaders = MockHeaders.Select(x => x.Clone()).ToList();
        config.MockEnableCors = MockEnableCors;
    }

    protected override void ApplyProtocolConfig(SessionConfig config)
    {
        base.ApplyProtocolConfig(config);
        HttpMethod = config.HttpMethod;
        Url = config.Url;
        BodyKind = config.HttpBodyKind;
        RawBody = config.HttpRawBody;
        RawContentType = config.HttpRawContentType;
        RequestHeaders.Clear();
        foreach (var header in config.HttpHeaders)
        {
            RequestHeaders.Add(header.Clone());
        }

        FormFields.Clear();
        foreach (var field in config.HttpFormFields)
        {
            FormFields.Add(field.Clone());
        }

        FormFilePath = config.HttpFormFile;
        MockStatus = config.MockStatus;
        MockContentType = config.MockContentType;
        MockBody = config.MockBody;
        MockHeaders.Clear();
        foreach (var header in config.MockHeaders)
        {
            MockHeaders.Add(header.Clone());
        }

        MockEnableCors = config.MockEnableCors;

        if (IsClientMode)
        {
            SetStateDirect(TransportState.Running, "就绪");
        }
        else
        {
            SetStateDirect(TransportState.Stopped, DefaultStatusMessage);
        }

        OnPropertyChanged(nameof(ShowRequestPanel));
        OnPropertyChanged(nameof(ShowMockPanel));
        OnPropertyChanged(nameof(ShowConnectButton));
        OnPropertyChanged(nameof(CanSendRequest));
        OnPropertyChanged(nameof(MockUrlText));
        SendRequestCommand.NotifyCanExecuteChanged();
    }

    public override string ConnectButtonText => IsActive ? "停止 Mock" : "启动 Mock";

    public override async Task ShutdownAsync()
    {
        CancelRequest();
        await base.ShutdownAsync();
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024 * 1024 => $"{bytes / 1024d / 1024d:0.0} MB",
        >= 1024 => $"{bytes / 1024d:0.0} KB",
        _ => $"{bytes} B"
    };
}
