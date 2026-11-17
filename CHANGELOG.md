# Changelog

## 1.0.0

First release. A client for every operation of the BillionVerify API:

- Verification: `VerifyAsync`, `VerifyBatchAsync` (up to 50 addresses), `CheckDisposableAsync`, `CheckDisposableViaQueryAsync`.
- File jobs: `UploadFileAsync`, `GetFileJobStatusAsync` (with long polling), `DownloadFileResultsAsync` (with filters), `ResumeJobAsync`, `WaitForFileJobAsync`.
- Account and webhooks: `GetCreditsAsync`, `CreateWebhookAsync`, `ListWebhooksAsync`, `DeleteWebhookAsync`, `WebhookSignature.Verify`.
- Domain search: `CreateDomainSearchAsync`, `GetDomainSearchAsync`, `WaitForDomainSearchAsync`, `CreateDomainSearchBatchAsync`, `ListDomainSearchBatchesAsync`, `GetDomainSearchBatchAsync`, `DownloadDomainSearchBatchResultsAsync`.
- `HealthCheckAsync`.
- Typed errors, retries on 429 / 500 / 502 / 503 with `Retry-After`, and result downloads that follow the storage redirect without sending the API key.
