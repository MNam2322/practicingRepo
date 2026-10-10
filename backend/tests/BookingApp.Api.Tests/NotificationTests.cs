using System.Net;
using System.Net.Http.Json;
using BookingApp.Core.Notifications;
using BookingApp.Infrastructure.Notifications;
using BookingApp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BookingApp.Api.Tests;

/// <summary>
/// Confirmation, cancellation and reminder emails through the outbox: written with the change, sent by the relay, never
/// lost and never sent twice. Library: 12.3 "Outbox + inbox patterns", 14.2 "The outbox relay".
/// </summary>
public abstract class NotificationTests(StorageProvider provider) : ApiTest(provider)
{
    private const string Email = "nam@example.com";

    private FileEmailSender Sender => Factory.Services.GetRequiredService<FileEmailSender>();

    /// <summary>
    /// The background relay polls hourly here, so the passes these tests make themselves (<see cref="Relay"/>) are the
    /// only ones while they move the clock by minutes; one test lets the background relay do the work.
    /// </summary>
    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?> { ["Notifications:PollSeconds"] = "3600" };

    /// <summary>One pass of the relay, as the background service runs it.</summary>
    private async Task<int> Relay()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<NotificationService>().SendDueAsync(Ct);
    }

    private async Task<IReadOnlyList<OutboxEntry>> Outbox(Guid bookingId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationOutbox>().ForBookingAsync(bookingId, Ct);
    }

    private static string Subject(string eml) => eml.Split("\r\n").Single(l => l.StartsWith("Subject: ", StringComparison.Ordinal))[9..];

    [Fact]
    public async Task Confirming_emails_a_confirmation_once()
    {
        var booking = await BookOk(10, email: Email);
        Assert.Equal(Email, booking.Email);
        Assert.Equal(1, await Relay());
        Assert.Equal(0, await Relay());   // nothing left to send

        var email = Assert.Single(Factory.Emails());
        Assert.Contains($"To: {Email}\r\n", email);
        Assert.Equal("Booking confirmed: Tennis court 1, Tue 2 Jun 10:00", Subject(email));
        Assert.Contains("Tuesday 2 June 2026, 10:00–11:00 (Asia/Ho Chi Minh time)", email);
        Assert.Contains($"/#/b/{booking.Id}", email);
    }

    [Fact]
    public async Task A_hold_is_not_emailed_until_it_is_confirmed_and_an_expired_one_never()
    {
        await HoldOk(10, email: Email);
        Factory.Time.Advance(TimeSpan.FromMinutes(11));
        Assert.Equal(0, await Relay());
        Assert.Empty(Factory.Emails());
    }

    [Fact]
    public async Task Cancelling_emails_a_cancellation()
    {
        var booking = await BookOk(10, email: Email);
        await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct);
        Assert.Equal(2, await Relay());
        Assert.Equal(["Booking confirmed: Tennis court 1, Tue 2 Jun 10:00", "Booking cancelled: Tennis court 1, Tue 2 Jun 10:00"],
            Factory.Emails().Select(Subject));
        Assert.Contains("Your booking is cancelled, as you asked.", Factory.Emails()[1]);
    }

    [Fact]
    public async Task A_staff_cancellation_says_why()
    {
        var booking = await BookOk(10, email: Email);
        var staff = await SignedInAs("Staff");
        await staff.PostAsJsonAsync($"/api/staff/bookings/{booking.Id}/cancel", new { reason = "Court flooded" }, Ct);
        await Relay();
        Assert.Contains("Reason: Court flooded", Factory.Emails()[1]);
    }

    [Fact]
    public async Task A_reminder_goes_out_24_hours_before_and_not_for_a_cancelled_booking()
    {
        var kept = await BookOk(10, email: Email);
        var cancelled = await BookOk(12, email: Email);
        await Client.PostAsync($"/api/bookings/{cancelled.Id}/cancel", null, Ct);
        await Relay();
        Assert.Equal(3, Factory.Emails().Count);   // two confirmations, one cancellation

        Factory.Time.SetUtcNow(kept.Start.AddHours(-24));
        await Relay();
        Assert.Equal(4, Factory.Emails().Count);
        Assert.Equal("Reminder: Tennis court 1 tomorrow at 10:00", Subject(Factory.Emails()[^1]));

        Factory.Time.SetUtcNow(cancelled.Start.AddHours(-24));
        await Relay();
        Assert.Equal(4, Factory.Emails().Count);
        Assert.Equal(DeliveryOutcome.Skipped, (await Outbox(cancelled.Id)).Single(e => e.Message.Kind == NotificationKind.Reminder).Outcome);
    }

    [Fact]
    public async Task Without_an_address_nobody_is_emailed()
    {
        var booking = await BookOk(10);
        Assert.Null(booking.Email);
        Assert.Empty(await Outbox(booking.Id));
    }

    [Fact]
    public async Task A_signed_in_customer_is_emailed_at_their_account_address()
    {
        var customer = await SignedInAs(null);
        var me = (await customer.GetFromJsonAsync<AccountResponse>("/api/me", Json, Ct))!;
        var booking = await BookOk(10, client: customer);
        Assert.Equal(me.Email, booking.Email);
        await Relay();
        Assert.Contains($"To: {me.Email}\r\n", Assert.Single(Factory.Emails()));
    }

    [Fact]
    public async Task A_bad_address_is_a_field_error()
    {
        var response = await Book(10, email: "not an address");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = (await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ValidationProblemDetails>(Ct))!;
        Assert.True(problem.Errors.ContainsKey("email"));
    }

    [Fact]
    public async Task A_send_that_was_not_recorded_is_sent_again_but_delivered_once()
    {
        var booking = await BookOk(10, email: Email);
        // The email is delivered, then the relay "crashes" before recording it.
        Sender.AfterWrite = _ => throw new IOException("Power cut after sending");
        Assert.Equal(0, await Relay());
        Assert.Single(Factory.Emails());
        Assert.Null(Assert.Single(await Outbox(booking.Id), e => e.Message.Kind == NotificationKind.BookingConfirmed).Outcome);

        Sender.AfterWrite = null;
        Factory.Time.Advance(NotificationService.Backoff(1));   // (the background relay may get there first: same result)
        await Relay();   // sent again with the same id: the receiver keeps one
        Assert.Single(Factory.Emails());
        Assert.Equal(DeliveryOutcome.Sent, (await Outbox(booking.Id)).Single(e => e.Message.Kind == NotificationKind.BookingConfirmed).Outcome);
    }

    [Fact]
    public async Task A_mail_server_that_is_down_is_retried_with_backoff_then_dead_lettered()
    {
        var booking = await BookOk(10, email: Email);
        Sender.Fail = _ => new IOException("Mail server unreachable");
        Assert.Equal(0, await Relay());
        var entry = (await Outbox(booking.Id)).Single(e => e.Message.Kind == NotificationKind.BookingConfirmed);
        Assert.Equal((1, null, "Mail server unreachable"), (entry.Message.Attempts, entry.Outcome, entry.LastError));
        Assert.Equal(0, await Relay());   // not yet: waiting 10 s
        Assert.Equal(1, (await Outbox(booking.Id)).Single(e => e.Message.Kind == NotificationKind.BookingConfirmed).Message.Attempts);

        for (var attempt = 1; attempt < 8; attempt++)
        {
            Factory.Time.Advance(NotificationService.Backoff(attempt));
            await Relay();
        }
        entry = (await Outbox(booking.Id)).Single(e => e.Message.Kind == NotificationKind.BookingConfirmed);
        Assert.Equal((8, DeliveryOutcome.Dead), (entry.Message.Attempts, entry.Outcome));
        Sender.Fail = null;
        Factory.Time.Advance(TimeSpan.FromHours(2));
        Assert.Equal(0, await Relay());   // dead letters wait for a person
        Assert.Empty(Factory.Emails());
    }

    [Fact]
    public async Task The_relay_sends_in_the_background()
    {
        await BookOk(10, email: Email);
        Factory.Time.Advance(TimeSpan.FromHours(1));   // one poll on the fake clock
        for (var i = 0; i < 100 && Factory.Emails().Count == 0; i++) await Task.Delay(50, Ct);
        Assert.Single(Factory.Emails());
    }
}

public sealed class NotificationTestsInMemory() : NotificationTests(StorageProvider.InMemory);

public sealed class NotificationTestsSqlite() : NotificationTests(StorageProvider.Sqlite);

public sealed class NotificationTestsPostgres() : NotificationTests(StorageProvider.Postgres);
