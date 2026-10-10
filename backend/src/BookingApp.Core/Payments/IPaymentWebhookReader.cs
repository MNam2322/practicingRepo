namespace BookingApp.Core.Payments;

/// <summary>
/// The provider's side of webhooks, behind a port like the gateway: the adapter knows the provider's signature scheme
/// and event format and turns a delivery into a <see cref="PaymentEvent"/>, or says why it cannot. It verifies the
/// signature over the raw bytes before parsing anything (library 8.9 "Verifying webhooks", 14.2 "Receiving a webhook:
/// verify, store, then work").
/// </summary>
public interface IPaymentWebhookReader
{
    /// <summary>The HTTP header that carries the provider's signature.</summary>
    string SignatureHeader { get; }

    WebhookRead Read(string? signature, byte[] body);
}

/// <summary>The outcome of reading one webhook delivery.</summary>
public abstract record WebhookRead
{
    private WebhookRead() { }

    /// <summary>Missing, wrong or too old a signature: not from the provider, or a replay. Nothing was parsed.</summary>
    public sealed record BadSignature : WebhookRead;

    /// <summary>Signed, but not an event this app can read.</summary>
    public sealed record Unreadable : WebhookRead;

    public sealed record Read(PaymentEvent Event, string Payload) : WebhookRead;
}

public enum PaymentEventType
{
    /// <summary>A charge succeeded (perhaps after we were told it timed out).</summary>
    PaymentSucceeded,
    /// <summary>A charge failed for good.</summary>
    PaymentFailed,
    /// <summary>A charge was refunded (perhaps by staff in the provider's dashboard).</summary>
    RefundSucceeded,
    /// <summary>Anything else the provider sends. Recorded and acknowledged, nothing else.</summary>
    Other,
}

/// <param name="Id">The provider's event id. The same event delivered twice has the same id.</param>
/// <param name="PaymentId">Our payment id, which the provider echoes back.</param>
public sealed record PaymentEvent(string Id, PaymentEventType Type, Guid PaymentId, string? ProviderReference, string? Code);
