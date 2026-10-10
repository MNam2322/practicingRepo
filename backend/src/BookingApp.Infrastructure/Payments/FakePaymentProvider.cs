using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BookingApp.Core.Payments;
using Microsoft.Extensions.Options;

namespace BookingApp.Infrastructure.Payments;

/// <summary>The "Payments:Fake" settings: the webhook signing secret shared with the (fake) provider.</summary>
public sealed class FakePaymentOptions
{
    public const string Section = "Payments:Fake";

    /// <summary>Secret for webhook signatures. Set it per environment; never commit a real one.</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>How old a signed delivery may be before it is refused as a possible replay.</summary>
    public int WebhookToleranceSeconds { get; set; } = 300;
}

/// <summary>
/// A payment provider in memory, for development and tests: no network, no real money, and deterministic by token.
/// Like a real provider it is idempotent per payment id: the same id charged twice is charged once and answered the
/// same way (library 4.2 "A seam for the payment provider"; tests drive it through the properties below).
/// <list type="bullet">
/// <item><c>tok_visa</c> (or any other <c>tok_…</c>): approved</item>
/// <item><c>tok_declined</c>, <c>tok_insufficient_funds</c>, <c>tok_expired_card</c>: declined</item>
/// <item><c>tok_unavailable</c>: the provider is down; nothing is charged</item>
/// <item><c>tok_timeout</c>: charged, but the answer is lost; a retry (same id) or the webhook tells the truth</item>
/// <item>anything not starting with <c>tok_</c>: declined, invalid_token</item>
/// </list>
/// </summary>
public sealed class FakePaymentGateway : IPaymentGateway
{
    private readonly ConcurrentDictionary<Guid, (Money Amount, string Reference)> _charges = new();
    private readonly ConcurrentDictionary<Guid, string> _refunds = new();

    /// <summary>Money actually taken, per payment id. A retried charge does not add a second entry.</summary>
    public IReadOnlyDictionary<Guid, (Money Amount, string Reference)> Charges => _charges;

    /// <summary>Refunds actually made, per payment id.</summary>
    public IReadOnlyDictionary<Guid, string> Refunds => _refunds;

    /// <summary>Tests: while true, refunds fail as if the provider were down.</summary>
    public bool RefundsUnavailable { get; set; }

    /// <summary>Tests: runs inside every charge call, such as moving the clock to make a hold expire mid-payment.</summary>
    public Action? OnCharge { get; set; }

    public Task<ChargeResult> ChargeAsync(Guid paymentId, Money amount, string paymentToken, CancellationToken cancellationToken)
    {
        OnCharge?.Invoke();
        if (_charges.TryGetValue(paymentId, out var earlier))
            return Task.FromResult<ChargeResult>(new ChargeResult.Approved(earlier.Reference));   // idempotent: same id, same answer
        if (paymentToken == "tok_timeout")
        {
            Take(paymentId, amount);   // the money is taken, but the answer never arrives
            return Task.FromResult<ChargeResult>(new ChargeResult.Unavailable("timeout"));
        }

        ChargeResult result = paymentToken switch
        {
            "tok_declined" => new ChargeResult.Declined("card_declined"),
            "tok_insufficient_funds" => new ChargeResult.Declined("insufficient_funds"),
            "tok_expired_card" => new ChargeResult.Declined("expired_card"),
            "tok_unavailable" => new ChargeResult.Unavailable("provider_unavailable"),
            _ when paymentToken.StartsWith("tok_", StringComparison.Ordinal) => new ChargeResult.Approved(Take(paymentId, amount)),
            _ => new ChargeResult.Declined("invalid_token"),
        };
        return Task.FromResult(result);
    }

    public Task<RefundResult> RefundAsync(Guid paymentId, string providerReference, Money amount, CancellationToken cancellationToken)
    {
        if (RefundsUnavailable) return Task.FromResult<RefundResult>(new RefundResult.Failed("provider_unavailable"));
        if (!_charges.TryGetValue(paymentId, out var charge) || charge.Reference != providerReference || charge.Amount != amount)
            return Task.FromResult<RefundResult>(new RefundResult.Failed("no_such_charge"));
        var reference = _refunds.GetOrAdd(paymentId, id => $"fake_re_{id:N}");   // idempotent
        return Task.FromResult<RefundResult>(new RefundResult.Refunded(reference));
    }

