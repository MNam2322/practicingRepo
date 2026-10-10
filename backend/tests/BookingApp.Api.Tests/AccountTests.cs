using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using BookingApp.Infrastructure.Persistence;

namespace BookingApp.Api.Tests;

/// <summary>Sign up, sign in, sign out, /api/me, and anonymous bookings following the person into their account.</summary>
public abstract class AccountTests(StorageProvider provider) : ApiTest(provider)
{
    private const string Password = "correct horse battery";

    private static string NewEmail() => $"nam.{Guid.NewGuid():N}@example.com";

    private Task<HttpResponseMessage> SignUp(string email, string password = Password, string name = "Nam", HttpClient? client = null) =>
        (client ?? Client).PostAsJsonAsync("/api/account/signup", new { email, password, name }, Ct);

    private Task<HttpResponseMessage> SignIn(string email, string password = Password, HttpClient? client = null) =>
        (client ?? Client).PostAsJsonAsync("/api/account/signin", new { email, password }, Ct);

    [Fact]
    public async Task Signing_up_signs_in_as_a_customer()
    {
        var email = NewEmail();
        var response = await SignUp(email);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var cookie = Assert.Single(response.Headers.GetValues("Set-Cookie"), c => c.StartsWith("bk-auth=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        var signedIn = (await response.Content.ReadFromJsonAsync<SignedInResponse>(Json, Ct))!;
        Assert.Equal(email, signedIn.Account.Email);
        Assert.Equal(["Customer"], signedIn.Account.Roles);

        var me = (await Client.GetFromJsonAsync<AccountResponse>("/api/me", Json, Ct))!;
        Assert.Equal(signedIn.Account.Id, me.Id);
        Assert.Equal(email, me.Email);
        Assert.Equal("Nam", me.Name);
        Assert.Equal(["Customer"], me.Roles);
    }

    [Fact]
    public async Task Me_without_signing_in_is_401_unauthenticated()
    {
        await AssertProblem(await Client.GetAsync("/api/me", Ct), 401, "unauthenticated");
    }

    [Fact]
    public async Task The_same_email_twice_is_409_email_taken()
    {
        var email = NewEmail();
        await SignUp(email);
        using var other = Factory.CreateClient();
        await AssertProblem(await SignUp(email.ToUpperInvariant(), client: other), 409, "email_taken");
    }

    [Fact]
    public async Task A_short_password_or_bad_email_is_a_400_field_error()
    {
        var response = await SignUp("not-an-email", "short");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        Assert.True(problem.GetProperty("errors").TryGetProperty("password", out _));
        Assert.True(problem.GetProperty("errors").TryGetProperty("email", out _));
    }

    [Fact]
    public async Task A_wrong_password_or_unknown_email_is_401_invalid_credentials()
    {
        var email = NewEmail();
        await SignUp(email);
        using var other = Factory.CreateClient();
        await AssertProblem(await SignIn(email, "wrong password here", other), 401, "invalid_credentials");
        await AssertProblem(await SignIn(NewEmail(), Password, other), 401, "invalid_credentials");
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account()
    {
        var email = NewEmail();
        await SignUp(email);
        using var other = Factory.CreateClient();
        for (var i = 0; i < 5; i++) await SignIn(email, "wrong password here", other);
        await AssertProblem(await SignIn(email, Password, other), 401, "account_locked");
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        await SignUp(NewEmail());
        Assert.Equal(HttpStatusCode.NoContent, (await Client.PostAsync("/api/account/signout", null, Ct)).StatusCode);
        await AssertProblem(await Client.GetAsync("/api/me", Ct), 401, "unauthenticated");
    }

    [Fact]
    public async Task Bookings_made_before_signing_up_move_to_the_new_account()
    {
        await BookOk(10);
        await BookOk(12);

        var signedIn = (await (await SignUp(NewEmail())).Content.ReadFromJsonAsync<SignedInResponse>(Json, Ct))!;
        Assert.Equal(2, signedIn.MovedBookings);
        Assert.Equal(2, (await Client.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct))!.Count);
    }

    [Fact]
    public async Task Bookings_follow_the_account_across_sign_out_and_sign_in()
    {
        var email = NewEmail();
        await SignUp(email);
        var booked = await BookOk(10);
        await Client.PostAsync("/api/account/signout", null, Ct);

        // Signed out, this browser is anonymous again: no bookings of its own.
        Assert.Empty((await Client.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct))!);
        // Book anonymously, then sign in: that booking moves too.
        await BookOk(14);
        var signedIn = (await (await SignIn(email)).Content.ReadFromJsonAsync<SignedInResponse>(Json, Ct))!;
        Assert.Equal(1, signedIn.MovedBookings);

        var mine = (await Client.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct))!;
        Assert.Equal(2, mine.Count);
        Assert.Contains(mine, b => b.Id == booked.Id);
        // And the account's booking can still be cancelled by its owner.
        Assert.Equal(HttpStatusCode.OK, (await Client.PostAsync($"/api/bookings/{booked.Id}/cancel", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task Another_browser_signed_into_the_same_account_sees_the_same_bookings()
    {
        var email = NewEmail();
        await SignUp(email);
        var booked = await BookOk(10);

        using var laptop = Factory.CreateClient();
        await SignIn(email, client: laptop);
        var mine = (await laptop.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct))!;
        Assert.Equal(booked.Id, Assert.Single(mine).Id);
    }
}

public sealed class AccountTestsInMemory() : AccountTests(StorageProvider.InMemory);

public sealed class AccountTestsSqlite() : AccountTests(StorageProvider.Sqlite);

public sealed class AccountTestsPostgres() : AccountTests(StorageProvider.Postgres);
