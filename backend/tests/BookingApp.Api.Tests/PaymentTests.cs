using System.Net;
using System.Net.Http.Json;
using BookingApp.Core.Bookings;
using BookingApp.Core.Payments;
using BookingApp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BookingApp.Api.Tests;

/// <summary>
/// Confirming a hold takes the deposit through the (fake) payment provider, never twice; cancelling under the rules
/// refunds it. Library: 4.9 Case 4 (Adapter), 12.5 "Idempotency keys — preventing double charges".
/// </summary>
public abstract class PaymentTests(StorageProvider provider) : ApiTest(provider)
{
    private async Task<BookingResponse> Mine(Guid id) =>
        (await Client.GetFromJsonAsync<BookingResponse>($"/api/bookings/{id}", Json, Ct))!;

    [Fact]
    public async Task The_venue_says_what_the_deposit_is()
    {
        var venue = (await Client.GetFromJsonAsync<VenueResponse>("/api/venue", Json, Ct))!;
        Assert.Equal(new MoneyResponse(100_000m, "VND"), venue.Deposit);
    }

    [Fact]
    public async Task A_hold_shows_the_deposit_due()
    {
        var hold = await HoldOk(10);
        Assert.Equal(new DepositResponse(null, 100_000m, "VND", DepositStatus.Due, null), hold.Deposit);
    }

    [Fact]
    public async Task Confirming_takes_the_deposit_once()
    {
        var hold = await HoldOk(10);
        var booking = await ConfirmOk(hold);

        Assert.Equal(BookingStatus.Confirmed, booking.Status);
        Assert.Equal(DepositStatus.Paid, booking.Deposit!.Status);
        Assert.Equal(100_000m, booking.Deposit.Amount);
        var charge = Assert.Single(Factory.Payments.Charges);
        Assert.Equal(booking.Deposit.PaymentId, charge.Key);
        Assert.Equal(new Money(100_000, "VND"), charge.Value.Amount);

        // Confirming again (a retry, a double click) answers the same and charges nothing more.
        Assert.Equal(BookingStatus.Confirmed, (await ConfirmOk(hold)).Status);
        Assert.Single(Factory.Payments.Charges);
    }

    [Fact]
    public async Task Confirming_without_a_payment_token_is_refused_and_keeps_the_hold()
    {
        var hold = await HoldOk(10);
        await AssertProblem(await Confirm(hold.Id, paymentToken: null), 422, "payment_required");
        var empty = await Client.PostAsync($"/api/bookings/{hold.Id}/confirm", null, Ct);
        await AssertProblem(empty, 422, "payment_required");
        Assert.Equal(BookingStatus.Pending, (await Mine(hold.Id)).Status);
        Assert.Empty(Factory.Payments.Charges);
    }

    [Fact]
    public async Task A_declined_card_keeps_the_hold_and_another_card_can_pay()
    {
        var hold = await HoldOk(10);
        await AssertProblem(await Confirm(hold.Id, paymentToken: "tok_insufficient_funds"), 402, "payment_declined");

        var after = await Mine(hold.Id);
        Assert.Equal(BookingStatus.Pending, after.Status);
        Assert.Equal(DepositStatus.Due, after.Deposit!.Status);
        Assert.Equal("insufficient_funds", after.Deposit.FailureCode);

        Assert.Equal(DepositStatus.Paid, (await ConfirmOk(hold)).Deposit!.Status);
        Assert.Single(Factory.Payments.Charges);
    }

    [Fact]
    public async Task A_provider_timeout_is_503_and_the_retry_cannot_charge_twice()
    {
        var hold = await HoldOk(10);
        // The provider takes the money but the answer is lost.
        await AssertProblem(await Confirm(hold.Id, paymentToken: "tok_timeout"), 503, "payment_unavailable");
        var waiting = await Mine(hold.Id);
        Assert.Equal(BookingStatus.Pending, waiting.Status);
        Assert.Equal(DepositStatus.Pending, waiting.Deposit!.Status);

        // The retry reuses the same payment (the provider's idempotency key) and gets the first charge back.
        var booking = await ConfirmOk(hold, paymentToken: "tok_timeout");
        Assert.Equal(DepositStatus.Paid, booking.Deposit!.Status);
        Assert.Equal(waiting.Deposit.PaymentId, booking.Deposit.PaymentId);
        Assert.Single(Factory.Payments.Charges);
    }

