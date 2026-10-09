using System.Net.Http.Json;
using System.Text.Json;

namespace BookingApp.Api.Tests;

/// <summary>
/// Request shape is checked at the boundary (400 with field errors); business rules stay in Core (422 with a code).
/// Library: 8.5 "Three layers of validation, three different jobs".
/// </summary>
public sealed class ValidationTests : ApiTest
{
    private Task<HttpResponseMessage> Post(object body) => Client.PostAsJsonAsync("/api/bookings", body, Ct);

    private static readonly DateTimeOffset Start = ApiFactory.At(ApiFactory.Tomorrow, 10);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_name_is_400_with_a_name_error(string name)
    {
        await AssertFieldError(await Post(new { resourceId = "court-1", start = Start, slots = 1, name }), "name", "Enter the name for the booking.");
    }

    [Fact]
    public async Task A_missing_name_is_400_with_a_name_error()
    {
        await AssertFieldError(await Post(new { resourceId = "court-1", start = Start, slots = 1 }), "name");
    }

    [Fact]
    public async Task A_name_longer_than_80_characters_is_400()
    {
        await AssertFieldError(await Post(new { resourceId = "court-1", start = Start, slots = 1, name = new string('a', 81) }), "name");
        Assert.Equal(System.Net.HttpStatusCode.Created, (await Post(new { resourceId = "court-1", start = Start, slots = 1, name = new string('a', 80) })).StatusCode);
    }

    [Fact]
    public async Task A_missing_resource_id_is_400()
    {
        await AssertFieldError(await Post(new { start = Start, slots = 1, name = "Nam" }), "resourceId");
    }

    [Fact]
    public async Task A_missing_start_is_400()
    {
        await AssertFieldError(await Post(new { resourceId = "court-1", slots = 1, name = "Nam" }), "start");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(25)]
    public async Task Slots_outside_1_to_24_are_400(int slots)
    {
        await AssertFieldError(await Post(new { resourceId = "court-1", start = Start, slots, name = "Nam" }), "slots");
    }

    [Fact]
    public async Task Leaving_out_slots_books_one_slot()
    {
        var response = await Post(new { resourceId = "court-1", start = Start, name = "Nam" });
        var booking = await response.Content.ReadFromJsonAsync<BookingResponse>(Json, Ct);
        Assert.Equal(TimeSpan.FromHours(1), booking!.End - booking.Start);
    }

    [Fact]
    public async Task Several_bad_fields_are_all_reported()
    {
        var problem = await AssertFieldError(await Post(new { slots = 0, name = "" }), "name");
        var errors = problem.GetProperty("errors");
        Assert.True(errors.TryGetProperty("resourceId", out _));
        Assert.True(errors.TryGetProperty("start", out _));
        Assert.True(errors.TryGetProperty("slots", out _));
    }

    [Fact]
    public async Task A_well_formed_booking_that_breaks_a_rule_is_still_422()
    {
        await AssertProblem(await Post(new { resourceId = "court-1", start = Start.AddDays(40), slots = 1, name = "Nam" }), 422, "too_far_ahead");
        await AssertProblem(await Post(new { resourceId = "court-1", start = Start, slots = 3, name = "Nam" }), 422, "too_long");
    }

    [Fact]
    public async Task Unreadable_json_is_400_validation_failed()
    {
        using var content = new StringContent("{ not json", System.Text.Encoding.UTF8, "application/json");
        var problem = await AssertValidationProblem(await Client.PostAsync("/api/bookings", content, Ct));
        Assert.Equal("The request could not be read.", problem.GetProperty("title").GetString());
    }

    [Fact]
    public async Task An_unreadable_date_is_400_with_a_date_error()
    {
        await AssertFieldError(await Client.GetAsync("/api/resources/court-1/availability?date=tomorrow", Ct), "date");
    }

    private static async Task<JsonElement> AssertFieldError(HttpResponseMessage response, string field, string? message = null)
    {
        var problem = await AssertValidationProblem(response);
        Assert.True(problem.GetProperty("errors").TryGetProperty(field, out var messages), $"No error for '{field}' in {problem}");
        if (message is not null) Assert.Contains(message, messages.EnumerateArray().Select(m => m.GetString()));
        return problem;
    }

    private static async Task<JsonElement> AssertValidationProblem(HttpResponseMessage response)
    {
        Assert.Equal(400, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        return problem;
    }
}
