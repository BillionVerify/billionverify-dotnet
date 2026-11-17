using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace BillionVerify.Tests;

public class OperationsTests
{
    private static JsonElement Body(Recorded r) => JsonDocument.Parse(r.Body).RootElement;

    // ---- verification ----

    [Fact]
    public async Task Verify_sends_only_the_options_that_were_set()
    {
        var handler = new FakeHandler(T.Ok("{\"email\":\"a@b.com\",\"status\":\"valid\"}"), T.Ok("{\"email\":\"a@b.com\",\"status\":\"valid\"}"));
        var (client, _) = T.Client(handler);
        await client.VerifyAsync("a@b.com");
        await client.VerifyAsync("a@b.com", new VerifyOptions { CheckSmtp = false, ForceRefresh = true, IncludeDomainReputation = true });

        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/v1/verify/single", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal("{\"email\":\"a@b.com\"}", handler.Requests[0].BodyText);
        var second = Body(handler.Requests[1]);
        Assert.False(second.GetProperty("check_smtp").GetBoolean());
        Assert.True(second.GetProperty("force_refresh").GetBoolean());
        Assert.True(second.GetProperty("include_domain_reputation").GetBoolean());
    }

    [Fact]
    public async Task Verify_parses_the_full_result()
    {
        var json = """
        {"email":"user@example.com","status":"risky","score":0.4,"is_deliverable":true,"is_disposable":false,"is_catchall":false,
         "is_role":false,"is_free":false,"domain":"example.com","domain_age":10,"mx_records":["mail.example.com"],"mx_asn":15169,
         "domain_reputation":{"mx_ip":"1.2.3.4","is_listed":true,"blacklists":["x"],"checked":true,"drop_listed":true,"drop_cidr":"1.2.3.0/24"},
         "check_smtp":true,"smtp_result":"deliverable","reason":"smtp_deliverable","risk_reasons":["spamhaus_dbl_spam"],
         "response_time":250,"credits_used":1,"verification_mode":"smtp","retryable":false,"retry_after_seconds":60}
        """;
        var handler = new FakeHandler(T.Ok(json));
        var (client, _) = T.Client(handler);
        var r = await client.VerifyAsync("user@example.com");
        Assert.Equal(VerificationStatus.Risky, r.Status);
        Assert.Equal(10, r.DomainAge);
        Assert.Equal(new[] { "mail.example.com" }, r.MxRecords);
        Assert.Equal(15169, r.MxAsn);
        Assert.True(r.DomainReputation!.IsListed);
        Assert.Equal("1.2.3.0/24", r.DomainReputation.DropCidr);
        Assert.Equal(new[] { "spamhaus_dbl_spam" }, r.RiskReasons);
        Assert.Equal(60, r.RetryAfterSeconds);
        Assert.Equal("smtp", r.VerificationMode);
    }

    [Fact]
    public async Task VerifyBatch_posts_emails_and_parses_buckets()
    {
        var json = """
        {"results":[{"email":"a@b.com","status":"valid","credits_used":1},{"email":"c@d.com","status":"invalid","credits_used":1}],
         "total_emails":2,"valid_emails":1,"invalid_emails":1,"credits_used":2,"process_time":1500,"retryable_emails":["c@d.com"],
         "deliverability_buckets":{"strict_deliverable":1,"usable_with_catchall":1,"not_deliverable":1,"unknown":0,"anti_verified":0,"transient_failure":0}}
        """;
        var handler = new FakeHandler(T.Ok(json));
        var (client, _) = T.Client(handler);
        var r = await client.VerifyBatchAsync(new[] { "a@b.com", "c@d.com" }, new VerifyOptions { ForceRefresh = true });
        Assert.Equal("/v1/verify/bulk", handler.Requests[0].Uri.AbsolutePath);
        var body = Body(handler.Requests[0]);
        Assert.Equal(2, body.GetProperty("emails").GetArrayLength());
        Assert.True(body.GetProperty("force_refresh").GetBoolean());
        Assert.False(body.TryGetProperty("check_smtp", out _));
        Assert.Equal(2, r.Results.Count);
        Assert.Equal(1500, r.ProcessTime);
        Assert.Equal(new[] { "c@d.com" }, r.RetryableEmails);
        Assert.Equal(1, r.DeliverabilityBuckets!.NotDeliverable);
    }

