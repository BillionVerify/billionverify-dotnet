# BillionVerify for .NET

The official .NET client for the [BillionVerify](https://billionverify.com) email verification API: verify one address or up to 50 at a time, run verification jobs over whole files, check disposable domains for free, find the emails on a company's site, and manage webhooks.

- .NET 8 and .NET 9, no dependencies beyond the framework (`System.Net.Http`, `System.Text.Json`).
- Every operation of the API, `async` with `CancellationToken`.
- Typed exceptions, retries with `Retry-After`, and a webhook signature check.

## Install

```bash
dotnet add package BillionVerify
```

## Quick start

```csharp
using BillionVerify;

using var client = new BillionVerifyClient(Environment.GetEnvironmentVariable("BILLIONVERIFY_API_KEY")!);

var result = await client.VerifyAsync("user@example.com");
Console.WriteLine($"{result.Email}: {result.Status} (score {result.Score}, {result.CreditsUsed} credit)");
```

`status` is one of `valid`, `invalid`, `unknown`, `risky`, `disposable`, `catchall`, `role` (constants in `VerificationStatus`). `unknown` is not billed and may succeed on a retry (`result.Retryable`).

Reuse one client for the life of the application; it is thread-safe.

## Options

```csharp
var client = new BillionVerifyClient(new BillionVerifyClientOptions
{
    ApiKey = "your_api_key",
    BaseUrl = "https://api.billionverify.com", // a trailing /v1 is accepted
    Timeout = TimeSpan.FromSeconds(30),
    Retries = 3,                                // total attempts, first try included
});
```

Requests that get 429, 500, 502 or 503 are retried: the wait is the `Retry-After` header when there is one, otherwise 2, 4, 8 … seconds. Network errors and timeouts are not retried.

With ASP.NET Core you can pass an `HttpClient` from `IHttpClientFactory` in `BillionVerifyClientOptions.HttpClient`. Turn automatic redirects off on its handler (`AllowAutoRedirect = false`): result downloads are redirected to a storage host, and the SDK follows that redirect itself so the API key is not sent there.

## Verify

```csharp
// One address, with options (anything you leave unset uses the server default)
var one = await client.VerifyAsync("user@example.com", new VerifyOptions
{
    CheckSmtp = true,
    ForceRefresh = false,
    IncludeDomainReputation = true,
});

// Up to 50 addresses in one call
var batch = await client.VerifyBatchAsync(new[] { "a@example.com", "b@example.com" });
foreach (var r in batch.Results) Console.WriteLine($"{r.Email} {r.Status}");
Console.WriteLine(batch.DeliverabilityBuckets?.StrictDeliverable);

// Free disposable-domain lookup
var disposable = await client.CheckDisposableAsync("someone@mailinator.com");
var sameThing  = await client.CheckDisposableViaQueryAsync("someone@mailinator.com");
```

More than 50 addresses throws `ValidationException` before any request is sent; use a file job for larger lists.

## File jobs

```csharp
var upload = await client.UploadFileAsync("emails.csv", new FileUploadOptions { EmailColumn = "email" });

// Wait for the end (completed, completed_with_warning, failed, cancelled, or paused)
var job = await client.WaitForFileJobAsync(upload.TaskId);
Console.WriteLine($"{job.Status}: {job.ValidEmails} valid of {job.TotalEmails}");

// Or poll yourself; timeoutSeconds (0-300) holds the request open until the job ends
var status = await client.GetFileJobStatusAsync(upload.TaskId, timeoutSeconds: 30);

// Download the results as CSV, optionally only some statuses
await using var file = File.Create("valid.csv");
await client.DownloadFileResultsAsync(upload.TaskId, file, new FileResultFilters { Valid = true, Role = true });

// Resume a paused job
await client.ResumeJobAsync(upload.TaskId);
```

`UploadFileAsync` also takes a `Stream` or a `byte[]` with a file name.

## Domain search

Give a domain, get the emails published on its site, each one verified (5 credits per returned email, at most `maxEmails`).

```csharp
var started = await client.CreateDomainSearchAsync("example.com", maxEmails: 20,
    new DomainSearchOptions { IncludeRoleBased = false });
var search = await client.WaitForDomainSearchAsync(started.SearchId);
Console.WriteLine(search.Result);   // JsonElement: emails, crawl_report, warnings

// A CSV of domains
var batch = await client.CreateDomainSearchBatchAsync("domains.csv", new DomainSearchBatchOptions { MaxEmails = 50 });
var progress = await client.GetDomainSearchBatchAsync(batch.BatchId);
var page = await client.ListDomainSearchBatchesAsync(new ListDomainSearchBatchesOptions { Limit = 20 });
var csv = await client.DownloadDomainSearchBatchResultsAsync(batch.BatchId); // 409 until the batch has finished
```

## Credits, health

```csharp
var credits = await client.GetCreditsAsync();
Console.WriteLine(credits.CreditsBalance);

var health = await client.HealthCheckAsync(); // needs no API key
```

## Webhooks

```csharp
var hook = await client.CreateWebhookAsync("https://your-app.example/webhooks/billionverify",
    new[] { WebhookEvents.FileCompleted, WebhookEvents.FileFailed });
// hook.Secret is shown once; store it.

var hooks = await client.ListWebhooksAsync();
await client.DeleteWebhookAsync(hook.Id);
```

Check the signature of every delivery against the raw request body. The signature covers `{X-Webhook-Timestamp}.{raw body}`, and timestamps more than five minutes old are refused:

```csharp
// ASP.NET Core minimal API
app.MapPost("/webhooks/billionverify", async (HttpRequest request) =>
{
    using var buffer = new MemoryStream();
    await request.Body.CopyToAsync(buffer);

    var valid = WebhookSignature.Verify(
        buffer.ToArray(),
        request.Headers["X-Webhook-Signature"],
        request.Headers["X-Webhook-Timestamp"],
        webhookSecret);

    return valid ? Results.Ok() : Results.Unauthorized();
});
```

The `tolerance` argument changes the window (`TimeSpan.Zero` turns the time check off).

## Errors

Every error is a `BillionVerifyException` with `Code`, `StatusCode`, `Details` and `RequestId`:

| Exception | When |
| --- | --- |
| `ValidationException` | 400, or a request the SDK refused to send (more than 50 emails, a timeout outside 0-300, ...) |
| `AuthenticationException` | 401, or an empty API key |
| `InsufficientCreditsException` | 402 |
| `NotFoundException` | 404 |
| `RateLimitException` | 429 once the attempts are used up; `RetryAfterSeconds` holds the header |
| `RequestTimeoutException` | the request, or a wait helper, ran out of time |
| `BillionVerifyException` | anything else (409, 5xx, `NETWORK_ERROR`, `INVALID_RESPONSE`) |

```csharp
try
{
    await client.VerifyAsync("user@example.com");
}
catch (InsufficientCreditsException)
{
    // top up
}
catch (RateLimitException e)
{
    await Task.Delay(TimeSpan.FromSeconds(e.RetryAfterSeconds));
}
catch (BillionVerifyException e)
{
    Console.WriteLine($"{e.StatusCode} {e.Code}: {e.Message} (request {e.RequestId})");
}
```

## Development

```bash
dotnet test tests/BillionVerify.Tests
dotnet pack src/BillionVerify -c Release -o artifacts
```

The tests use an in-process fake of the network; they never call the real API.

## Releasing

Releases are published from GitHub Actions to nuget.org with [Trusted Publishing](https://learn.microsoft.com/nuget/nuget-org/trusted-publishing), so no API key is stored in the repository.

One-time setup on nuget.org (owner of the package): *Trusted Publishing* → add a policy for repository owner `BillionVerify`, repository `billionverify-dotnet`, workflow file `release.yml`.

For each release:

1. Set `<Version>` in `src/BillionVerify/BillionVerify.csproj` and `BillionVerifyClient.Version` to the new number (a test fails when they differ), and add a section to `CHANGELOG.md`.
2. Commit, then tag and push: `git tag v1.0.1 && git push origin main v1.0.1`.
3. The `Release` workflow checks that the tag equals the version, runs the tests, packs, trades its GitHub identity for a short-lived key with `NuGet/login@v1`, and pushes the `.nupkg` and `.snupkg`.

## License

MIT