    private string Take(Guid paymentId, Money amount) => _charges.GetOrAdd(paymentId, id => (amount, $"fake_ch_{id:N}")).Reference;
}

/// <summary>
/// The fake provider's webhooks. Each delivery has <c>Payment-Signature: t=&lt;unix seconds&gt;,v1=&lt;hex&gt;</c>, where the
/// hex is HMAC-SHA256 over <c>"&lt;t&gt;." + raw body</c> with the shared secret (the scheme most providers use).
/// The body is <c>{ "id": "evt_…", "type": "payment.succeeded" | "payment.failed" | "refund.succeeded", "paymentId": "…",
/// "providerReference": "…", "code": "…" }</c>.
/// Verified over the raw bytes, compared in constant time, refused when older than the tolerance; parsed only after
/// (library 8.9 "Verifying webhooks", 14.2 "Receiving a webhook: verify, store, then work").
/// </summary>
public sealed class FakePaymentWebhookReader(IOptions<FakePaymentOptions> options, TimeProvider time) : IPaymentWebhookReader
{
    public const string Header = "Payment-Signature";

    public string SignatureHeader => Header;

    public WebhookRead Read(string? signature, byte[] body)
    {
        if (!Verify(signature, body)) return new WebhookRead.BadSignature();
        var payload = Encoding.UTF8.GetString(body);
        try
        {
            var json = JsonSerializer.Deserialize<EventJson>(payload, JsonOptions);
            if (json is null || string.IsNullOrWhiteSpace(json.Id) || json.Id.Length > 100 || json.Type is null || json.PaymentId is not { } paymentId)
                return new WebhookRead.Unreadable();
            var type = json.Type switch
            {
                "payment.succeeded" => PaymentEventType.PaymentSucceeded,
                "payment.failed" => PaymentEventType.PaymentFailed,
                "refund.succeeded" => PaymentEventType.RefundSucceeded,
                _ => PaymentEventType.Other,
            };
            return new WebhookRead.Read(new PaymentEvent(json.Id, type, paymentId, json.ProviderReference, json.Code), payload);
        }
        catch (JsonException)
        {
            return new WebhookRead.Unreadable();
        }
    }

    /// <summary>The header value the fake provider sends for <paramref name="body"/> at <paramref name="timestamp"/>.</summary>
    public static string Sign(string secret, long timestamp, byte[] body) =>
        $"t={timestamp.ToString(CultureInfo.InvariantCulture)},v1={Convert.ToHexStringLower(Mac(secret, timestamp, body))}";

    private bool Verify(string? signature, byte[] body)
    {
        var secret = options.Value.WebhookSecret;
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature)) return false;
        long? timestamp = null;
        byte[]? given = null;
        foreach (var part in signature.Split(','))
        {
            var kv = part.Trim().Split('=', 2);
            if (kv.Length != 2) continue;
            if (kv[0] == "t" && long.TryParse(kv[1], NumberStyles.None, CultureInfo.InvariantCulture, out var t)) timestamp = t;
            else if (kv[0] == "v1" && kv[1].Length == 64)
            {
                try { given = Convert.FromHexString(kv[1]); }
                catch (FormatException) { return false; }
            }
        }
        if (timestamp is null || given is null) return false;
        var age = Math.Abs(time.GetUtcNow().ToUnixTimeSeconds() - timestamp.Value);
        if (age > options.Value.WebhookToleranceSeconds) return false;   // a replay of a captured delivery
        return CryptographicOperations.FixedTimeEquals(Mac(secret, timestamp.Value, body), given);
    }

    private static byte[] Mac(string secret, long timestamp, byte[] body)
    {
        var prefix = Encoding.UTF8.GetBytes($"{timestamp.ToString(CultureInfo.InvariantCulture)}.");
        var signed = new byte[prefix.Length + body.Length];
        prefix.CopyTo(signed, 0);
        body.CopyTo(signed, prefix.Length);
        return HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), signed);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record EventJson(string? Id, string? Type, Guid? PaymentId, string? ProviderReference, string? Code);
}
