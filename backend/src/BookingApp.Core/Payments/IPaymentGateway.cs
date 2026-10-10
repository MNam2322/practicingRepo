namespace BookingApp.Core.Payments;

/// <summary>
/// The port in front of a payment provider. An adapter translates its provider's dialect (amount formats, error codes,
/// exceptions) into these results, and nothing provider-shaped gets past it. Library: 4.9 Case 4 (Adapter), 4.2
/// "A seam for the payment provider".
/// <para>
/// Every call carries our payment id, and an adapter must send it as the provider's idempotency key: a retry after a
/// timeout must never move money twice. That is what makes every retry below safe.
/// </para>
/// </summary>
public interface IPaymentGateway
{
    /// <summary>Takes <paramref name="amount"/> with the customer's payment token (from the provider's card form).</summary>
    Task<ChargeResult> ChargeAsync(Guid paymentId, Money amount, string paymentToken, CancellationToken cancellationToken);

    /// <summary>Gives a charge back in full.</summary>
    Task<RefundResult> RefundAsync(Guid paymentId, string providerReference, Money amount, CancellationToken cancellationToken);
}

/// <summary>What a charge came to: a closed set, so callers handle every case.</summary>
public abstract record ChargeResult
{
    private ChargeResult() { }

    /// <summary>The money was taken.</summary>
    public sealed record Approved(string ProviderReference) : ChargeResult;

    /// <summary>The provider refused, for a reason the customer can act on (another card). Not worth retrying as is.</summary>
    /// <param name="Reason">Our code: "card_declined", "insufficient_funds", "expired_card", "invalid_token".</param>
    public sealed record Declined(string Reason) : ChargeResult;

    /// <summary>
    /// No clear answer (the provider is down, or timed out). The money may or may not have been taken: retry with the
    /// same payment id, or wait for the provider's webhook.
    /// </summary>
    public sealed record Unavailable(string ProviderCode) : ChargeResult;
}

/// <summary>What a refund came to.</summary>
public abstract record RefundResult
{
    private RefundResult() { }

    public sealed record Refunded(string ProviderReference) : RefundResult;

    /// <summary>Not refunded this time. Safe to retry later with the same payment id.</summary>
    public sealed record Failed(string ProviderCode) : RefundResult;
}
