using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace BillionVerify.Tests;

public class WebhookSignatureTests
{
    private const string Secret = "5c609e6893ab3b65ce8940549a030bc165762fe171e0b38f880bd570099619bf";
    private const string Body = "{\"event\":\"file.completed\",\"data\":{\"job_id\":\"j1\"}}";

    private static string Sign(string timestamp, string body, string secret = Secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(timestamp + "." + body))).ToLowerInvariant();
    }

    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static string Ts(int secondsAgo = 0) => Now.AddSeconds(-secondsAgo).ToUnixTimeSeconds().ToString();

    [Fact]
    public void A_correct_signature_verifies_for_string_and_bytes()
    {
        var ts = Ts();
        var sig = Sign(ts, Body);
        Assert.True(WebhookSignature.Verify(Body, sig, ts, Secret, now: Now));
        Assert.True(WebhookSignature.Verify(Encoding.UTF8.GetBytes(Body), sig, ts, Secret, now: Now));
    }

    [Fact]
    public void A_changed_body_secret_or_timestamp_fails()
    {
        var ts = Ts();
        var sig = Sign(ts, Body);
        Assert.False(WebhookSignature.Verify(Body + " ", sig, ts, Secret, now: Now));
        Assert.False(WebhookSignature.Verify(Body, sig, ts, "other-secret", now: Now));
        Assert.False(WebhookSignature.Verify(Body, sig, Ts(1), Secret, now: Now));
    }

    [Fact]
    public void Signing_the_body_alone_is_not_accepted()
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var bodyOnly = "sha256=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(Body))).ToLowerInvariant();
        Assert.False(WebhookSignature.Verify(Body, bodyOnly, Ts(), Secret, now: Now));
    }

    [Fact]
    public void A_stale_or_future_timestamp_is_refused()
    {
        var old = Ts(301);
        Assert.False(WebhookSignature.Verify(Body, Sign(old, Body), old, Secret, now: Now));
        var future = Now.AddSeconds(400).ToUnixTimeSeconds().ToString();
        Assert.False(WebhookSignature.Verify(Body, Sign(future, Body), future, Secret, now: Now));
        var edge = Ts(299);
        Assert.True(WebhookSignature.Verify(Body, Sign(edge, Body), edge, Secret, now: Now));
    }

    [Fact]
    public void Tolerance_can_be_widened_or_turned_off()
    {
        var old = Ts(3600);
        var sig = Sign(old, Body);
        Assert.False(WebhookSignature.Verify(Body, sig, old, Secret, now: Now));
        Assert.True(WebhookSignature.Verify(Body, sig, old, Secret, TimeSpan.FromHours(2), Now));
        Assert.True(WebhookSignature.Verify(Body, sig, old, Secret, TimeSpan.Zero, Now));
    }

    [Theory]
    [InlineData(null, "1")]
    [InlineData("", "1")]
    [InlineData("sha256=abc", null)]
    [InlineData("sha256=abc", "")]
    [InlineData("sha256=abc", "not-a-number")]
    [InlineData("abc", "1")]
    public void Missing_or_malformed_headers_fail_without_throwing(string? sig, string? ts)
    {
        Assert.False(WebhookSignature.Verify(Body, sig, ts, Secret, now: Now));
    }

    [Fact]
    public void An_empty_secret_never_verifies()
    {
        var ts = Ts();
        Assert.False(WebhookSignature.Verify(Body, Sign(ts, Body, ""), ts, "", now: Now));
    }

    [Fact]
    public void The_client_exposes_the_same_check()
    {
        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        Assert.True(BillionVerifyClient.VerifyWebhookSignature(Body, Sign(ts, Body), ts, Secret));
        Assert.False(BillionVerifyClient.VerifyWebhookSignature(Body, "sha256=00", ts, Secret));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("\t\n")]
    public void A_blank_secret_never_verifies(string secret)
    {
        var ts = Ts();
        Assert.False(WebhookSignature.Verify(Body, Sign(ts, Body, secret), ts, secret, now: Now));
        Assert.False(WebhookSignature.Verify(Body, Sign(ts, Body, secret), ts, secret, TimeSpan.Zero, Now));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("-9223372036854775808")]
    [InlineData("+5")]
    [InlineData(" 5")]
    [InlineData("1.5")]
    [InlineData("99999999999999999999999999")]
    [InlineData("9223372036854775807")]
    public void Negative_signed_or_huge_timestamps_are_refused_without_throwing(string ts)
    {
        Assert.False(WebhookSignature.Verify(Body, Sign(ts, Body), ts, Secret, now: Now));
    }

    [Fact]
    public void A_huge_tolerance_does_not_overflow()
    {
        var ts = Ts(10);
        Assert.True(WebhookSignature.Verify(Body, Sign(ts, Body), ts, Secret, TimeSpan.MaxValue, Now));
        var far = long.MaxValue.ToString();
        Assert.False(WebhookSignature.Verify(Body, Sign(far, Body), far, Secret, TimeSpan.FromSeconds(300), Now));
    }
}
