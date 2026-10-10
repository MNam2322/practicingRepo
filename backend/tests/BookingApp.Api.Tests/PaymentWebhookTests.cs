using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BookingApp.Core.Bookings;
using BookingApp.Core.Payments;
using BookingApp.Infrastructure.Payments;
using BookingApp.Infrastructure.Persistence;

namespace BookingApp.Api.Tests;

/// <summary>
/// The payment provider's webhooks: verified before anything is read, applied once per event id, acknowledged fast.
/// Library: 8.9 "Example 2: Receiving a payment webhook safely", 14.2 "Receiving a webhook: verify, store, then work".
/// </summary>
public abstract class PaymentWebhookTests(StorageProvider provider) : ApiTest(provider)
{
    private static string Event(string id, string type, Guid paymentId, string? providerReference = null) =>
        JsonSerializer.Serialize(new { id, type, paymentId, providerReference });

    private Task<HttpResponseMessage> Deliver(string body, string? signature = null, long? timestamp = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/payments/webhook") { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add(FakePaymentWebhookReader.Header,
            signature ?? FakePaymentWebhookReader.Sign(ApiFactory.WebhookSecret, timestamp ?? Factory.Time.GetUtcNow().ToUnixTimeSeconds(), bytes));
        return Factory.CreateClient().SendAsync(request, Ct);
    }

    private async Task<WebhookAck> DeliverOk(string body)
    {
        var response = await Deliver(body);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WebhookAck>(Json, Ct))!;
    }

    private async Task<BookingResponse> Mine(Guid id) =>
        (await Client.GetFromJsonAsync<BookingResponse>($"/api/bookings/{id}", Json, Ct))!;

    /// <summary>A hold whose deposit was charged but whose answer was lost: only the webhook can tell.</summary>
    private async Task<BookingResponse> TimedOutHold()
    {
        var hold = await HoldOk(10);
        await AssertProblem(await Confirm(hold.Id, paymentToken: "tok_timeout"), 503, "payment_unavailable");
        return await Mine(hold.Id);
    }

    [Fact]
    public async Task Payment_succeeded_confirms_the_hold_and_the_same_event_again_changes_nothing()
    {
        var hold = await TimedOutHold();
        var paymentId = hold.Deposit!.PaymentId!.Value;
        var body = Event("evt_1", "payment.succeeded", paymentId, Factory.Payments.Charges[paymentId].Reference);

        Assert.Equal(WebhookOutcome.Applied, (await DeliverOk(body)).Outcome);
        var booking = await Mine(hold.Id);
        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(DepositStatus.Paid, booking.Deposit!.Status);

        // The provider delivers it again (it does, at least once): acknowledged, nothing changes.
        Assert.Equal(WebhookOutcome.Duplicate, (await DeliverOk(body)).Outcome);
        // Even a different body under the same event id is the same event: ignored.
        Assert.Equal(WebhookOutcome.Duplicate, (await DeliverOk(Event("evt_1", "payment.failed", paymentId))).Outcome);
        Assert.Equal(booking, await Mine(hold.Id));
    }

    [Fact]
    public async Task Payment_failed_settles_a_payment_whose_outcome_was_unknown()
    {
        var hold = await HoldOk(10);
        await AssertProblem(await Confirm(hold.Id, paymentToken: "tok_unavailable"), 503, "payment_unavailable");
        var paymentId = (await Mine(hold.Id)).Deposit!.PaymentId!.Value;

        Assert.Equal(WebhookOutcome.Applied, (await DeliverOk(Event("evt_f", "payment.failed", paymentId))).Outcome);
        var after = await Mine(hold.Id);
        Assert.Equal(BookingStatus.Pending, after.Status);
        Assert.Equal(DepositStatus.Due, after.Deposit!.Status);   // pay with another card while the hold lasts
        Assert.Equal(DepositStatus.Paid, (await ConfirmOk(hold)).Deposit!.Status);
    }

    [Fact]
    public async Task Refund_succeeded_marks_a_paid_deposit_refunded()
    {
        var booking = await BookOk(10);
        var body = Event("evt_r", "refund.succeeded", booking.Deposit!.PaymentId!.Value);
        Assert.Equal(WebhookOutcome.Applied, (await DeliverOk(body)).Outcome);
        Assert.Equal(DepositStatus.Refunded, (await Mine(booking.Id)).Deposit!.Status);
        Assert.Equal(WebhookOutcome.Duplicate, (await DeliverOk(body)).Outcome);
    }

    [Fact]
    public async Task Events_that_change_nothing_are_acknowledged()
    {
        var booking = await BookOk(10);
        // Already paid; an unknown payment; a type this app does not use: 200, so the provider stops retrying.
        Assert.Equal(WebhookOutcome.Ignored, (await DeliverOk(Event("evt_a", "payment.succeeded", booking.Deposit!.PaymentId!.Value))).Outcome);
        Assert.Equal(WebhookOutcome.Ignored, (await DeliverOk(Event("evt_b", "payment.succeeded", Guid.NewGuid()))).Outcome);
        Assert.Equal(WebhookOutcome.Ignored, (await DeliverOk(Event("evt_c", "customer.updated", Guid.NewGuid()))).Outcome);
    }

    [Fact]
    public async Task A_bad_signature_is_refused_and_changes_nothing()
    {
        var hold = await TimedOutHold();
        var paymentId = hold.Deposit!.PaymentId!.Value;
        var body = Event("evt_x", "payment.succeeded", paymentId);
        var now = Factory.Time.GetUtcNow().ToUnixTimeSeconds();
        var bytes = Encoding.UTF8.GetBytes(body);

        await AssertProblem(await Deliver(body, signature: FakePaymentWebhookReader.Sign("whsec_someone_else_entirely", now, bytes)), 401, "invalid_signature");
        await AssertProblem(await Deliver(body, signature: ""), 401, "invalid_signature");
        await AssertProblem(await Deliver(body, signature: "t=123,v1=abc"), 401, "invalid_signature");
        // Signed for one body, sent with another.
        var tampered = Event("evt_x", "payment.succeeded", Guid.NewGuid());
        await AssertProblem(await Deliver(tampered, signature: FakePaymentWebhookReader.Sign(ApiFactory.WebhookSecret, now, bytes)), 401, "invalid_signature");
        // A captured delivery replayed ten minutes later.
        await AssertProblem(await Deliver(body, timestamp: now - 600), 401, "invalid_signature");

        Assert.Equal(hold, await Mine(hold.Id));
        // The event id was not used up by the forgeries: the real delivery still applies.
        Assert.Equal(WebhookOutcome.Applied, (await DeliverOk(body)).Outcome);
    }

    [Fact]
    public async Task A_signed_body_that_is_not_an_event_is_422()
    {
        await AssertProblem(await Deliver("not json"), 422, "invalid_event");
        await AssertProblem(await Deliver("""{"type":"payment.succeeded"}"""), 422, "invalid_event");
    }
}

public sealed class PaymentWebhookTestsInMemory() : PaymentWebhookTests(StorageProvider.InMemory);

public sealed class PaymentWebhookTestsSqlite() : PaymentWebhookTests(StorageProvider.Sqlite);

public sealed class PaymentWebhookTestsPostgres() : PaymentWebhookTests(StorageProvider.Postgres);
