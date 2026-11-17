namespace BillionVerify;

/// <summary>Settings for <see cref="BillionVerifyClient"/>.</summary>
public sealed class BillionVerifyClientOptions
{
    /// <summary>Your API key. Required.</summary>
    public string? ApiKey { get; set; }

    /// <summary>API base URL. A trailing <c>/v1</c> is accepted and ignored.</summary>
    public string BaseUrl { get; set; } = BillionVerifyClient.DefaultBaseUrl;

    /// <summary>Per-request timeout. Default 30 seconds.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Total attempts for 429, 500, 502 and 503 responses (first try included). Default 3.</summary>
    public int Retries { get; set; } = 3;

    /// <summary>
    /// Your own <see cref="HttpClient"/>. The SDK then does not dispose it. It must not follow redirects
    /// automatically (<c>AllowAutoRedirect = false</c>), or the API key header would travel to the storage host
    /// when a result download is redirected; its own <c>Timeout</c> should be at least as long as <see cref="Timeout"/>.
    /// </summary>
    public HttpClient? HttpClient { get; set; }

    /// <summary>How retries and polling wait. Default <see cref="Task.Delay(TimeSpan, CancellationToken)"/>; replace it in tests.</summary>
    public Func<TimeSpan, CancellationToken, Task>? Delay { get; set; }
}

public sealed record VerifyOptions
{
    /// <summary>SMTP check override; unset means the server default.</summary>
    public bool? CheckSmtp { get; init; }
    public bool? ForceRefresh { get; init; }
    public bool? IncludeDomainReputation { get; init; }
}

public sealed record FileUploadOptions
{
    public bool? CheckSmtp { get; init; }
    /// <summary>Column that holds the addresses; detected when unset.</summary>
    public string? EmailColumn { get; init; }
    public bool? PreserveOriginal { get; init; }
}

/// <summary>Result filters; a filter set to true is sent, and several are combined with OR.</summary>
public sealed record FileResultFilters
{
    public bool Valid { get; init; }
    public bool Invalid { get; init; }
    public bool Catchall { get; init; }
    public bool Role { get; init; }
    public bool Unknown { get; init; }
    public bool Disposable { get; init; }
    public bool Risky { get; init; }
}

public sealed record DomainSearchOptions
{
    public bool? IncludeRoleBased { get; init; }
    /// <summary>Sitemap-wide crawl truncated at this many pages.</summary>
    public int? MaxPages { get; init; }
}

public sealed record DomainSearchBatchOptions
{
    /// <summary>1-200, server default 200.</summary>
    public int? MaxEmails { get; init; }
    public bool? IncludeRoleBased { get; init; }
    public int? MaxPages { get; init; }
    /// <summary>Header mapping, for example <c>{"domain": "Company Website"}</c>; sent as JSON.</summary>
    public IReadOnlyDictionary<string, string>? ColumnMap { get; init; }
    /// <summary>The same mapping as a ready-made JSON string; wins over <see cref="ColumnMap"/>.</summary>
    public string? ColumnMapJson { get; init; }
}

public sealed record ListDomainSearchBatchesOptions
{
    /// <summary>1-100, server default 20.</summary>
    public int? Limit { get; init; }
    /// <summary>queued, running, completed, completed_with_warning, failed or cancelled.</summary>
    public string? Status { get; init; }
    public string? Cursor { get; init; }
}

public sealed record WaitOptions
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan MaxWait { get; init; } = TimeSpan.FromSeconds(600);
}
