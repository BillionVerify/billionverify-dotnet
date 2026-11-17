using System.Net;
using Xunit;

namespace BillionVerify.Tests;

public class ClientCoreTests
{
    [Fact]
    public void Version_matches_the_assembly()
    {
        var info = typeof(BillionVerifyClient).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion;
        Assert.StartsWith(BillionVerifyClient.Version, info);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_api_key_is_refused_without_a_request(string? key)
    {
        var ex = Assert.Throws<AuthenticationException>(() => new BillionVerifyClient(new BillionVerifyClientOptions { ApiKey = key }));
        Assert.Equal(401, ex.StatusCode);
    }

    [Fact]
    public async Task Sends_api_key_user_agent_and_unwraps_the_envelope()
    {
        var handler = new FakeHandler(T.Ok("{\"email\":\"a@b.com\",\"status\":\"valid\",\"score\":0.95,\"is_deliverable\":true,\"credits_used\":1,\"verification_mode\":\"smtp\",\"retryable\":false}"));
        var (client, _) = T.Client(handler);
        var result = await client.VerifyAsync("a@b.com");
        Assert.Equal("valid", result.Status);
        Assert.Equal(0.95, result.Score);
        Assert.True(result.IsDeliverable);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("test-key", request.Headers["BV-API-KEY"]);
        Assert.StartsWith("billionverify-dotnet/1.0.0", request.Headers["User-Agent"]);
        Assert.False(request.Headers.ContainsKey("EV-API-KEY"));
        Assert.Equal("application/json", request.ContentType);
    }

    [Theory]
    [InlineData("https://api.billionverify.com")]
    [InlineData("https://api.billionverify.com/")]
    [InlineData("https://api.billionverify.com/v1")]
    [InlineData("https://api.billionverify.com/v1/")]
    public async Task Base_url_with_or_without_v1_gives_the_same_request(string baseUrl)
    {
        var handler = new FakeHandler(T.Ok("{\"account_id\":\"abc\"}"));
        var (client, _) = T.Client(handler, o => o.BaseUrl = baseUrl);
        await client.GetCreditsAsync();
        Assert.Equal("https://api.billionverify.com/v1/credits", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public async Task Base_url_keeps_a_path_prefix()
    {
        var handler = new FakeHandler(T.Ok("{\"account_id\":\"abc\"}"));
        var (client, _) = T.Client(handler, o => o.BaseUrl = "http://localhost:8080/proxy/v1");
        await client.GetCreditsAsync();
        Assert.Equal("http://localhost:8080/proxy/v1/credits", handler.Requests[0].Uri.ToString());
    }

    [Fact]
    public void Bad_base_url_is_refused()
    {
        Assert.Throws<ValidationException>(() => new BillionVerifyClient(new BillionVerifyClientOptions { ApiKey = "k", BaseUrl = "not a url" }));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, typeof(ValidationException), "INVALID_REQUEST")]
    [InlineData(HttpStatusCode.Unauthorized, typeof(AuthenticationException), "INVALID_API_KEY")]
    [InlineData(HttpStatusCode.PaymentRequired, typeof(InsufficientCreditsException), "INSUFFICIENT_CREDITS")]
    [InlineData(HttpStatusCode.NotFound, typeof(NotFoundException), "JOB_NOT_FOUND")]
    [InlineData(HttpStatusCode.Conflict, typeof(BillionVerifyException), "NOT_READY")]
    public async Task Error_statuses_map_to_typed_exceptions(HttpStatusCode status, Type type, string code)
    {
        var handler = new FakeHandler(T.Error(status, code, "boom", ("X-Request-ID", "req-1")));
        var (client, _) = T.Client(handler);
        var ex = await Assert.ThrowsAnyAsync<BillionVerifyException>(() => client.GetCreditsAsync());
        Assert.Equal(type, ex.GetType());
        Assert.Equal(code, ex.Code);
        Assert.Equal((int)status, ex.StatusCode);
        Assert.Equal("boom", ex.Message);
        Assert.Equal("req-1", ex.RequestId);
    }

    [Fact]
    public async Task Error_details_and_non_json_bodies_are_handled()
    {
        var handler = new FakeHandler(
            T.Json(HttpStatusCode.BadRequest, "{\"success\":false,\"message\":\"top\",\"error\":{\"code\":\"X\",\"message\":\"inner\",\"details\":{\"field\":\"email\"}}}"),
            new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("<html>nope</html>"), ReasonPhrase = "Forbidden" });
        var (client, _) = T.Client(handler);
        var first = await Assert.ThrowsAsync<ValidationException>(() => client.GetCreditsAsync());
        Assert.Equal("inner", first.Message);
        Assert.Equal("{\"field\":\"email\"}", first.Details);
        var second = await Assert.ThrowsAsync<BillionVerifyException>(() => client.GetCreditsAsync());
        Assert.Equal("Forbidden", second.Message);
        Assert.Equal("UNKNOWN_ERROR", second.Code);
        Assert.Equal(403, second.StatusCode);
    }

    [Fact]
    public async Task Rate_limit_carries_retry_after_when_attempts_run_out()
    {
        var handler = new FakeHandler((_, _) => T.Error(HttpStatusCode.TooManyRequests, "RATE_LIMIT_EXCEEDED", "slow down", ("Retry-After", "7")));
        var (client, delays) = T.Client(handler, o => o.Retries = 2);
        var ex = await Assert.ThrowsAsync<RateLimitException>(() => client.GetCreditsAsync());
        Assert.Equal(7, ex.RetryAfterSeconds);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(7) }, delays);
    }

    [Fact]
    public async Task Retries_503_with_exponential_backoff_then_succeeds()
    {
        var handler = new FakeHandler(
            T.Error(HttpStatusCode.ServiceUnavailable, "DOWN", "down"),
            T.Error(HttpStatusCode.BadGateway, "DOWN", "down"),
            T.Ok("{\"account_id\":\"abc\",\"credits_balance\":10}"));
        var (client, delays) = T.Client(handler);
        var credits = await client.GetCreditsAsync();
        Assert.Equal(10, credits.CreditsBalance);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4) }, delays);
    }

    [Fact]
    public async Task Server_errors_are_raised_after_the_last_attempt()
    {
        var handler = new FakeHandler((_, _) => T.Error(HttpStatusCode.InternalServerError, "INTERNAL", "oops"));
        var (client, _) = T.Client(handler);
        var ex = await Assert.ThrowsAsync<BillionVerifyException>(() => client.GetCreditsAsync());
        Assert.Equal(500, ex.StatusCode);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Client_errors_are_not_retried()
    {
        var handler = new FakeHandler((_, _) => T.Error(HttpStatusCode.BadRequest, "INVALID_REQUEST", "bad"));
        var (client, delays) = T.Client(handler);
        await Assert.ThrowsAsync<ValidationException>(() => client.GetCreditsAsync());
        Assert.Single(handler.Requests);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Retried_requests_resend_the_same_body()
    {
        var handler = new FakeHandler(T.Error(HttpStatusCode.ServiceUnavailable, "DOWN", "down"), T.Ok("{\"email\":\"a@b.com\",\"status\":\"valid\"}"));
        var (client, _) = T.Client(handler);
        await client.VerifyAsync("a@b.com");
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(handler.Requests[0].BodyText, handler.Requests[1].BodyText);
        Assert.Equal("{\"email\":\"a@b.com\"}", handler.Requests[1].BodyText);
    }

    [Fact]
    public async Task Network_failure_is_wrapped_and_not_retried()
    {
        var (client, delays) = T.Client(new ThrowingHandler());
        var ex = await Assert.ThrowsAsync<BillionVerifyException>(() => client.GetCreditsAsync());
        Assert.Equal("NETWORK_ERROR", ex.Code);
        Assert.Equal(0, ex.StatusCode);
        Assert.Empty(delays);
    }

    [Fact]
    public async Task Timeout_raises_RequestTimeoutException()
    {
        var (client, _) = T.Client(new HangingHandler(), o => o.Timeout = TimeSpan.FromMilliseconds(50));
        var ex = await Assert.ThrowsAsync<RequestTimeoutException>(() => client.GetCreditsAsync());
        Assert.Equal("TIMEOUT", ex.Code);
        Assert.Equal(504, ex.StatusCode);
    }

    [Fact]
    public async Task Caller_cancellation_is_not_a_timeout()
    {
        var (client, _) = T.Client(new HangingHandler());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetCreditsAsync(cts.Token));
    }

    [Fact]
    public async Task Malformed_json_in_a_success_response_is_INVALID_RESPONSE()
    {
        var handler = new FakeHandler(T.Json(HttpStatusCode.OK, "{not json"));
        var (client, _) = T.Client(handler);
        var ex = await Assert.ThrowsAsync<BillionVerifyException>(() => client.GetCreditsAsync());
        Assert.Equal("INVALID_RESPONSE", ex.Code);
    }

    [Fact]
    public async Task Unknown_fields_in_responses_are_ignored()
    {
        var handler = new FakeHandler(T.Ok("{\"account_id\":\"abc\",\"brand_new_field\":{\"a\":1}}"));
        var (client, _) = T.Client(handler);
        Assert.Equal("abc", (await client.GetCreditsAsync()).AccountId);
    }

    [Fact]
    public async Task Path_segments_are_escaped_and_blank_ids_refused()
    {
        var handler = new FakeHandler(T.Ok("{\"task_id\":\"a/b\",\"status\":\"pending\"}"));
        var (client, _) = T.Client(handler);
        await client.GetFileJobStatusAsync("a/b c");
        Assert.EndsWith("/v1/verify/file/a%2Fb%20c", handler.Requests[0].Uri.AbsoluteUri);
        await Assert.ThrowsAsync<ValidationException>(() => client.GetFileJobStatusAsync(" "));
        Assert.Single(handler.Requests);
    }

    private sealed class FailingHandler : HttpMessageHandler
    {
        private readonly Exception _error;
        public FailingHandler(Exception error) => _error = error;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw _error;
    }

    [Fact]
    public async Task Network_errors_do_not_leak_the_query_string_of_a_presigned_url()
    {
        var url = "https://storage.example.com/results_j1.csv?X-Amz-Signature=TOPSECRET&X-Amz-Expires=900";
        var inner = new System.Net.Sockets.SocketException(111);
        var (client, _) = T.Client(new FailingHandler(new HttpRequestException($"The SSL connection to {url} could not be established, see {url}#frag.", inner)));
        var ex = await Assert.ThrowsAsync<BillionVerifyException>(() => client.GetCreditsAsync());
        Assert.Equal("NETWORK_ERROR", ex.Code);
        Assert.DoesNotContain("TOPSECRET", ex.ToString());
        Assert.DoesNotContain("X-Amz", ex.ToString());
        Assert.DoesNotContain("frag", ex.Message);
        Assert.Contains("https://storage.example.com/results_j1.csv", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public async Task A_real_connection_failure_on_a_redirected_download_names_no_query()
    {
        // The API answers with a redirect to a closed port; the signature in the query must not reach the message.
        using var api = new LoopbackServer(_ => (307, null, "http://127.0.0.1:1/file.csv?signature=TOPSECRET"));
        using var client = new BillionVerifyClient(new BillionVerifyClientOptions { ApiKey = "k", BaseUrl = api.Url });
        var ex = await Assert.ThrowsAsync<BillionVerifyException>(() => client.DownloadFileResultsAsync("j1"));
        Assert.Equal("NETWORK_ERROR", ex.Code);
        Assert.DoesNotContain("TOPSECRET", ex.ToString());
    }

    [Theory]
    [InlineData("failed for https://h.example/p?a=1&b=2 now", "failed for https://h.example/p now")]
    [InlineData("a http://x/y#z b", "a http://x/y b")]
    [InlineData("no url here", "no url here")]
    public void RedactUrls_cuts_query_and_fragment(string input, string expected)
    {
        Assert.Equal(expected, BillionVerifyClient.RedactUrls(input));
    }

    [Fact]
    public async Task Invalid_response_messages_carry_no_query_either()
    {
        var handler = new FakeHandler(T.Json(HttpStatusCode.OK, "{not json"));
        var (client, _) = T.Client(handler);
        var ex = await Assert.ThrowsAsync<BillionVerifyException>(() => client.GetFileJobStatusAsync("j1", 5));
        Assert.DoesNotContain("timeout=", ex.Message);
        Assert.Null(ex.InnerException);
    }
}
