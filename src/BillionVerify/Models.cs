using System.Text.Json;
using System.Text.Json.Serialization;

namespace BillionVerify;

/// <summary>Verification status values.</summary>
public static class VerificationStatus
{
    public const string Valid = "valid";
    public const string Invalid = "invalid";
    public const string Unknown = "unknown";
    public const string Risky = "risky";
    public const string Disposable = "disposable";
    public const string Catchall = "catchall";
    public const string Role = "role";
}

/// <summary>Webhook event names.</summary>
public static class WebhookEvents
{
    public const string FileCompleted = "file.completed";
    public const string FileFailed = "file.failed";
}

public sealed record DomainReputation
{
    [JsonPropertyName("mx_ip")] public string? MxIp { get; init; }
    [JsonPropertyName("is_listed")] public bool IsListed { get; init; }
    [JsonPropertyName("blacklists")] public IReadOnlyList<string>? Blacklists { get; init; }
    [JsonPropertyName("advisory")] public IReadOnlyList<string>? Advisory { get; init; }
    [JsonPropertyName("checked")] public bool Checked { get; init; }
    [JsonPropertyName("drop_listed")] public bool DropListed { get; init; }
    [JsonPropertyName("drop_cidr")] public string? DropCidr { get; init; }
    [JsonPropertyName("drop_sblid")] public string? DropSblid { get; init; }
    [JsonPropertyName("drop_attribution")] public string? DropAttribution { get; init; }
}

public sealed record EmailVerificationResult
{
    [JsonPropertyName("email")] public string Email { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("score")] public double Score { get; init; }
    [JsonPropertyName("is_deliverable")] public bool IsDeliverable { get; init; }
    [JsonPropertyName("is_disposable")] public bool IsDisposable { get; init; }
    [JsonPropertyName("is_catchall")] public bool IsCatchall { get; init; }
    [JsonPropertyName("is_role")] public bool IsRole { get; init; }
    [JsonPropertyName("is_free")] public bool IsFree { get; init; }
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("domain_age")] public int? DomainAge { get; init; }
    [JsonPropertyName("mx_records")] public IReadOnlyList<string>? MxRecords { get; init; }
    [JsonPropertyName("mx_ip")] public string? MxIp { get; init; }
    [JsonPropertyName("mx_asn")] public long? MxAsn { get; init; }
    [JsonPropertyName("mx_as_org")] public string? MxAsOrg { get; init; }
    [JsonPropertyName("domain_reputation")] public DomainReputation? DomainReputation { get; init; }
    [JsonPropertyName("check_smtp")] public bool CheckSmtp { get; init; }
    [JsonPropertyName("smtp_result")] public string? SmtpResult { get; init; }
    [JsonPropertyName("catch_all_evidence")] public string? CatchAllEvidence { get; init; }
    [JsonPropertyName("reason")] public string? Reason { get; init; }
    [JsonPropertyName("suggestion")] public string? Suggestion { get; init; }
    [JsonPropertyName("domain_suggestion")] public string? DomainSuggestion { get; init; }
    [JsonPropertyName("alias_of")] public string? AliasOf { get; init; }
    [JsonPropertyName("risk_reasons")] public IReadOnlyList<string>? RiskReasons { get; init; }
    [JsonPropertyName("smtp_response")] public string? SmtpResponse { get; init; }
    [JsonPropertyName("response_time")] public int ResponseTime { get; init; }
    [JsonPropertyName("credits_used")] public int CreditsUsed { get; init; }
    [JsonPropertyName("verification_mode")] public string? VerificationMode { get; init; }
    [JsonPropertyName("retryable")] public bool Retryable { get; init; }
    [JsonPropertyName("smtputf8")] public bool Smtputf8 { get; init; }
    [JsonPropertyName("retry_after_seconds")] public int? RetryAfterSeconds { get; init; }
}

