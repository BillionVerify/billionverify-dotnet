using System.Text;
using Xunit;

namespace BillionVerify.Tests;

/// <summary>The caller's own handler must not be able to carry the API key to the storage host.</summary>
public class InjectedHandlerRedirectTests
{
    private sealed class PassThrough : DelegatingHandler
    {
        public PassThrough(HttpMessageHandler inner) : base(inner) { }
    }

    private static async Task AssertKeyStaysAtTheApi(HttpMessageHandler handler)
    {
        using var storage = new LoopbackServer(_ => (200, "text/csv", "email,status\na@b.com,valid\n"));
        using var api = new LoopbackServer(_ => (307, null, storage.Url + "results.csv?signature=abc"));
        using var client = new BillionVerifyClient(new BillionVerifyClientOptions { ApiKey = "secret-key", BaseUrl = api.Url, HttpMessageHandler = handler });

        var bytes = await client.DownloadFileResultsAsync("j1");

        Assert.Equal("email,status\na@b.com,valid\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal("secret-key", api.Seen.Single().Headers["BV-API-KEY"]);
        var stored = storage.Seen.Single();
        Assert.Equal("/results.csv?signature=abc", stored.Path);
        Assert.DoesNotContain("BV-API-KEY", stored.Headers.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public Task An_HttpClientHandler_that_follows_redirects_is_switched_off() =>
        AssertKeyStaysAtTheApi(new HttpClientHandler { AllowAutoRedirect = true });

    [Fact]
    public Task A_SocketsHttpHandler_that_follows_redirects_is_switched_off() =>
        AssertKeyStaysAtTheApi(new SocketsHttpHandler { AllowAutoRedirect = true });

    [Fact]
    public Task A_handler_buried_under_delegating_handlers_is_switched_off() =>
        AssertKeyStaysAtTheApi(new PassThrough(new PassThrough(new HttpClientHandler { AllowAutoRedirect = true })));

    [Fact]
    public void The_switch_is_made_on_the_callers_handler()
    {
        var inner = new HttpClientHandler { AllowAutoRedirect = true };
        using var client = new BillionVerifyClient(new BillionVerifyClientOptions { ApiKey = "k", HttpMessageHandler = new PassThrough(inner) });
        Assert.False(inner.AllowAutoRedirect);
    }

    [Fact]
    public async Task A_handler_that_has_already_sent_requests_and_follows_redirects_is_refused()
    {
        using var server = new LoopbackServer(_ => (200, "text/plain", "ok"));
        var used = new HttpClientHandler { AllowAutoRedirect = true };
        using (var plain = new HttpClient(used, disposeHandler: false))
        {
            await plain.GetStringAsync(server.Url);
        }
        var ex = Assert.Throws<ValidationException>(() =>
            new BillionVerifyClient(new BillionVerifyClientOptions { ApiKey = "k", HttpMessageHandler = used }));
        Assert.Contains("AllowAutoRedirect", ex.Message);


        // A used handler that already has redirects off is fine.
        var off = new HttpClientHandler { AllowAutoRedirect = false };
        using (var plain = new HttpClient(off, disposeHandler: false))
        {
            await plain.GetStringAsync(server.Url);
        }
        using var ok = new BillionVerifyClient(new BillionVerifyClientOptions { ApiKey = "k", HttpMessageHandler = off });
    }

    [Fact]
    public async Task The_callers_handler_is_not_disposed_with_the_client()
    {
        var handler = new FakeHandler(T.Ok("{\"account_id\":\"a\"}"), T.Ok("{\"account_id\":\"b\"}"));
        var client = new BillionVerifyClient(new BillionVerifyClientOptions { ApiKey = "k", HttpMessageHandler = handler });
        await client.GetCreditsAsync();
        client.Dispose();
        using var again = new BillionVerifyClient(new BillionVerifyClientOptions { ApiKey = "k", HttpMessageHandler = handler });
        Assert.Equal("b", (await again.GetCreditsAsync()).AccountId);
    }
}
