using BookingApp.Core.Common;
using BookingApp.Core.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BookingApp.Api.Controllers;

/// <summary>
/// Where the payment provider tells us what happened to a payment. Anyone can POST here, so nothing is trusted until
/// the signature over the raw body checks out; then the event is recorded once (by its id) with its change, and the
/// provider gets its answer fast. Library: 8.9 "Example 2: Receiving a payment webhook safely", 14.2 "Receiving a
/// webhook: verify, store, then work".
/// </summary>
[AllowAnonymous]
[IgnoreAntiforgeryToken]   // server to server, no cookies: the signature is the proof of origin
[Route("api/payments/webhook")]
public sealed class PaymentWebhookController(DepositService deposits, IPaymentWebhookReader reader) : ApiControllerBase
{
    /// <summary>Larger deliveries are refused before they are read.</summary>
    public const int MaxBodyBytes = 64 * 1024;

    /// <summary>Receives a signed event from the payment provider (payment.succeeded, payment.failed, refund.succeeded).</summary>
    /// <remarks>
    /// The Payment-Signature header is "t=&lt;unix seconds&gt;,v1=&lt;hex HMAC-SHA256 of t + "." + raw body&gt;" with the
    /// shared secret (Payments:Fake:WebhookSecret); deliveries older than 5 minutes are refused. The same event delivered
    /// again is answered 200 with outcome "duplicate" and changes nothing. backend/tools/payment-webhook.mjs sends one.
    /// </remarks>
    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    [EndpointName("ReceivePaymentWebhook")]
    [Consumes(Json)]
    [ProducesResponseType<WebhookAck>(StatusCodes.Status200OK, Json)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, ProblemJson, Description = "invalid_signature")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, ProblemJson, Description = "invalid_event")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, ProblemJson, Description = "try_again (deliver it again)")]
    public async Task<ActionResult<WebhookAck>> Receive(CancellationToken ct)
    {
        // The raw bytes: the signature is over exactly what was sent, never over a re-serialised copy.
        using var buffer = new MemoryStream();
        await Request.Body.CopyToAsync(buffer, ct);
        var read = reader.Read(Request.Headers[reader.SignatureHeader].ToString(), buffer.ToArray());
        if (read is not WebhookRead.Read { Event: var e, Payload: var payload })
            return read is WebhookRead.BadSignature
                ? Failure(new Error(ErrorKind.Unauthenticated, "invalid_signature", "The webhook signature is missing, wrong or too old."))
                : Failure(new Error(ErrorKind.RuleViolation, "invalid_event", "The webhook body is not an event this app can read."));

        var outcome = await deposits.HandleEventAsync(e, payload, ct);
        return outcome == WebhookOutcome.Retry
            ? Failure(new Error(ErrorKind.Unavailable, "try_again", "The payment changed at the same moment. Deliver the event again."))
            : new WebhookAck(e.Id, outcome);
    }
}