public sealed record DeliverabilityBuckets
{
    [JsonPropertyName("strict_deliverable")] public int StrictDeliverable { get; init; }
    [JsonPropertyName("usable_with_catchall")] public int UsableWithCatchall { get; init; }
    [JsonPropertyName("not_deliverable")] public int NotDeliverable { get; init; }
    [JsonPropertyName("unknown")] public int Unknown { get; init; }
    [JsonPropertyName("anti_verified")] public int AntiVerified { get; init; }
    [JsonPropertyName("transient_failure")] public int TransientFailure { get; init; }
}

public sealed record BatchVerificationResult
{
    [JsonPropertyName("results")] public IReadOnlyList<EmailVerificationResult> Results { get; init; } = Array.Empty<EmailVerificationResult>();
    [JsonPropertyName("total_emails")] public int TotalEmails { get; init; }
    [JsonPropertyName("valid_emails")] public int ValidEmails { get; init; }
    [JsonPropertyName("invalid_emails")] public int InvalidEmails { get; init; }
    [JsonPropertyName("credits_used")] public int CreditsUsed { get; init; }
    /// <summary>Milliseconds.</summary>
    [JsonPropertyName("process_time")] public int ProcessTime { get; init; }
    [JsonPropertyName("retryable_emails")] public IReadOnlyList<string>? RetryableEmails { get; init; }
    [JsonPropertyName("deferred_emails")] public IReadOnlyList<string>? DeferredEmails { get; init; }
    [JsonPropertyName("retry_after_seconds")] public int? RetryAfterSeconds { get; init; }
    [JsonPropertyName("deliverability_buckets")] public DeliverabilityBuckets? DeliverabilityBuckets { get; init; }
}

public sealed record DisposableCheckResult
{
    [JsonPropertyName("email")] public string Email { get; init; } = "";
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("is_disposable")] public bool IsDisposable { get; init; }
    [JsonPropertyName("checked_at")] public DateTimeOffset? CheckedAt { get; init; }
}

public sealed record FileUploadResult
{
    [JsonPropertyName("task_id")] public string TaskId { get; init; } = "";
    [JsonPropertyName("file_name")] public string? FileName { get; init; }
    [JsonPropertyName("file_size")] public long FileSize { get; init; }
    /// <summary><c>pending</c>, or <c>preparing</c> for large uploads (counts then arrive through status polling).</summary>
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("message")] public string? Message { get; init; }
    [JsonPropertyName("status_url")] public string? StatusUrl { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("estimated_count")] public int EstimatedCount { get; init; }
    [JsonPropertyName("unique_emails")] public int? UniqueEmails { get; init; }
    [JsonPropertyName("total_rows")] public int? TotalRows { get; init; }
    [JsonPropertyName("email_column")] public string? EmailColumn { get; init; }
    [JsonPropertyName("email_column_candidates")] public IReadOnlyList<string>? EmailColumnCandidates { get; init; }
}

public sealed record CapacityWait
{
    [JsonPropertyName("waiting_emails")] public int WaitingEmails { get; init; }
    [JsonPropertyName("next_eligible_at")] public DateTimeOffset? NextEligibleAt { get; init; }
    [JsonPropertyName("waiting_since")] public DateTimeOffset? WaitingSince { get; init; }
}

public sealed record FileJobRuntimeHints
{
    [JsonPropertyName("has_live_execution")] public bool? HasLiveExecution { get; init; }
    [JsonPropertyName("inflight_batches")] public int? InflightBatches { get; init; }
    [JsonPropertyName("last_execution_fact_at")] public DateTimeOffset? LastExecutionFactAt { get; init; }
}