    [Fact]
    public async Task Cancelling_before_the_cut_off_refunds_the_deposit()
    {
        var booking = await BookOk(10);
        var cancelled = await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct);
        Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);

        var body = (await cancelled.Content.ReadFromJsonAsync<BookingResponse>(Json, Ct))!;
        Assert.Equal(DepositStatus.Refunded, body.Deposit!.Status);
        Assert.True(Factory.Payments.Refunds.ContainsKey(body.Deposit.PaymentId!.Value));
    }

    [Fact]
    public async Task After_the_cut_off_the_booking_cannot_be_cancelled_and_the_deposit_is_kept()
    {
        var booking = await BookOk(10);
        Factory.Time.SetUtcNow(ApiFactory.At(ApiFactory.Tomorrow, 9));   // one hour before; the cut-off is two

        await AssertProblem(await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct), 422, "too_late_to_cancel");
        Assert.Equal(DepositStatus.Paid, (await Mine(booking.Id)).Deposit!.Status);
        Assert.Empty(Factory.Payments.Refunds);
    }

    [Fact]
    public async Task Staff_cancelling_refunds_the_deposit_even_after_the_cut_off()
    {
        var booking = await BookOk(10);
        Factory.Time.SetUtcNow(ApiFactory.At(ApiFactory.Tomorrow, 9));
        var staff = await SignedInAs("Staff");
        var response = await staff.PostAsJsonAsync($"/api/staff/bookings/{booking.Id}/cancel", new { reason = "Court flooded" }, Ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(DepositStatus.Refunded, (await Mine(booking.Id)).Deposit!.Status);
    }

    [Fact]
    public async Task Releasing_an_unpaid_hold_moves_no_money()
    {
        var hold = await HoldOk(10);
        await Client.PostAsync($"/api/bookings/{hold.Id}/cancel", null, Ct);
        Assert.Null((await Mine(hold.Id)).Deposit);
        Assert.Empty(Factory.Payments.Charges);
        Assert.Empty(Factory.Payments.Refunds);
    }

    [Fact]
    public async Task A_refund_the_provider_could_not_make_is_retried_by_the_refund_job()
    {
        var booking = await BookOk(10);
        Factory.Payments.RefundsUnavailable = true;
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct)).StatusCode);
        Assert.Equal(DepositStatus.Paid, (await Mine(booking.Id)).Deposit!.Status);   // owed, not refunded yet

        Factory.Payments.RefundsUnavailable = false;
        Factory.Time.Advance(TimeSpan.FromSeconds(31));   // past a sweep of the refund job (on the fake clock)
        for (var i = 0; i < 100 && (await Mine(booking.Id)).Deposit!.Status != DepositStatus.Refunded; i++)
            await Task.Delay(50, Ct);
        Assert.Equal(DepositStatus.Refunded, (await Mine(booking.Id)).Deposit!.Status);
        Assert.Single(Factory.Payments.Refunds);
    }

    [Fact]
    public async Task A_hold_that_expires_while_paying_is_refunded()
    {
        var hold = await HoldOk(10);
        Factory.Payments.OnCharge = () => Factory.Time.Advance(TimeSpan.FromMinutes(11));   // a very slow card

        await AssertProblem(await Confirm(hold.Id), 409, "hold_expired");
        var after = await Mine(hold.Id);
        Assert.Equal(BookingStatus.Expired, after.Status);
        Assert.Equal(DepositStatus.Refunded, after.Deposit!.Status);
    }

    [Fact]
    public async Task Two_confirms_at_once_charge_once()
    {
        var hold = await HoldOk(10);
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Confirm(hold.Id), Ct)));

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, r.StatusCode.ToString()));
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(Factory.Payments.Charges);
        Assert.Equal(BookingStatus.Confirmed, (await Mine(hold.Id)).Status);
    }

    [Fact]
    public async Task My_bookings_show_each_deposit()
    {
        var paid = await BookOk(10);
        await HoldOk(12);
        var mine = (await Client.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct))!;
        Assert.Equal([DepositStatus.Paid, DepositStatus.Due], mine.Select(b => b.Deposit!.Status));
        Assert.Equal(paid.Deposit!.PaymentId, mine[0].Deposit!.PaymentId);
    }

    [Fact]
    public async Task The_refund_job_is_safe_to_run_twice()
    {
        var booking = await BookOk(10);
        await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct);
        using var scope = Factory.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<DepositService>().RefundDueAsync(Ct));
        Assert.Single(Factory.Payments.Refunds);
    }
}

public sealed class PaymentTestsInMemory() : PaymentTests(StorageProvider.InMemory);

public sealed class PaymentTestsSqlite() : PaymentTests(StorageProvider.Sqlite);

public sealed class PaymentTestsPostgres() : PaymentTests(StorageProvider.Postgres);
