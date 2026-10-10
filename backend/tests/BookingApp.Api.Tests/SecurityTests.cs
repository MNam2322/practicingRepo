using System.Net;
using System.Net.Http.Json;
using BookingApp.Core.Bookings;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BookingApp.Api.Tests;

/// <summary>
/// Hardening: security headers, antiforgery on cookie-authenticated writes, rate limits per account. Library: 10.4
/// "Cross-site request forgery", "Baseline hardening", "Rate limiting (.NET 7+, built in)".
/// </summary>
public sealed class SecurityTests : ApiTest
{
    [Fact]
    public async Task Api_responses_carry_the_security_headers()
    {
        var response = await Client.GetAsync("/api/venue", Ct);
        var h = response.Headers;
        Assert.Equal("nosniff", h.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", h.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", h.GetValues("Referrer-Policy").Single());
        Assert.Equal("default-src 'none'; frame-ancestors 'none'", h.GetValues("Content-Security-Policy").Single());
        Assert.True(h.CacheControl?.NoStore);
        Assert.False(h.Contains("Server"));
    }

    [Fact]
    public async Task A_write_without_the_antiforgery_token_is_refused()
    {
        // A browser that has our cookies (as on a hostile page) but cannot read XSRF-TOKEN to send it.
        using var raw = Factory.CreateClient(new WebApplicationFactoryClientOptions());   // the base client: no token header
        await raw.GetAsync("/api/antiforgery", Ct);
        var response = await raw.PostAsJsonAsync("/api/bookings", BookingBody(10), Ct);
        await AssertProblem(response, 400, "antiforgery_failed");

        // A made-up token is no better.
        var forged = new HttpRequestMessage(HttpMethod.Post, "/api/bookings") { Content = JsonContent.Create(BookingBody(10)) };
        forged.Headers.Add(BookingSecurity.HeaderName, "forged");
        await AssertProblem(await raw.SendAsync(forged, Ct), 400, "antiforgery_failed");

        // Nothing was booked.
        var mine = await raw.GetFromJsonAsync<List<BookingResponse>>("/api/bookings/mine", Json, Ct);
        Assert.Empty(mine!);
    }

    [Fact]
    public async Task Reads_need_no_token_and_the_web_app_client_writes_fine()
    {
        using var raw = Factory.CreateClient(new WebApplicationFactoryClientOptions());
        Assert.Equal(HttpStatusCode.OK, (await raw.GetAsync("/api/bookings/mine", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await Book(10)).StatusCode);   // Client sends the token, like the SPA
    }

    [Fact]
    public async Task Signing_in_issues_a_new_token_and_the_old_one_no_longer_works()
    {
        var cookies = new CookieContainer();
        using var browser = Factory.CreateClient(cookies);
        await browser.GetAsync("/api/antiforgery", Ct);
        var anonymousToken = cookies.GetCookies(new Uri("http://localhost"))[BookingSecurity.TokenCookie]!.Value;

        var email = $"af.{Guid.NewGuid():N}@example.com";
        Assert.Equal(HttpStatusCode.Created, (await browser.PostAsJsonAsync("/api/account/signup", new { email, password = Password, name = "Af" }, Ct)).StatusCode);
        var signedInToken = cookies.GetCookies(new Uri("http://localhost"))[BookingSecurity.TokenCookie]!.Value;
        Assert.NotEqual(anonymousToken, signedInToken);

        var stale = new HttpRequestMessage(HttpMethod.Post, "/api/bookings") { Content = JsonContent.Create(BookingBody(10)) };
        stale.Headers.Add(BookingSecurity.HeaderName, anonymousToken);
        await AssertProblem(await browser.SendAsync(stale, Ct), 400, "antiforgery_failed");
        Assert.Equal(HttpStatusCode.Created, (await Book(10, client: browser)).StatusCode);   // with the new one
    }

    [Fact]
    public async Task The_payment_webhook_needs_no_token()
    {
        using var raw = Factory.CreateClient(new WebApplicationFactoryClientOptions());
        var response = await raw.PostAsync("/api/payments/webhook", new StringContent("{}", System.Text.Encoding.UTF8, "application/json"), Ct);
        await AssertProblem(response, 401, "invalid_signature");   // refused for its signature, not for antiforgery
    }
}

/// <summary>The real limits (the other tests lift them).</summary>
public sealed class RateLimitTests : ApiTest
{
    protected override IReadOnlyDictionary<string, string?> Settings => new Dictionary<string, string?>
    {
        ["RateLimits:RequestsPerMinute"] = "300",
        ["RateLimits:WritesPerMinutePerAccount"] = "20",
        ["RateLimits:WritesPerMinutePerIp"] = "60",
        ["RateLimits:SignInsPerMinutePerIp"] = "10",
    };

    private Task<HttpResponseMessage> Cancel(HttpClient client) => client.PostAsync($"/api/bookings/{Guid.NewGuid()}/cancel", null, Ct);

    [Fact]
    public async Task Anonymous_writes_are_limited_per_ip_however_many_customer_cookies_are_minted()
    {
        for (var i = 0; i < 60; i++)
        {
            using var fresh = Factory.CreateClient();   // a new customer cookie every time
            Assert.Equal(HttpStatusCode.NotFound, (await Cancel(fresh)).StatusCode);
        }
        using var another = Factory.CreateClient();
        var limited = await Cancel(another);
        await AssertProblem(limited, 429, "rate_limited");
        Assert.True(limited.Headers.RetryAfter is not null);
    }

    [Fact]
    public async Task A_signed_in_account_has_its_own_allowance()
    {
        var alice = await SignedInAs(null);
        for (var i = 0; i < 20; i++) Assert.Equal(HttpStatusCode.NotFound, (await Cancel(alice)).StatusCode);
        await AssertProblem(await Cancel(alice), 429, "rate_limited");

        var bob = await SignedInAs(null);   // another account is not affected
        Assert.Equal(HttpStatusCode.NotFound, (await Cancel(bob)).StatusCode);
    }

    [Fact]
    public async Task Sign_in_attempts_are_limited_per_ip()
    {
        using var guesser = Factory.CreateClient();
        HttpResponseMessage last = null!;
        for (var i = 0; i < 11; i++)
            last = await guesser.PostAsJsonAsync("/api/account/signin", new { email = "nobody@example.com", password = "wrong password!" }, Ct);
        await AssertProblem(last, 429, "rate_limited");
    }
}
