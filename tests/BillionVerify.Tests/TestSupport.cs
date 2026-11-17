using System.Net;
using System.Text;

namespace BillionVerify.Tests;

public sealed record Recorded(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, byte[] Body, string? ContentType)
{
    public string BodyText => Encoding.UTF8.GetString(Body);
    public string PathAndQuery => Uri.PathAndQuery;
}

/// <summary>An in-process stand-in for the network: records each request and answers from a script.</summary>
public sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<Recorded, int, Task<HttpResponseMessage>> _respond;
    public List<Recorded> Requests { get; } = new();

    public FakeHandler(Func<Recorded, int, HttpResponseMessage> respond) => _respond = (r, n) => Task.FromResult(respond(r, n));

    private FakeHandler(Func<Recorded, int, Task<HttpResponseMessage>> respond) => _respond = respond;

    public static FakeHandler Async(Func<Recorded, int, Task<HttpResponseMessage>> respond) => new(respond);

    public FakeHandler(params HttpResponseMessage[] queue)
    {
        var items = new Queue<HttpResponseMessage>(queue);
        _respond = (_, _) => Task.FromResult(items.Dequeue());
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in request.Headers) headers[h.Key] = string.Join(",", h.Value);
        byte[] body = Array.Empty<byte>();
        string? contentType = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
            contentType = request.Content.Headers.ContentType?.ToString();
        }
        var rec = new Recorded(request.Method, request.RequestUri!, headers, body, contentType);
        Requests.Add(rec);
        return await _respond(rec, Requests.Count);
    }
}

public sealed class HangingHandler : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        throw new InvalidOperationException();
    }
}

public sealed class ThrowingHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        throw new HttpRequestException("connection refused");
}

public static class T
{
    public static HttpResponseMessage Json(HttpStatusCode status, string json, params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers) response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    public static HttpResponseMessage Ok(string dataJson) =>
        Json(HttpStatusCode.OK, "{\"success\":true,\"code\":\"0\",\"message\":\"Success\",\"data\":" + dataJson + "}");

    public static HttpResponseMessage Error(HttpStatusCode status, string code, string message, params (string Name, string Value)[] headers) =>
        Json(status, "{\"success\":false,\"code\":\"" + (int)status + "\",\"message\":\"" + message + "\",\"error\":{\"code\":\"" + code + "\",\"message\":\"" + message + "\"}}", headers);

    public static HttpResponseMessage Csv(string text) =>
        new(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "text/csv") };

    public static HttpResponseMessage Redirect(string location, HttpStatusCode status = HttpStatusCode.TemporaryRedirect)
    {
        var r = new HttpResponseMessage(status);
        r.Headers.Location = new Uri(location, UriKind.RelativeOrAbsolute);
        return r;
    }

    /// <summary>A client whose waits are recorded, not slept.</summary>
    public static (BillionVerifyClient Client, List<TimeSpan> Delays) Client(HttpMessageHandler handler, Action<BillionVerifyClientOptions>? configure = null)
    {
        var delays = new List<TimeSpan>();
        var options = new BillionVerifyClientOptions
        {
            ApiKey = "test-key",
            HttpMessageHandler = handler,
            Delay = (span, _) => { delays.Add(span); return Task.CompletedTask; },
        };
        configure?.Invoke(options);
        return (new BillionVerifyClient(options), delays);
    }
}
