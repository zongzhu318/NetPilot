using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using NetPilot.Core.Codec;
using NetPilot.Core.Models;

namespace NetPilot.Core.Http;

public static class HttpRequestExecutor
{
    public static async Task<HttpResponseData> ExecuteAsync(HttpRequestSpec spec, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(spec.Url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("请输入有效的 HTTP / HTTPS 地址");
        }

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2)
        };

        if (spec.IgnoreCertificate)
        {
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        using var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(Math.Clamp(spec.TimeoutSeconds, 1, 600))
        };

        using var request = new HttpRequestMessage(new HttpMethod(spec.Method.ToUpperInvariant()), uri);
        HttpContent? content = null;
        Stream? fileStream = null;

        try
        {
            switch (spec.BodyKind)
            {
                case HttpBodyKind.Raw:
                    var rawBytes = EncodeBody(spec.RawBody, spec.RawBodyFormat, spec.Encoding);
                    content = new ByteArrayContent(rawBytes);
                    content.Headers.ContentType = MediaTypeHeaderValue.TryParse(spec.RawContentType, out var mediaType)
                        ? mediaType
                        : new MediaTypeHeaderValue("text/plain");
                    break;
                case HttpBodyKind.FormUrlEncoded:
                    content = new FormUrlEncodedContent(
                        spec.FormFields.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key))
                            .Select(x => new KeyValuePair<string, string>(x.Key, x.Value)));
                    break;
                case HttpBodyKind.FormData:
                    var multipart = new MultipartFormDataContent();
                    foreach (var field in spec.FormFields.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)))
                    {
                        multipart.Add(new StringContent(field.Value), field.Key);
                    }

                    if (!string.IsNullOrWhiteSpace(spec.FormFilePath) && File.Exists(spec.FormFilePath))
                    {
                        fileStream = File.OpenRead(spec.FormFilePath);
                        var fileContent = new StreamContent(fileStream);
                        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                        multipart.Add(fileContent, "file", Path.GetFileName(spec.FormFilePath));
                    }

                    content = multipart;
                    break;
            }

            request.Content = content;

            foreach (var header in spec.Headers.Where(x => x.Enabled && !string.IsNullOrWhiteSpace(x.Key)))
            {
                var key = header.Key.Trim();
                if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) && content is not null)
                {
                    if (MediaTypeHeaderValue.TryParse(header.Value, out var mediaTypeOverride))
                    {
                        content.Headers.ContentType = mediaTypeOverride;
                    }
                    continue;
                }

                if (!request.Headers.TryAddWithoutValidation(key, header.Value))
                {
                    content?.Headers.TryAddWithoutValidation(key, header.Value);
                }
            }

            if (spec.Method.Equals("GET", StringComparison.OrdinalIgnoreCase) ||
                spec.Method.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
            {
                request.Content = content = null;
            }

            var stopwatch = Stopwatch.StartNew();
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();

            var headers = new List<KeyValuePair<string, string>>();
            foreach (var header in response.Headers)
            {
                foreach (var value in header.Value)
                {
                    headers.Add(new KeyValuePair<string, string>(header.Key, value));
                }
            }

            foreach (var header in response.Content.Headers)
            {
                foreach (var value in header.Value)
                {
                    headers.Add(new KeyValuePair<string, string>(header.Key, value));
                }
            }

            return new HttpResponseData
            {
                StatusCode = (int)response.StatusCode,
                ReasonPhrase = response.ReasonPhrase ?? response.StatusCode.ToString(),
                Body = body,
                Headers = headers,
                Elapsed = stopwatch.Elapsed,
                ContentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty,
                FinalUrl = response.RequestMessage?.RequestUri?.ToString() ?? spec.Url,
                HttpVersion = response.Version.ToString()
            };
        }
        finally
        {
            content?.Dispose();
            fileStream?.Dispose();
        }
    }

    private static byte[] EncodeBody(string body, SendFormat format, TextEncodingKind encoding) => format switch
    {
        SendFormat.Hex => CodecEngine.ParseHex(body),
        SendFormat.Base64 => CodecEngine.ParseBase64(body),
        _ => CodecEngine.Encode(body, encoding)
    };
}
