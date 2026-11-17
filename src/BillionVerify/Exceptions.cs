namespace BillionVerify;

/// <summary>Base class for every error the SDK raises.</summary>
public class BillionVerifyException : Exception
{
    public BillionVerifyException(
        string message,
        string code = "UNKNOWN_ERROR",
        int statusCode = 0,
        string? details = null,
        string? requestId = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        StatusCode = statusCode;
        Details = details;
        RequestId = requestId;
    }

    /// <summary>The API's error code (<c>error.code</c>), or <c>UNKNOWN_ERROR</c>; <c>NETWORK_ERROR</c> / <c>TIMEOUT</c> for transport failures.</summary>
    public string Code { get; }

    /// <summary>The HTTP status code; 0 for a network error.</summary>
    public int StatusCode { get; }

    /// <summary>Extra context from the error body, when the API sent any.</summary>
    public string? Details { get; }

    /// <summary>The <c>X-Request-ID</c> response header, when present.</summary>
    public string? RequestId { get; }
}

/// <summary>HTTP 401, or an empty API key.</summary>
public sealed class AuthenticationException : BillionVerifyException
{
    public AuthenticationException(string message = "Invalid or missing API key", string? code = null, string? details = null, string? requestId = null)
        : base(message, code ?? "INVALID_API_KEY", 401, details, requestId) { }
}

/// <summary>HTTP 429. <see cref="RetryAfterSeconds"/> is the <c>Retry-After</c> header, 0 when absent.</summary>
public sealed class RateLimitException : BillionVerifyException
{
    public RateLimitException(string message = "Rate limit exceeded", int retryAfterSeconds = 0, string? code = null, string? details = null, string? requestId = null)
        : base(message, code ?? "RATE_LIMIT_EXCEEDED", 429, details, requestId)
    {
        RetryAfterSeconds = retryAfterSeconds;
    }

    public int RetryAfterSeconds { get; }
}

/// <summary>HTTP 400, or a request the SDK refused to send (batch over 50 emails, bad timeout, ...).</summary>
public sealed class ValidationException : BillionVerifyException
{
    public ValidationException(string message, string? code = null, string? details = null, string? requestId = null)
        : base(message, code ?? "INVALID_REQUEST", 400, details, requestId) { }
}

/// <summary>HTTP 402.</summary>
public sealed class InsufficientCreditsException : BillionVerifyException
{
    public InsufficientCreditsException(string message = "Insufficient credits", string? code = null, string? details = null, string? requestId = null)
        : base(message, code ?? "INSUFFICIENT_CREDITS", 402, details, requestId) { }
}

/// <summary>HTTP 404.</summary>
public sealed class NotFoundException : BillionVerifyException
{
    public NotFoundException(string message = "Resource not found", string? code = null, string? details = null, string? requestId = null)
        : base(message, code ?? "NOT_FOUND", 404, details, requestId) { }
}

/// <summary>The request, or a wait helper, ran past its time limit.</summary>
public sealed class RequestTimeoutException : BillionVerifyException
{
    public RequestTimeoutException(string message = "Request timed out", Exception? innerException = null)
        : base(message, "TIMEOUT", 504, null, null, innerException) { }
}