    [Fact]
    public async Task VerifyBatch_refuses_more_than_50_and_empty_without_a_request()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException("no request expected"));
        var (client, _) = T.Client(handler);
        var tooMany = Enumerable.Range(0, 51).Select(i => $"u{i}@example.com");
        var ex = await Assert.ThrowsAsync<ValidationException>(() => client.VerifyBatchAsync(tooMany));
        Assert.Contains("50", ex.Message);
        await Assert.ThrowsAsync<ValidationException>(() => client.VerifyBatchAsync(Array.Empty<string>()));
        Assert.Empty(handler.Requests);
        // exactly 50 is fine
        var ok = new FakeHandler(T.Ok("{\"results\":[],\"total_emails\":50}"));
        var (client2, _) = T.Client(ok);
        await client2.VerifyBatchAsync(Enumerable.Range(0, 50).Select(i => $"u{i}@example.com"));
        Assert.Single(ok.Requests);
    }

    [Fact]
    public async Task CheckDisposable_post_and_query_variants()
    {
        var data = "{\"email\":\"x@mailinator.com\",\"domain\":\"mailinator.com\",\"is_disposable\":true,\"checked_at\":\"2026-02-04T10:30:00Z\"}";
        var handler = new FakeHandler(T.Ok(data), T.Ok(data));
        var (client, _) = T.Client(handler);
        var post = await client.CheckDisposableAsync("x@mailinator.com");
        var get = await client.CheckDisposableViaQueryAsync("a+b@mailinator.com");
        Assert.True(post.IsDisposable);
        Assert.Equal(new DateTimeOffset(2026, 2, 4, 10, 30, 0, TimeSpan.Zero), post.CheckedAt);
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/v1/verify/disposable", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal("{\"email\":\"x@mailinator.com\"}", handler.Requests[0].BodyText);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("/v1/verify/disposable?email=a%2Bb%40mailinator.com", handler.Requests[1].PathAndQuery);
        Assert.True(get.IsDisposable);
    }

    // ---- file jobs ----

    private const string UploadResponse = "{\"task_id\":\"7143874e\",\"status\":\"pending\",\"file_name\":\"emails.csv\",\"file_size\":45678,\"estimated_count\":1000,\"unique_emails\":950,\"total_rows\":1000,\"email_column\":\"email\",\"status_url\":\"/v1/verify/file/7143874e\",\"created_at\":\"2026-02-04T10:30:00Z\"}";

    [Fact]
    public async Task UploadFile_sends_multipart_with_file_and_form_fields()
    {
        var handler = new FakeHandler(T.Ok(UploadResponse));
        var (client, _) = T.Client(handler);
        var result = await client.UploadFileAsync(Encoding.UTF8.GetBytes("email\na@b.com\n"), "emails.csv",
            new FileUploadOptions { CheckSmtp = false, EmailColumn = "email", PreserveOriginal = true });
        var req = handler.Requests[0];
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("/v1/verify/file", req.Uri.AbsolutePath);
        Assert.StartsWith("multipart/form-data", req.ContentType);
        var text = req.BodyText;
        Assert.Contains("name=file; filename=emails.csv", text);
        Assert.Contains("Content-Type: text/csv", text);
        Assert.Contains("email\na@b.com\n", text);
        Assert.Contains("name=check_smtp", text);
        Assert.Contains("name=email_column", text);
        Assert.Contains("name=preserve_original", text);
        Assert.Equal("7143874e", result.TaskId);
        Assert.Equal(45678, result.FileSize);
        Assert.Equal(950, result.UniqueEmails);
    }

    [Fact]
    public async Task UploadFile_from_a_path_and_a_stream()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bv-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "a@b.com\n");
        try
        {
            var handler = new FakeHandler(T.Ok(UploadResponse), T.Ok(UploadResponse));
            var (client, _) = T.Client(handler);
            await client.UploadFileAsync(path);
            await client.UploadFileAsync(new MemoryStream(Encoding.UTF8.GetBytes("a@b.com")), "list.xlsx");
            Assert.Contains($"filename={Path.GetFileName(path)}", handler.Requests[0].BodyText);
            Assert.Contains("Content-Type: text/plain", handler.Requests[0].BodyText);
            Assert.Contains("Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", handler.Requests[1].BodyText);
            await Assert.ThrowsAsync<ValidationException>(() => client.UploadFileAsync(path + ".missing"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task UploadFile_retry_resends_the_whole_file()
    {
        var handler = new FakeHandler(T.Error(HttpStatusCode.ServiceUnavailable, "DOWN", "down"), T.Ok(UploadResponse));
        var (client, _) = T.Client(handler);
        await client.UploadFileAsync(Encoding.UTF8.GetBytes("a@b.com"), "e.csv");
        Assert.Contains("a@b.com", handler.Requests[0].BodyText);
        Assert.Contains("a@b.com", handler.Requests[1].BodyText);
    }

    [Fact]
    public async Task GetFileJobStatus_parses_status_and_sends_the_timeout()
    {
        var json = """
        {"task_id":"j1","status":"completed_with_warning","stage":"generating_report","progress":100,"total_emails":1000,"processed_emails":1000,
         "valid_emails":850,"credits_used":970,"download_url":"https://api.billionverify.com/v1/verify/file/j1/results",
         "direct_download_expires_at":"2026-02-04T11:32:05Z","result_download_available":true,"can_resume":false,
         "runtime_hints":{"has_live_execution":true,"inflight_batches":4},"capacity_wait":{"waiting_emails":3},
         "deliverability_buckets":{"strict_deliverable":865}}
        """;
        var handler = new FakeHandler(T.Ok(json), T.Ok(json));
        var (client, _) = T.Client(handler);
        var plain = await client.GetFileJobStatusAsync("j1");
        await client.GetFileJobStatusAsync("j1", 30);
        Assert.Equal("/v1/verify/file/j1", handler.Requests[0].PathAndQuery);
        Assert.Equal("/v1/verify/file/j1?timeout=30", handler.Requests[1].PathAndQuery);
        Assert.Equal("completed_with_warning", plain.Status);
        Assert.Equal(970, plain.CreditsUsed);
        Assert.True(plain.ResultDownloadAvailable);
        Assert.Equal(4, plain.RuntimeHints!.InflightBatches);
        Assert.Equal(3, plain.CapacityWait!.WaitingEmails);
        Assert.Equal(865, plain.DeliverabilityBuckets!.StrictDeliverable);
        Assert.False(plain.CanResume);
        Assert.Null(plain.CanPause);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(301)]
    public async Task GetFileJobStatus_refuses_a_timeout_outside_0_to_300(int seconds)
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException());
        var (client, _) = T.Client(handler);
        await Assert.ThrowsAsync<ValidationException>(() => client.GetFileJobStatusAsync("j1", seconds));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Long_polling_extends_the_request_timeout()
    {
        // A 1 s client timeout would cancel a 50 ms hang; with timeout=0 it still applies, with timeout=1 it becomes 11 s.
        var handler = FakeHandler.Async(async (_, _) =>
        {
            await Task.Delay(150);
            return T.Ok("{\"task_id\":\"j1\",\"status\":\"running\"}");
        });
        var (client, _) = T.Client(handler, o => o.Timeout = TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<RequestTimeoutException>(() => client.GetFileJobStatusAsync("j1"));
        var status = await client.GetFileJobStatusAsync("j1", 1);
        Assert.Equal("running", status.Status);
    }

    [Fact]
    public async Task DownloadFileResults_filters_are_sent_as_true_only()
    {
        var handler = new FakeHandler(T.Csv("email,status\na@b.com,valid\n"), T.Csv("x"));
        var (client, _) = T.Client(handler);
        var bytes = await client.DownloadFileResultsAsync("j1", new FileResultFilters { Valid = true, Risky = true, Invalid = false });
        Assert.Equal("email,status\na@b.com,valid\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal("/v1/verify/file/j1/results?valid=true&risky=true", handler.Requests[0].PathAndQuery);
        await client.DownloadFileResultsAsync("j1");
        Assert.Equal("/v1/verify/file/j1/results", handler.Requests[1].PathAndQuery);
    }

    [Fact]
    public async Task DownloadFileResults_streams_into_a_destination()
    {
        var handler = new FakeHandler(T.Csv("a,b\n1,2\n"));
        var (client, _) = T.Client(handler);
        using var sink = new MemoryStream();
        await client.DownloadFileResultsAsync("j1", sink);
        Assert.Equal("a,b\n1,2\n", Encoding.UTF8.GetString(sink.ToArray()));
    }

    [Fact]
    public async Task DownloadFileResults_redirect_goes_to_the_storage_host_without_the_api_key()
    {
        var handler = new FakeHandler((req, _) => req.Uri.Host == "api.billionverify.com"
            ? T.Redirect("https://storage.example.com/results_j1.csv?signature=abc")
            : T.Csv("email,status\na@b.com,valid\n"));
        var (client, _) = T.Client(handler);
        var bytes = await client.DownloadFileResultsAsync("j1");

        Assert.Equal("email,status\na@b.com,valid\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("test-key", handler.Requests[0].Headers["BV-API-KEY"]);
        Assert.Equal("storage.example.com", handler.Requests[1].Uri.Host);
        Assert.Equal("/results_j1.csv?signature=abc", handler.Requests[1].PathAndQuery);
        Assert.False(handler.Requests[1].Headers.ContainsKey("BV-API-KEY"));
        Assert.False(handler.Requests[1].Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task DownloadFileResults_a_relative_redirect_on_the_api_host_keeps_the_key()
    {
        var handler = new FakeHandler((req, n) => n == 1 ? T.Redirect("/v1/verify/file/j1/final.csv") : T.Csv("ok"));
        var (client, _) = T.Client(handler);
        await client.DownloadFileResultsAsync("j1");
        Assert.Equal("test-key", handler.Requests[1].Headers["BV-API-KEY"]);
        Assert.Equal("/v1/verify/file/j1/final.csv", handler.Requests[1].Uri.AbsolutePath);
    }

    [Fact]
    public async Task DownloadFileResults_gives_up_on_a_redirect_loop()
    {
        var handler = new FakeHandler((_, _) => T.Redirect("https://storage.example.com/loop"));
        var (client, _) = T.Client(handler);
        var ex = await Assert.ThrowsAsync<BillionVerifyException>(() => client.DownloadFileResultsAsync("j1"));
        Assert.Equal(307, ex.StatusCode);
        Assert.Equal(6, handler.Requests.Count);
    }

    [Fact]
    public async Task The_owned_http_client_does_not_follow_redirects_by_itself()
    {
        // A real loopback server answers 307 to a second path on another port; the key must not reach it.
        using var storage = new LoopbackServer(_ => (200, "text/csv", "email\n"));
        using var api = new LoopbackServer(_ => (307, null, storage.Url + "file.csv"));
        using var client = new BillionVerifyClient(new BillionVerifyClientOptions { ApiKey = "secret-key", BaseUrl = api.Url });
        var bytes = await client.DownloadFileResultsAsync("j1");
        Assert.Equal("email\n", Encoding.UTF8.GetString(bytes));
        Assert.Equal("secret-key", api.Seen.Single().Headers["BV-API-KEY"]);
        Assert.DoesNotContain("BV-API-KEY", storage.Seen.Single().Headers.Keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ResumeJob_posts_and_returns_the_status()
    {
        var handler = new FakeHandler(T.Ok("{\"task_id\":\"j1\",\"status\":\"processing\",\"progress\":66,\"total_emails\":1000,\"processed_emails\":660}"));
        var (client, _) = T.Client(handler);
        var status = await client.ResumeJobAsync("j1");
        Assert.Equal(HttpMethod.Post, handler.Requests[0].Method);
        Assert.Equal("/v1/jobs/j1/resume", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal(66, status.Progress);
    }

    [Fact]
    public async Task WaitForFileJob_polls_until_a_terminal_status()
    {
        var handler = new FakeHandler(
            T.Ok("{\"task_id\":\"j1\",\"status\":\"pending\"}"),
            T.Ok("{\"task_id\":\"j1\",\"status\":\"processing\"}"),
            T.Ok("{\"task_id\":\"j1\",\"status\":\"completed\"}"));
        var (client, delays) = T.Client(handler);
        var done = await client.WaitForFileJobAsync("j1");
        Assert.Equal("completed", done.Status);
        Assert.Equal(new[] { TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5) }, delays);
    }

    [Theory]
    [InlineData("completed_with_warning")]
    [InlineData("failed")]
    [InlineData("cancelled")]
    [InlineData("paused")]
    public async Task WaitForFileJob_stops_on_each_end_state(string status)
    {
        var handler = new FakeHandler(T.Ok("{\"task_id\":\"j1\",\"status\":\"" + status + "\"}"));
        var (client, _) = T.Client(handler);
        Assert.Equal(status, (await client.WaitForFileJobAsync("j1")).Status);
    }

    [Fact]
    public async Task WaitForFileJob_times_out()
    {
        var handler = new FakeHandler((_, _) => T.Ok("{\"task_id\":\"j1\",\"status\":\"processing\"}"));
        var (client, _) = T.Client(handler);
        var ex = await Assert.ThrowsAsync<RequestTimeoutException>(() =>
            client.WaitForFileJobAsync("j1", new WaitOptions { PollInterval = TimeSpan.FromMilliseconds(1), MaxWait = TimeSpan.FromMilliseconds(30) }));
        Assert.Equal("TIMEOUT", ex.Code);
    }

    // ---- account, webhooks ----

    [Fact]
    public async Task GetCredits_parses_the_balance()
    {
        var handler = new FakeHandler(T.Ok("{\"account_id\":\"abc123\",\"api_key_id\":\"key_xyz\",\"api_key_name\":\"Default API Key\",\"credits_balance\":9500,\"credits_consumed\":500,\"credits_added\":10000,\"last_updated\":\"2026-02-04T10:30:00Z\"}"));
        var (client, _) = T.Client(handler);
        var c = await client.GetCreditsAsync();
        Assert.Equal(HttpMethod.Get, handler.Requests[0].Method);
        Assert.Equal("/v1/credits", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal(9500, c.CreditsBalance);
        Assert.Equal("Default API Key", c.ApiKeyName);
        Assert.Equal(10000, c.CreditsAdded);
    }

    [Fact]
    public async Task Webhook_create_list_delete()
    {
        var handler = new FakeHandler(
            T.Ok("{\"id\":\"w1\",\"url\":\"https://app.example/hook\",\"events\":[\"file.completed\",\"file.failed\"],\"secret\":\"s3cret\",\"is_active\":true,\"created_at\":\"2026-02-04T10:30:00Z\",\"updated_at\":\"2026-02-04T10:30:00Z\"}"),
            T.Ok("{\"webhooks\":[{\"id\":\"w1\",\"url\":\"https://app.example/hook\",\"events\":[\"file.completed\"],\"is_active\":true,\"last_delivery_status\":\"failed\",\"last_delivery_at\":\"2026-04-01T12:00:00Z\",\"last_error\":\"HTTP 500\"}],\"total\":1}"),
            T.Ok("{\"message\":\"Webhook deleted successfully\",\"webhook_id\":\"w1\"}"));
        var (client, _) = T.Client(handler);

        var created = await client.CreateWebhookAsync("https://app.example/hook", new[] { WebhookEvents.FileCompleted, WebhookEvents.FileFailed });
        Assert.Equal("s3cret", created.Secret);
        var body = Body(handler.Requests[0]);
        Assert.Equal("https://app.example/hook", body.GetProperty("url").GetString());
        Assert.Equal(new[] { "file.completed", "file.failed" }, body.GetProperty("events").EnumerateArray().Select(e => e.GetString()));

        var list = await client.ListWebhooksAsync();
        var hook = Assert.Single(list);
        Assert.Null(hook.Secret);
        Assert.Equal("failed", hook.LastDeliveryStatus);
        Assert.Equal("HTTP 500", hook.LastError);
        Assert.Equal("/v1/webhooks", handler.Requests[1].Uri.AbsolutePath);

        await client.DeleteWebhookAsync("w1");
        Assert.Equal(HttpMethod.Delete, handler.Requests[2].Method);
        Assert.Equal("/v1/webhooks/w1", handler.Requests[2].Uri.AbsolutePath);
    }

    [Fact]
    public async Task Webhook_arguments_are_checked_locally()
    {
        var handler = new FakeHandler((_, _) => throw new InvalidOperationException());
        var (client, _) = T.Client(handler);
        await Assert.ThrowsAsync<ValidationException>(() => client.CreateWebhookAsync("", new[] { "file.completed" }));
        await Assert.ThrowsAsync<ValidationException>(() => client.CreateWebhookAsync("https://a.example", Array.Empty<string>()));
        await Assert.ThrowsAsync<ValidationException>(() => client.DeleteWebhookAsync(""));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Delete_of_a_missing_webhook_is_NotFound()
    {
        var handler = new FakeHandler(T.Error(HttpStatusCode.NotFound, "WEBHOOK_NOT_FOUND", "no such webhook"));
        var (client, _) = T.Client(handler);
        await Assert.ThrowsAsync<NotFoundException>(() => client.DeleteWebhookAsync("nope"));
    }

    // ---- domain search ----

    [Fact]
    public async Task CreateDomainSearch_posts_and_checks_max_emails()
    {
        var handler = new FakeHandler(T.Json(HttpStatusCode.Accepted, "{\"success\":true,\"data\":{\"search_id\":\"s1\",\"status\":\"queued\",\"domain\":\"example.com\",\"credits_reserved\":50,\"status_url\":\"/v1/domain/search/s1\"}}"));
        var (client, _) = T.Client(handler);
        var created = await client.CreateDomainSearchAsync("example.com", 10, new DomainSearchOptions { IncludeRoleBased = false, MaxPages = 100 });
        Assert.Equal("s1", created.SearchId);
        Assert.Equal(50, created.CreditsReserved);
        var body = Body(handler.Requests[0]);
        Assert.Equal("/v1/domain/search", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal(10, body.GetProperty("max_emails").GetInt32());
        Assert.False(body.GetProperty("include_role_based").GetBoolean());
        Assert.Equal(100, body.GetProperty("max_pages").GetInt32());

        await Assert.ThrowsAsync<ValidationException>(() => client.CreateDomainSearchAsync("example.com", 0));
        await Assert.ThrowsAsync<ValidationException>(() => client.CreateDomainSearchAsync("example.com", 201));
        await Assert.ThrowsAsync<ValidationException>(() => client.CreateDomainSearchAsync("", 5));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetDomainSearch_keeps_the_result_as_json()
    {
        var handler = new FakeHandler(T.Ok("{\"search_id\":\"s1\",\"status\":\"completed\",\"domain\":\"example.com\",\"credits_charged\":10,\"result\":{\"emails\":[{\"email\":\"a@example.com\",\"name_method\":\"title\"}],\"crawl_report\":{\"pages_fetched\":12}}}"));
        var (client, _) = T.Client(handler);
        var s = await client.GetDomainSearchAsync("s1");
        Assert.Equal("/v1/domain/search/s1", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal(10, s.CreditsCharged);
        Assert.Equal(12, s.Result!.Value.GetProperty("crawl_report").GetProperty("pages_fetched").GetInt32());
        Assert.Equal("a@example.com", s.Result.Value.GetProperty("emails")[0].GetProperty("email").GetString());
    }

    [Fact]
    public async Task WaitForDomainSearch_polls_until_it_ends()
    {
        var handler = new FakeHandler(
            T.Ok("{\"search_id\":\"s1\",\"status\":\"queued\"}"),
            T.Ok("{\"search_id\":\"s1\",\"status\":\"running\"}"),
            T.Ok("{\"search_id\":\"s1\",\"status\":\"completed_with_warning\"}"));
        var (client, delays) = T.Client(handler);
        var s = await client.WaitForDomainSearchAsync("s1", new WaitOptions { PollInterval = TimeSpan.FromSeconds(2) });
        Assert.Equal("completed_with_warning", s.Status);
        Assert.Equal(2, delays.Count);
    }

    [Fact]
    public async Task CreateDomainSearchBatch_sends_csv_and_options()
    {
        var handler = new FakeHandler(T.Json(HttpStatusCode.Accepted, "{\"success\":true,\"data\":{\"batch_id\":\"b1\",\"status\":\"queued\",\"total_rows\":3,\"unique_domains\":2,\"max_emails\":50,\"include_role_based\":false,\"status_url\":\"/v1/domain/search/batch/b1\",\"results_url\":\"/v1/domain/search/batch/b1/results\"}}"));
        var (client, _) = T.Client(handler);
        var created = await client.CreateDomainSearchBatchAsync(Encoding.UTF8.GetBytes("domain\na.com\n"), "domains.csv",
            new DomainSearchBatchOptions { MaxEmails = 50, IncludeRoleBased = false, MaxPages = 20, ColumnMap = new Dictionary<string, string> { ["domain"] = "Company Website" } });
        var req = handler.Requests[0];
        Assert.Equal("/v1/domain/search/batch", req.Uri.AbsolutePath);
        Assert.StartsWith("multipart/form-data", req.ContentType);
        Assert.Contains("name=file; filename=domains.csv", req.BodyText);
        Assert.Contains("name=max_emails", req.BodyText);
        Assert.Contains("name=include_role_based", req.BodyText);
        Assert.Contains("name=max_pages", req.BodyText);
        Assert.Contains("{\"domain\":\"Company Website\"}", req.BodyText);
        Assert.Equal("b1", created.BatchId);
        Assert.Equal(2, created.UniqueDomains);
        Assert.Equal("/v1/domain/search/batch/b1/results", created.ResultsUrl);
        await Assert.ThrowsAsync<ValidationException>(() => client.CreateDomainSearchBatchAsync(new byte[] { 1 }, "d.csv", new DomainSearchBatchOptions { MaxEmails = 201 }));
    }

    [Fact]
    public async Task ColumnMapJson_wins_over_the_dictionary()
    {
        var handler = new FakeHandler(T.Ok("{\"batch_id\":\"b1\",\"status\":\"queued\"}"));
        var (client, _) = T.Client(handler);
        await client.CreateDomainSearchBatchAsync(new byte[] { 1 }, "d.csv",
            new DomainSearchBatchOptions { ColumnMapJson = "{\"domain\":\"Site\"}", ColumnMap = new Dictionary<string, string> { ["domain"] = "Other" } });
        Assert.Contains("{\"domain\":\"Site\"}", handler.Requests[0].BodyText);
        Assert.DoesNotContain("Other", handler.Requests[0].BodyText);
    }

    [Fact]
    public async Task ListDomainSearchBatches_sends_paging_and_returns_the_page()
    {
        var handler = new FakeHandler(T.Ok("{\"items\":[{\"batch_id\":\"b1\"},{\"batch_id\":\"b2\"}],\"has_more\":true,\"next_cursor\":\"c2\"}"), T.Ok("{\"items\":[],\"has_more\":false}"));
        var (client, _) = T.Client(handler);
        var page = await client.ListDomainSearchBatchesAsync(new ListDomainSearchBatchesOptions { Limit = 2, Status = "completed", Cursor = "c1" });
        Assert.Equal("/v1/domain/search/batch?limit=2&status=completed&cursor=c1", handler.Requests[0].PathAndQuery);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("b2", page.Items[1].GetProperty("batch_id").GetString());
        Assert.True(page.HasMore);
        Assert.Equal("c2", page.NextCursor);
        var empty = await client.ListDomainSearchBatchesAsync();
        Assert.Equal("/v1/domain/search/batch", handler.Requests[1].PathAndQuery);
        Assert.Empty(empty.Items);
        await Assert.ThrowsAsync<ValidationException>(() => client.ListDomainSearchBatchesAsync(new ListDomainSearchBatchesOptions { Limit = 101 }));
    }

    [Fact]
    public async Task GetDomainSearchBatch_parses_progress()
    {
        var handler = new FakeHandler(T.Ok("{\"batch_id\":\"b1\",\"status\":\"running\",\"max_emails\":200,\"progress\":{\"total_rows\":10,\"processed\":4,\"found\":3,\"emails_returned\":9,\"unique_domains\":10},\"credits_charged\":45}"));
        var (client, _) = T.Client(handler);
        var b = await client.GetDomainSearchBatchAsync("b1");
        Assert.Equal("/v1/domain/search/batch/b1", handler.Requests[0].Uri.AbsolutePath);
        Assert.Equal(4, b.Progress!.Processed);
        Assert.Equal(9, b.Progress.EmailsReturned);
        Assert.Equal(45, b.CreditsCharged);
    }

    [Fact]
    public async Task DownloadDomainSearchBatchResults_returns_csv_and_409_means_not_ready()
    {
        var handler = new FakeHandler(T.Csv("row_no,domain,email\n1,a.com,x@a.com\n"), T.Error(HttpStatusCode.Conflict, "BATCH_NOT_READY", "not ready"));
        var (client, _) = T.Client(handler);
        var csv = await client.DownloadDomainSearchBatchResultsAsync("b1");
        Assert.StartsWith("row_no,domain,email", Encoding.UTF8.GetString(csv));
        Assert.Equal("/v1/domain/search/batch/b1/results", handler.Requests[0].Uri.AbsolutePath);
        var ex = await Assert.ThrowsAsync<BillionVerifyException>(() => client.DownloadDomainSearchBatchResultsAsync("b1"));
        Assert.Equal(409, ex.StatusCode);
        Assert.Equal("BATCH_NOT_READY", ex.Code);
    }

    // ---- health ----

    [Fact]
    public async Task HealthCheck_needs_no_key_and_has_no_envelope()
    {
        var handler = new FakeHandler(T.Json(HttpStatusCode.OK, "{\"status\":\"ok\",\"time\":1705319400}"));
        var (client, _) = T.Client(handler);
        var h = await client.HealthCheckAsync();
        Assert.Equal("/health", handler.Requests[0].Uri.AbsolutePath);
        Assert.False(handler.Requests[0].Headers.ContainsKey("BV-API-KEY"));
        Assert.Equal("ok", h.Status);
        Assert.Equal(1705319400L, h.Time);
    }
}