public sealed record FileJobStatus
{
    [JsonPropertyName("task_id")] public string TaskId { get; init; } = "";
    /// <summary>preparing, pending, running, processing, merging, paused, completed, completed_with_warning, failed or cancelled.</summary>
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("stage")] public string? Stage { get; init; }
    [JsonPropertyName("pending_retry_emails")] public int? PendingRetryEmails { get; init; }
    [JsonPropertyName("capacity_wait")] public CapacityWait? CapacityWait { get; init; }
    [JsonPropertyName("progress")] public int Progress { get; init; }
    [JsonPropertyName("email_progress")] public int? EmailProgress { get; init; }
    [JsonPropertyName("chunk_progress")] public int? ChunkProgress { get; init; }
    [JsonPropertyName("progress_source")] public string? ProgressSource { get; init; }
    [JsonPropertyName("total_emails")] public int TotalEmails { get; init; }
    [JsonPropertyName("processed_emails")] public int ProcessedEmails { get; init; }
    [JsonPropertyName("valid_emails")] public int ValidEmails { get; init; }
    [JsonPropertyName("invalid_emails")] public int InvalidEmails { get; init; }
    [JsonPropertyName("role_emails")] public int RoleEmails { get; init; }
    [JsonPropertyName("catchall_emails")] public int CatchallEmails { get; init; }
    [JsonPropertyName("unknown_emails")] public int UnknownEmails { get; init; }
    [JsonPropertyName("risky_emails")] public int RiskyEmails { get; init; }
    [JsonPropertyName("disposable_emails")] public int DisposableEmails { get; init; }
    [JsonPropertyName("disposable_charged_emails")] public int DisposableChargedEmails { get; init; }
    [JsonPropertyName("credits_used")] public long CreditsUsed { get; init; }
    [JsonPropertyName("cache_hit_not_charged")] public int? CacheHitNotCharged { get; init; }
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; init; }
    [JsonPropertyName("completed_at")] public DateTimeOffset? CompletedAt { get; init; }
    [JsonPropertyName("error_message")] public string? ErrorMessage { get; init; }
    [JsonPropertyName("pause_reason")] public string? PauseReason { get; init; }
    [JsonPropertyName("can_pause")] public bool? CanPause { get; init; }
    [JsonPropertyName("can_resume")] public bool? CanResume { get; init; }
    [JsonPropertyName("can_restart")] public bool? CanRestart { get; init; }
    [JsonPropertyName("restart_attempts")] public int? RestartAttempts { get; init; }
    [JsonPropertyName("current_attempt")] public int? CurrentAttempt { get; init; }
    [JsonPropertyName("download_url")] public string? DownloadUrl { get; init; }
    [JsonPropertyName("direct_download_url")] public string? DirectDownloadUrl { get; init; }
    [JsonPropertyName("direct_download_expires_at")] public DateTimeOffset? DirectDownloadExpiresAt { get; init; }
    [JsonPropertyName("result_download_available")] public bool ResultDownloadAvailable { get; init; }
    [JsonPropertyName("result_download_expires_at")] public DateTimeOffset? ResultDownloadExpiresAt { get; init; }
    [JsonPropertyName("result_download_unavailable_reason")] public string? ResultDownloadUnavailableReason { get; init; }
    [JsonPropertyName("total_chunks")] public int? TotalChunks { get; init; }
    [JsonPropertyName("completed_chunks")] public int? CompletedChunks { get; init; }
    [JsonPropertyName("failed_chunks")] public int? FailedChunks { get; init; }
    [JsonPropertyName("runtime_hints")] public FileJobRuntimeHints? RuntimeHints { get; init; }
    [JsonPropertyName("deliverability_buckets")] public DeliverabilityBuckets? DeliverabilityBuckets { get; init; }
    [JsonPropertyName("unique_emails")] public int? UniqueEmails { get; init; }
    [JsonPropertyName("total_rows")] public int? TotalRows { get; init; }
}

public sealed record CreditBalance
{
    [JsonPropertyName("account_id")] public string AccountId { get; init; } = "";
    [JsonPropertyName("api_key_id")] public string? ApiKeyId { get; init; }
    [JsonPropertyName("api_key_name")] public string? ApiKeyName { get; init; }
    [JsonPropertyName("credits_balance")] public long CreditsBalance { get; init; }
    [JsonPropertyName("credits_consumed")] public long CreditsConsumed { get; init; }
    [JsonPropertyName("credits_added")] public long CreditsAdded { get; init; }
    [JsonPropertyName("last_updated")] public DateTimeOffset? LastUpdated { get; init; }
}

