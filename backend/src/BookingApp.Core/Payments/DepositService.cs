using BookingApp.Core.Bookings;
using BookingApp.Core.Common;

namespace BookingApp.Core.Payments;

/// <summary>What a webhook delivery came to.</summary>
public enum WebhookOutcome
{
    /// <summary>New, and it changed a payment.</summary>
    Applied,
    /// <summary>New, and nothing needed to change (already known, unknown payment, or an event type we do not use).</summary>
    Ignored,
    /// <summary>Delivered before. Nothing changed this time.</summary>
    Duplicate,
    /// <summary>The payment changed at the same moment; nothing was saved. The provider should deliver it again.</summary>
    Retry,
}

/// <summary>
/// Deposits: confirming a hold takes one, and a booking that no longer happens gives it back. Refunds follow the
/// cancellation rules: whenever the rules let a booking be cancelled (by the customer before the cut-off, by staff at
/// any time), or a paid hold expires, the deposit is refunded in full. A customer cannot cancel after the cut-off, so
/// that deposit is kept, as it is for a booking that took place or a no-show.
/// <para>
/// Money moves only through <see cref="IPaymentGateway"/>, always with the payment's id as the idempotency key, so a
/// retry of anything here never charges or refunds twice.
/// </para>
/// </summary>
public sealed class DepositService(
    IBookingRepository bookings, IPaymentRepository payments, IPaymentGateway gateway, BookingService bookingService,
    PaymentOptions options, TimeProvider time)
{
    /// <summary>The deposit a booking takes now; zero when deposits are off.</summary>
    public Money Deposit => options.DepositAmount;

    /// <summary>
    /// Confirms the customer's hold, taking the deposit first. Safe to repeat: a paid deposit is not taken again, and a
    /// payment whose outcome is unknown is retried under the same id. A declined card leaves the hold as it is, so the
    /// customer can try another card until it expires.
    /// </summary>
    public async Task<Result<Booking>> ConfirmAsync(CustomerId customer, Guid bookingId, string? paymentToken, CancellationToken cancellationToken)
    {
        if (Deposit.IsZero) return await bookingService.ConfirmAsync(customer, bookingId, cancellationToken);

        var booking = await bookings.FindAsync(bookingId, cancellationToken);
        if (booking is null) return BookingNotFound;
        // Check before taking money: an expired or foreign hold must not be charged at all.
        if (booking.CheckConfirm(customer, time.GetUtcNow()) is { } refused) return refused;
        if (booking.Status == BookingStatus.Confirmed) return Result<Booking>.Ok(booking);

        var existing = await payments.ForBookingsAsync([bookingId], cancellationToken);
        var payment = existing.FirstOrDefault(p => p.Status == PaymentStatus.Paid) ?? existing.FirstOrDefault(p => p.Status == PaymentStatus.Pending);
        if (payment?.Status != PaymentStatus.Paid)
        {
            if (string.IsNullOrWhiteSpace(paymentToken))
                return new Error(ErrorKind.RuleViolation, "payment_required", $"Confirming takes a deposit of {Deposit}. Send a payment token.");
            if (payment is null)
            {
                payment = Payment.Start(bookingId, Deposit, time.GetUtcNow());
                if (!await payments.TryAddAsync(payment, cancellationToken))
                {
                    // Another confirm got there first. If it has paid, go on and confirm; if it is still paying, wait.
                    var winner = (await payments.ForBookingsAsync([bookingId], cancellationToken)).FirstOrDefault(p => p.Status == PaymentStatus.Paid);
                    if (winner is null)
                        return new Error(ErrorKind.Conflict, "payment_in_progress", "This hold is being paid for already. Wait a moment, then reload.");
                    payment = winner;
                }
            }
            if (payment.Status == PaymentStatus.Pending && await ChargeAsync(payment, paymentToken.Trim(), cancellationToken) is { } failed)
                return failed;
        }

        var confirmed = await bookingService.ConfirmAsync(customer, bookingId, cancellationToken);
        // The hold ran out while the card was being charged: the money goes straight back.
        if (!confirmed.IsSuccess) await SettleAsync(bookingId, cancellationToken);
        return confirmed;
    }

    /// <summary>
    /// After a booking was cancelled (or a paid hold expired): refunds its deposit if the rules say so. Called right after
    /// the cancel; the refund job calls it again for anything that did not go through (the provider was down).
    /// Returns how many deposits were refunded.
    /// </summary>
    public async Task<int> SettleAsync(Guid bookingId, CancellationToken cancellationToken)
    {
        var booking = await bookings.FindAsync(bookingId, cancellationToken);
        if (booking is null || !RefundDue(booking, time.GetUtcNow())) return 0;
        var refunded = 0;
        foreach (var payment in (await payments.ForBookingsAsync([bookingId], cancellationToken)).Where(p => p.Status == PaymentStatus.Paid))
            if (await RefundAsync(payment, cancellationToken)) refunded++;
        return refunded;
    }

    /// <summary>The refund job: gives back every paid deposit whose booking no longer happens. Returns how many.</summary>
    public async Task<int> RefundDueAsync(CancellationToken cancellationToken)
    {
        var refunded = 0;
        foreach (var payment in await payments.AwaitingRefundAsync(time.GetUtcNow(), 50, cancellationToken))
            if (await RefundAsync(payment, cancellationToken)) refunded++;
        return refunded;
    }

    /// <summary>
    /// Applies a webhook event from the provider, once: the event id is recorded with the change in one transaction, so a
    /// second delivery finds it and changes nothing. A charge that succeeds late confirms its hold if the hold is still
    /// live; otherwise the refund job gives the money back. No money moves here, so the provider gets its answer fast.
    /// </summary>
    public async Task<WebhookOutcome> HandleEventAsync(PaymentEvent e, string payload, CancellationToken cancellationToken)
    {
        if (await payments.EventSeenAsync(e.Id, cancellationToken)) return WebhookOutcome.Duplicate;
        var now = time.GetUtcNow();
        var payment = await payments.FindAsync(e.PaymentId, cancellationToken);
        var changed = payment is not null && e.Type switch
        {
            PaymentEventType.PaymentSucceeded => payment.MarkPaid(e.ProviderReference ?? payment.ProviderReference ?? "", now),
            PaymentEventType.PaymentFailed => payment.MarkFailed(e.Code ?? "payment_failed"),
            PaymentEventType.RefundSucceeded => payment.MarkRefunded(now),
            _ => false,
        };
        var saved = await payments.RecordEventAsync(new ReceivedPaymentEvent(e.Id, e.Type, e.PaymentId, now, payload), changed ? payment : null, cancellationToken);
        if (saved == EventSave.Duplicate) return WebhookOutcome.Duplicate;
        if (saved == EventSave.Conflict) return WebhookOutcome.Retry;
        if (!changed) return WebhookOutcome.Ignored;

        if (e.Type == PaymentEventType.PaymentSucceeded)
        {
            var booking = await bookings.FindAsync(payment!.BookingId, cancellationToken);
            if (booking is not null && booking.CheckConfirm(booking.Customer, now) is null && booking.Status == BookingStatus.Pending)
                await bookingService.ConfirmAsync(booking.Customer, booking.Id, cancellationToken);
        }
        return WebhookOutcome.Applied;
    }

    /// <summary>Every payment for these bookings, for showing the deposit next to each booking.</summary>
    public async Task<IReadOnlyDictionary<Guid, Payment>> CurrentForAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken cancellationToken)
    {
        if (bookingIds.Count == 0) return new Dictionary<Guid, Payment>();
        return (await payments.ForBookingsAsync(bookingIds, cancellationToken))
            .GroupBy(p => p.BookingId)
            .ToDictionary(g => g.Key, g => Current(g)!);
    }

    /// <summary>The payment that matters for a booking: money held or returned, then an attempt still running, then a refusal.</summary>
    public static Payment? Current(IEnumerable<Payment> payments) =>
        payments.OrderBy(p => p.Status switch
        {
            PaymentStatus.Paid => 0,
            PaymentStatus.Refunded => 1,
            PaymentStatus.Pending => 2,
            _ => 3,
        }).ThenByDescending(p => p.CreatedAt).FirstOrDefault();

    /// <summary>
    /// Refunds follow the cancellation rules: a booking cancelled under them, or a hold that ran out, gets its deposit
    /// back. Bookings that happened, no-shows and bookings still on are not refunded.
    /// </summary>
    public static bool RefundDue(Booking booking, DateTimeOffset now) => booking.StatusAt(now) is BookingStatus.Cancelled or BookingStatus.Expired;

    /// <summary>Charges a pending payment. Null when the money was taken; otherwise the error to show.</summary>
    private async Task<Error?> ChargeAsync(Payment payment, string paymentToken, CancellationToken cancellationToken)
    {
        var result = await gateway.ChargeAsync(payment.Id, payment.Amount, paymentToken, cancellationToken);
        switch (result)
        {
            case ChargeResult.Approved approved:
                payment.MarkPaid(approved.ProviderReference, time.GetUtcNow());
                await SaveAsync(payment, cancellationToken);
                return null;
            case ChargeResult.Declined declined:
                payment.MarkFailed(declined.Reason);
                await SaveAsync(payment, cancellationToken);
                return new Error(ErrorKind.PaymentDeclined, "payment_declined", DeclineMessage(declined.Reason));
            default:
                // Unknown outcome: the payment stays pending. A retry reuses its id, so the provider cannot charge twice.
                return new Error(ErrorKind.Unavailable, "payment_unavailable",
                    "The payment provider did not answer. Try again in a moment: you will not be charged twice.");
        }
    }

    private async Task<bool> RefundAsync(Payment payment, CancellationToken cancellationToken)
    {
        var result = await gateway.RefundAsync(payment.Id, payment.ProviderReference ?? "", payment.Amount, cancellationToken);
        if (result is not RefundResult.Refunded) return false;   // the refund job tries again later
        payment.MarkRefunded(time.GetUtcNow());
        await SaveAsync(payment, cancellationToken);
        return true;
    }

    /// <summary>
    /// Saves what the provider said. If a webhook saved the same payment at the same moment, the stored state already
    /// says what happened (the provider tells both paths the same thing), so losing the race is fine.
    /// </summary>
    private async Task SaveAsync(Payment payment, CancellationToken cancellationToken) => await payments.UpdateAsync(payment, cancellationToken);

    private static string DeclineMessage(string reason) => reason switch
    {
        "insufficient_funds" => "The card was declined: not enough funds. Try another card.",
        "expired_card" => "The card has expired. Try another card.",
        "invalid_token" => "The card details could not be read. Enter them again.",
        _ => "The card was declined. Try another card.",
    };

    private static readonly Error BookingNotFound = new(ErrorKind.NotFound, "booking_not_found", "There is no such booking.");
}
