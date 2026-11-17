using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;

namespace BillionVerify;

public sealed partial class BillionVerifyClient
{
    public const int MaxBatchSize = 50;

    private static readonly HashSet<string> FileJobTerminalStatuses = new(StringComparer.Ordinal)
    {
        "completed", "completed_with_warning", "failed", "cancelled", "paused",
    };

    private static readonly HashSet<string> DomainSearchTerminalStatuses = new(StringComparer.Ordinal)
    {
        "completed", "completed_with_warning", "failed",
    };

    private static string Segment(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException($"{name} is required");
        }
        return Uri.EscapeDataString(value);
    }

    private static void Require(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException($"{name} is required");
        }
    }

    // ---- verification ----------------------------------------------------------------------------------------

    /// <summary>Verifies one address in real time. <c>POST /v1/verify/single</c>.</summary>
    public Task<EmailVerificationResult> VerifyAsync(string email, VerifyOptions? options = null, CancellationToken cancellationToken = default)
    {
        Require(email, nameof(email));
        var body = new Dictionary<string, object?>
        {
            ["email"] = email,
            ["check_smtp"] = options?.CheckSmtp,
            ["force_refresh"] = options?.ForceRefresh,
            ["include_domain_reputation"] = options?.IncludeDomainReputation,
        };
        return SendJsonAsync<EmailVerificationResult>(new RequestSpec(HttpMethod.Post, "/v1/verify/single", Content: JsonBody(body)), cancellationToken);
    }

    /// <summary>Verifies up to 50 addresses in one synchronous request. <c>POST /v1/verify/bulk</c>. Use <see cref="UploadFileAsync(string, FileUploadOptions?, CancellationToken)"/> for more.</summary>
    public Task<BatchVerificationResult> VerifyBatchAsync(IEnumerable<string> emails, VerifyOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(emails);
        var list = emails.ToList();
        if (list.Count == 0)
        {
            throw new ValidationException("emails must not be empty");
        }
        if (list.Count > MaxBatchSize)
        {
            throw new ValidationException($"Maximum {MaxBatchSize} emails per batch request. For larger lists, use UploadFileAsync().");
        }
        var body = new Dictionary<string, object?>
        {
            ["emails"] = list,
            ["check_smtp"] = options?.CheckSmtp,
            ["force_refresh"] = options?.ForceRefresh,
            ["include_domain_reputation"] = options?.IncludeDomainReputation,
        };
        return SendJsonAsync<BatchVerificationResult>(new RequestSpec(HttpMethod.Post, "/v1/verify/bulk", Content: JsonBody(body)), cancellationToken);
    }

    /// <summary>Free lookup against the disposable-domain table. <c>POST /v1/verify/disposable</c>.</summary>
    public Task<DisposableCheckResult> CheckDisposableAsync(string email, CancellationToken cancellationToken = default)
    {
        Require(email, nameof(email));
        var body = new Dictionary<string, object?> { ["email"] = email };
        return SendJsonAsync<DisposableCheckResult>(new RequestSpec(HttpMethod.Post, "/v1/verify/disposable", Content: JsonBody(body)), cancellationToken);
    }

    /// <summary>The same check with the address in the query string. <c>GET /v1/verify/disposable?email=</c>.</summary>
    public Task<DisposableCheckResult> CheckDisposableViaQueryAsync(string email, CancellationToken cancellationToken = default)
    {
        Require(email, nameof(email));
        var query = new[] { new KeyValuePair<string, string>("email", email) };
        return SendJsonAsync<DisposableCheckResult>(new RequestSpec(HttpMethod.Get, "/v1/verify/disposable", Query: query), cancellationToken);
    }

    // ---- file jobs -------------------------------------------------------------------------------------------

    /// <summary>Uploads a CSV, XLSX or TXT file for asynchronous verification. <c>POST /v1/verify/file</c>.</summary>
    public async Task<FileUploadResult> UploadFileAsync(string filePath, FileUploadOptions? options = null, CancellationToken cancellationToken = default)
    {
        Require(filePath, nameof(filePath));
        if (!File.Exists(filePath))
        {
            throw new ValidationException($"File not found: {filePath}");
        }
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        return await UploadFileAsync(bytes, Path.GetFileName(filePath), options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="UploadFileAsync(string, FileUploadOptions?, CancellationToken)"/>
    public async Task<FileUploadResult> UploadFileAsync(Stream file, string fileName, FileUploadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return await UploadFileAsync(buffer.ToArray(), fileName, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="UploadFileAsync(string, FileUploadOptions?, CancellationToken)"/>
    public Task<FileUploadResult> UploadFileAsync(byte[] file, string fileName, FileUploadOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        Require(fileName, nameof(fileName));
        var fields = new List<KeyValuePair<string, string>>();
        if (options?.CheckSmtp is { } smtp) fields.Add(new("check_smtp", Bool(smtp)));
        if (!string.IsNullOrEmpty(options?.EmailColumn)) fields.Add(new("email_column", options!.EmailColumn!));
        if (options?.PreserveOriginal is { } keep) fields.Add(new("preserve_original", Bool(keep)));
        return SendJsonAsync<FileUploadResult>(
            new RequestSpec(HttpMethod.Post, "/v1/verify/file", Content: () => Multipart(file, fileName, fields)), cancellationToken);
    }

    /// <summary>
    /// Status and progress of a file job. <c>GET /v1/verify/file/{job_id}</c>. <paramref name="timeoutSeconds"/> (0-300)
    /// makes the server hold the request until the job ends or the time is up.
    /// </summary>
    public Task<FileJobStatus> GetFileJobStatusAsync(string jobId, int? timeoutSeconds = null, CancellationToken cancellationToken = default)
    {
        var id = Segment(jobId, nameof(jobId));
        List<KeyValuePair<string, string>>? query = null;
        TimeSpan? requestTimeout = null;
        if (timeoutSeconds is { } t)
        {
            if (t < 0 || t > 300)
            {
                throw new ValidationException("timeout must be between 0 and 300 seconds");
            }
            if (t > 0)
            {
                query = new() { new("timeout", t.ToString(System.Globalization.CultureInfo.InvariantCulture)) };
                requestTimeout = TimeSpan.FromSeconds(t + 10);
            }
        }
        return SendJsonAsync<FileJobStatus>(new RequestSpec(HttpMethod.Get, $"/v1/verify/file/{id}", Query: query, Timeout: requestTimeout), cancellationToken);
    }

    /// <summary>
    /// Downloads a finished job's results as CSV. <c>GET /v1/verify/file/{job_id}/results</c>. Without filters the API
    /// redirects to the stored file; that redirect is followed without the API key.
    /// </summary>
    public async Task<byte[]> DownloadFileResultsAsync(string jobId, FileResultFilters? filters = null, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await DownloadFileResultsAsync(jobId, buffer, filters, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>Streams the CSV into <paramref name="destination"/> instead of holding it in memory.</summary>
    public Task DownloadFileResultsAsync(string jobId, Stream destination, FileResultFilters? filters = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var id = Segment(jobId, nameof(jobId));
        var query = new List<KeyValuePair<string, string>>();
        if (filters is not null)
        {
            if (filters.Valid) query.Add(new("valid", "true"));
            if (filters.Invalid) query.Add(new("invalid", "true"));
            if (filters.Catchall) query.Add(new("catchall", "true"));
            if (filters.Role) query.Add(new("role", "true"));
            if (filters.Unknown) query.Add(new("unknown", "true"));
            if (filters.Disposable) query.Add(new("disposable", "true"));
            if (filters.Risky) query.Add(new("risky", "true"));
        }
        return CopyBodyAsync(new RequestSpec(HttpMethod.Get, $"/v1/verify/file/{id}/results", Query: query), destination, cancellationToken);
    }

    /// <summary>Resumes a paused job. <c>POST /v1/jobs/{job_id}/resume</c>.</summary>
    public Task<FileJobStatus> ResumeJobAsync(string jobId, CancellationToken cancellationToken = default)
    {
        var id = Segment(jobId, nameof(jobId));
        return SendJsonAsync<FileJobStatus>(new RequestSpec(HttpMethod.Post, $"/v1/jobs/{id}/resume"), cancellationToken);
    }

    /// <summary>
    /// Polls a file job until it ends (completed, completed_with_warning, failed, cancelled) or stops at paused,
    /// which does not move on by itself. Throws <see cref="RequestTimeoutException"/> after <c>MaxWait</c>.
    /// </summary>
    public async Task<FileJobStatus> WaitForFileJobAsync(string jobId, WaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        var wait = options ?? new WaitOptions();
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var status = await GetFileJobStatusAsync(jobId, null, cancellationToken).ConfigureAwait(false);
            if (FileJobTerminalStatuses.Contains(status.Status))
            {
                return status;
            }
            if (clock.Elapsed + wait.PollInterval > wait.MaxWait)
            {
                throw new RequestTimeoutException($"File job {jobId} did not finish within {(long)wait.MaxWait.TotalMilliseconds}ms");
            }
            await _delay(wait.PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    // ---- account and webhooks --------------------------------------------------------------------------------

    /// <summary>Current credit balance. <c>GET /v1/credits</c>.</summary>
    public Task<CreditBalance> GetCreditsAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync<CreditBalance>(new RequestSpec(HttpMethod.Get, "/v1/credits"), cancellationToken);

    /// <summary>
    /// Creates a webhook (HTTPS URL). <c>POST /v1/webhooks</c>. The returned <see cref="Webhook.Secret"/> is shown once.
    /// </summary>
    public Task<Webhook> CreateWebhookAsync(string url, IEnumerable<string> events, CancellationToken cancellationToken = default)
    {
        Require(url, nameof(url));
        ArgumentNullException.ThrowIfNull(events);
        var list = events.ToList();
        if (list.Count == 0)
        {
            throw new ValidationException("events must not be empty");
        }
        var body = new Dictionary<string, object?> { ["url"] = url, ["events"] = list };
        return SendJsonAsync<Webhook>(new RequestSpec(HttpMethod.Post, "/v1/webhooks", Content: JsonBody(body)), cancellationToken);
    }

    /// <summary>Lists webhooks (without secrets). <c>GET /v1/webhooks</c>.</summary>
    public async Task<IReadOnlyList<Webhook>> ListWebhooksAsync(CancellationToken cancellationToken = default)
    {
        var list = await SendJsonAsync<WebhookList>(new RequestSpec(HttpMethod.Get, "/v1/webhooks"), cancellationToken).ConfigureAwait(false);
        return list.Webhooks ?? Array.Empty<Webhook>();
    }

    /// <summary>Deletes a webhook. <c>DELETE /v1/webhooks/{webhook_id}</c>.</summary>
    public Task DeleteWebhookAsync(string webhookId, CancellationToken cancellationToken = default)
    {
        var id = Segment(webhookId, nameof(webhookId));
        return SendNoContentAsync(new RequestSpec(HttpMethod.Delete, $"/v1/webhooks/{id}"), cancellationToken);
    }

    /// <inheritdoc cref="WebhookSignature.Verify(byte[], string?, string?, string, TimeSpan?, DateTimeOffset?)"/>
    public static bool VerifyWebhookSignature(byte[] rawBody, string? signatureHeader, string? timestampHeader, string secret, TimeSpan? tolerance = null) =>
        WebhookSignature.Verify(rawBody, signatureHeader, timestampHeader, secret, tolerance);

    /// <inheritdoc cref="WebhookSignature.Verify(string, string?, string?, string, TimeSpan?, DateTimeOffset?)"/>
    public static bool VerifyWebhookSignature(string rawBody, string? signatureHeader, string? timestampHeader, string secret, TimeSpan? tolerance = null) =>
        WebhookSignature.Verify(rawBody, signatureHeader, timestampHeader, secret, tolerance);

    // ---- domain search ---------------------------------------------------------------------------------------

    /// <summary>
    /// Starts an email search on a company's site (asynchronous, 5 credits per returned email, at most
    /// <paramref name="maxEmails"/>). <c>POST /v1/domain/search</c>.
    /// </summary>
    public Task<DomainSearchCreated> CreateDomainSearchAsync(string domain, int maxEmails, DomainSearchOptions? options = null, CancellationToken cancellationToken = default)
    {
        Require(domain, nameof(domain));
        if (maxEmails < 1 || maxEmails > 200)
        {
            throw new ValidationException("maxEmails must be between 1 and 200");
        }
        if (options?.MaxPages is < 1)
        {
            throw new ValidationException("maxPages must be at least 1");
        }
        var body = new Dictionary<string, object?>
        {
            ["domain"] = domain,
            ["max_emails"] = maxEmails,
            ["include_role_based"] = options?.IncludeRoleBased,
            ["max_pages"] = options?.MaxPages,
        };
        return SendJsonAsync<DomainSearchCreated>(new RequestSpec(HttpMethod.Post, "/v1/domain/search", Content: JsonBody(body)), cancellationToken);
    }

    /// <summary>Status and, once finished, the result. <c>GET /v1/domain/search/{search_id}</c>.</summary>
    public Task<DomainSearch> GetDomainSearchAsync(string searchId, CancellationToken cancellationToken = default)
    {
        var id = Segment(searchId, nameof(searchId));
        return SendJsonAsync<DomainSearch>(new RequestSpec(HttpMethod.Get, $"/v1/domain/search/{id}"), cancellationToken);
    }

    /// <summary>Polls a domain search until completed, completed_with_warning or failed.</summary>
    public async Task<DomainSearch> WaitForDomainSearchAsync(string searchId, WaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        var wait = options ?? new WaitOptions();
        var clock = Stopwatch.StartNew();
        while (true)
        {
            var search = await GetDomainSearchAsync(searchId, cancellationToken).ConfigureAwait(false);
            if (DomainSearchTerminalStatuses.Contains(search.Status))
            {
                return search;
            }
            if (clock.Elapsed + wait.PollInterval > wait.MaxWait)
            {
                throw new RequestTimeoutException($"Domain search {searchId} did not finish within {(long)wait.MaxWait.TotalMilliseconds}ms");
            }
            await _delay(wait.PollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Uploads a CSV of domains for a batch search. <c>POST /v1/domain/search/batch</c>.</summary>
    public async Task<DomainSearchBatchCreated> CreateDomainSearchBatchAsync(string filePath, DomainSearchBatchOptions? options = null, CancellationToken cancellationToken = default)
    {
        Require(filePath, nameof(filePath));
        if (!File.Exists(filePath))
        {
            throw new ValidationException($"File not found: {filePath}");
        }
        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
        return await CreateDomainSearchBatchAsync(bytes, Path.GetFileName(filePath), options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="CreateDomainSearchBatchAsync(string, DomainSearchBatchOptions?, CancellationToken)"/>
    public async Task<DomainSearchBatchCreated> CreateDomainSearchBatchAsync(Stream file, string fileName, DomainSearchBatchOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return await CreateDomainSearchBatchAsync(buffer.ToArray(), fileName, options, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc cref="CreateDomainSearchBatchAsync(string, DomainSearchBatchOptions?, CancellationToken)"/>
    public Task<DomainSearchBatchCreated> CreateDomainSearchBatchAsync(byte[] file, string fileName, DomainSearchBatchOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(file);
        Require(fileName, nameof(fileName));
        if (options?.MaxEmails is { } max && (max < 1 || max > 200))
        {
            throw new ValidationException("maxEmails must be between 1 and 200");
        }
        if (options?.MaxPages is < 1)
        {
            throw new ValidationException("maxPages must be at least 1");
        }
        var fields = new List<KeyValuePair<string, string>>();
        if (options?.MaxEmails is { } m) fields.Add(new("max_emails", m.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (options?.IncludeRoleBased is { } role) fields.Add(new("include_role_based", Bool(role)));
        if (options?.MaxPages is { } pages) fields.Add(new("max_pages", pages.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (!string.IsNullOrEmpty(options?.ColumnMapJson))
        {
            fields.Add(new("column_map", options!.ColumnMapJson!));
        }
        else if (options?.ColumnMap is { Count: > 0 } map)
        {
            fields.Add(new("column_map", JsonSerializer.Serialize(map)));
        }
        return SendJsonAsync<DomainSearchBatchCreated>(
            new RequestSpec(HttpMethod.Post, "/v1/domain/search/batch", Content: () => Multipart(file, fileName, fields)), cancellationToken);
    }

    /// <summary>One page of batch searches. <c>GET /v1/domain/search/batch</c>.</summary>
    public Task<DomainSearchBatchPage> ListDomainSearchBatchesAsync(ListDomainSearchBatchesOptions? options = null, CancellationToken cancellationToken = default)
    {
        var query = new List<KeyValuePair<string, string>>();
        if (options?.Limit is { } limit)
        {
            if (limit < 1 || limit > 100)
            {
                throw new ValidationException("limit must be between 1 and 100");
            }
            query.Add(new("limit", limit.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }
        if (!string.IsNullOrEmpty(options?.Status)) query.Add(new("status", options!.Status!));
        if (!string.IsNullOrEmpty(options?.Cursor)) query.Add(new("cursor", options!.Cursor!));
        return SendJsonAsync<DomainSearchBatchPage>(new RequestSpec(HttpMethod.Get, "/v1/domain/search/batch", Query: query), cancellationToken);
    }

    /// <summary>Status and progress of a batch search. <c>GET /v1/domain/search/batch/{batch_id}</c>.</summary>
    public Task<DomainSearchBatch> GetDomainSearchBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        var id = Segment(batchId, nameof(batchId));
        return SendJsonAsync<DomainSearchBatch>(new RequestSpec(HttpMethod.Get, $"/v1/domain/search/batch/{id}"), cancellationToken);
    }

    /// <summary>
    /// The batch results as CSV, one row per returned email. <c>GET /v1/domain/search/batch/{batch_id}/results</c>.
    /// Answers 409 (a <see cref="BillionVerifyException"/> with <c>StatusCode</c> 409) until the batch has finished.
    /// </summary>
    public async Task<byte[]> DownloadDomainSearchBatchResultsAsync(string batchId, CancellationToken cancellationToken = default)
    {
        var id = Segment(batchId, nameof(batchId));
        using var buffer = new MemoryStream();
        await CopyBodyAsync(new RequestSpec(HttpMethod.Get, $"/v1/domain/search/batch/{id}/results"), buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    // ---- health ----------------------------------------------------------------------------------------------

    /// <summary>Health check; needs no API key. <c>GET /health</c>.</summary>
    public Task<HealthStatus> HealthCheckAsync(CancellationToken cancellationToken = default) =>
        SendJsonAsync<HealthStatus>(new RequestSpec(HttpMethod.Get, "/health", Auth: false), cancellationToken);

    // ---- helpers ---------------------------------------------------------------------------------------------

    private static string Bool(bool value) => value ? "true" : "false";

    private Task<bool> CopyBodyAsync(RequestSpec spec, Stream destination, CancellationToken cancellationToken) =>
        ExecuteAsync<bool>(
            spec,
            async (response, token) =>
            {
                await response.Content.CopyToAsync(destination, token).ConfigureAwait(false);
                return true;
            },
            readUnderTimeout: false,
            cancellationToken);

    private static MultipartFormDataContent Multipart(byte[] file, string fileName, IEnumerable<KeyValuePair<string, string>> fields)
    {
        var form = new MultipartFormDataContent();
        var part = new ByteArrayContent(file);
        part.Headers.ContentType = new MediaTypeHeaderValue(ContentTypeFor(fileName));
        form.Add(part, "file", fileName);
        foreach (var field in fields)
        {
            form.Add(new StringContent(field.Value), field.Key);
        }
        return form;
    }

    private static string ContentTypeFor(string fileName) =>
        Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream",
        };
}
