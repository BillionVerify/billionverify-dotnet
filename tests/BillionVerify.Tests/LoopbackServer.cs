using System.Net;
using System.Text;

namespace BillionVerify.Tests;

/// <summary>A tiny real HTTP server on a free loopback port, for the one test that must exercise the real HttpClient.</summary>
public sealed class LoopbackServer : IDisposable
{
    public sealed record Hit(string Path, Dictionary<string, string> Headers);

    private readonly HttpListener _listener = new();
    private readonly Func<string, (int Status, string? ContentType, string Body)> _respond;
    private readonly CancellationTokenSource _stop = new();
    public List<Hit> Seen { get; } = new();
    public string Url { get; }

    public LoopbackServer(Func<string, (int, string?, string)> respond)
    {
        _respond = respond;
        for (var attempt = 0; ; attempt++)
        {
            var port = Random.Shared.Next(20000, 60000);
            var prefix = $"http://127.0.0.1:{port}/";
            try
            {
                _listener.Prefixes.Add(prefix);
                _listener.Start();
                Url = prefix;
                break;
            }
            catch (HttpListenerException) when (attempt < 20)
            {
                _listener.Prefixes.Clear();
            }
        }
        _ = Task.Run(Loop);
    }

    private async Task Loop()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try { ctx = await _listener.GetContextAsync(); }
            catch { return; }
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in ctx.Request.Headers.AllKeys) if (key is not null) headers[key] = ctx.Request.Headers[key] ?? "";
            Seen.Add(new Hit(ctx.Request.RawUrl ?? "", headers));
            var (status, contentType, body) = _respond(ctx.Request.RawUrl ?? "");
            ctx.Response.StatusCode = status;
            if (status is >= 300 and < 400)
            {
                ctx.Response.RedirectLocation = body;
            }
            else
            {
                var bytes = Encoding.UTF8.GetBytes(body);
                if (contentType is not null) ctx.Response.ContentType = contentType;
                ctx.Response.ContentLength64 = bytes.Length;
                await ctx.Response.OutputStream.WriteAsync(bytes);
            }
            ctx.Response.Close();
        }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
    }
}