public sealed record Webhook
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("events")] public IReadOnlyList<string> Events { get; init; } = Array.Empty<string>();
    /// <summary>Returned only when the webhook is created; store it, it cannot be read again.</summary>
    [JsonPropertyName("secret")] public string? Secret { get; init; }
    [JsonPropertyName("is_active")] public bool IsActive { get; init; }
    /// <summary><c>success</c> or <c>failed</c>; list responses only.</summary>
    [JsonPropertyName("last_delivery_status")] public string? LastDeliveryStatus { get; init; }
    [JsonPropertyName("last_delivery_at")] public DateTimeOffset? LastDeliveryAt { get; init; }
    [JsonPropertyName("last_error")] public string? LastError { get; init; }
    [JsonPropertyName("created_at")] public DateTimeOffset? CreatedAt { get; init; }
    [JsonPropertyName("updated_at")] public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record DomainSearchCreated
{
    [JsonPropertyName("search_id")] public string SearchId { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("credits_reserved")] public int CreditsReserved { get; init; }
    [JsonPropertyName("status_url")] public string? StatusUrl { get; init; }
}

public sealed record DomainSearch
{
    [JsonPropertyName("search_id")] public string SearchId { get; init; } = "";
    /// <summary>queued / running until it ends as completed, completed_with_warning or failed.</summary>
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("domain")] public string? Domain { get; init; }
    [JsonPropertyName("credits_charged")] public int CreditsCharged { get; init; }
    /// <summary>Free-form: <c>emails</c>, <c>crawl_report</c>, <c>warnings</c>; present once the search has finished.</summary>
    [JsonPropertyName("result")] public JsonElement? Result { get; init; }
}

public sealed record DomainSearchBatchCreated
{
    [JsonPropertyName("batch_id")] public string BatchId { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("total_rows")] public int TotalRows { get; init; }
    [JsonPropertyName("unique_domains")] public int UniqueDomains { get; init; }
    [JsonPropertyName("max_emails")] public int MaxEmails { get; init; }
    [JsonPropertyName("include_role_based")] public bool IncludeRoleBased { get; init; }
    [JsonPropertyName("max_pages")] public int? MaxPages { get; init; }
    [JsonPropertyName("status_url")] public string? StatusUrl { get; init; }
    [JsonPropertyName("results_url")] public string? ResultsUrl { get; init; }
}

public sealed record DomainSearchBatchProgress
{
    [JsonPropertyName("total_rows")] public int TotalRows { get; init; }
    [JsonPropertyName("processed")] public int Processed { get; init; }
    [JsonPropertyName("found")] public int Found { get; init; }
    [JsonPropertyName("emails_returned")] public int EmailsReturned { get; init; }
    [JsonPropertyName("unique_domains")] public int UniqueDomains { get; init; }
}

public sealed record DomainSearchBatch
{
    [JsonPropertyName("batch_id")] public string BatchId { get; init; } = "";
    /// <summary>queued, running, completed, completed_with_warning, failed or cancelled.</summary>
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("max_emails")] public int? MaxEmails { get; init; }
    [JsonPropertyName("max_pages")] public int? MaxPages { get; init; }
    [JsonPropertyName("progress")] public DomainSearchBatchProgress? Progress { get; init; }
    [JsonPropertyName("credits_charged")] public int CreditsCharged { get; init; }
}

public sealed record DomainSearchBatchPage
{
    /// <summary>Batch summaries, left as JSON because the API does not fix their shape.</summary>
    [JsonPropertyName("items")] public IReadOnlyList<JsonElement> Items { get; init; } = Array.Empty<JsonElement>();
    [JsonPropertyName("has_more")] public bool HasMore { get; init; }
    [JsonPropertyName("next_cursor")] public string? NextCursor { get; init; }
}

public sealed record HealthStatus
{
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    /// <summary>Unix timestamp.</summary>
    [JsonPropertyName("time")] public long Time { get; init; }
}

internal sealed record WebhookList
{
    [JsonPropertyName("webhooks")] public IReadOnlyList<Webhook>? Webhooks { get; init; }
    [JsonPropertyName("total")] public int Total { get; init; }
}
