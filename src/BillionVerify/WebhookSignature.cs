using System.Security.Cryptography;
using System.Text;

namespace BillionVerify;

/// <summary>Verifies the signature on a webhook delivery.</summary>
public static class WebhookSignature
{
    public const string SignatureHeader = "X-Webhook-Signature";
    public const string TimestampHeader = "X-Webhook-Timestamp";
    public static readonly TimeSpan DefaultTolerance = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Checks <c>X-Webhook-Signature</c> (<c>sha256=&lt;hex&gt;</c>), an HMAC-SHA256 with your webhook secret over
    /// <c>{X-Webhook-Timestamp}.{raw body}</c>. Pass the body exactly as received. A timestamp (Unix seconds) further
    /// from now than <paramref name="tolerance"/> is rejected; a zero tolerance turns that check off.
    /// </summary>
    public static bool Verify(
        byte[] rawBody,
        string? signatureHeader,
        string? timestampHeader,
        string secret,
        TimeSpan? tolerance = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrEmpty(signatureHeader) || string.IsNullOrEmpty(timestampHeader))
        {
            return false;
        }

        var window = tolerance ?? DefaultTolerance;
        if (window > TimeSpan.Zero)
        {
            // Digits only: a sign, a space or a fraction is refused, and so is anything past long.MaxValue.
            if (!long.TryParse(timestampHeader, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var sent))
            {
                return false;
            }
            var current = (now ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds();
            // Both are non-negative, so the larger minus the smaller cannot overflow.
            var distance = sent > current ? sent - current : current - sent;
            if (distance > (long)Math.Min(window.TotalSeconds, long.MaxValue / 2.0))
            {
                return false;
            }
        }

        var prefix = Encoding.UTF8.GetBytes(timestampHeader + ".");
        var signed = new byte[prefix.Length + rawBody.Length];
        Buffer.BlockCopy(prefix, 0, signed, 0, prefix.Length);
        Buffer.BlockCopy(rawBody, 0, signed, prefix.Length, rawBody.Length);

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var expected = Encoding.ASCII.GetBytes("sha256=" + Convert.ToHexString(hmac.ComputeHash(signed)).ToLowerInvariant());
        var given = Encoding.UTF8.GetBytes(signatureHeader.Trim().ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(expected, given);
    }

    /// <inheritdoc cref="Verify(byte[], string?, string?, string, TimeSpan?, DateTimeOffset?)"/>
    public static bool Verify(
        string rawBody,
        string? signatureHeader,
        string? timestampHeader,
        string secret,
        TimeSpan? tolerance = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(rawBody);
        return Verify(Encoding.UTF8.GetBytes(rawBody), signatureHeader, timestampHeader, secret, tolerance, now);
    }
}
