using System.Net;
using System.Net.Http.Json;
using BookingApp.Core.Notifications;
using BookingApp.Core.Waitlist;
using BookingApp.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace BookingApp.Api.Tests;

/// <summary>Waiting for a taken time: in line by joining order, told through the outbox when it frees up, once.</summary>
public abstract class WaitlistTests(StorageProvider provider) : ApiTest(provider)
{
    /// <summary>The sweep and the relay run hourly here; the tests run them (<see cref="Sweep"/>, <see cref="Relay"/>) when they choose.</summary>
    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["Waitlist:SweepSeconds"] = "3600",
        ["Notifications:PollSeconds"] = "3600",
    };

    private async Task<int> Sweep()
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<WaitlistService>().OfferFreedTimesAsync(Ct);
    }

    private async Task Relay()
    {
        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<NotificationService>().SendDueAsync(Ct);
    }

    private Task<HttpResponseMessage> Join(HttpClient client, int hour = 10, string? email = "wait@example.com") =>
        client.PostAsJsonAsync("/api/waitlist", new { resourceId = "court-1", start = ApiFactory.At(ApiFactory.Tomorrow, hour), name = "Waiter", email }, Ct);

    private async Task<WaitlistEntryResponse> JoinOk(HttpClient client, int hour = 10, string? email = "wait@example.com")
    {
        var response = await Join(client, hour, email);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<WaitlistEntryResponse>(Json, Ct))!;
    }

    private async Task<List<WaitlistEntryResponse>> Mine(HttpClient client) =>
        (await client.GetFromJsonAsync<List<WaitlistEntryResponse>>("/api/waitlist/mine", Json, Ct))!;

    [Fact]
    public async Task The_first_in_line_is_emailed_when_the_time_frees_up()
    {
        var booking = await BookOk(10);
        using var first = Factory.CreateClient();
        using var second = Factory.CreateClient();
        Assert.Equal(1, (await JoinOk(first, email: "first@example.com")).Position);
        Assert.Equal(2, (await JoinOk(second, email: "second@example.com")).Position);
        Assert.Equal(0, await Sweep());   // still taken: nobody is told

        await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct);
        Assert.Equal(1, await Sweep());
        Assert.Equal(0, await Sweep());   // told once
        await Relay();

        var email = Assert.Single(Factory.Emails(), e => e.Contains("To: first@example.com"));
        Assert.Contains("Subject: Free again: Tennis court 1, Tue 2 Jun 10:00", email);
        Assert.Contains("/#/r/court-1/2026-06-02?at=2026-06-02T03%3A00%3A00%2B00%3A00", email);
        Assert.DoesNotContain(Factory.Emails(), e => e.Contains("To: second@example.com"));
        Assert.Equal((WaitlistStatus.Notified, 0), ((await Mine(first))[0].Status, (await Mine(first))[0].Position));
        Assert.Equal(1, (await Mine(second))[0].Position);   // next in line now
    }

    [Fact]
    public async Task If_the_time_is_still_free_after_30_minutes_the_next_in_line_is_told()
    {
        var booking = await BookOk(10);
        using var first = Factory.CreateClient();
        using var second = Factory.CreateClient();
        await JoinOk(first, email: "first@example.com");
        await JoinOk(second, email: "second@example.com");
        await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct);
        await Sweep();

        Factory.Time.Advance(TimeSpan.FromMinutes(29));
        Assert.Equal(0, await Sweep());
        Factory.Time.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, await Sweep());
        Assert.Equal(WaitlistStatus.Notified, (await Mine(second))[0].Status);
    }

    [Fact]
    public async Task Nobody_is_told_while_the_time_is_taken_again()
    {
        var booking = await BookOk(10);
        using var waiter = Factory.CreateClient();
        await JoinOk(waiter);
        await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct);
        using var quick = Factory.CreateClient();
        await BookOk(10, client: quick);   // someone books it before the sweep
        Assert.Equal(0, await Sweep());
        Assert.Equal(WaitlistStatus.Waiting, (await Mine(waiter))[0].Status);
    }

    [Fact]
    public async Task An_expired_hold_frees_the_time_too()
    {
        await HoldOk(10);
        using var waiter = Factory.CreateClient();
        await JoinOk(waiter);
        Factory.Time.Advance(TimeSpan.FromMinutes(10));
        Assert.Equal(1, await Sweep());
    }

    [Fact]
    public async Task Leaving_the_line_passes_the_turn_on()
    {
        var booking = await BookOk(10);
        using var first = Factory.CreateClient();
        using var second = Factory.CreateClient();
        var entry = await JoinOk(first, email: "first@example.com");
        await JoinOk(second, email: "second@example.com");

        var left = await first.PostAsync($"/api/waitlist/{entry.Id}/leave", null, Ct);
        Assert.Equal(HttpStatusCode.OK, left.StatusCode);
        Assert.Equal(WaitlistStatus.Left, (await left.Content.ReadFromJsonAsync<WaitlistEntryResponse>(Json, Ct))!.Status);
        Assert.Equal(1, (await Mine(second))[0].Position);

        await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct);
        await Sweep();
        await Relay();
        Assert.Contains(Factory.Emails(), e => e.Contains("To: second@example.com"));
        Assert.DoesNotContain(Factory.Emails(), e => e.Contains("To: first@example.com"));
    }

    [Fact]
    public async Task Only_a_taken_time_can_be_waited_for_once_and_not_your_own()
    {
        using var waiter = Factory.CreateClient();
        await AssertProblem(await Join(waiter), 409, "slot_free");

        await BookOk(10);
        await AssertProblem(await Join(Client), 409, "already_yours");
        await JoinOk(waiter);
        await AssertProblem(await Join(waiter), 409, "already_waiting");
        await AssertProblem(await Join(waiter, hour: 23), 422, "outside_opening_hours");
    }

    [Fact]
    public async Task An_address_is_needed_and_a_signed_in_customer_has_one()
    {
        await BookOk(10);
        using var anonymous = Factory.CreateClient();
        await AssertProblem(await Join(anonymous, email: null), 422, "email_required");

        var signedIn = await SignedInAs(null);
        var me = (await signedIn.GetFromJsonAsync<AccountResponse>("/api/me", Json, Ct))!;
        Assert.Equal(me.Email, (await JoinOk(signedIn, email: null)).Email);
    }

    [Fact]
    public async Task Entries_for_times_that_have_passed_are_over()
    {
        await BookOk(10);
        using var waiter = Factory.CreateClient();
        await JoinOk(waiter);
        Factory.Time.SetUtcNow(ApiFactory.At(ApiFactory.Tomorrow, 11));
        await Sweep();
        Assert.Empty(await Mine(waiter));
    }

    [Fact]
    public async Task Someone_else_cannot_leave_your_place()
    {
        await BookOk(10);
        using var waiter = Factory.CreateClient();
        var entry = await JoinOk(waiter);
        using var other = Factory.CreateClient();
        await AssertProblem(await other.PostAsync($"/api/waitlist/{entry.Id}/leave", null, Ct), 404, "waitlist_entry_not_found");
    }

    [Fact]
    public async Task The_sweep_runs_in_the_background()
    {
        var booking = await BookOk(10);
        using var waiter = Factory.CreateClient();
        await JoinOk(waiter);
        await Client.PostAsync($"/api/bookings/{booking.Id}/cancel", null, Ct);
        Factory.Time.Advance(TimeSpan.FromHours(1));   // one sweep on the fake clock
        for (var i = 0; i < 100 && (await Mine(waiter))[0].Status != WaitlistStatus.Notified; i++) await Task.Delay(50, Ct);
        Assert.Equal(WaitlistStatus.Notified, (await Mine(waiter))[0].Status);
    }
}

public sealed class WaitlistTestsInMemory() : WaitlistTests(StorageProvider.InMemory);

public sealed class WaitlistTestsSqlite() : WaitlistTests(StorageProvider.Sqlite);

public sealed class WaitlistTestsPostgres() : WaitlistTests(StorageProvider.Postgres);
