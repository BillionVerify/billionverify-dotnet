using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BillionVerify;

/// <summary>Client for the BillionVerify API. Create one and reuse it; it is thread-safe.</summary>
public sealed partial class BillionVerifyClient : IDisposable
{
    public const string DefaultBaseUrl = "https://api.billionverify.com";
    public const string Version = "1.0.0";

    private const string ApiKeyHeader = "BV-API-KEY";
    private const int MaxRedirects = 5;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _apiKey;
    private readonly Uri _baseUri;
    private readonly TimeSpan _timeout;
    private readonly int _attempts;
    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;

    public BillionVerifyClient(string apiKey)
        : this(new BillionVerifyClientOptions { ApiKey = apiKey })
    {
    }

    public BillionVerifyClient(BillionVerifyClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.ApiKey))
        {
            throw new AuthenticationException("API key is required");
        }

        _apiKey = options.ApiKey;
        _baseUri = NormalizeBaseUrl(options.BaseUrl);
        _timeout = options.Timeout > TimeSpan.Zero ? options.Timeout : TimeSpan.FromSeconds(30);
        _attempts = Math.Max(1, options.Retries);
        _delay = options.Delay ?? ((span, token) => Task.Delay(span, token));

        if (options.HttpClient is not null)
        {
            _http = options.HttpClient;
            _ownsHttp = false;
        }
        else
        {
            // Redirects are followed by hand so the API key never goes to another host.
            _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
            {
                Timeout = System.Threading.Timeout.InfiniteTimeSpan,
            };
            _ownsHttp = true;
        }
    }

    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    private static Uri NormalizeBaseUrl(string? baseUrl)
    {
        var text = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl.Trim();
        text = text.TrimEnd('/');
        if (text.EndsWith("/v1", StringComparison.Ordinal))
        {
            text = text[..^3].TrimEnd('/');
        }
        if (!Uri.TryCreate(text + "/", UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ValidationException($"Invalid base URL: {baseUrl}");
        }
        return uri;
    }

    // ---- transport -------------------------------------------------------------------------------------------

    private sealed record RequestSpec(
        HttpMethod Method,
        string Path,
        IReadOnlyList<KeyValuePair<string, string>>? Query = null,
        Func<HttpContent>? Content = null,
        bool Auth = true,
        TimeSpan? Timeout = null);

    private Uri BuildUri(RequestSpec spec)
    {
        var builder = new UriBuilder(new Uri(_baseUri, spec.Path.TrimStart('/')));
        if (spec.Query is { Count: > 0 })
        {
            builder.Query = string.Join("&", spec.Query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        }
        return builder.Uri;
    }

    private static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    private static bool SameOrigin(Uri a, Uri b) =>
        string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
        && a.Port == b.Port;

    private async Task<HttpResponseMessage> SendOnceAsync(RequestSpec spec, CancellationToken token)
    {
        var uri = BuildUri(spec);
        var sendKey = spec.Auth;
        for (var hop = 0; ; hop++)
        {
            using var request = new HttpRequestMessage(spec.Method, uri);
            request.Headers.TryAddWithoutValidation("Accept", "application/json");
            request.Headers.TryAddWithoutValidation("User-Agent", "billionverify-dotnet/" + Version);
            if (sendKey)
            {
                request.Headers.TryAddWithoutValidation(ApiKeyHeader, _apiKey);
            }
            if (hop == 0 && spec.Content is not null)
            {
                request.Content = spec.Content();
            }

            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if (spec.Method == HttpMethod.Get && IsRedirect(response.StatusCode) && response.Headers.Location is { } location && hop < MaxRedirects)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                // The key goes only to the API's own origin; a storage host gets a bare request.
                sendKey = sendKey && SameOrigin(next, _baseUri);
                response.Dispose();
                uri = next;
                continue;
            }
            return response;
        }
    }

    private static bool IsRetryable(int status) => status is 429 or 500 or 502 or 503;

    private static int ParseRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta)
        {
            return (int)Math.Ceiling(Math.Max(0, delta.TotalSeconds));
        }
        if (header?.Date is { } date)
        {
            return (int)Math.Ceiling(Math.Max(0, (date - DateTimeOffset.UtcNow).TotalSeconds));
        }
        return 0;
    }

    private async Task<T> ExecuteAsync<T>(
        RequestSpec spec,
        Func<HttpResponseMessage, CancellationToken, Task<T>> read,
        bool readUnderTimeout,
        CancellationToken cancellationToken)
    {
        var timeout = spec.Timeout ?? _timeout;
        for (var attempt = 1; ; attempt++)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(timeout);
            HttpResponseMessage? response = null;
            try
            {
                response = await SendOnceAsync(spec, cts.Token).ConfigureAwait(false);
                var status = (int)response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    if (readUnderTimeout)
                    {
                        return await read(response, cts.Token).ConfigureAwait(false);
                    }
                    // A large download streams on the caller's token; the timeout covered the headers only.
                    cts.CancelAfter(System.Threading.Timeout.InfiniteTimeSpan);
                    return await read(response, cancellationToken).ConfigureAwait(false);
                }

                var body = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
                if (attempt < _attempts && IsRetryable(status))
                {
                    var retryAfter = status == 429 ? ParseRetryAfter(response) : 0;
                    var wait = retryAfter > 0 ? TimeSpan.FromSeconds(retryAfter) : TimeSpan.FromSeconds(Math.Pow(2, attempt));
                    response.Dispose();
                    response = null;
                    await _delay(wait, cancellationToken).ConfigureAwait(false);
                    continue;
                }
                throw BuildException(response, body);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new RequestTimeoutException($"Request timed out after {(int)timeout.TotalMilliseconds}ms", ex);
            }
            catch (HttpRequestException ex)
            {
                // No inner exception and no query string: a pre-signed download URL carries its signature there.
                throw new BillionVerifyException($"Network error: {RedactUrls(ex.Message)}", "NETWORK_ERROR", 0);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    private static readonly System.Text.RegularExpressions.Regex UrlQuery =
        new(@"(?<url>[a-zA-Z][a-zA-Z0-9+.\-]*://[^\s?#""'<>]*)[?#][^\s""'<>]*", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>Cuts the query string and fragment off every URL in a message.</summary>
    internal static string RedactUrls(string message) => UrlQuery.Replace(message, "${url}");

    private static BillionVerifyException BuildException(HttpResponseMessage response, string body)
    {
        var status = (int)response.StatusCode;
        string? message = null, code = null, details = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
                {
                    message = StringProperty(error, "message");
                    code = StringProperty(error, "code");
                    if (error.TryGetProperty("details", out var d) && d.ValueKind != JsonValueKind.Null)
                    {
                        details = d.ValueKind == JsonValueKind.String ? d.GetString() : d.GetRawText();
                    }
                    details ??= StringProperty(error, "description");
                }
                message ??= StringProperty(root, "message");
                if (code is null && root.TryGetProperty("error", out var e2) && e2.ValueKind == JsonValueKind.String)
                {
                    code = e2.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON: fall back to the status text.
        }

        message = string.IsNullOrEmpty(message) ? (response.ReasonPhrase ?? $"HTTP {status}") : message;
        var requestId = response.Headers.TryGetValues("X-Request-ID", out var ids) ? ids.FirstOrDefault() : null;

        return status switch
        {
            400 => new ValidationException(message, code, details, requestId),
            401 => new AuthenticationException(message, code, details, requestId),
            402 => new InsufficientCreditsException(message, code, details, requestId),
            404 => new NotFoundException(message, code, details, requestId),
            429 => new RateLimitException(message, ParseRetryAfter(response), code, details, requestId),
            _ => new BillionVerifyException(message, code ?? "UNKNOWN_ERROR", status, details, requestId),
        };
    }

    private static string? StringProperty(JsonElement element, string name) =>
        element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    /// <summary>Reads the <c>data</c> member of the response envelope (or the whole body when there is no envelope).</summary>
    private static async Task<T?> ReadDataAsync<T>(HttpResponseMessage response, CancellationToken token)
    {
        var text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(text))
        {
            return default;
        }
        try
        {
            using var doc = JsonDocument.Parse(text);
            var element = doc.RootElement;
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("data", out var data))
            {
                element = data;
            }
            return element.ValueKind == JsonValueKind.Null ? default : element.Deserialize<T>(JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new BillionVerifyException($"Invalid response from {response.RequestMessage?.RequestUri?.AbsolutePath}: {RedactUrls(ex.Message)}", "INVALID_RESPONSE", (int)response.StatusCode, RedactUrls(ex.Message));
        }
    }

    private async Task<T> SendJsonAsync<T>(RequestSpec spec, CancellationToken cancellationToken)
    {
        var result = await ExecuteAsync(spec, ReadDataAsync<T>, readUnderTimeout: true, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            throw new BillionVerifyException($"Empty response from {spec.Path}", "EMPTY_RESPONSE", 200);
        }
        return result;
    }

    private Task SendNoContentAsync(RequestSpec spec, CancellationToken cancellationToken) =>
        ExecuteAsync<bool>(spec, async (r, t) => { await r.Content.ReadAsStringAsync(t).ConfigureAwait(false); return true; }, readUnderTimeout: true, cancellationToken);

    private static Func<HttpContent> JsonBody(IDictionary<string, object?> fields)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(fields.Where(kv => kv.Value is not null).ToDictionary(kv => kv.Key, kv => kv.Value), JsonOptions);
        return () =>
        {
            var content = new ByteArrayContent(bytes);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            return content;
        };
    }
}
